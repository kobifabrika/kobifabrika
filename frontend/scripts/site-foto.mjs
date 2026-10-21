// Tanıtım sitesi fotoğraflarını Pexels'in orijinal boyutundan üretir: `npm run site:foto` (karar 2026-10-21).
// Her fotoğraf orijinalinden (en büyük boyut) kırpılır ve gösterildiği en büyük boyutun en az 2 katı genişlikte WebP (kalite 85)
// yazılır; next/image ekran genişliğine ve yoğunluğuna göre küçültür. Kaynaklar ve lisans: docs/foto-kaynaklari.md (bu listeyle
// aynı tutun). İndirilen orijinaller test-results/foto-orijinal/ altında önbelleklenir (repoya girmez).
import { existsSync, mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

import sharp from "sharp";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const out = path.join(root, "public", "site", "foto");
const cache = path.join(root, "test-results", "foto-orijinal");

/**
 * file: public/site/foto altındaki ad · id: Pexels fotoğraf numarası · crop: orijinal üzerinde piksel kırpma (left, top, width,
 * height) · width: çıktı genişliği (yükseklik orana göre).
 */
const photos = [
  // İlk ekran: gösterim en fazla ~520 px → 2400 px (4:3,4). Kafe masasında sipariş alan garson; soldaki bitki kırpılır.
  { file: "kafe-siparis.webp", id: 4921154, crop: [300, 0, 4546, 3864], width: 2400 },
  // Sektör kartları: en fazla ~460 px (sm, 2 kolon) → 1600 px (4:3).
  // Berber: dikeyden 4:3; berber ve müşterinin yüzü kadrajda.
  { file: "berber.webp", id: 7697224, crop: [0, 770, 2697, 2023], width: 1600 },
  { file: "guzellik.webp", id: 20309789, crop: [496, 0, 3968, 2976], width: 1600 },
  { file: "pilates.webp", id: 9288101, crop: [667, 0, 5333, 4000], width: 1600 },
  // Kafe ve restoran: dolu salonda tabak taşıyan garson (ilk ekrandan farklı sahne).
  { file: "restoran.webp", id: 25809274, crop: [300, 0, 5973, 4480], width: 1600 },
  // Çiçekçi: üstteki el yazısı tabela kırpılır.
  { file: "cicekci.webp", id: 5409710, crop: [300, 880, 4160, 3120], width: 1600 },
  // "Telefondan yönet": ~400 px → 1600 px (4:5); pelerindeki yazı kırpılır.
  { file: "kuafor.webp", id: 35225425, crop: [0, 0, 3990, 4988], width: 1600 },
  // Son çağrı: telefonda tam genişlik (md'de ~1000 px) → 2400 px (3:2).
  { file: "servis.webp", id: 4350094, crop: [95, 0, 5736, 3824], width: 2400 },
];

mkdirSync(cache, { recursive: true });
mkdirSync(out, { recursive: true });

for (const { file, id, crop, width } of photos) {
  const original = path.join(cache, `${id}.jpg`);
  if (!existsSync(original)) {
    const res = await fetch(`https://images.pexels.com/photos/${id}/pexels-photo-${id}.jpeg`);
    if (!res.ok) throw new Error(`${id} indirilemedi: ${res.status}`);
    writeFileSync(original, Buffer.from(await res.arrayBuffer()));
  }
  const [left, top, w, h] = crop;
  const info = await sharp(original)
    .rotate()
    .extract({ left, top, width: w, height: h })
    .resize({ width, kernel: "lanczos3" })
    .webp({ quality: 85, effort: 6, smartSubsample: true })
    .toFile(path.join(out, file));
  console.log(`${file}: ${info.width}x${info.height}, ${Math.round(info.size / 1024)} KB`);
}
