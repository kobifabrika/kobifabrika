import type { Instrumentation } from "next";

// Sunucu tarafı hata izleme (canlı; docs/canli-ortam.md → Sentry). SENTRY_DSN (sunucu .env'de SENTRY_DSN_WEB) boşsa veya
// geçersizse Sentry hiç başlamaz. Tarayıcıya Sentry yüklenmez. Kişisel veri gönderilmez (lib/sentry-scrub), performans
// izleme kapalı (ücretsiz kota hatalar için).
function sentryDsn() {
  const dsn = process.env.SENTRY_DSN?.replace(/^﻿/, "").trim();
  if (!dsn || !/^https:\/\/[^@\s]+@[^/\s]+\/\d+$/.test(dsn)) return undefined;
  return dsn;
}

export async function register() {
  if (process.env.NEXT_RUNTIME !== "nodejs") return;
  if (process.env.SENTRY_DSN && !sentryDsn()) {
    console.warn("UYARI: SENTRY_DSN geçersiz; Sentry kapalı.");
    return;
  }
  const dsn = sentryDsn();
  if (!dsn) return;

  const Sentry = await import("@sentry/nextjs");
  const { scrubBreadcrumb, scrubEvent } = await import("./lib/sentry-scrub");
  Sentry.init({
    dsn,
    environment: "production",
    // Sürüm 11'de varsayılanlar çok şey toplar (çerez, başlık, gövde, yerel değişkenler): hepsi kapalı.
    dataCollection: {
      userInfo: false,
      cookies: false,
      httpHeaders: false,
      httpBodies: [],
      urlQueryParams: false,
      databaseQueryData: false,
      queues: false,
      stackFrameVariables: false,
      graphQL: { document: false, variables: false },
      genAI: { inputs: false, outputs: false },
    },
    tracesSampleRate: 0,
    beforeSend: scrubEvent,
    beforeBreadcrumb: scrubBreadcrumb,
  });
  if (process.env.SENTRY_STARTUP_TEST === "true") {
    Sentry.captureException(
      new Error("Sentry deneme hatası (web): canlı kurulum testi (maskelenmeli: deneme.kisi@ornek.com, +90 532 123 45 67)"),
    );
    console.warn("Sentry deneme hatası gönderildi (web).");
  }
}

export const onRequestError: Instrumentation.onRequestError = async (...args) => {
  if (process.env.NEXT_RUNTIME !== "nodejs" || !sentryDsn()) return;
  const Sentry = await import("@sentry/nextjs");
  Sentry.captureRequestError(...args);
};
