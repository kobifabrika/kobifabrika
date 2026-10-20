"use client";

import { ErrorView } from "@/components/feedback/error-view";

/** Tanıtım sitesinde beklenmeyen hata. */
export default function SiteError({ error, reset }: { error: Error & { digest?: string }; reset: () => void }) {
  return (
    <ErrorView
      bare
      error={error}
      reset={reset}
      home={{ href: "/", label: "Ana sayfaya dön" }}
      links={[{ href: "/iletisim", label: "Bize yaz" }]}
    />
  );
}
