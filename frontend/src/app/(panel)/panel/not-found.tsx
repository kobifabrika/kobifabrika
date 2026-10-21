import Link from "next/link";

import { StatusPage } from "@/components/feedback/status-page";
import { Button } from "@/components/ui/button";

/** Panelde olmayan adres (menü ve üst çubuk yerinde). */
export default function PanelNotFound() {
  return (
    <StatusPage
      bare
      code="404"
      title="Bu ekran panelde yok"
      actions={
        <Button asChild size="lg">
          <Link href="/panel">Bugün&apos;e dön</Link>
        </Button>
      }
    >
      Kayıt silinmiş ya da bağlantı eksik kopyalanmış olabilir. Menüden istediğiniz ekrana geçebilirsiniz.
    </StatusPage>
  );
}
