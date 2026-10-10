"use client";

import { useQuery } from "@tanstack/react-query";
import { Landmark, Wallet } from "lucide-react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { useState } from "react";

import { Balance, EmptyState, TabNav } from "@/components/contacts/contact-bits";
import { contactHref, ContactList, contactsKey } from "@/components/contacts/contact-list";
import { QueryError } from "@/components/feedback/query-error";
import { CollectionDialog, useCashAccounts } from "@/components/finance/finance-dialogs";
import { PaymentRequestList } from "@/components/finance/payment-request-list";
import { RequirePermission } from "@/components/panel/settings-nav";
import { useSession } from "@/components/providers/session-provider";
import { Button } from "@/components/ui/button";
import { api, unwrap } from "@/lib/api/client";
import type { ContactListItem } from "@/lib/api/types";
import { displayPhone } from "@/lib/contacts";
import { formatMoney } from "@/lib/format";
import { can } from "@/lib/permissions";

const tabs = [
  { key: "bakiyeler", label: "Bakiyeler" },
  { key: "tahsilat-linkleri", label: "Tahsilat linkleri" },
  { key: "tedarikciler", label: "Tedarikçiler" },
  { key: "kasa", label: "Kasa ve banka" },
] as const;

type TabKey = (typeof tabs)[number]["key"];

/** Cari ve Kasa: alacak/borç listeleri, tahsilat linkleri ("Ödedim" bekleyenler), tedarikçiler, kasa bakiyeleri. */
export function FinanceHome() {
  const requested = useSearchParams().get("sekme");
  const active: TabKey = tabs.some((t) => t.key === requested) ? (requested as TabKey) : "bakiyeler";

  return (
    <div className="mx-auto flex max-w-6xl flex-col gap-6">
      <h1 className="text-2xl font-bold tracking-tight text-black">Cari ve Kasa</h1>
      <RequirePermission permission="viewFinance">
        <TabNav label="Cari ve Kasa" active={active} tabs={tabs.map((t) => ({ ...t, href: `?sekme=${t.key}` }))} />
        {active === "bakiyeler" && <Balances />}
        {active === "tahsilat-linkleri" && <PaymentRequestList />}
        {active === "tedarikciler" && <ContactList mode="supplier" />}
        {active === "kasa" && <CashAccounts />}
      </RequirePermission>
    </div>
  );
}

function BalanceGroup({ title, balance, empty }: { title: string; balance: "debtors" | "creditors"; empty: string }) {
  const { role } = useSession();
  const [collecting, setCollecting] = useState<ContactListItem | null>(null);
  const list = useQuery({
    queryKey: [...contactsKey, "balances", balance],
    queryFn: () => unwrap(api.GET("/api/contacts", { params: { query: { balance, sort: "balance", pageSize: 100 } } })),
  });
  const items = balance === "creditors" ? [...(list.data?.items ?? [])].reverse() : (list.data?.items ?? []);
  const total = items.reduce((sum, c) => sum + Math.abs(c.balance ?? 0), 0);

  return (
    <section className="flex flex-col gap-3">
      <h2 className="flex items-baseline justify-between gap-3 text-lg font-semibold text-black">
        {title}
        {items.length > 0 && <span className="text-base tabular-nums">{formatMoney(total)}</span>}
      </h2>
      {list.isPending ? (
        <p role="status" className="text-sm text-neutral-600">
          Yükleniyor…
        </p>
      ) : list.isError ? (
        <QueryError error={list.error} onRetry={list.refetch} />
      ) : items.length === 0 ? (
        <p className="text-sm text-neutral-600">{empty}</p>
      ) : (
        <ul className="flex flex-col divide-y divide-border border-y border-border">
          {items.map((c) => (
            <li key={c.id} className="flex items-center gap-3 py-2">
              <Link
                href={`${contactHref(c)}?sekme=cari`}
                className="flex min-h-11 min-w-0 flex-1 flex-col justify-center"
              >
                <span className="truncate text-sm font-semibold text-black">{c.displayName}</span>
                {c.phone && <span className="text-xs text-neutral-600 tabular-nums">{displayPhone(c.phone)}</span>}
              </Link>
              <Balance value={c.balance ?? 0} className="text-sm" />
              {balance === "debtors" && can(role, "manageFinance") && (
                <Button variant="outline" onClick={() => setCollecting(c)}>
                  Tahsilat al
                </Button>
              )}
            </li>
          ))}
        </ul>
      )}
      {collecting && (
        <CollectionDialog
          contact={{ id: collecting.id, displayName: collecting.displayName, balance: collecting.balance }}
          open
          onOpenChange={(v) => !v && setCollecting(null)}
        />
      )}
    </section>
  );
}

function Balances() {
  return (
    <div className="grid gap-8 lg:grid-cols-2">
      <BalanceGroup title="Alacaklarımız" balance="debtors" empty="Size borcu olan müşteri yok." />
      <BalanceGroup title="Borçlarımız" balance="creditors" empty="Kimseye borcunuz görünmüyor." />
    </div>
  );
}

function CashAccounts() {
  const accounts = useCashAccounts();
  if (accounts.isPending)
    return (
      <p role="status" className="text-sm text-neutral-600">
        Yükleniyor…
      </p>
    );
  if (accounts.isError)
    return (
      <QueryError error={accounts.error} onRetry={accounts.refetch} />
    );
  if (accounts.data.length === 0) return <EmptyState icon={<Wallet className="size-10" />} title="Kasa hesabı yok" />;
  return (
    <div className="flex flex-col gap-3">
      <ul className="grid gap-3 sm:grid-cols-2">
        {accounts.data.map((a) => (
          <li key={a.id} className="flex items-center gap-3 border border-border px-4 py-4">
            {a.type === "Cash" ? (
              <Wallet aria-hidden className="size-6 text-black" />
            ) : (
              <Landmark aria-hidden className="size-6 text-black" />
            )}
            <div className="flex flex-1 flex-col">
              <span className="text-sm font-semibold text-black">{a.name}</span>
              <span className="text-xs text-neutral-600">{a.type === "Cash" ? "Nakit" : "Banka"}</span>
            </div>
            <span className="text-lg font-bold text-black tabular-nums">{formatMoney(a.balance)}</span>
          </li>
        ))}
      </ul>
      <p className="text-xs text-neutral-600">
        Şimdilik kasaya tahsilatlar girer. Ödeme, gider ve hesaplar arası transfer yakında.
      </p>
    </div>
  );
}
