import type { Metadata } from "next";
import Link from "next/link";

import { StatusPage } from "@/components/feedback/status-page";
import { Button } from "@/components/ui/button";

export const metadata: Metadata = { title: "Sayfa bulunamadı", robots: { index: false } };

/** Genel 404 (site ve bilinmeyen adresler). */
export default function NotFound() {
  return (
    <StatusPage
      code="404"
      title="Aradığın sayfa burada değil"
      actions={
        <>
          <Button asChild size="lg">
            <Link href="/">Ana sayfaya dön</Link>
          </Button>
          <Button asChild size="lg" variant="outline">
            <Link href="/panel">Panele git</Link>
          </Button>
        </>
      }
      links={[
        { href: "/yardim", label: "Yardım" },
        { href: "/fiyatlar", label: "Fiyatlar" },
        { href: "/iletisim", label: "İletişim" },
      ]}
    >
      Bağlantı eksik kopyalanmış ya da sayfa taşınmış olabilir. Adresi kontrol et veya aşağıdan devam et.
    </StatusPage>
  );
}
