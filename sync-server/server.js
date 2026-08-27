const express = require("express");
const path = require("path");
const { openDatabase } = require("./db");
const { hashPassword, isBcryptHash, passwordPolicyError, verifyPassword } = require("./password");
const { migrateBlobsFromCatalog, openSheetVault } = require("./sheet-vault");

const PORT = process.env.PORT || 3000;
const HOST = process.env.HOST || "0.0.0.0";
const API_KEY = process.env.SYNC_API_KEY || "";
const PUBLIC_DIR = path.join(__dirname, "public");
const DEFAULT_WEB_PASSWORD = "admin";
const webSessions = new Map();

function createSessionToken() {
  const token = require("crypto").randomBytes(32).toString("hex");
  webSessions.set(token, Date.now() + 24 * 60 * 60 * 1000);
  return token;
}

function isValidSession(token) {
  if (!token || !webSessions.has(token)) {
    return false;
  }
  if (webSessions.get(token) < Date.now()) {
    webSessions.delete(token);
    return false;
  }
  return true;
}

function extractWebToken(req) {
  const auth = req.header("authorization");
  if (auth && auth.startsWith("Bearer ")) {
    return auth.slice(7);
  }
  return null;
}

function requireWebAuth(req, res, next) {
  const token = extractWebToken(req);
  if (!isValidSession(token)) {
    return res.status(401).json({ error: "Nicht angemeldet" });
  }
  next();
}

function requireApiKey(req, res, next) {
  if (API_KEY && req.header("x-api-key") !== API_KEY) {
    return res.status(401).json({ error: "Unauthorized" });
  }
  next();
}

function asyncHandler(handler) {
  return (req, res, next) => {
    Promise.resolve(handler(req, res, next)).catch(next);
  };
}

function parseInstrumentNames(value) {
  if (value == null || value === "") {
    return [];
  }
  if (Array.isArray(value)) {
    return value.flatMap((item) => parseInstrumentNames(item)).filter(Boolean);
  }
  if (typeof Buffer !== "undefined" && Buffer.isBuffer(value)) {
    return parseInstrumentNames(value.toString("utf8"));
  }
  if (typeof value === "object") {
    return Object.values(value).flatMap((item) => parseInstrumentNames(item)).filter(Boolean);
  }
  if (typeof value !== "string") {
    const asText = String(value).trim();
    return asText ? [asText] : [];
  }
  const text = value.trim();
  if (!text) {
    return [];
  }
  const looksJson = text.startsWith("[") || text.startsWith("{") || text.startsWith('"');
  if (looksJson) {
    try {
      return parseInstrumentNames(JSON.parse(text));
    } catch {
      return [text];
    }
  }
  return [text];
}

function mapPieceRow(row, sheetCount) {
  return {
    syncUid: row.sync_uid,
    title: row.title,
    composer: row.composer,
    arranger: row.arranger,
    publisher: row.publisher,
    isbn: row.isbn,
    tags: row.tags,
    genre: row.genre,
    cabinet: row.cabinet,
    compartment: row.compartment,
    slot: row.slot,
    isActive: Number(row.is_active) === 1,
    folderPath: row.folder_path,
    instrumentNames: parseInstrumentNames(row.instrument_names_json),
    updatedAt: row.updated_at,
    sheetCount: Number(sheetCount ?? row.sheet_count ?? 0) || 0,
  };
}

function mapSheetMeta(row) {
  return {
    syncUid: row.sync_uid,
    pieceSyncUid: row.piece_sync_uid,
    fileName: row.file_name,
    contentType: row.content_type,
    contentHash: row.content_hash,
    instrumentId: row.instrument_id,
    instrumentName: row.instrument_name,
    instrumentGroupId: row.instrument_group_id,
    sortOrder: row.sort_order,
    updatedAt: row.updated_at,
  };
}

async function start() {
  const db = await openDatabase();
  const sheetVault = openSheetVault();
  await migrateBlobsFromCatalog(db, sheetVault);

  async function countRows(table) {
    const row = await db.get(`SELECT COUNT(*) AS n FROM ${table}`);
    return Number(row?.n ?? 0) || 0;
  }

  async function wipeCatalog() {
    const pieces = await countRows("pieces");
    const sheets = await countRows("sheet_files");
    const tombstones = await countRows("sync_tombstones");
    await db.transaction(async (tx) => {
      await tx.run("DELETE FROM sheet_files");
      await tx.run("DELETE FROM pieces");
      await tx.run("DELETE FROM sync_tombstones");
    });
    const vaultFiles = await sheetVault.clearAll();
    return { pieces, sheets, tombstones, vaultFiles };
  }

  async function getSetting(key, fallback = "") {
    const row = await db.get("SELECT value FROM server_settings WHERE `key` = ?", [key]);
    return row?.value ?? fallback;
  }

  async function setSetting(key, value) {
    if (db.dialect === "mysql") {
      await db.run(
        "INSERT INTO server_settings (`key`, value) VALUES (?, ?) ON DUPLICATE KEY UPDATE value = VALUES(value)",
        [key, value],
      );
      return;
    }
    await db.run(
      "INSERT INTO server_settings (`key`, value) VALUES (?, ?) ON CONFLICT(`key`) DO UPDATE SET value = excluded.value",
      [key, value],
    );
  }

  async function getWebViewPassword() {
    return getSetting("web_view_password", DEFAULT_WEB_PASSWORD);
  }

  async function upsertTombstone(tx, syncUid, entityType, deletedAt) {
    if (db.dialect === "mysql") {
      await tx.run(
        `INSERT INTO sync_tombstones (sync_uid, entity_type, deleted_at)
         VALUES (?, ?, ?)
         ON DUPLICATE KEY UPDATE entity_type = VALUES(entity_type), deleted_at = VALUES(deleted_at)`,
        [syncUid, entityType, deletedAt],
      );
      return;
    }
    await tx.run(
      `INSERT INTO sync_tombstones (sync_uid, entity_type, deleted_at)
       VALUES (?, ?, ?)
       ON CONFLICT(sync_uid) DO UPDATE SET
         entity_type = excluded.entity_type,
         deleted_at = excluded.deleted_at`,
      [syncUid, entityType, deletedAt],
    );
  }

  async function applyTombstone(tx, tombstone) {
    const syncUid = tombstone.syncUid;
    const entityType = String(tombstone.entityType || "").toLowerCase();
    const deletedAt = tombstone.deletedAt || new Date().toISOString();
    if (!syncUid) {
      return;
    }

    if (entityType === "sheet") {
      await sheetVault.remove(syncUid);
      await tx.run("DELETE FROM sheet_files WHERE sync_uid = ?", [syncUid]);
    } else if (entityType === "piece") {
      const pieceSheets = await tx.all("SELECT sync_uid FROM sheet_files WHERE piece_sync_uid = ?", [syncUid]);
      for (const sheet of pieceSheets) {
        await sheetVault.remove(sheet.sync_uid);
      }
      await tx.run("DELETE FROM sheet_files WHERE piece_sync_uid = ?", [syncUid]);
      await tx.run("DELETE FROM pieces WHERE sync_uid = ?", [syncUid]);
    }

    await upsertTombstone(tx, syncUid, entityType, deletedAt);
  }

  const upsertPieceSql =
    db.dialect === "mysql"
      ? `INSERT INTO pieces (
          sync_uid, title, composer, arranger, publisher, isbn, tags, genre,
          cabinet, compartment, slot, is_active, folder_path, instrument_names_json, updated_at
        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
        ON DUPLICATE KEY UPDATE
          title = IF(VALUES(updated_at) >= updated_at, VALUES(title), title),
          composer = IF(VALUES(updated_at) >= updated_at, VALUES(composer), composer),
          arranger = IF(VALUES(updated_at) >= updated_at, VALUES(arranger), arranger),
          publisher = IF(VALUES(updated_at) >= updated_at, VALUES(publisher), publisher),
          isbn = IF(VALUES(updated_at) >= updated_at, VALUES(isbn), isbn),
          tags = IF(VALUES(updated_at) >= updated_at, VALUES(tags), tags),
          genre = IF(VALUES(updated_at) >= updated_at, VALUES(genre), genre),
          cabinet = IF(VALUES(updated_at) >= updated_at, VALUES(cabinet), cabinet),
          compartment = IF(VALUES(updated_at) >= updated_at, VALUES(compartment), compartment),
          slot = IF(VALUES(updated_at) >= updated_at, VALUES(slot), slot),
          is_active = IF(VALUES(updated_at) >= updated_at, VALUES(is_active), is_active),
          folder_path = IF(VALUES(updated_at) >= updated_at, VALUES(folder_path), folder_path),
          instrument_names_json = IF(VALUES(updated_at) >= updated_at, VALUES(instrument_names_json), instrument_names_json),
          updated_at = IF(VALUES(updated_at) >= updated_at, VALUES(updated_at), updated_at)`
      : `INSERT INTO pieces (
          sync_uid, title, composer, arranger, publisher, isbn, tags, genre,
          cabinet, compartment, slot, is_active, folder_path, instrument_names_json, updated_at
        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
        ON CONFLICT(sync_uid) DO UPDATE SET
          title = excluded.title,
          composer = excluded.composer,
          arranger = excluded.arranger,
          publisher = excluded.publisher,
          isbn = excluded.isbn,
          tags = excluded.tags,
          genre = excluded.genre,
          cabinet = excluded.cabinet,
          compartment = excluded.compartment,
          slot = excluded.slot,
          is_active = excluded.is_active,
          folder_path = excluded.folder_path,
          instrument_names_json = excluded.instrument_names_json,
          updated_at = excluded.updated_at
        WHERE excluded.updated_at >= pieces.updated_at`;

  const upsertSheetSql =
    db.dialect === "mysql"
      ? `INSERT INTO sheet_files (
          sync_uid, piece_sync_uid, file_name, content_type, content_hash, file_data,
          instrument_id, instrument_name, instrument_group_id, sort_order, updated_at
        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
        ON DUPLICATE KEY UPDATE
          piece_sync_uid = IF(VALUES(updated_at) >= updated_at, VALUES(piece_sync_uid), piece_sync_uid),
          file_name = IF(VALUES(updated_at) >= updated_at, VALUES(file_name), file_name),
          content_type = IF(VALUES(updated_at) >= updated_at, VALUES(content_type), content_type),
          content_hash = IF(VALUES(updated_at) >= updated_at, VALUES(content_hash), content_hash),
          file_data = IF(VALUES(updated_at) >= updated_at, VALUES(file_data), file_data),
          instrument_id = IF(VALUES(updated_at) >= updated_at, VALUES(instrument_id), instrument_id),
          instrument_name = IF(VALUES(updated_at) >= updated_at, VALUES(instrument_name), instrument_name),
          instrument_group_id = IF(VALUES(updated_at) >= updated_at, VALUES(instrument_group_id), instrument_group_id),
          sort_order = IF(VALUES(updated_at) >= updated_at, VALUES(sort_order), sort_order),
          updated_at = IF(VALUES(updated_at) >= updated_at, VALUES(updated_at), updated_at)`
      : `INSERT INTO sheet_files (
          sync_uid, piece_sync_uid, file_name, content_type, content_hash, file_data,
          instrument_id, instrument_name, instrument_group_id, sort_order, updated_at
        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
        ON CONFLICT(sync_uid) DO UPDATE SET
          piece_sync_uid = excluded.piece_sync_uid,
          file_name = excluded.file_name,
          content_type = excluded.content_type,
          content_hash = excluded.content_hash,
          file_data = excluded.file_data,
          instrument_id = excluded.instrument_id,
          instrument_name = excluded.instrument_name,
          instrument_group_id = excluded.instrument_group_id,
          sort_order = excluded.sort_order,
          updated_at = excluded.updated_at
        WHERE excluded.updated_at >= sheet_files.updated_at`;

  function pieceParams(piece) {
    return [
      piece.syncUid,
      piece.title,
      piece.composer || "",
      piece.arranger || "",
      piece.publisher || "",
      piece.isbn || "",
      piece.tags || "",
      piece.genre || "",
      piece.cabinet || "",
      piece.compartment || "",
      piece.slot || "",
      piece.isActive ? 1 : 0,
      piece.folderPath || "",
      JSON.stringify(piece.instrumentNames || []),
      piece.updatedAt,
    ];
  }

  function sheetParams(sheet) {
    return [
      sheet.syncUid,
      sheet.pieceSyncUid,
      sheet.fileName,
      sheet.contentType || "",
      sheet.contentHash || "",
      Buffer.alloc(0),
      sheet.instrumentId ?? null,
      sheet.instrumentName || "",
      sheet.instrumentGroupId ?? null,
      sheet.sortOrder || 0,
      sheet.updatedAt,
    ];
  }

  const app = express();
  app.disable("x-powered-by");
  app.set("trust proxy", 1);
  app.use(express.json({ limit: "200mb" }));
  app.use((req, res, next) => {
    res.setHeader("X-Content-Type-Options", "nosniff");
    res.setHeader("Referrer-Policy", "same-origin");
    res.setHeader("X-Frame-Options", "SAMEORIGIN");
    if (req.path.startsWith("/api/")) {
      res.setHeader("Cache-Control", "no-store");
    }
    next();
  });

  app.get("/api/health", (_req, res) => {
    res.json({ ok: true, version: "1", db: db.dialect });
  });

  app.post("/api/auth/login", async (req, res) => {
    const password = String(req.body?.password ?? "");
    const stored = await getWebViewPassword();
    if (!(await verifyPassword(password, stored))) {
      return res.status(401).json({ error: "Ungültiges Passwort" });
    }
    if (!isBcryptHash(stored) && !passwordPolicyError(password)) {
      await setSetting("web_view_password", hashPassword(password));
    }
    const token = createSessionToken();
    res.json({ ok: true, token });
  });

  app.post("/api/auth/logout", (req, res) => {
    const token = extractWebToken(req);
    if (token) {
      webSessions.delete(token);
    }
    res.json({ ok: true });
  });

  app.get("/api/auth/status", (req, res) => {
    const token = extractWebToken(req);
    res.json({ authenticated: isValidSession(token) });
  });

  app.get("/api/pieces", requireWebAuth, asyncHandler(async (_req, res) => {
    const orderSql = db.dialect === "mysql" ? "ORDER BY p.title" : "ORDER BY p.title COLLATE NOCASE";
    const rows = await db.all(
      `SELECT p.*, (
         SELECT COUNT(*) FROM sheet_files sf WHERE sf.piece_sync_uid = p.sync_uid
       ) AS sheet_count
       FROM pieces p
       ${orderSql}`,
    );
    res.json({ pieces: rows.map((row) => mapPieceRow(row, row.sheet_count)) });
  }));

  app.get("/api/pieces/:syncUid", requireWebAuth, asyncHandler(async (req, res) => {
    const row = await db.get("SELECT * FROM pieces WHERE sync_uid = ?", [req.params.syncUid]);
    if (!row) {
      return res.status(404).json({ error: "Stück nicht gefunden" });
    }

    const sheets = (
      await db.all(
        `SELECT sync_uid, piece_sync_uid, file_name, content_type, content_hash,
                instrument_id, instrument_name, instrument_group_id, sort_order, updated_at
         FROM sheet_files
         WHERE piece_sync_uid = ?
         ORDER BY sort_order, file_name`,
        [req.params.syncUid],
      )
    ).map(mapSheetMeta);

    res.json({
      piece: mapPieceRow(row, sheets.length),
      sheets,
    });
  }));

  app.get("/api/meta/filters", requireWebAuth, asyncHandler(async (_req, res) => {
    const genres = (
      await db.all("SELECT DISTINCT genre FROM pieces WHERE genre IS NOT NULL AND genre != '' ORDER BY genre")
    ).map((r) => r.genre);
    const cabinets = (
      await db.all("SELECT DISTINCT cabinet FROM pieces WHERE cabinet IS NOT NULL AND cabinet != '' ORDER BY cabinet")
    ).map((r) => r.cabinet);
    res.json({ genres, cabinets });
  }));

  app.post("/api/sync/wipe", requireApiKey, asyncHandler(async (_req, res) => {
    const result = await wipeCatalog();
    res.json({ ok: true, ...result });
  }));

  app.post("/api/web/wipe", requireWebAuth, asyncHandler(async (req, res) => {
    const confirm = String(req.body?.confirm || "").trim();
    if (confirm !== "LÖSCHEN") {
      return res.status(400).json({ error: "Bestätigung ungültig. Bitte LÖSCHEN eingeben." });
    }
    const result = await wipeCatalog();
    res.json({ ok: true, ...result });
  }));

  app.post("/api/sync/push", requireApiKey, asyncHandler(async (req, res) => {
    const pieces = req.body?.pieces || [];
    const sheets = req.body?.sheets || [];
    const tombstones = req.body?.tombstones || [];
    let passwordWarning = null;

    if (req.body?.webViewPassword) {
      const nextPassword = String(req.body.webViewPassword);
      const policyError = passwordPolicyError(nextPassword);
      if (policyError) {
        passwordWarning = policyError;
      } else {
        await setSetting("web_view_password", hashPassword(nextPassword));
      }
    }

    await db.transaction(async (tx) => {
      for (const tombstone of tombstones) {
        await applyTombstone(tx, tombstone);
      }

      for (const piece of pieces) {
        await tx.run(upsertPieceSql, pieceParams(piece));
      }

      for (const sheet of sheets) {
        await tx.run(upsertSheetSql, sheetParams(sheet));
        const blob = Buffer.from(sheet.contentBase64 || "", "base64");
        if (blob.length > 0) {
          await sheetVault.put(sheet.syncUid, blob);
        }
      }

      const sheetsByPiece = new Map();
      for (const sheet of sheets) {
        if (!sheetsByPiece.has(sheet.pieceSyncUid)) {
          sheetsByPiece.set(sheet.pieceSyncUid, new Set());
        }
        sheetsByPiece.get(sheet.pieceSyncUid).add(sheet.syncUid);
      }

      const reconcileDeletedAt = new Date().toISOString();
      for (const piece of pieces) {
        const clientSheetUids = sheetsByPiece.get(piece.syncUid) || new Set();
        const serverSheets = await tx.all("SELECT sync_uid FROM sheet_files WHERE piece_sync_uid = ?", [
          piece.syncUid,
        ]);

        for (const row of serverSheets) {
          if (!clientSheetUids.has(row.sync_uid)) {
            await sheetVault.remove(row.sync_uid);
            await tx.run("DELETE FROM sheet_files WHERE sync_uid = ?", [row.sync_uid]);
            await upsertTombstone(tx, row.sync_uid, "sheet", reconcileDeletedAt);
          }
        }
      }
    });

    res.json({
      ok: true,
      pieces: pieces.length,
      sheets: sheets.length,
      tombstones: tombstones.length,
      passwordWarning,
    });
  }));

  app.get("/api/sync/pull", requireApiKey, asyncHandler(async (req, res) => {
    const since = req.query.since ? String(req.query.since) : "";

    const pieceRows = since
      ? await db.all("SELECT * FROM pieces WHERE updated_at > ? ORDER BY updated_at", [since])
      : await db.all("SELECT * FROM pieces ORDER BY updated_at");

    const sheetRows = since
      ? await db.all(
          `SELECT sync_uid, piece_sync_uid, file_name, content_type, content_hash,
                  instrument_id, instrument_name, instrument_group_id, sort_order, updated_at
           FROM sheet_files WHERE updated_at > ? ORDER BY updated_at`,
          [since],
        )
      : await db.all(
          `SELECT sync_uid, piece_sync_uid, file_name, content_type, content_hash,
                  instrument_id, instrument_name, instrument_group_id, sort_order, updated_at
           FROM sheet_files ORDER BY updated_at`,
        );

    const tombstoneRows = since
      ? await db.all(
          "SELECT sync_uid, entity_type, deleted_at FROM sync_tombstones WHERE deleted_at > ? ORDER BY deleted_at",
          [since],
        )
      : await db.all("SELECT sync_uid, entity_type, deleted_at FROM sync_tombstones ORDER BY deleted_at");

    res.json({
      serverTime: new Date().toISOString(),
      pieces: pieceRows.map((row) => mapPieceRow(row)),
      sheets: await Promise.all(
        sheetRows.map(async (row) => {
          const blob = await sheetVault.getForDesktop(row.sync_uid);
          return {
            ...mapSheetMeta(row),
            contentBase64: blob ? blob.toString("base64") : "",
          };
        }),
      ),
      tombstones: tombstoneRows.map((row) => ({
        syncUid: row.sync_uid,
        entityType: row.entity_type,
        deletedAt: row.deleted_at,
      })),
    });
  }));

  app.use(express.static(PUBLIC_DIR));

  app.get("*", (_req, res) => {
    res.sendFile(path.join(PUBLIC_DIR, "index.html"));
  });

  app.use((err, _req, res, _next) => {
    console.error(err);
    if (res.headersSent) {
      return;
    }
    res.status(500).json({ error: "Interner Serverfehler" });
  });

  const server = app.listen(PORT, HOST, () => {
    console.log(`MusikArchiv Server: http://${HOST}:${PORT}`);
    console.log(`Web-App:          http://localhost:${PORT}/`);
    console.log(`Datenbank:        ${db.dialect}`);
    console.log(`Notentresor:      ${sheetVault.root} (nur Desktop-Sync, kein Webzugriff)`);
    if (API_KEY) {
      console.log("API-Schlüssel aktiv (Sync-Endpunkte geschützt).");
    }
  });

  server.on("error", (err) => {
    if (err.code === "EADDRINUSE") {
      console.error(`Port ${PORT} ist bereits belegt.`);
      console.error("Eine andere Instanz läuft vermutlich schon. Beenden mit:");
      console.error(`  netstat -ano | findstr :${PORT}`);
      console.error("  taskkill /PID <PID> /F");
      console.error("Oder anderen Port wählen: set PORT=3001 && npm start");
      process.exit(1);
    }
    throw err;
  });
}

start().catch((err) => {
  console.error(err);
  process.exit(1);
});
