// Marka görsellerini üretir: `npm run marka`.
// Tek kaynak yazı tipi: scripts/fonts/SchibstedGrotesk-ExtraBold.ttf (sitenin başlık fontu, 800; OFL lisansı yanında).
// - Logo (2026-10-21): "KobiFabrika" yazısı, harfler vektör yola çevrilir (font yüklenmeden her yerde aynı görünür).
//   public/marka/logo-koyu.svg (koyu zemin: "Kobi" beyaz) ve logo-acik.svg (açık zemin: "Kobi" #000a1d); "Fabrika" ikisinde de
//   marka mavisi #00b0ff. Harf aralığı sitedeki yazıyla aynı (tracking-tight, -0,025em), alt çizgi yok.
// - E-posta logosu public/marka/logo-eposta.png: açık zemin logosu, beyaz zeminli, 2x (gösterim 180 px genişlik).
// - Favicon ve uygulama ikonları işaretten (public/marka/isaret.svg: koyu kare, beyaz "K", mavi "F"; harfler aynı fonttan,
//   karenin genişliğinin %50'si).
// Kontrol için 16, 32 ve 180 px görüntülerin 8 kat büyütülmüş hali test-results/marka/ altına yazılır (repoya girmez).
// sharp ayrı bağımlılık değil, Next.js ile (görsel optimizasyonu) gelir.
import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

import opentype from "opentype.js";
import sharp from "sharp";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const marka = path.join(root, "public", "marka");
const app = path.join(root, "src", "app");

// Renkler globals.css ile aynı (navy-950, brand, white).
const navy = "#000a1d";
const brand = "#00b0ff";
const white = "#ffffff";

const font = opentype.loadSync(path.join(root, "scripts", "fonts", "SchibstedGrotesk-ExtraBold.ttf"));

/** Metni yazı tipinin kerning'i ve verilen harf aralığıyla (em cinsinden) glif yollarına çevirir. */
function layout(text, size, tracking) {
  const glyphs = font.stringToGlyphs(text);
  const scale = size / font.unitsPerEm;
  let x = 0;
  return glyphs.map((g, i) => {
    const p = g.getPath(x, 0, size);
    x += g.advanceWidth * scale + tracking * size;
    if (i < glyphs.length - 1) x += font.getKerningValue(g, glyphs[i + 1]) * scale;
    return p;
  });
}

function bounds(paths) {
  const b = paths.map((p) => p.getBoundingBox());
  return {
    x1: Math.min(...b.map((v) => v.x1)),
    y1: Math.min(...b.map((v) => v.y1)),
    x2: Math.max(...b.map((v) => v.x2)),
    y2: Math.max(...b.map((v) => v.y2)),
  };
}

const d = (p, dx, dy) => {
  for (const c of p.commands) {
    for (const k of ["x", "x1", "x2"]) if (k in c) c[k] += dx;
    for (const k of ["y", "y1", "y2"]) if (k in c) c[k] += dy;
  }
  return p.toPathData(1);
};

// Logo: 100 birim yazı boyu; görünüm kutusu harflerin sınırına sıkı (K'nin üstü - satır tabanı).
const paths = layout("KobiFabrika", 100, -0.025);
const box = bounds(paths);
const w = Math.ceil(box.x2 - box.x1);
const h = Math.ceil(box.y2 - box.y1);
const kobi = paths.slice(0, 4).map((p) => d(p, -box.x1, -box.y1)).join("");
const fabrika = paths.slice(4).map((p) => d(p, -box.x1, -box.y1)).join("");
const logoSvg = (kobiFill) =>
  `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${w} ${h}" role="img" aria-label="KobiFabrika">` +
  `<title>KobiFabrika</title><path fill="${kobiFill}" d="${kobi}"/><path fill="${brand}" d="${fabrika}"/></svg>\n`;
const logoKoyu = logoSvg(white);
const logoAcik = logoSvg(navy);
writeFileSync(path.join(marka, "logo-koyu.svg"), logoKoyu);
writeFileSync(path.join(marka, "logo-acik.svg"), logoAcik);
console.log(`Logo ${w}×${h} (oran ${(w / h).toFixed(3)}:1)`);

// E-posta: istemciler SVG'yi göstermez; beyaz zeminli PNG, 2x (gösterim 180 px genişlik).
const emailWidth = 180;
await sharp(Buffer.from(logoAcik), { density: 600 })
  .resize({ width: emailWidth * 2 })
  .flatten({ background: white })
  .png()
  .toFile(path.join(marka, "logo-eposta.png"));
console.log(`E-posta logosu ${emailWidth}×${Math.round((emailWidth * h) / w)} px gösterim (2x PNG)`);

// İşaret: 512 kare, rx 72; "K" beyaz + "F" mavi aynı fonttan, ikisi birlikte karenin genişliğinin %50'si, ortada.
const mark = layout("KF", 100, 0.06);
const mb = bounds(mark);
const s = 256 / (mb.x2 - mb.x1);
const tx = (512 - (mb.x2 - mb.x1) * s) / 2 - mb.x1 * s;
const ty = (512 - (mb.y2 - mb.y1) * s) / 2 - mb.y1 * s;
const markPath = (p) => {
  for (const c of p.commands) {
    for (const k of ["x", "x1", "x2"]) if (k in c) c[k] = c[k] * s + tx;
    for (const k of ["y", "y1", "y2"]) if (k in c) c[k] = c[k] * s + ty;
  }
  return p.toPathData(1);
};
const isaretSvg = (rx) =>
  `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512" role="img" aria-label="KobiFabrika">` +
  `<rect width="512" height="512" rx="${rx}" fill="${navy}"/>` +
  `<path fill="${white}" d="${markPath(mark[0])}"/><path fill="${brand}" d="${markPath(mark[1])}"/></svg>\n`;
const isaret = Buffer.from(isaretSvg(72));
writeFileSync(path.join(marka, "isaret.svg"), isaret);
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
  await sharp({ create: { width: 512, height: 512, channels: 4, background: navy } })
    .composite([{ input: await render(isaretKare, 358), left: 77, top: 77 }])
    .png()
    .toBuffer(),
);

console.log("Marka görselleri üretildi.");

// Yakından kontrol: 16, 32 ve 180 px ikonlar en yakın komşu ile 8 kat büyütülür; logolar 12 ve 24 px yükseklikte 2x.
const kontrol = path.join(root, "test-results", "marka");
mkdirSync(kontrol, { recursive: true });
for (const [ad, size, svg] of [
  ["favicon-16", 16, isaret],
  ["favicon-32", 32, isaret],
  ["apple-180", 180, isaretKare],
]) {
  const png = await render(svg, size);
  await sharp(png)
    .resize(size * 8, size * 8, { kernel: "nearest" })
    .toFile(path.join(kontrol, `${ad}-8x.png`));
}
for (const [ad, svg, bg] of [
  ["logo-koyu", logoKoyu, navy],
  ["logo-acik", logoAcik, white],
]) {
  for (const px of [12, 24]) {
    await sharp(Buffer.from(svg), { density: 600 })
      .resize({ height: px * 2 })
      .flatten({ background: bg })
      .extend({ top: 8, bottom: 8, left: 8, right: 8, background: bg })
      .png()
      .toFile(path.join(kontrol, `${ad}-${px}px-2x.png`));
  }
}
