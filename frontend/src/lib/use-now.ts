"use client";

import { useEffect, useState } from "react";

/** Şimdiki zaman (ms); dakikada bir yenilenir. Render içinde Date.now() çağırmamak için. */
export function useNow(intervalMs = 60_000): number {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), intervalMs);
    return () => clearInterval(timer);
  }, [intervalMs]);
  return now;
}
