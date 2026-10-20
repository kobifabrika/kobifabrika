"use client";

import { Plus } from "lucide-react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";

import { AutomationList } from "@/components/marketing/automations";
import { CampaignList } from "@/components/marketing/campaigns";
import { CreditStrip } from "@/components/marketing/sms-credit";
import { SurveyResults } from "@/components/marketing/surveys";
import { useSession } from "@/components/providers/session-provider";
import { Button } from "@/components/ui/button";
import { TabNav } from "@/components/ui/scroll-row";
import { can } from "@/lib/permissions";

const tabs = [
  { key: "kampanyalar", label: "Kampanyalar" },
  { key: "otomasyonlar", label: "Otomasyonlar" },
  { key: "anketler", label: "Anketler" },
] as const;

/**
 * /panel/kampanyalar?sekme=kampanyalar|otomasyonlar|anketler: kredi şeridi, sekmeler. SMS kapalıyken (pilot) kredi şeridi ve yeni
 * SMS kampanyası yok; e-posta ve WhatsApp kampanyaları yayından sonra (Sprint 8A, A6).
 */
export function MarketingHome() {
  const { role, me } = useSession();
  const sms = me.platform.smsEnabled;
  const params = useSearchParams();
  const active = tabs.find((t) => t.key === params.get("sekme"))?.key ?? "kampanyalar";

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-bold tracking-tight text-black">Kampanyalar</h1>
        {active === "kampanyalar" && sms && can(role, "manageMarketing") && (
          <Button asChild className="max-sm:w-full">
            <Link href="/panel/kampanyalar/yeni">
              <Plus aria-hidden /> Yeni kampanya
            </Link>
          </Button>
        )}
      </div>
      {sms ? (
        <CreditStrip />
      ) : (
        <p className="border-l-2 border-brand bg-neutral-50 px-4 py-3 text-sm text-neutral-700">
          SMS gönderimi pilot döneminde kapalı. Randevu bildirimlerini WhatsApp&apos;tan ve e-postayla yapabilirsiniz;
          e-posta ve WhatsApp kampanyaları yakında burada.
        </p>
      )}
      <TabNav label="Pazarlama sekmeleri" active={active} tabs={tabs.map((t) => ({ ...t, href: `?sekme=${t.key}` }))} />
      {active === "kampanyalar" && <CampaignList />}
      {active === "otomasyonlar" && <AutomationList />}
      {active === "anketler" && <SurveyResults />}
    </div>
  );
}
