function env(...names) {
  for (const name of names) {
    if (!name) {
      continue;
    }
    const raw = process.env[name];
    if (raw == null) {
      continue;
    }
    const value = String(raw).trim();
    if (value) {
      return value;
    }
  }
  return "";
}

function isLocalMysqlHost(host) {
  const value = host.trim().toLowerCase();
  return value === "localhost" || value === "127.0.0.1" || value === "::1";
}

function stripPasswordWrapper(raw) {
  let value = String(raw ?? "");
  if (
    (value.startsWith('"') && value.endsWith('"') && value.length >= 2) ||
    (value.startsWith("'") && value.endsWith("'") && value.length >= 2)
  ) {
    value = value.slice(1, -1);
  }
  return value.replace(/\r?\n$/, "");
}

function passwordFromBase64() {
  const b64 = env("MYSQL_PASSWORD_B64", "MUSIKARCHIV_MYSQL_PASSWORD_B64");
  if (!b64) {
    return "";
  }
  try {
    return Buffer.from(b64, "base64").toString("utf8").replace(/\r?\n$/, "");
  } catch {
    return "";
  }
}

function decodeMysqlPassword() {
  return passwordFromBase64();
}

function mysqlPasswordCandidates() {
  const out = [];
  const fromB64 = passwordFromBase64();
  if (fromB64) {
    out.push(fromB64);
  }
  const aliasPlain = stripPasswordWrapper(env("MUSIKARCHIV_MYSQL_PASSWORD"));
  if (aliasPlain && !out.includes(aliasPlain)) {
    out.push(aliasPlain);
  }
  return out.filter((value) => value.length > 0);
}

function passwordLogHint(value) {
  const bytes = Buffer.byteLength(value, "utf8");
  return `len=${value.length} bytes=${bytes} $=${value.includes("$") ? "yes" : "no"} #=${value.includes("#") ? "yes" : "no"} @=${value.includes("@") ? "yes" : "no"}`;
}

function maskSecret(value) {
  if (!value) {
    return "(leer)";
  }
  return "*".repeat(Math.min(12, Math.max(8, value.length)));
}

function mysqlConfigFromEnv() {
  const requestedHost = env("MYSQL_HOST", "MUSIKARCHIV_MYSQL_HOST");
  const user = env("MYSQL_USER", "MUSIKARCHIV_MYSQL_USER");
  const database = env("MYSQL_DATABASE", "MUSIKARCHIV_MYSQL_DATABASE");
  if (!requestedHost || !user || !database) {
    return null;
  }
  const portRaw = env("MYSQL_PORT", "MUSIKARCHIV_MYSQL_PORT");
  const port = portRaw ? Number(portRaw) : 3306;
  const sslRaw = env("MYSQL_SSL", "MUSIKARCHIV_MYSQL_SSL").toLowerCase();
  const sslForced = sslRaw === "force";
  const sslRequested = sslForced || sslRaw === "1" || sslRaw === "true" || sslRaw === "yes";
  const local = isLocalMysqlHost(requestedHost);
  const host = requestedHost === "127.0.0.1" || requestedHost === "::1" ? "localhost" : requestedHost;
  return {
    host,
    port: Number.isInteger(port) && port > 0 ? port : 3306,
    user,
    password: decodeMysqlPassword(),
    database,
    ssl: local && !sslForced ? false : sslRequested,
  };
}

function mysqlSocketCandidates() {
  const fromEnv = env("MYSQL_SOCKET", "MUSIKARCHIV_MYSQL_SOCKET");
  const paths = [
    fromEnv,
    "/var/run/mysqld/mysqld.sock",
    "/run/mysqld/mysqld.sock",
    "/tmp/mysql.sock",
    "/var/lib/mysql/mysql.sock",
  ].filter(Boolean);
  return [...new Set(paths)];
}

function mysqlConfigured() {
  return mysqlConfigFromEnv() != null;
}

module.exports = {
  decodeMysqlPassword,
  env,
  isLocalMysqlHost,
  maskSecret,
  mysqlConfigFromEnv,
  mysqlConfigured,
  mysqlPasswordCandidates,
  mysqlSocketCandidates,
  passwordLogHint,
};
