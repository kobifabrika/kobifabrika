import type { Metadata, Viewport } from "next";
import { Schibsted_Grotesk } from "next/font/google";

import { QueryProvider } from "@/components/providers/query-provider";
import { Toaster } from "@/components/ui/sonner";
import { siteUrl } from "@/content/seo";
import { homeTitle, siteDescription } from "@/content/site";
import { colors } from "@/lib/theme/colors";

import "./globals.css";

/** Başlık fontu (docs/karar-gunlugu.md, 2026-10-10). Gövde metni sistem fontu kalır. */
const display = Schibsted_Grotesk({
  subsets: ["latin", "latin-ext"],
  variable: "--font-display",
  display: "swap",
});

export const metadata: Metadata = {
  metadataBase: new URL(siteUrl),
  // Sekme başlığı "Sayfa | KobiFabrika"; ana sayfa "KobiFabrika | KOBİ'nin dijital fabrikası" (2026-10-21).
  title: {
    default: homeTitle,
    template: "%s | KobiFabrika",
  },
  description: siteDescription,
  applicationName: "KobiFabrika",
  openGraph: { type: "website", locale: "tr_TR", siteName: "KobiFabrika", title: homeTitle },
  twitter: { card: "summary_large_image" },
};

export const viewport: Viewport = {
  width: "device-width",
  initialScale: 1,
  themeColor: colors["navy-950"],
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
