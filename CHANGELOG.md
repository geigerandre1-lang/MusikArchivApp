# Changelog

Alle nennenswerten Änderungen. Die Versionsnummer steht in `MusikArchivApp.csproj`, `Data/AppVersion.cs`, im Dateinamen der portable ZIP und im Fenstertitel.

**Download:** [GitHub Releases](https://github.com/geigerandre1-lang/MusikArchivApp/releases). Ein Release-Push veröffentlicht **zwei** Einträge:

- **`v…`** (Versionsnummer) — bleibt stehen, mit diesen Notes, Reiter **Latest**
- **`latest`** — dieselben Dateien, Titel `v… (aktuell)`, für den gewohnten Download-Link

Portable ZIP: `MusikArchivApp-portable-win-x64-v*.zip` — entpacken und `MusikArchivApp.exe` starten, kein Setup. Den Ordner `data/` nicht aus einem fremden ZIP übernehmen.

## [1.1.4] - 2026-08-27

### Neu

- Desktop und Web: Web-Datenbank leeren (Stücke, Notenlisten, Notentresor). Das Web-Passwort bleibt erhalten.

### Behoben

- Web-App: Filter (Suche, Gattung, Schrank, Nur mit Noten, Nur im Probelokal) und Detailansicht.

## [1.1.3] - 2026-08-27

### Geändert

- Desktop-Release zum Testen der In-App-Aktualisierung (von 1.1.2 auf 1.1.3).

## [1.1.2] - 2026-08-27

### Behoben

- In-App-Update: Das PowerShell-Skript ohne UTF-8-BOM hat unter Pfaden mit Umlauten (z. B. `Musikstückeditor`) die Dateien nicht überschrieben. Nach dem Neustart blieb die alte Version.

### Neu

- Einstellungen → Aktualisierung: Liste der GitHub-Releases, Version wählen (wie Dart-Counter).
- GitHub: versionierte Releases plus Rolling-Tag `latest` (Reiter **Latest**).

## [1.1.1] - 2026-08-26

### Geändert

- Desktop-Release zum Testen der In-App-Aktualisierung (von 1.1.0 auf 1.1.1).

## [1.1.0] - 2026-08-26

### Neu

- In-App-Aktualisierung aus GitHub Releases
- Sync-Server: MySQL auf Hostinger, SQLite lokal

### Behoben

- Duplicate-Cleanup: SQLite „database is locked“
