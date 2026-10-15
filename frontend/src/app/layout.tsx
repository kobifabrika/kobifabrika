import type { Metadata, Viewport } from "next";
import { Schibsted_Grotesk } from "next/font/google";

import { QueryProvider } from "@/components/providers/query-provider";
import { Toaster } from "@/components/ui/sonner";

import "./globals.css";

/** Başlık fontu (docs/karar-gunlugu.md, 2026-10-10). Gövde metni sistem fontu kalır. */
const display = Schibsted_Grotesk({
  subsets: ["latin", "latin-ext"],
  variable: "--font-display",
  display: "swap",
});

export const metadata: Metadata = {
  title: {
    default: "KobiFabrika — KOBİ'nin dijital fabrikası",
    template: "%s · KobiFabrika",
  },
  description:
    "KOBİ'nin dijital fabrikası: esnafın parasını takip eden, müşteri getiren ve personelini yöneten tek uygulama. Cari, kasa, stok, satış, randevu, SMS kampanyası, mesai ve izin.",
  applicationName: "KobiFabrika",
};

export const viewport: Viewport = {
  width: "device-width",
  initialScale: 1,
  themeColor: "#000000",
};

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html lang="tr" className={`h-full ${display.variable}`}>
      <body className="min-h-full">
        <QueryProvider>{children}</QueryProvider>
        <Toaster position="top-center" />
      </body>
    </html>
  );
}
