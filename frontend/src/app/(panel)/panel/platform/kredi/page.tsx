import { redirect } from "next/navigation";

/** Eski kredi yükleme ekranı: süper admin paneline taşındı (/platform → işletme → Kredi). */
export default function PlatformCreditPage() {
  redirect("/platform/isletmeler");
}
