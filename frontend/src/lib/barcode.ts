"use client";

import { useEffect, useRef } from "react";

/**
 * USB barkod okuyucu klavye gibi yazar: karakterler çok hızlı (genelde < 20 ms arayla) gelir ve Enter ile biter.
 * İnsan yazımı bu hızda olmaz; böylece odak bir input'ta değilken bile okutulan barkod yakalanır.
 */
export const SCAN_MAX_GAP_MS = 35;
export const SCAN_MIN_LENGTH = 4;

type KeyLike = { key: string; timeStamp: number };

/** Tuş vuruşlarını izler; hızlı gelen karakter dizisi Enter ile bitince barkodu döner, yoksa null. */
export function createScanDetector(maxGap = SCAN_MAX_GAP_MS, minLength = SCAN_MIN_LENGTH) {
  let buffer = "";
  let last = 0;
  let fast = true;

  return {
    /** Tuşu işler; okuma tamamlandıysa barkod metnini döner. */
    push(event: KeyLike): string | null {
      if (event.key === "Enter") {
        const code = buffer;
        const isScan = fast && code.length >= minLength && event.timeStamp - last <= maxGap * 3;
        buffer = "";
        fast = true;
        return isScan ? code : null;
      }
      if (event.key.length !== 1) return null;
      if (buffer && event.timeStamp - last > maxGap) {
        // Yavaş yazım: yeni bir dizi başlar (önceki karakterler insan yazımı).
        buffer = "";
        fast = true;
      }
      buffer += event.key;
      last = event.timeStamp;
      return null;
    },
    reset() {
      buffer = "";
      fast = true;
    },
  };
}

/**
 * Sayfa genelinde barkod okuyucuyu dinler (odak bir yazı alanında değilken). Odak yazı alanındaysa o alan
 * kendi Enter davranışıyla barkodu işler.
 */
export function useBarcodeScanner(onScan: (code: string) => void, enabled = true) {
  const handler = useRef(onScan);
  useEffect(() => {
    handler.current = onScan;
  }, [onScan]);

  useEffect(() => {
    if (!enabled) return;
    const detector = createScanDetector();
    const listener = (e: KeyboardEvent) => {
      const target = e.target as HTMLElement | null;
      if (target && (target.tagName === "INPUT" || target.tagName === "TEXTAREA" || target.isContentEditable)) {
        detector.reset();
        return;
      }
      const code = detector.push({ key: e.key, timeStamp: e.timeStamp });
      if (code) {
        e.preventDefault();
        handler.current(code);
      }
    };
    window.addEventListener("keydown", listener);
    return () => window.removeEventListener("keydown", listener);
  }, [enabled]);
}
