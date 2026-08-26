const path = require("path");
const { importSqliteInto, mysqlConfigured, openDatabase, sqliteImportCandidates } = require("./db");

async function main() {
  const sqlitePath = process.argv[2] || sqliteImportCandidates().find((candidate) => {
    try {
      return require("fs").existsSync(candidate);
    } catch {
      return false;
    }
  });

  if (!mysqlConfigured()) {
    console.error("MySQL ist nicht konfiguriert. MYSQL_HOST, MYSQL_USER, MYSQL_DATABASE und MYSQL_PASSWORD_B64 setzen.");
    process.exit(1);
  }
  if (!sqlitePath) {
    console.error("Keine SQLite-Datei gefunden. Aufruf: node migrate-sqlite.js [pfad/zu/sync.db]");
    process.exit(1);
  }

  const db = await openDatabase();
  await importSqliteInto(db, path.resolve(sqlitePath));
  await db.close();
  console.log("Import abgeschlossen.");
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
