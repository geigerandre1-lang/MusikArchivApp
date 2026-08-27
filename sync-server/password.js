const bcrypt = require("bcryptjs");
const crypto = require("crypto");

const MIN_LENGTH = 14;
const BCRYPT_ROUNDS = 12;

function passwordPolicyError(password) {
  const value = String(password ?? "");
  if (value.length < MIN_LENGTH) {
    return `Das Web-Passwort muss mindestens ${MIN_LENGTH} Zeichen haben.`;
  }
  if (!/[A-Z]/.test(value)) {
    return "Das Web-Passwort braucht mindestens einen Großbuchstaben.";
  }
  if (!/[a-z]/.test(value)) {
    return "Das Web-Passwort braucht mindestens einen Kleinbuchstaben.";
  }
  if (!/[^A-Za-z0-9]/.test(value)) {
    return "Das Web-Passwort braucht mindestens ein Sonderzeichen.";
  }
  return null;
}

function isBcryptHash(value) {
  return typeof value === "string" && /^\$2[aby]\$\d{2}\$[./A-Za-z0-9]{53}$/.test(value);
}

function hashPassword(plain) {
  const error = passwordPolicyError(plain);
  if (error) {
    throw new Error(error);
  }
  return bcrypt.hashSync(String(plain), BCRYPT_ROUNDS);
}

function safeEqual(left, right) {
  const a = Buffer.from(String(left ?? ""), "utf8");
  const b = Buffer.from(String(right ?? ""), "utf8");
  if (a.length !== b.length || a.length === 0) {
    return false;
  }
  return crypto.timingSafeEqual(a, b);
}

async function verifyPassword(plain, stored) {
  const candidate = String(plain ?? "");
  const secret = String(stored ?? "");
  if (!secret) {
    return false;
  }
  if (isBcryptHash(secret)) {
    return bcrypt.compare(candidate, secret);
  }
  return safeEqual(candidate, secret);
}

module.exports = {
  MIN_LENGTH,
  hashPassword,
  isBcryptHash,
  passwordPolicyError,
  verifyPassword,
};
