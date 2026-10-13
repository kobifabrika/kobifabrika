"use client";

import { useSyncExternalStore } from "react";

const DESKTOP = "(min-width: 1024px)";

function subscribe(callback: () => void) {
  const query = window.matchMedia(DESKTOP);
  query.addEventListener("change", callback);
  return () => query.removeEventListener("change", callback);
}

/** lg (1024 px) ve üstü mü; sunucuda ve ilk render'da false (telefon önce). */
export function useIsDesktop(): boolean {
  return useSyncExternalStore(
    subscribe,
    () => window.matchMedia(DESKTOP).matches,
    () => false,
  );
}
