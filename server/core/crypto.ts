// Подпись лицензий и сведений об обновлении (SPEC 13.2): ECDSA P-256 / SHA-256, формат IEEE P1363
// (r‖s, 64 байта), base64. Закрытый ключ — только в переменной окружения LICENSE_PRIVATE_KEY
// (base64 от PKCS#8 DER). Открытый ключ для плагина — base64 от 64 байт X‖Y.
import { createPrivateKey, createPublicKey, generateKeyPairSync, randomInt, sign, verify, KeyObject } from "node:crypto";

export interface SignedDocument {
  payload: string;
  signature: string;
}

export class Signer {
  private readonly key: KeyObject;

  constructor(privateKeyBase64: string) {
    this.key = createPrivateKey({ key: Buffer.from(privateKeyBase64, "base64"), format: "der", type: "pkcs8" });
  }

  sign(payload: string): SignedDocument {
    const signature = sign("sha256", Buffer.from(payload, "utf8"), { key: this.key, dsaEncoding: "ieee-p1363" });
    return { payload, signature: signature.toString("base64") };
  }

  /** Открытый ключ в виде, который ждёт плагин (LicensePublicKey.FromBase64). */
  publicKeyXY(): string {
    return publicKeyXY(createPublicKey(this.key));
  }
}

export function publicKeyXY(key: KeyObject): string {
  const jwk = key.export({ format: "jwk" });
  return Buffer.concat([Buffer.from(jwk.x!, "base64url"), Buffer.from(jwk.y!, "base64url")]).toString("base64");
}

export function verifyDocument(doc: SignedDocument, publicXY: string): boolean {
  const raw = Buffer.from(publicXY, "base64");
  const key = createPublicKey({
    key: { kty: "EC", crv: "P-256", x: raw.subarray(0, 32).toString("base64url"), y: raw.subarray(32).toString("base64url") },
    format: "jwk",
  });
  return verify("sha256", Buffer.from(doc.payload, "utf8"), { key, dsaEncoding: "ieee-p1363" }, Buffer.from(doc.signature, "base64"));
}

export function generateKeys(): { privateKey: string; publicKey: string } {
  const { privateKey, publicKey } = generateKeyPairSync("ec", { namedCurve: "P-256" });
  return {
    privateKey: (privateKey.export({ format: "der", type: "pkcs8" }) as Buffer).toString("base64"),
    publicKey: publicKeyXY(publicKey),
  };
}

// Серийный ключ STRS-XXXX-XXXX-XXXX (SPEC 13.2). Без похожих знаков (0/O, 1/I/L): ключ диктуют по телефону.
const ALPHABET = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

export function newSerial(): string {
  const groups: string[] = [];
  for (let g = 0; g < 3; g++) {
    let s = "";
    for (let i = 0; i < 4; i++) s += ALPHABET[randomInt(ALPHABET.length)];
    groups.push(s);
  }
  return "STRS-" + groups.join("-");
}

/** Ключ как ввёл человек (пробелы, строчные, без дефисов) → STRS-XXXX-XXXX-XXXX; не похоже на ключ — null. */
export function normalizeSerial(text: unknown): string | null {
  if (typeof text !== "string") return null;
  let s = text.toUpperCase().replace(/[^A-Z0-9]/g, "");
  if (s.startsWith("STRS")) s = s.substring(4);
  if (s.length !== 12) return null;
  return "STRS-" + s.substring(0, 4) + "-" + s.substring(4, 8) + "-" + s.substring(8, 12);
}
