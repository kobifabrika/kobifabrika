import type { Metadata } from "next";

import { HelpList } from "@/components/site/help-list";
import { PageIntro } from "@/components/site/page-intro";

export const metadata: Metadata = {
  title: "Yardım",
  description: "KobiFabrika yardım makaleleri: ilk kurulum, online randevu linki, WhatsApp'tan hatırlatma, telefonda kullanım.",
  alternates: { canonical: "/yardim" },
};

export default function HelpPage() {
  return (
    <>
      <PageIntro label="Yardım" title="Nasıl yapılır?">
        Adım adım anlatımlar. Bulamadığın bir şey olursa panelde Yardım menüsünden WhatsApp&apos;la bize yaz.
      </PageIntro>
      <div className="mx-auto max-w-3xl px-4 py-12 sm:px-6 sm:py-16">
        <HelpList />
      </div>
    </>
  );
}
