"use client";

import { useEffect } from "react";

/** Eski bağlantıyı yeni sayfaya yönlendirir: "/#ozellikler" → "/ozellikler" (2026-10-21). Hash sunucuya gitmediği için istemcide. */
export function HashRedirect({ hash, to }: { hash: string; to: string }) {
  useEffect(() => {
    if (window.location.hash === hash) window.location.replace(to);
  }, [hash, to]);
  return null;
}
