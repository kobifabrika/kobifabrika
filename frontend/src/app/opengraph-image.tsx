import { ImageResponse } from "next/og";

/** Paylaşım görseli (1200×630): koyu lacivert zemin, "KOBI" beyaz + "FABRIKA" mavi, slogan ve kısa açıklama. */
export const alt = "KobiFabrika: KOBİ'nin dijital fabrikası. Kasa, veresiye, randevu ve personel tek uygulamada.";
export const size = { width: 1200, height: 630 };
export const contentType = "image/png";

export default function OpenGraphImage() {
  return new ImageResponse(
    (
      <div
        style={{
          width: "100%",
          height: "100%",
          display: "flex",
          flexDirection: "column",
          justifyContent: "space-between",
          background: "#000a1d",
          padding: "72px 80px",
          color: "#ffffff",
        }}
      >
        <div style={{ display: "flex", fontSize: 56, fontWeight: 800, letterSpacing: 2 }}>
          <span>KOBI</span>
          <span style={{ color: "#00b0ff", marginLeft: 18 }}>FABRIKA</span>
        </div>
        <div style={{ display: "flex", flexDirection: "column" }}>
          <div style={{ fontSize: 72, fontWeight: 800, lineHeight: 1.05 }}>KOBİ&apos;nin dijital fabrikası</div>
          <div style={{ marginTop: 28, fontSize: 34, color: "#d4d4d4" }}>Kasa, veresiye, randevu ve personel tek uygulamada.</div>
        </div>
        <div style={{ display: "flex", alignItems: "center", fontSize: 28, color: "#00fdff" }}>kobifabrika.com · Çırak paketi ücretsiz</div>
      </div>
    ),
    size,
  );
}
