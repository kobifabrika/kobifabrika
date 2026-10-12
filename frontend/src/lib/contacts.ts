/** Müşteriyle tek dokunuşla iletişim linkleri. Telefonlar API'de E.164 (+905321234567). */

export function telLink(e164: string): string {
  return `tel:${e164}`;
}

/** wa.me: numara "+" olmadan; mesaj isteğe bağlı. */
export function waLink(e164: string | null | undefined, text?: string): string {
  const number = (e164 ?? "").replace(/^\+/, "");
  const query = text ? `?text=${encodeURIComponent(text)}` : "";
  return `https://wa.me/${number}${query}`;
}

/**
 * Telefonun kendi SMS uygulamasını açan link; iOS ve Android'in ikisinde de çalışan "?&body=" biçimi.
 * Numara yoksa uygulama alıcı sorar.
 */
export function smsLink(e164: string | null | undefined, text: string): string {
  return `sms:${e164 ?? ""}?&body=${encodeURIComponent(text)}`;
}

/** +905321234567 → "0532 123 45 67" (listede ve kartta gösterim). */
export function displayPhone(e164: string | null | undefined): string {
  if (!e164?.startsWith("+90") || e164.length !== 13) return e164 ?? "";
  const n = e164.slice(3);
  return `0${n.slice(0, 3)} ${n.slice(3, 6)} ${n.slice(6, 8)} ${n.slice(8)}`;
}
