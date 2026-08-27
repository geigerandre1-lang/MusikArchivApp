# Musikarchiv

Desktop-App (WPF) und Sync-Server für Notenarchiv und Stückdaten.

## Portable App (Windows)

**Download:** [GitHub Releases](https://github.com/geigerandre1-lang/MusikArchivApp/releases). Ein Release-Push veröffentlicht **zwei** Einträge:

- **`v…`** (Versionsnummer) — bleibt stehen, mit Notes aus `CHANGELOG.md`, Reiter **Latest**
- **`latest`** — dieselben Dateien, Titel `v… (aktuell)`

Datei: `MusikArchivApp-portable-win-x64-v*.zip` — entpacken, `MusikArchivApp.exe` doppelklicken, **kein Setup**. Den Ordner `data/` nicht aus einem Update-ZIP übernehmen (lokale Daten bleiben im App-Ordner).

In der App: Einstellungen → **Aktualisierung** — Releases auflisten, Version wählen, **Aktualisieren**.

Web-App: Fußzeile mit Copyright, Impressum und Datenschutz. Noten-PDFs sind in der Web-Ansicht nicht zugänglich (nur „Note … vorhanden“).

Neues Release: Version in `MusikArchivApp.csproj`, `Data/AppVersion.cs` und `sync-server/package.json` hochzählen, Abschnitt in `CHANGELOG.md` anlegen, auf `main` pushen. GitHub Actions baut die ZIP und setzt `v…` plus `latest`.

## Sync-Server

Siehe `sync-server/README.md` (Hostinger, MySQL).
