"use client";

import { FileText, Plus } from "lucide-react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";

import { TabNav } from "@/components/contacts/contact-bits";
import { useSession } from "@/components/providers/session-provider";
import { QuoteList } from "@/components/quotes/quote-list";
import { SaleList } from "@/components/sales/sale-list";
import { Button } from "@/components/ui/button";
import { can } from "@/lib/permissions";

/** Satışlar ekranı: `?sekme=satislar|teklifler`. Üstte "Yeni satış" (hızlı satış) ve "Yeni teklif". */
export function SalesHome() {
  const { role } = useSession();
  const params = useSearchParams();
  const tab = params.get("sekme") === "teklifler" ? "teklifler" : "satislar";
  return (
    <div className="flex flex-col gap-5">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-bold tracking-tight text-black">Satışlar</h1>
        {can(role, "createSales") && (
          <div className="flex gap-2">
            <Button asChild variant="outline">
              <Link href="/panel/teklifler/yeni">
                <FileText aria-hidden />
                Yeni teklif
              </Link>
            </Button>
            <Button asChild>
              <Link href="/panel/satislar/yeni">
                <Plus aria-hidden />
                Yeni satış
              </Link>
            </Button>
          </div>
        )}
      </div>
      <TabNav
        label="Satış sekmeleri"
        active={tab}
        tabs={[
          { key: "satislar", label: "Satışlar", href: "?sekme=satislar" },
          { key: "teklifler", label: "Teklifler", href: "?sekme=teklifler" },
        ]}
      />
      {tab === "satislar" ? <SaleList /> : <QuoteList />}
    </div>
  );
}
