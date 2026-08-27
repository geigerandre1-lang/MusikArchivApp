import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const cs = fs.readFileSync(path.join(root, "Data", "AppVersion.cs"), "utf8");
const matchVersion = cs.match(/Value\s*=\s*"([^"]+)"/);
if (!matchVersion) {
  console.error("Data/AppVersion.cs hat keine Value-Konstante.");
  process.exit(1);
}

const version = matchVersion[1];
const log = fs.readFileSync(path.join(root, "CHANGELOG.md"), "utf8");
const escaped = version.replace(/\./g, "\\.");
const match = log.match(new RegExp(`## \\[${escaped}\\][\\s\\S]*?(?=\\n## \\[|$)`));
if (!match) {
  console.error(`CHANGELOG.md hat keinen Abschnitt ## [${version}].`);
  process.exit(1);
}

const sha = process.env.GITHUB_SHA ? process.env.GITHUB_SHA.slice(0, 7) : "";
let notes = `${match[0].trim()}

**Windows (x64):** \`MusikArchivApp-portable-win-x64-v*.zip\` — entpacken, \`MusikArchivApp.exe\` starten, kein Installer. Ordner \`data/\` nicht aus dem ZIP übernehmen.
**GitHub:** Tag \`v${version}\` (Reiter Latest) und Rolling-Tag \`latest\` mit denselben Dateien.`;
if (sha) notes += `\nCommit: ${sha}`;

process.stdout.write(`${notes}\n`);
