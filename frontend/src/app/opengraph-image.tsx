import { readFile } from "node:fs/promises";
import { join } from "node:path";

import { ImageResponse } from "next/og";

import { colors } from "@/lib/theme/colors";

/** Paylaşım görseli (1200×630): #000a1d zemin, koyu zemin logosu (public/marka/logo-koyu.svg), slogan ve kısa açıklama. */
export const alt = "KobiFabrika: KOBİ'nin dijital fabrikası. Kasa, veresiye, randevu ve personel tek uygulamada.";
export const size = { width: 1200, height: 630 };
export const contentType = "image/png";

export default async function OpenGraphImage() {
  const [logo, font] = await Promise.all([
    readFile(join(process.cwd(), "public/marka/logo-koyu.svg")),
    // Logo ve site başlıklarıyla aynı yazı tipi (npm run marka da bunu kullanır).
    readFile(join(process.cwd(), "scripts/fonts/SchibstedGrotesk-ExtraBold.ttf")),
  ]);
  return new ImageResponse(
    (
      <div
        style={{
          width: "100%",
          height: "100%",
          display: "flex",
          flexDirection: "column",
          justifyContent: "space-between",
          background: colors["navy-950"],
          padding: "72px 80px",
          color: colors.white,
          fontFamily: "Schibsted Grotesk",
        }}
      >
        {/* eslint-disable-next-line @next/next/no-img-element -- ImageResponse (satori) yalnızca img destekler. */}
        <img src={`data:image/svg+xml;base64,${logo.toString("base64")}`} alt="KobiFabrika" height={56} width={405} />
        <div style={{ display: "flex", flexDirection: "column" }}>
          <div style={{ fontSize: 72, lineHeight: 1.05, letterSpacing: -1.5 }}>KOBİ&apos;nin dijital fabrikası</div>
          <div style={{ marginTop: 28, fontSize: 34, color: colors["neutral-300"] }}>
            Kasa, veresiye, randevu ve personel tek uygulamada.
          </div>
        </div>
        <div style={{ display: "flex", alignItems: "center", fontSize: 28, color: colors.brand }}>
          kobifabrika.com · Çırak paketi ücretsiz
        </div>
      </div>
    ),
    { ...size, fonts: [{ name: "Schibsted Grotesk", data: font, weight: 800, style: "normal" }] },
  );
}
