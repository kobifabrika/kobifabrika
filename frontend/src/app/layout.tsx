import type { Metadata, Viewport } from "next";

import { QueryProvider } from "@/components/providers/query-provider";
import { Toaster } from "@/components/ui/sonner";

import "./globals.css";

export const metadata: Metadata = {
  title: {
    default: "KobiFabrika — Paranı takip eden, müşteri getiren tek uygulama",
    template: "%s · KobiFabrika",
  },
  description:
    "KOBİ ve esnafın parasını takip eden, müşteri getiren ve personelini yöneten tek uygulama. Cari, kasa, stok, satış, randevu, SMS kampanyası, mesai ve izin.",
  applicationName: "KobiFabrika",
};

export const viewport: Viewport = {
  width: "device-width",
  initialScale: 1,
  themeColor: "#000a1d",
};

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html lang="tr" className="h-full">
      <body className="min-h-full">
        <QueryProvider>{children}</QueryProvider>
        <Toaster position="top-center" />
      </body>
    </html>
  );
}
