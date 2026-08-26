const fs = require("fs");
const path = require("path");
const {
  env,
  isLocalMysqlHost,
  maskSecret,
  mysqlConfigFromEnv,
  mysqlConfigured,
  mysqlPasswordCandidates,
  mysqlSocketCandidates,
  passwordLogHint,
} = require("./mysql-config");

const SQLITE_SCHEMA = `
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
`;

const MYSQL_SCHEMA = `
CREATE TABLE IF NOT EXISTS pieces (
  sync_uid VARCHAR(64) PRIMARY KEY,
  title VARCHAR(255) NOT NULL,
  composer VARCHAR(255),
  arranger VARCHAR(255),
  publisher VARCHAR(255),
  isbn VARCHAR(64),
  tags TEXT,
  genre VARCHAR(191),
  cabinet VARCHAR(191),
  compartment VARCHAR(191),
  slot VARCHAR(191),
  is_active TINYINT NOT NULL DEFAULT 1,
  folder_path TEXT,
  instrument_names_json MEDIUMTEXT NOT NULL,
  updated_at VARCHAR(64) NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS sheet_files (
  sync_uid VARCHAR(64) PRIMARY KEY,
  piece_sync_uid VARCHAR(64) NOT NULL,
  file_name VARCHAR(255) NOT NULL,
  content_type VARCHAR(191),
  content_hash VARCHAR(128) NOT NULL,
  file_data LONGBLOB NOT NULL,
  instrument_id INT,
  instrument_name VARCHAR(191),
  instrument_group_id INT,
  sort_order INT NOT NULL DEFAULT 0,
  updated_at VARCHAR(64) NOT NULL,
  INDEX idx_sheet_piece (piece_sync_uid)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS server_settings (
  \`key\` VARCHAR(191) PRIMARY KEY,
  value TEXT NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS sync_tombstones (
  sync_uid VARCHAR(64) PRIMARY KEY,
  entity_type VARCHAR(32) NOT NULL,
  deleted_at VARCHAR(64) NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
`;

function hostingerDataDir(cwd = process.cwd()) {
  let dir = path.resolve(cwd);
  const { root } = path.parse(dir);
  while (true) {
    if (path.basename(dir).toLowerCase() === "hbuilds") {
      return path.join(path.dirname(dir), "data");
    }
    const parent = path.dirname(dir);
    if (parent === dir || dir === root) {
      break;
    }
    dir = parent;
  }
  return null;
}

function defaultSqlitePath(cwd = process.cwd()) {
  const fromEnv = env("MUSIKARCHIV_DB", "SYNC_DB");
  if (fromEnv) {
    return fromEnv;
  }
  const hosted = hostingerDataDir(cwd);
  if (hosted) {
    return path.join(hosted, "sync.db");
  }
  return path.join(__dirname, "data", "sync.db");
}

function sqliteImportCandidates() {
  const fromEnv = env("MUSIKARCHIV_SQLITE_IMPORT");
  const hosted = hostingerDataDir();
  return [
    fromEnv,
    path.join(__dirname, "data", "sync.db"),
    hosted ? path.join(hosted, "sync.db") : "",
    path.join(process.cwd(), "data", "sync.db"),
    path.join(process.cwd(), "sync.db"),
  ].filter(Boolean);
}

function createSqliteAdapter(raw) {
  const adapter = {
    dialect: "sqlite",
    async get(sql, params = []) {
      return raw.prepare(sql).get(...params);
    },
    async all(sql, params = []) {
      return raw.prepare(sql).all(...params);
    },
    async run(sql, params = []) {
      return raw.prepare(sql).run(...params);
    },
    async exec(sql) {
      raw.exec(sql);
    },
    async transaction(fn) {
      raw.exec("BEGIN IMMEDIATE");
      try {
        const result = await fn(adapter);
        raw.exec("COMMIT");
        return result;
      } catch (err) {
        try {
          raw.exec("ROLLBACK");
        } catch {
          /* ignore */
        }
        throw err;
      }
    },
    close() {
      raw.close();
    },
  };
  return adapter;
}

function createMysqlAdapter(client) {
  async function query(sql, params = []) {
    const [rows] = await client.query(sql, params);
    return rows;
  }

  const adapter = {
    dialect: "mysql",
    async get(sql, params = []) {
      const rows = await query(sql, params);
      return Array.isArray(rows) ? rows[0] : undefined;
    },
    async all(sql, params = []) {
      const rows = await query(sql, params);
      return Array.isArray(rows) ? rows : [];
    },
    async run(sql, params = []) {
      const header = await query(sql, params);
      return { changes: Number(header?.affectedRows ?? 0) };
    },
    async exec(sql) {
      const parts = String(sql)
        .split(/;\s*(?=\S)/)
        .map((part) => part.trim())
        .filter(Boolean);
      for (const part of parts) {
        await client.query(part);
      }
    },
    async transaction(fn) {
      if (typeof client.getConnection !== "function") {
        await client.beginTransaction();
        try {
          const result = await fn(adapter);
          await client.commit();
          return result;
        } catch (err) {
          await client.rollback();
          throw err;
        }
      }

      const conn = await client.getConnection();
      const tx = createMysqlAdapter(conn);
      try {
        await conn.beginTransaction();
        const result = await fn(tx);
        await conn.commit();
        return result;
      } catch (err) {
        try {
          await conn.rollback();
        } catch {
          /* ignore */
        }
        throw err;
      } finally {
        conn.release();
      }
    },
    async close() {
      if (typeof client.end === "function") {
        await client.end();
      }
    },
  };
  return adapter;
}

let mysqlLogged = false;

function logMysqlOnce(config) {
  if (mysqlLogged) {
    return;
  }
  mysqlLogged = true;
  const via = config.socketPath
    ? `socket=${config.socketPath}`
    : `host=${config.host} port=${config.port}`;
  console.log(
    `mysql: ${via} user=${config.user} database=${config.database} password=${maskSecret(config.password)} ${passwordLogHint(config.password)} ssl=${config.ssl ? "on" : "off"}`,
  );
}

function poolOptions(attempt) {
  const opts = {
    user: attempt.user,
    password: attempt.password,
    database: attempt.database,
    waitForConnections: true,
    connectionLimit: 8,
    charset: "utf8mb4",
    supportBigNumbers: true,
    bigNumberStrings: false,
  };
  if (attempt.socketPath) {
    opts.socketPath = attempt.socketPath;
    return opts;
  }
  opts.host = attempt.host;
  opts.port = attempt.port;
  if (attempt.ssl) {
    opts.ssl = { rejectUnauthorized: false };
  }
  return opts;
}

async function openMysqlPool() {
  const mysql = require("mysql2/promise");
  const config = mysqlConfigFromEnv();
  if (!config) {
    throw new Error(
      "MySQL ist nicht konfiguriert. MYSQL_HOST, MYSQL_USER und MYSQL_DATABASE in hPanel setzen.",
    );
  }

  const passwords = mysqlPasswordCandidates();
  if (!passwords.length) {
    throw new Error(
      "MySQL-Passwort fehlt. MYSQL_PASSWORD_B64 in hPanel setzen (UTF-8, Base64).",
    );
  }

  const local = isLocalMysqlHost(config.host);
  const attempts = [];

  if (local) {
    const sockets = mysqlSocketCandidates().filter((file) => {
      try {
        return fs.existsSync(file);
      } catch {
        return false;
      }
    });
    const socketList = sockets.length ? sockets : mysqlSocketCandidates();
    for (const socketPath of socketList) {
      for (const password of passwords) {
        attempts.push({
          host: "localhost",
          port: config.port,
          user: config.user,
          password,
          database: config.database,
          ssl: false,
          socketPath,
        });
      }
    }
  } else {
    const sslForced = env("MYSQL_SSL", "MUSIKARCHIV_MYSQL_SSL").toLowerCase() === "force";
    const ssls = sslForced ? [true] : [...new Set([config.ssl, false])];
    for (const ssl of ssls) {
      for (const password of passwords) {
        attempts.push({
          host: config.host,
          port: config.port,
          user: config.user,
          password,
          database: config.database,
          ssl,
        });
      }
    }
  }

  let lastError = "unbekannt";
  for (const attempt of attempts) {
    if (!mysqlLogged) {
      logMysqlOnce({ ...config, ...attempt });
    } else {
      const via = attempt.socketPath ? `socket=${attempt.socketPath}` : `host=${attempt.host}`;
      console.warn(
        `mysql: neuer Versuch ${via} ssl=${attempt.ssl ? "on" : "off"} password=${maskSecret(attempt.password)}`,
      );
    }
    const pool = mysql.createPool(poolOptions(attempt));
    try {
      await pool.query("SELECT 1");
      return pool;
    } catch (err) {
      lastError = err && err.message ? String(err.message) : String(err);
      try {
        await pool.end();
      } catch {
        /* ignore */
      }
    }
  }

  if (/access denied/i.test(lastError)) {
    throw new Error(
      "MySQL hat den User erkannt, aber das Passwort abgelehnt. " +
        "Bitte das Passwort des Datenbank-Users aus hPanel → Datenbanken verwenden (nicht das Hosting-Passwort). " +
        "Passwort als UTF-8 Base64 in MYSQL_PASSWORD_B64 setzen (kein Klartext-MYSQL_PASSWORD). " +
        lastError,
    );
  }
  throw new Error(`MySQL-Verbindung fehlgeschlagen: ${lastError}`);
}

function tryOpenSqliteFile(filePath, readonly = false) {
  let Database;
  try {
    Database = require("better-sqlite3");
  } catch (err) {
    throw new Error(
      "better-sqlite3 ist nicht verfügbar. Für Hostinger MYSQL_HOST, MYSQL_USER, MYSQL_DATABASE und MYSQL_PASSWORD_B64 setzen; lokal optionalDependencies installieren. " +
        (err && err.message ? err.message : ""),
    );
  }
  return new Database(filePath, readonly ? { readonly: true, fileMustExist: true } : undefined);
}

async function importSqliteInto(db, sqlitePath) {
  const sqlite = tryOpenSqliteFile(sqlitePath, true);
  try {
    const pieces = sqlite.prepare("SELECT * FROM pieces").all();
    const sheets = sqlite.prepare("SELECT * FROM sheet_files").all();
    const settings = sqlite.prepare("SELECT * FROM server_settings").all();
    const tombstones = sqlite.prepare("SELECT * FROM sync_tombstones").all();

    await db.transaction(async (tx) => {
      for (const row of pieces) {
        await tx.run(
          `INSERT INTO pieces (
            sync_uid, title, composer, arranger, publisher, isbn, tags, genre,
            cabinet, compartment, slot, is_active, folder_path, instrument_names_json, updated_at
          ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
          ON DUPLICATE KEY UPDATE
            title = VALUES(title), composer = VALUES(composer), arranger = VALUES(arranger),
            publisher = VALUES(publisher), isbn = VALUES(isbn), tags = VALUES(tags),
            genre = VALUES(genre), cabinet = VALUES(cabinet), compartment = VALUES(compartment),
            slot = VALUES(slot), is_active = VALUES(is_active), folder_path = VALUES(folder_path),
            instrument_names_json = VALUES(instrument_names_json), updated_at = VALUES(updated_at)`,
          [
            row.sync_uid,
            row.title,
            row.composer,
            row.arranger,
            row.publisher,
            row.isbn,
            row.tags,
            row.genre,
            row.cabinet,
            row.compartment,
            row.slot,
            row.is_active,
            row.folder_path,
            row.instrument_names_json,
            row.updated_at,
          ],
        );
      }

      for (const row of sheets) {
        await tx.run(
          `INSERT INTO sheet_files (
            sync_uid, piece_sync_uid, file_name, content_type, content_hash, file_data,
            instrument_id, instrument_name, instrument_group_id, sort_order, updated_at
          ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
          ON DUPLICATE KEY UPDATE
            piece_sync_uid = VALUES(piece_sync_uid), file_name = VALUES(file_name),
            content_type = VALUES(content_type), content_hash = VALUES(content_hash),
            file_data = VALUES(file_data), instrument_id = VALUES(instrument_id),
            instrument_name = VALUES(instrument_name), instrument_group_id = VALUES(instrument_group_id),
            sort_order = VALUES(sort_order), updated_at = VALUES(updated_at)`,
          [
            row.sync_uid,
            row.piece_sync_uid,
            row.file_name,
            row.content_type,
            row.content_hash,
            row.file_data,
            row.instrument_id,
            row.instrument_name,
            row.instrument_group_id,
            row.sort_order,
            row.updated_at,
          ],
        );
      }

      for (const row of settings) {
        await tx.run(
          "INSERT INTO server_settings (`key`, value) VALUES (?, ?) ON DUPLICATE KEY UPDATE value = VALUES(value)",
          [row.key, row.value],
        );
      }

      for (const row of tombstones) {
        await tx.run(
          `INSERT INTO sync_tombstones (sync_uid, entity_type, deleted_at)
           VALUES (?, ?, ?)
           ON DUPLICATE KEY UPDATE entity_type = VALUES(entity_type), deleted_at = VALUES(deleted_at)`,
          [row.sync_uid, row.entity_type, row.deleted_at],
        );
      }
    });

    console.log(
      `mysql: SQLite-Backup importiert (${pieces.length} Stücke, ${sheets.length} Noten, ${tombstones.length} Tombstones) aus ${sqlitePath}`,
    );
  } finally {
    sqlite.close();
  }
}

async function maybeImportSqlite(db) {
  if (db.dialect !== "mysql") {
    return;
  }
  const countRow = await db.get("SELECT COUNT(*) AS n FROM pieces");
  if (Number(countRow?.n ?? 0) > 0) {
    return;
  }

  for (const candidate of sqliteImportCandidates()) {
    if (!fs.existsSync(candidate)) {
      continue;
    }
    try {
      await importSqliteInto(db, candidate);
      return;
    } catch (err) {
      console.warn(`mysql: SQLite-Import aus ${candidate} fehlgeschlagen: ${err.message}`);
    }
  }
}

async function openSqlite() {
  const dbPath = defaultSqlitePath();
  fs.mkdirSync(path.dirname(dbPath), { recursive: true });
  const raw = tryOpenSqliteFile(dbPath);
  raw.pragma("journal_mode = WAL");
  raw.exec(SQLITE_SCHEMA);
  const db = createSqliteAdapter(raw);
  console.log(`sqlite: ${dbPath}`);
  return db;
}

async function openDatabase() {
  if (mysqlConfigured()) {
    const pool = await openMysqlPool();
    const db = createMysqlAdapter(pool);
    await db.exec(MYSQL_SCHEMA);
    await maybeImportSqlite(db);
    return db;
  }
  return openSqlite();
}

module.exports = {
  defaultSqlitePath,
  hostingerDataDir,
  importSqliteInto,
  mysqlConfigured,
  openDatabase,
  sqliteImportCandidates,
};
