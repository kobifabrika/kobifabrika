"use client";

import "./globals.css";

import { ErrorView } from "@/components/feedback/error-view";

/** Kök düzende hata (en son çare): kendi html/body'si ile. */
export default function GlobalError({ error, reset }: { error: Error & { digest?: string }; reset: () => void }) {
  return (
    <html lang="tr">
      <body>
        <ErrorView error={error} reset={reset} home={{ href: "/", label: "Ana sayfaya dön" }} links={[{ href: "/iletisim", label: "Bize yaz" }]} />
      </body>
    </html>
  );
}
