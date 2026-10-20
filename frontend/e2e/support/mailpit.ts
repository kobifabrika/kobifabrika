import { expect, request } from "@playwright/test";

import { mailpitUrl } from "../scripts/env.mjs";
import { target } from "./target";

type Summary = { ID: string; Subject: string; To: { Address: string }[]; Created: string };

/** Adrese gelen e-postalar (en yeni önce). */
async function search(to: string): Promise<Summary[]> {
  const ctx = await request.newContext({ baseURL: mailpitUrl });
  try {
    const res = await ctx.get(`/api/v1/search?query=${encodeURIComponent(`to:"${to}"`)}&limit=50`);
    expect(res.ok(), `Mailpit'e ulaşılamadı (${mailpitUrl}). docker compose up -d çalıştı mı?`).toBe(true);
    return ((await res.json()) as { messages: Summary[] }).messages ?? [];
  } finally {
    await ctx.dispose();
  }
}

async function text(id: string): Promise<string> {
  const ctx = await request.newContext({ baseURL: mailpitUrl });
  try {
    return ((await (await ctx.get(`/api/v1/message/${id}`)).json()) as { Text: string }).Text;
  } finally {
    await ctx.dispose();
  }
}

/**
 * Adrese gelen ve konusu eşleşen son e-postadaki, verilen yolla başlayan frontend linki (ör. "/e-posta-dogrula").
 * E-posta gelene kadar 20 saniye bekler. `count`: bu kadar e-posta gelmesini bekle (aynı konuyla ikinci e-posta için).
 */
export async function linkFromEmail(to: string, subject: RegExp, path: string, count = 1): Promise<string> {
  let found: Summary | undefined;
  await expect
    .poll(
      async () => {
        const matches = (await search(to)).filter((m) => subject.test(m.Subject));
        found = matches[0];
        return matches.length;
      },
      { message: `${to} adresine "${subject}" e-postası gelmedi`, timeout: 20_000 },
    )
    .toBeGreaterThanOrEqual(count);
  const body = await text(found!.ID);
  const escaped = `${target().webUrl}${path}`.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  const link = body.match(new RegExp(`${escaped}[^\\s)>\\]]*`))?.[0];
  expect(link, `E-postada ${path} linki yok:\n${body}`).toBeTruthy();
  return link!;
}

/** Adrese gelen e-postaların konuları (en yeni önce). */
export async function subjects(to: string): Promise<string[]> {
  return (await search(to)).map((m) => m.Subject);
}

/** Sahte SMS sağlayıcısının Mailpit yansıması (Sms:MirrorToEmail): numaraya giden SMS metinleri, en yeni önce. */
export async function smsTexts(phone: string): Promise<string[]> {
  const digits = phone.replace(/\D/g, "");
  const national = digits.startsWith("90") ? digits.slice(2) : digits.startsWith("0") ? digits.slice(1) : digits;
  const messages = await search(`sms+90${national}@sms.test`);
  return Promise.all(messages.map((m) => text(m.ID)));
}

/** Numaraya gelen son doğrulama kodu (SMS gelene kadar 20 saniye bekler). `count`: bu kadar kod SMS'i gelmesini bekle. */
export async function smsCode(phone: string, count = 1): Promise<string> {
  let code = "";
  await expect
    .poll(
      async () => {
        const codes = (await smsTexts(phone)).filter((t) => /randevu kodunuz: \d{6}/.test(t));
        code = codes[0]?.match(/\d{6}/)?.[0] ?? "";
        return codes.length;
      },
      { message: `${phone} numarasına doğrulama kodu gelmedi`, timeout: 20_000 },
    )
    .toBeGreaterThanOrEqual(count);
  return code;
}
