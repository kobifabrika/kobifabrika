"use client";

import { ErrorView } from "@/components/feedback/error-view";

/** Panel içinde beklenmeyen hata: menü ve üst çubuk yerinde kalır, sadece içerik değişir. */
export default function PanelError({ error, reset }: { error: Error & { digest?: string }; reset: () => void }) {
  return <ErrorView bare error={error} reset={reset} home={{ href: "/panel", label: "Bugün'e dön" }} />;
}
