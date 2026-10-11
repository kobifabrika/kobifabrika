import { redirect } from "next/navigation";

/** Teklif listesi Satışlar ekranının sekmesinde. */
export default function QuotesPage() {
  redirect("/panel/satislar?sekme=teklifler");
}
