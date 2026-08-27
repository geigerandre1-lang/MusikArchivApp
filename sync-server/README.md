# MusikArchiv Sync-Server

Node-API und Web-Ansicht für den Musikstückeditor. Lokal läuft SQLite, auf Hostinger **MySQL** — sonst gehen die Daten bei jedem Redeploy verloren (`hbuilds/versions/...` wird gelöscht).

## Hostinger (Node.js)

| Panel-Feld | Wert |
| --- | --- |
| Node.js-Version | **18** oder höher |
| Anwendungsroot | `sync-server` |
| Eingabedatei / Startdatei | **`app.js`** |
| Build-Befehl | leer |
| Start-Befehl | `npm start` |
| **PORT** | nicht setzen — das Panel setzt den Port |

Umgebung (gleiche Datenbank-Seite, nicht Remote-Host):

```bash
SYNC_API_KEY=dein-sync-schlüssel
MYSQL_HOST=localhost
MYSQL_USER=uXXXX_musikarchiv
MYSQL_DATABASE=uXXXX_musikarchiv
MYSQL_PASSWORD_B64=ZGVpbi1kYi1wYXNzd29ydA==
```

Kein `MYSQL_PASSWORD` im Klartext — nur `MYSQL_PASSWORD_B64` (UTF-8, Base64). PowerShell:

```powershell
[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes('dein-db-passwort'))
```

`127.0.0.1` / `localhost` werden über den Unix-Socket verbunden (wie Dart-Counter). SSL lokal aus lassen.

Passwort: das des **MySQL-Users** (hPanel → Datenbanken), nicht das Hosting-Login.

Optionale Aliase: `MUSIKARCHIV_MYSQL_HOST`, `MUSIKARCHIV_MYSQL_USER`, `MUSIKARCHIV_MYSQL_DATABASE`, `MUSIKARCHIV_MYSQL_PASSWORD_B64`. Die `MYSQL_*`-Namen haben Vorrang.

Ohne Host, Benutzer und Datenbank bleibt SQLite aktiv (lokal). Auf Hostinger ohne MySQL landet die Datei unter `domains/.../data/sync.db`, nicht im Redeploy-Ordner.

## Bestehende Server-Daten übernehmen

Beim ersten Start mit leerer MySQL-Datenbank importiert der Server automatisch eine vorhandene `sync.db` (Deploy-Ordner oder `domains/.../data/sync.db`).

Manuell:

```bash
node migrate-sqlite.js /pfad/zur/sync.db
```

Zusätzlich die Desktop-App einmal **hochladen / synchronisieren** — sie ist die vollständige lokale Quelle.

## Web-App

Fußzeile mit Copyright, [Impressum](/impressum.html) und [Datenschutz](/datenschutz.html) (ohne Login erreichbar).

Noten-PDFs sind **nicht** in der Web-Ansicht: kein Anzeigen, kein Download. Es steht nur „Note … vorhanden“. Die Dateien liegen im **Notentresor** (`data/sheets-vault/`, auf Hostinger `domains/.../data/sheets-vault/`) **außerhalb** der Katalog-Datenbank. Die Web-App hat darauf keinen Zugriff. Nur die Desktop-App (API-Schlüssel) darf sichern und wiederherstellen.

Web-Passwort: mindestens 14 Zeichen, ein Großbuchstabe, ein Kleinbuchstabe, ein Sonderzeichen. Speicherung nur als bcrypt-Hash. Übertragung per HTTPS (im Panel SSL aktivieren) und POST-JSON, nicht in der URL.

## Lokal

```bash
cd sync-server
npm install
npm start
```
