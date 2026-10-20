// Basit dosya kilidi: iki E2E sunucusu (SMS kapalı/açık çift) aynı anda başlarken aynı projeyi aynı anda derlemesin.
// Kilit dosyası özel oluşturulur (wx); 15 dakikadan eski kilit (çöken önceki koşu) silinir.
import { mkdirSync, openSync, closeSync, rmSync, statSync } from "node:fs";
import path from "node:path";

const STALE_MS = 15 * 60_000;

export async function withLock(file, fn) {
  mkdirSync(path.dirname(file), { recursive: true });
  for (;;) {
    try {
      closeSync(openSync(file, "wx"));
      break;
    } catch {
      try {
        if (Date.now() - statSync(file).mtimeMs > STALE_MS) rmSync(file, { force: true });
      } catch {
        // Kilit bu arada kalktı.
      }
      await new Promise((r) => setTimeout(r, 1000));
    }
  }
  try {
    return await fn();
  } finally {
    rmSync(file, { force: true });
  }
}
