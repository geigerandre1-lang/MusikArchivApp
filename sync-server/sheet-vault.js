const fs = require("fs");
const path = require("path");
const { hostingerDataDir, defaultSqlitePath } = require("./db");

function persistentDataDir() {
  const hosted = hostingerDataDir();
  if (hosted) {
    return hosted;
  }
  return path.dirname(defaultSqlitePath());
}

function ensureDir(dir) {
  fs.mkdirSync(dir, { recursive: true });
}

function blobPath(root, syncUid) {
  return path.join(root, `${syncUid}.bin`);
}

function writeAtomic(filePath, buffer) {
  const tmp = `${filePath}.tmp`;
  fs.writeFileSync(tmp, buffer);
  fs.renameSync(tmp, filePath);
}

function openSheetVault() {
  const root = path.join(persistentDataDir(), "sheets-vault");
  ensureDir(root);

  return {
    root,
    async put(syncUid, buffer) {
      if (!syncUid || !Buffer.isBuffer(buffer) || buffer.length === 0) {
        return;
      }
      writeAtomic(blobPath(root, syncUid), buffer);
    },
    async getForDesktop(syncUid) {
      if (!syncUid) {
        return null;
      }
      const file = blobPath(root, syncUid);
      try {
        if (fs.existsSync(file)) {
          return fs.readFileSync(file);
        }
      } catch {
        return null;
      }
      return null;
    },
    async remove(syncUid) {
      if (!syncUid) {
        return;
      }
      try {
        fs.unlinkSync(blobPath(root, syncUid));
      } catch {
        /* ignore */
      }
    },
  };
}

async function migrateBlobsFromCatalog(db, vault) {
  const rows = await db.all("SELECT sync_uid, file_data FROM sheet_files");
  let moved = 0;
  for (const row of rows) {
    const buffer = Buffer.isBuffer(row.file_data)
      ? row.file_data
      : row.file_data
        ? Buffer.from(row.file_data)
        : Buffer.alloc(0);
    if (buffer.length === 0) {
      continue;
    }
    await vault.put(row.sync_uid, buffer);
    await db.run("UPDATE sheet_files SET file_data = ? WHERE sync_uid = ?", [Buffer.alloc(0), row.sync_uid]);
    moved += 1;
  }
  if (moved > 0) {
    console.log(`sheets-vault: ${moved} Notendateien aus der Web-Katalog-DB in den Desktop-Tresor verschoben.`);
  }
}

module.exports = {
  migrateBlobsFromCatalog,
  openSheetVault,
  persistentDataDir,
};
