// Marka görsellerini kaynak dosyalardan üretir: `npm run marka`.
// Kaynaklar public/marka/ altında: logo.png (siyah zeminde "KOBI FABRIKA" yazısı) ve isaret.svg ("KF" işareti).
// sharp ayrı bağımlılık değil, Next.js ile (görsel optimizasyonu) gelir.
// Logo 16-48 px'te okunmadığı için favicon ve uygulama ikonları işaretten üretilir (docs/karar-gunlugu.md, 2026-10-20).
import { readFileSync, writeFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

import sharp from "sharp";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const marka = path.join(root, "public", "marka");
const app = path.join(root, "src", "app");

const isaret = readFileSync(path.join(marka, "isaret.svg"));
/** Köşesiz kare (iOS ana ekran ikonu köşeleri kendisi yuvarlar). */
const isaretKare = Buffer.from(isaret.toString().replace('rx="72"', 'rx="0"'));

const render = (svg, size) => sharp(svg, { density: 600 }).resize(size, size).png().toBuffer();

/** PNG girdili ICO (bütün güncel tarayıcılar okur). */
function ico(pngs) {
  const header = Buffer.alloc(6 + 16 * pngs.length);
  header.writeUInt16LE(0, 0);
  header.writeUInt16LE(1, 2);
  header.writeUInt16LE(pngs.length, 4);
  let offset = header.length;
  pngs.forEach(({ size, data }, i) => {
    const e = 6 + 16 * i;
    header.writeUInt8(size >= 256 ? 0 : size, e);
    header.writeUInt8(size >= 256 ? 0 : size, e + 1);
    header.writeUInt16LE(1, e + 4);
    header.writeUInt16LE(32, e + 6);
    header.writeUInt32LE(data.length, e + 8);
    header.writeUInt32LE(offset, e + 12);
    offset += data.length;
  });
  return Buffer.concat([header, ...pngs.map((p) => p.data)]);
}

const sizes = [16, 32, 48];
const pngs = await Promise.all(sizes.map(async (size) => ({ size, data: await render(isaret, size) })));
writeFileSync(path.join(app, "favicon.ico"), ico(pngs));
writeFileSync(path.join(app, "icon.png"), await render(isaret, 512));
writeFileSync(path.join(app, "apple-icon.png"), await render(isaretKare, 180));
writeFileSync(path.join(marka, "icon-192.png"), await render(isaret, 192));
// Maskable: Android ikonu daireye kırpabilir; harfler ortadaki güvenli bölgeye (%80 daire) sığsın diye işaret %70'e küçülür.
writeFileSync(
  path.join(marka, "icon-maskable-512.png"),
  await sharp({ create: { width: 512, height: 512, channels: 4, background: "#000000" } })
    .composite([{ input: await render(isaretKare, 358), left: 77, top: 77 }])
    .png()
    .toBuffer(),
);

// Logo: siyah zemin kırpılır ve saydama çevrilir (yazı rengi = piksel / en parlak kanal). Koyu zeminde kullanılır.
const { data, info } = await sharp(path.join(marka, "logo.png"))
  .trim({ background: "#000000", threshold: 10 })
  .ensureAlpha()
  .raw()
  .toBuffer({ resolveWithObject: true });
for (let i = 0; i < data.length; i += 4) {
  const a = Math.max(data[i], data[i + 1], data[i + 2]);
  if (a === 0) {
    data[i + 3] = 0;
    continue;
  }
  data[i] = Math.round((data[i] * 255) / a);
  data[i + 1] = Math.round((data[i + 1] * 255) / a);
  data[i + 2] = Math.round((data[i + 2] * 255) / a);
  data[i + 3] = a;
}
const yazi = sharp(data, { raw: { width: info.width, height: info.height, channels: 4 } });
// Sitede/girişte 2x yoğunlukta en fazla ~280 px genişlik gösterilir.
await yazi.clone().resize({ width: 640 }).webp({ quality: 90 }).toFile(path.join(marka, "logo-yazi.webp"));
await yazi.clone().resize({ width: 640 }).png().toFile(path.join(marka, "logo-yazi.png"));
// E-posta: istemciler WebP ve saydamlığı her zaman desteklemez; siyah zeminli, 2x (gösterim 200×37 px; sharp önce boyutlar
// sonra kenar ekler: 464×87).
await sharp(path.join(marka, "logo.png"))
  .trim({ background: "#000000", threshold: 10 })
  .extend({ top: 24, bottom: 24, left: 32, right: 32, background: "#000000" })
  .resize({ width: 400 })
  .png()
  .toFile(path.join(marka, "logo-eposta.png"));

const meta = await sharp(path.join(marka, "logo-yazi.png")).metadata();
console.log(`Marka görselleri üretildi. Logo yazısı ${meta.width}x${meta.height} (oran ${(meta.width / meta.height).toFixed(2)}).`);
