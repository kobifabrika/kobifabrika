import { redirect } from "next/navigation";

/** Sadece yönlendirir; panelin diğer segmentleri gibi anında gezinme doğrulamasının dışında (geliştirmede konsol hatası veriyordu). */
export const instant = false;

/** Teklif listesi Satışlar ekranının sekmesinde. */
export default function QuotesPage() {
  redirect("/panel/satislar?sekme=teklifler");
}
