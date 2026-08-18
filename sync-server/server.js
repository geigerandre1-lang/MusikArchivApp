const express = require('express');
const fs = require('fs');
const path = require('path');
const Database = require('better-sqlite3');

const PORT = process.env.PORT || 3000;
const API_KEY = process.env.SYNC_API_KEY || '';
const DATA_DIR = path.join(__dirname, 'data');
const DB_PATH = path.join(DATA_DIR, 'sync.db');
const PUBLIC_DIR = path.join(__dirname, 'public');

fs.mkdirSync(DATA_DIR, { recursive: true });

const db = new Database(DB_PATH);
db.pragma('journal_mode = WAL');

db.exec(`
CREATE TABLE IF NOT EXISTS pieces (
  sync_uid TEXT PRIMARY KEY,
  title TEXT NOT NULL,
  composer TEXT,
  arranger TEXT,
  publisher TEXT,
  isbn TEXT,
  tags TEXT,
  genre TEXT,
  cabinet TEXT,
  compartment TEXT,
  slot TEXT,
  is_active INTEGER NOT NULL DEFAULT 1,
  folder_path TEXT,
  instrument_names_json TEXT NOT NULL DEFAULT '[]',
  updated_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS sheet_files (
  sync_uid TEXT PRIMARY KEY,
  piece_sync_uid TEXT NOT NULL,
  file_name TEXT NOT NULL,
  content_type TEXT,
  content_hash TEXT NOT NULL,
  file_data BLOB NOT NULL,
  instrument_id INTEGER,
  instrument_name TEXT,
  instrument_group_id INTEGER,
  sort_order INTEGER NOT NULL DEFAULT 0,
  updated_at TEXT NOT NULL,
  FOREIGN KEY (piece_sync_uid) REFERENCES pieces(sync_uid) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS server_settings (
  key TEXT PRIMARY KEY,
  value TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS sync_tombstones (
  sync_uid TEXT PRIMARY KEY,
  entity_type TEXT NOT NULL,
  deleted_at TEXT NOT NULL
);
`);

const DEFAULT_WEB_PASSWORD = 'admin';
const webSessions = new Map();

function getSetting(key, fallback = '') {
  const row = db.prepare('SELECT value FROM server_settings WHERE key = ?').get(key);
  return row?.value ?? fallback;
}

function setSetting(key, value) {
  db.prepare(`
    INSERT INTO server_settings (key, value) VALUES (?, ?)
    ON CONFLICT(key) DO UPDATE SET value = excluded.value
  `).run(key, value);
}

function getWebViewPassword() {
  return getSetting('web_view_password', DEFAULT_WEB_PASSWORD);
}

function createSessionToken() {
  const token = require('crypto').randomBytes(32).toString('hex');
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
  const auth = req.header('authorization');
  if (auth && auth.startsWith('Bearer ')) {
    return auth.slice(7);
  }
  if (req.query.token) {
    return String(req.query.token);
  }
  return null;
}

function requireWebAuth(req, res, next) {
  const token = extractWebToken(req);
  if (!isValidSession(token)) {
    return res.status(401).json({ error: 'Nicht angemeldet' });
  }
  next();
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
    isActive: row.is_active === 1,
    folderPath: row.folder_path,
    instrumentNames: JSON.parse(row.instrument_names_json || '[]'),
    updatedAt: row.updated_at,
    sheetCount: sheetCount ?? 0
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
    updatedAt: row.updated_at
  };
}

function requireApiKey(req, res, next) {
  if (API_KEY && req.header('x-api-key') !== API_KEY) {
    return res.status(401).json({ error: 'Unauthorized' });
  }
  next();
}

const app = express();
app.use(express.json({ limit: '200mb' }));

app.get('/api/health', (_req, res) => {
  res.json({ ok: true, version: '1' });
});

app.post('/api/auth/login', (req, res) => {
  const password = String(req.body?.password ?? '');
  if (password !== getWebViewPassword()) {
    return res.status(401).json({ error: 'Ungültiges Passwort' });
  }

  const token = createSessionToken();
  res.json({ ok: true, token });
});

app.post('/api/auth/logout', (req, res) => {
  const token = extractWebToken(req);
  if (token) {
    webSessions.delete(token);
  }
  res.json({ ok: true });
});

app.get('/api/auth/status', (req, res) => {
  const token = extractWebToken(req);
  res.json({ authenticated: isValidSession(token) });
});

app.get('/api/pieces', requireWebAuth, (req, res) => {
  const q = String(req.query.q || '').trim().toLowerCase();
  const genre = String(req.query.genre || '').trim();
  const cabinet = String(req.query.cabinet || '').trim();
  const withScores = req.query.withScores === '1';
  const activeOnly = req.query.activeOnly === '1';

  let sql = `
    SELECT p.*, COUNT(sf.sync_uid) AS sheet_count
    FROM pieces p
    LEFT JOIN sheet_files sf ON sf.piece_sync_uid = p.sync_uid
    WHERE 1=1
  `;
  const params = [];

  if (activeOnly) {
    sql += ' AND p.is_active = 1';
  }

  if (genre) {
    sql += ' AND p.genre = ?';
    params.push(genre);
  }
  if (cabinet) {
    sql += ' AND p.cabinet = ?';
    params.push(cabinet);
  }
  if (withScores) {
    sql += ' AND EXISTS (SELECT 1 FROM sheet_files sf2 WHERE sf2.piece_sync_uid = p.sync_uid)';
  }

  sql += ' GROUP BY p.sync_uid ORDER BY p.title COLLATE NOCASE';

  const rows = db.prepare(sql).all(...params);
  let pieces = rows.map((row) => mapPieceRow(row, row.sheet_count));

  if (q) {
    pieces = pieces.filter((piece) => {
      const haystack = [
        piece.title,
        piece.composer,
        piece.arranger,
        piece.tags,
        piece.folderPath,
        piece.instrumentNames.join(' ')
      ]
        .join(' ')
        .toLowerCase();
      return haystack.includes(q);
    });
  }

  res.json({ pieces });
});

app.get('/api/pieces/:syncUid', requireWebAuth, (req, res) => {
  const row = db.prepare('SELECT * FROM pieces WHERE sync_uid = ?').get(req.params.syncUid);
  if (!row) {
    return res.status(404).json({ error: 'Stück nicht gefunden' });
  }

  const sheets = db
    .prepare(
      `SELECT sync_uid, piece_sync_uid, file_name, content_type, content_hash,
              instrument_id, instrument_name, instrument_group_id, sort_order, updated_at
       FROM sheet_files
       WHERE piece_sync_uid = ?
       ORDER BY sort_order, file_name`
    )
    .all(req.params.syncUid)
    .map(mapSheetMeta);

  res.json({
    piece: mapPieceRow(row, sheets.length),
    sheets
  });
});

app.get('/api/sheets/:syncUid/file', requireWebAuth, (req, res) => {
  const row = db
    .prepare('SELECT file_name, content_type, file_data FROM sheet_files WHERE sync_uid = ?')
    .get(req.params.syncUid);

  if (!row) {
    return res.status(404).json({ error: 'Datei nicht gefunden' });
  }

  const contentType = row.content_type || guessContentType(row.file_name);
  res.setHeader('Content-Type', contentType);
  res.setHeader('Content-Disposition', `inline; filename="${encodeURIComponent(row.file_name)}"`);
  res.send(row.file_data);
});

app.get('/api/meta/filters', requireWebAuth, (_req, res) => {
  const genres = db
    .prepare("SELECT DISTINCT genre FROM pieces WHERE genre IS NOT NULL AND genre != '' ORDER BY genre")
    .all()
    .map((r) => r.genre);
  const cabinets = db
    .prepare("SELECT DISTINCT cabinet FROM pieces WHERE cabinet IS NOT NULL AND cabinet != '' ORDER BY cabinet")
    .all()
    .map((r) => r.cabinet);

  res.json({ genres, cabinets });
});

function applyTombstone(tombstone) {
  const syncUid = tombstone.syncUid;
  const entityType = String(tombstone.entityType || '').toLowerCase();
  const deletedAt = tombstone.deletedAt || new Date().toISOString();

  if (!syncUid) {
    return;
  }

  if (entityType === 'sheet') {
    db.prepare('DELETE FROM sheet_files WHERE sync_uid = ?').run(syncUid);
  } else if (entityType === 'piece') {
    db.prepare('DELETE FROM sheet_files WHERE piece_sync_uid = ?').run(syncUid);
    db.prepare('DELETE FROM pieces WHERE sync_uid = ?').run(syncUid);
  }

  db.prepare(`
    INSERT INTO sync_tombstones (sync_uid, entity_type, deleted_at)
    VALUES (?, ?, ?)
    ON CONFLICT(sync_uid) DO UPDATE SET
      entity_type = excluded.entity_type,
      deleted_at = excluded.deleted_at
  `).run(syncUid, entityType, deletedAt);
}

app.post('/api/sync/push', requireApiKey, (req, res) => {
  const pieces = req.body?.pieces || [];
  const sheets = req.body?.sheets || [];
  const tombstones = req.body?.tombstones || [];

  if (req.body?.webViewPassword) {
    setSetting('web_view_password', String(req.body.webViewPassword));
  }

  const upsertPiece = db.prepare(`
    INSERT INTO pieces (
      sync_uid, title, composer, arranger, publisher, isbn, tags, genre,
      cabinet, compartment, slot, is_active, folder_path, instrument_names_json, updated_at
    ) VALUES (
      @syncUid, @title, @composer, @arranger, @publisher, @isbn, @tags, @genre,
      @cabinet, @compartment, @slot, @isActive, @folderPath, @instrumentNamesJson, @updatedAt
    )
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
    WHERE excluded.updated_at >= pieces.updated_at
  `);

  const upsertSheet = db.prepare(`
    INSERT INTO sheet_files (
      sync_uid, piece_sync_uid, file_name, content_type, content_hash, file_data,
      instrument_id, instrument_name, instrument_group_id, sort_order, updated_at
    ) VALUES (
      @syncUid, @pieceSyncUid, @fileName, @contentType, @contentHash, @fileData,
      @instrumentId, @instrumentName, @instrumentGroupId, @sortOrder, @updatedAt
    )
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
    WHERE excluded.updated_at >= sheet_files.updated_at
  `);

  const tx = db.transaction(() => {
    for (const tombstone of tombstones) {
      applyTombstone(tombstone);
    }

    for (const piece of pieces) {
      upsertPiece.run({
        syncUid: piece.syncUid,
        title: piece.title,
        composer: piece.composer || '',
        arranger: piece.arranger || '',
        publisher: piece.publisher || '',
        isbn: piece.isbn || '',
        tags: piece.tags || '',
        genre: piece.genre || '',
        cabinet: piece.cabinet || '',
        compartment: piece.compartment || '',
        slot: piece.slot || '',
        isActive: piece.isActive ? 1 : 0,
        folderPath: piece.folderPath || '',
        instrumentNamesJson: JSON.stringify(piece.instrumentNames || []),
        updatedAt: piece.updatedAt
      });
    }

    for (const sheet of sheets) {
      const bytes = Buffer.from(sheet.contentBase64 || '', 'base64');
      upsertSheet.run({
        syncUid: sheet.syncUid,
        pieceSyncUid: sheet.pieceSyncUid,
        fileName: sheet.fileName,
        contentType: sheet.contentType || '',
        contentHash: sheet.contentHash || '',
        fileData: bytes,
        instrumentId: sheet.instrumentId ?? null,
        instrumentName: sheet.instrumentName || '',
        instrumentGroupId: sheet.instrumentGroupId ?? null,
        sortOrder: sheet.sortOrder || 0,
        updatedAt: sheet.updatedAt
      });
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
      const serverSheets = db
        .prepare('SELECT sync_uid FROM sheet_files WHERE piece_sync_uid = ?')
        .all(piece.syncUid);

      for (const row of serverSheets) {
        if (!clientSheetUids.has(row.sync_uid)) {
          db.prepare('DELETE FROM sheet_files WHERE sync_uid = ?').run(row.sync_uid);
          db.prepare(`
            INSERT INTO sync_tombstones (sync_uid, entity_type, deleted_at)
            VALUES (?, 'sheet', ?)
            ON CONFLICT(sync_uid) DO UPDATE SET
              entity_type = excluded.entity_type,
              deleted_at = excluded.deleted_at
          `).run(row.sync_uid, reconcileDeletedAt);
        }
      }
    }
  });

  tx();
  res.json({ ok: true, pieces: pieces.length, sheets: sheets.length, tombstones: tombstones.length });
});

app.get('/api/sync/pull', requireApiKey, (req, res) => {
  const since = req.query.since ? String(req.query.since) : '';

  const pieceRows = since
    ? db.prepare('SELECT * FROM pieces WHERE updated_at > ? ORDER BY updated_at').all(since)
    : db.prepare('SELECT * FROM pieces ORDER BY updated_at').all();

  const sheetRows = since
    ? db.prepare('SELECT * FROM sheet_files WHERE updated_at > ? ORDER BY updated_at').all(since)
    : db.prepare('SELECT * FROM sheet_files ORDER BY updated_at').all();

  const tombstoneRows = since
    ? db.prepare('SELECT sync_uid, entity_type, deleted_at FROM sync_tombstones WHERE deleted_at > ? ORDER BY deleted_at').all(since)
    : db.prepare('SELECT sync_uid, entity_type, deleted_at FROM sync_tombstones ORDER BY deleted_at').all();

  res.json({
    serverTime: new Date().toISOString(),
    pieces: pieceRows.map((row) => mapPieceRow(row)),
    sheets: sheetRows.map((row) => ({
      ...mapSheetMeta(row),
      contentBase64: Buffer.from(row.file_data).toString('base64')
    })),
    tombstones: tombstoneRows.map((row) => ({
      syncUid: row.sync_uid,
      entityType: row.entity_type,
      deletedAt: row.deleted_at
    }))
  });
});

app.use(express.static(PUBLIC_DIR));

app.get('*', (_req, res) => {
  res.sendFile(path.join(PUBLIC_DIR, 'index.html'));
});

function guessContentType(fileName) {
  const ext = path.extname(fileName).toLowerCase();
  switch (ext) {
    case '.pdf':
      return 'application/pdf';
    case '.png':
      return 'image/png';
    case '.jpg':
    case '.jpeg':
      return 'image/jpeg';
    case '.tif':
    case '.tiff':
      return 'image/tiff';
    case '.bmp':
      return 'image/bmp';
    default:
      return 'application/octet-stream';
  }
}

const server = app.listen(PORT, () => {
  console.log(`MusikArchiv Server: http://localhost:${PORT}`);
  console.log(`Web-App:          http://localhost:${PORT}/`);
  if (API_KEY) {
    console.log('API-Schlüssel aktiv (Sync-Endpunkte geschützt).');
  }
});

server.on('error', (err) => {
  if (err.code === 'EADDRINUSE') {
    console.error(`Port ${PORT} ist bereits belegt.`);
    console.error('Eine andere Instanz läuft vermutlich schon. Beenden mit:');
    console.error(`  netstat -ano | findstr :${PORT}`);
    console.error('  taskkill /PID <PID> /F');
    console.error(`Oder anderen Port wählen: set PORT=3001 && npm start`);
    process.exit(1);
  }

  throw err;
});
