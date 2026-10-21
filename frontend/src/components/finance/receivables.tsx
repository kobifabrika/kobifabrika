"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { cn } from "cn";
import { BellRing, CalendarClock, HandCoins, Mail, MessageCircle, MessageSquareText, Search } from "lucide-react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { useState } from "react";
import { toast } from "sonner";

import { useDebounced } from "@/components/catalog/product-list";
import { contactHref } from "@/components/contacts/contact-list";
import { EmptyState } from "@/components/contacts/contact-bits";
import { QueryError } from "@/components/feedback/query-error";
import { FormMessage } from "@/components/forms/field";
import { CollectionDialog, CopyButton } from "@/components/finance/finance-dialogs";
import { useSession } from "@/components/providers/session-provider";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { API_URL, api, errorMessage, unwrap } from "@/lib/api/client";
import type { ReceivableRow, ReceivableTab } from "@/lib/api/types";
import { displayPhone } from "@/lib/contacts";
import { formatDayShort, formatMoney, formatRelative } from "@/lib/format";
import { can } from "@/lib/permissions";

export const receivablesKey = ["receivables"] as const;

const tabs: { key: string; tab: ReceivableTab; label: string }[] = [
  { key: "gecikmis", tab: "Overdue", label: "Gecikmiş" },
  { key: "bugun", tab: "Today", label: "Bugün" },
  { key: "bu-hafta", tab: "Week", label: "Bu hafta" },
  { key: "ileri", tab: "Later", label: "İleri tarihli" },
];

const countKey: Record<ReceivableTab, "overdue" | "today" | "week" | "later"> = {
  Overdue: "overdue",
  Today: "today",
  Week: "week",
  Later: "later",
};

function dueText(row: ReceivableRow, tab: ReceivableTab) {
  if (tab === "Overdue") return `${row.daysOverdue} gün gecikti · vade ${formatDayShort(row.oldestDueDate)}`;
  if (tab === "Today") return "Vadesi bugün";
  return `Vade ${formatDayShort(row.oldestDueDate)}`;
}

/** Cari ve Kasa → Vadeler: gecikmiş / bugün / bu hafta / ileri tarihli alacaklar (FIFO), hatırlatma ve tahsilat. */
export function ReceivablesTab() {
  const router = useRouter();
  const params = useSearchParams();
  const { role } = useSession();
  const manage = can(role, "manageFinance");
  const active = tabs.find((t) => t.key === params.get("filtre")) ?? tabs[0];
  const [search, setSearch] = useState("");
  const q = useDebounced(search.trim(), 300);
  const [reminding, setReminding] = useState<ReceivableRow | null>(null);
  const [collecting, setCollecting] = useState<ReceivableRow | null>(null);
  const list = useQuery({
    queryKey: [...receivablesKey, active.tab, q],
    queryFn: () =>
      unwrap(api.GET("/api/receivables", { params: { query: { tab: active.tab, search: q || undefined } } })),
  });

  return (
    <div className="flex flex-col gap-4">
      <div role="tablist" aria-label="Vade" className="grid grid-cols-2 gap-2 sm:flex sm:flex-wrap">
        {tabs.map((t) => {
          const count = list.data?.counts[countKey[t.tab]];
          const sum = list.data?.sums[countKey[t.tab]];
          return (
            <button
              key={t.key}
              role="tab"
              type="button"
              aria-selected={t.key === active.key}
              onClick={() => router.replace(`/panel/cari-kasa?sekme=vadeler&filtre=${t.key}`, { scroll: false })}
              className={cn(
                "flex min-h-14 flex-col items-start justify-center border px-3 py-2 text-left",
                t.key === active.key
                  ? "border-navy-950 bg-navy-950 text-white"
                  : "border-border bg-white text-black hover:border-navy-950",
              )}
            >
              <span className="text-sm font-semibold">
                {t.label}
                {count !== undefined && ` (${count})`}
              </span>
              {sum !== undefined && <span className="text-xs tabular-nums">{formatMoney(sum)}</span>}
            </button>
          );
        })}
      </div>
      <div className="relative max-w-md">
        <Search
          aria-hidden
          className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-neutral-600"
        />
        <Input
          type="search"
          aria-label="Müşteri ara"
          placeholder="Ad veya telefon ara"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          className="pl-9"
        />
      </div>
      {list.isPending ? (
        <p role="status" className="text-sm text-neutral-600">
          Yükleniyor…
        </p>
      ) : list.isError ? (
        <QueryError error={list.error} onRetry={list.refetch} />
      ) : list.data.items.length === 0 ? (
        <EmptyState
          icon={<CalendarClock className="size-10" />}
          title={q ? "Aramaya uyan alacak yok" : `${active.label} alacak yok`}
        >
          <p className="text-sm text-neutral-600">
            {"Veresiye satışların vadesi Ayarlar → Finans'taki varsayılan gün kadar sonradır."}
          </p>
        </EmptyState>
      ) : (
        <ul className="flex flex-col divide-y divide-border border-y border-border">
          {list.data.items.map((row) => (
            <li key={row.contactId} className="flex flex-col gap-3 py-3 md:flex-row md:items-center">
              <Link
                href={`${contactHref({ id: row.contactId, isCustomer: true })}?sekme=cari`}
                className="flex min-h-11 min-w-0 flex-1 flex-col justify-center"
              >
                <span className="truncate text-sm font-semibold text-black">{row.name}</span>
                <span className="text-xs text-neutral-600">
                  {dueText(row, active.tab)}
                  {row.phone && ` · ${displayPhone(row.phone)}`}
                </span>
                <span className="text-xs text-neutral-600">
                  {row.lastReminderAt
                    ? `Son hatırlatma: ${formatRelative(row.lastReminderAt)} (${reminderChannelName(row.lastReminderChannel)})`
                    : "Hatırlatma gönderilmedi"}
                </span>
              </Link>
              <span className="flex items-baseline justify-between gap-3 md:flex-col md:items-end">
                <span className="text-lg font-bold text-black tabular-nums">{formatMoney(row.dueAmount)}</span>
                {row.balance !== row.dueAmount && (
                  <span className="text-xs text-neutral-600 tabular-nums">Toplam borç {formatMoney(row.balance)}</span>
                )}
              </span>
              {manage && (
                <span className="grid grid-cols-2 gap-2 md:flex">
                  <Button onClick={() => setReminding(row)}>
                    <BellRing aria-hidden />
                    Hatırlat
                  </Button>
                  <Button variant="outline" onClick={() => setCollecting(row)}>
                    <HandCoins aria-hidden />
                    Tahsilat al
                  </Button>
                </span>
              )}
            </li>
          ))}
        </ul>
      )}
      {reminding && <ReminderSheet row={reminding} onClose={() => setReminding(null)} />}
      {collecting && (
        <CollectionDialog
          contact={{ id: collecting.contactId, displayName: collecting.name, balance: collecting.dueAmount }}
          open
          onOpenChange={(v) => !v && setCollecting(null)}
        />
      )}
    </div>
  );
}

/**
 * Hatırlatma penceresi. Pencere açılırken mesaj POST ile hazırlanır (tahsilat linki gerekirse burada, bir kez oluşur); WhatsApp
 * ve SMS düğmeleri tıklanmadan önce hazır gerçek linklerdir (iPhone Safari await sonrası açılan pencereyi engeller).
 * Tıklama kaydı fetch keepalive ile gider, link aynı anda açılır.
 */
export function ReminderSheet({
  row,
  onClose,
}: {
  row: Pick<ReceivableRow, "contactId" | "name">;
  onClose: () => void;
}) {
  const queryClient = useQueryClient();
  const message = useQuery({
    queryKey: [...receivablesKey, "message", row.contactId],
    queryFn: () =>
      unwrap(
        api.POST("/api/receivables/{contactId}/reminder-message", { params: { path: { contactId: row.contactId } } }),
      ),
    staleTime: 0,
    gcTime: 0,
    retry: false,
  });

  const sendEmail = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST("/api/receivables/{contactId}/email-reminder", { params: { path: { contactId: row.contactId } } }),
      ),
    onSuccess: async () => {
      toast.success("Hatırlatma e-postası gönderildi.");
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: receivablesKey }),
        queryClient.invalidateQueries({ queryKey: ["timeline"] }),
      ]);
    },
    onError: (e) => toast.error(errorMessage(e)),
  });

  const log = (channel: "WhatsApp" | "Sms") => {
    void fetch(`${API_URL}/api/receivables/${row.contactId}/reminders`, {
      method: "POST",
      credentials: "include",
      keepalive: true,
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ channel }),
    })
      .then(() =>
        Promise.all([
          queryClient.invalidateQueries({ queryKey: receivablesKey }),
          queryClient.invalidateQueries({ queryKey: ["timeline"] }),
        ]),
      )
      .catch(() => undefined);
  };

  return (
    <Sheet open onOpenChange={(v) => !v && onClose()}>
      <SheetContent
        side="bottom"
        className="max-h-[92vh] overflow-y-auto rounded-t-xl pb-[calc(1rem+env(safe-area-inset-bottom))] sm:mx-auto sm:max-w-lg"
      >
        <SheetHeader>
          <SheetTitle>Hatırlatma gönder</SheetTitle>
          <SheetDescription>{row.name}</SheetDescription>
        </SheetHeader>
        <div className="flex flex-col gap-4 px-4">
          {message.isPending ? (
            <p role="status" className="text-sm text-neutral-600">
              Mesaj hazırlanıyor…
            </p>
          ) : message.isError ? (
            <FormMessage>{errorMessage(message.error)}</FormMessage>
          ) : (
            <>
              <p className="text-sm text-neutral-700">
                Vadesi gelen:{" "}
                <span className="font-semibold text-black tabular-nums">{formatMoney(message.data.amount)}</span>
                {message.data.paymentLinkCreated && " · Tahsilat linki oluşturuldu"}
              </p>
              <p
                aria-label="Mesaj metni"
                className="border border-border bg-neutral-50 px-3 py-2 text-sm whitespace-pre-wrap text-black"
              >
                {message.data.text}
              </p>
              {!message.data.phone && (
                <p className="text-sm text-neutral-600">
                  Müşterinin telefonu yok; uygulamada alıcıyı seçmeniz gerekir.
                </p>
              )}
              <div className="grid grid-cols-2 gap-2">
                <Button asChild>
                  <a
                    href={message.data.whatsappUrl}
                    target="_blank"
                    rel="noopener noreferrer"
                    onClick={() => log("WhatsApp")}
                  >
                    <MessageCircle aria-hidden />
                    WhatsApp
                  </a>
                </Button>
                <Button variant="outline" asChild>
                  <a href={message.data.smsUrl} onClick={() => log("Sms")}>
                    <MessageSquareText aria-hidden />
                    SMS
                  </a>
                </Button>
                {message.data.hasEmail && (
                  <Button
                    variant="outline"
                    className="col-span-2"
                    disabled={sendEmail.isPending || sendEmail.isSuccess}
                    onClick={() => sendEmail.mutate()}
                  >
                    <Mail aria-hidden />
                    {sendEmail.isSuccess ? "E-posta gönderildi" : "E-posta gönder"}
                  </Button>
                )}
                <div className="col-span-2 grid">
                  <CopyButton text={message.data.text} label="Metni kopyala" />
                </div>
              </div>
            </>
          )}
        </div>
      </SheetContent>
    </Sheet>
  );
}

function reminderChannelName(channel: string | null | undefined): string {
  return channel === "Sms" ? "SMS" : channel === "Email" ? "E-posta" : "WhatsApp";
}
