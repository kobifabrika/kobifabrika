"use client";

import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  Archive,
  ArchiveRestore,
  ArrowLeft,
  BellRing,
  FileText,
  HandCoins,
  Link2,
  Mail,
  MoreHorizontal,
  Pencil,
  ShieldCheck,
  StickyNote,
  Trash2,
  UserPlus,
} from "lucide-react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { useState, type ReactNode } from "react";
import { toast } from "sonner";

import { Balance, ContactActions, Notice, TabNav, TagList } from "@/components/contacts/contact-bits";
import { contactsKey } from "@/components/contacts/contact-list";
import { ReminderList } from "@/components/contacts/reminders";
import { LedgerPanel } from "@/components/finance/ledger-panel";
import { FormMessage } from "@/components/forms/field";
import { useSession } from "@/components/providers/session-provider";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { Textarea } from "@/components/ui/textarea";
import { ApiError, api, errorMessage, unwrap } from "@/lib/api/client";
import { consentNames, consentSourceNames, sourceNames, type Contact, type TimelineItem } from "@/lib/api/types";
import { displayPhone } from "@/lib/contacts";
import { formatDate, formatDateTime, formatDay, formatMoney } from "@/lib/format";
import { can } from "@/lib/permissions";

type TabKey = "ozet" | "zaman" | "cari" | "notlar" | "hatirlatmalar";

/** Müşteri/tedarikçi kartı. basePath: /panel/musteriler veya /panel/cari-kasa/tedarikciler. */
export function ContactDetail({ id, basePath }: { id: string; basePath: string }) {
  const { role } = useSession();
  const params = useSearchParams();
  const contact = useQuery({
    queryKey: [...contactsKey, "detail", id],
    queryFn: () => unwrap(api.GET("/api/contacts/{id}", { params: { path: { id } } })),
  });

  if (contact.isPending)
    return (
      <p role="status" className="text-sm text-neutral-600">
        Yükleniyor…
      </p>
    );
  if (contact.isError) {
    return (
      <div className="flex flex-col gap-3">
        <FormMessage>
          {contact.error instanceof ApiError && contact.error.status === 404
            ? "Kayıt bulunamadı."
            : errorMessage(contact.error)}
        </FormMessage>
        <Link href={basePath} className="text-sm font-semibold text-black underline">
          Listeye dön
        </Link>
      </div>
    );
  }

  const c = contact.data;
  const finance = can(role, "viewFinance");
  const tabs: { key: TabKey; label: string }[] = [
    { key: "ozet", label: "Özet" },
    { key: "zaman", label: "Zaman çizelgesi" },
    ...(finance ? [{ key: "cari" as const, label: "Cari" }] : []),
    { key: "notlar", label: "Notlar" },
    { key: "hatirlatmalar", label: "Hatırlatmalar" },
  ];
  const requested = params.get("sekme") as TabKey | null;
  const active: TabKey = tabs.some((t) => t.key === requested) ? requested! : "ozet";
  const listHref = basePath.includes("tedarikciler") ? "/panel/cari-kasa?sekme=tedarikciler" : basePath;

  return (
    <div className="mx-auto flex max-w-5xl flex-col gap-5">
      <Link
        href={listHref}
        className="flex min-h-11 w-fit items-center gap-2 text-sm font-semibold text-neutral-700 hover:text-black"
      >
        <ArrowLeft aria-hidden className="size-4" />
        {basePath.includes("tedarikciler") ? "Tedarikçiler" : "Müşteriler"}
      </Link>

      <header className="flex flex-col gap-3">
        <div className="flex items-start justify-between gap-3">
          <div className="flex min-w-0 flex-col gap-1">
            <h1 className="text-2xl font-bold tracking-tight break-words text-black">{c.displayName}</h1>
            <p className="flex flex-wrap items-center gap-x-3 gap-y-1 text-sm text-neutral-700">
              {c.isCustomer && c.isSupplier ? "Müşteri ve tedarikçi" : c.isSupplier ? "Tedarikçi" : "Müşteri"}
              {c.companyName && c.companyName !== c.displayName && <span>{c.companyName}</span>}
              {c.source && <span>Kaynak: {sourceNames[c.source]}</span>}
              {c.isArchived && <span className="font-semibold text-black">Arşivde</span>}
            </p>
            <TagList tags={c.tags} max={10} />
          </div>
          <ContactMenu contact={c} basePath={basePath} />
        </div>
        <div className="flex flex-wrap items-center gap-3">
          {c.phone && <span className="text-base font-semibold text-black tabular-nums">{displayPhone(c.phone)}</span>}
          <ContactActions phone={c.phone} />
          {c.email && (
            <a
              href={`mailto:${c.email}`}
              className="inline-flex min-h-11 items-center gap-2 rounded-lg border border-border px-3 text-sm font-semibold text-black hover:border-black"
            >
              <Mail aria-hidden className="size-4" />
              E-posta
            </a>
          )}
          {finance && c.balance != null && <Balance value={c.balance} className="text-base" />}
        </div>
        {c.notes && <Notice>{c.notes}</Notice>}
      </header>

      <TabNav label="Kart sekmeleri" active={active} tabs={tabs.map((t) => ({ ...t, href: `?sekme=${t.key}` }))} />

      {active === "ozet" && <Overview contact={c} />}
      {active === "zaman" && <Timeline id={c.id} />}
      {active === "cari" && finance && <LedgerPanel contact={c} />}
      {active === "notlar" && <Notes id={c.id} />}
      {active === "hatirlatmalar" && <ReminderList contactId={c.id} contactName={c.displayName} />}
    </div>
  );
}

function ContactMenu({ contact: c, basePath }: { contact: Contact; basePath: string }) {
  const { role } = useSession();
  const router = useRouter();
  const queryClient = useQueryClient();
  const refresh = () => queryClient.invalidateQueries({ queryKey: contactsKey });

  const archive = useMutation({
    mutationFn: () =>
      unwrap(
        c.isArchived
          ? api.POST("/api/contacts/{id}/restore", { params: { path: { id: c.id } } })
          : api.POST("/api/contacts/{id}/archive", { params: { path: { id: c.id } } }),
      ),
    onSuccess: async () => {
      toast.success(c.isArchived ? "Arşivden çıkarıldı." : "Arşivlendi. Listede 'Arşivdekileri göster' ile bulunur.");
      await refresh();
    },
    onError: (e) => toast.error(errorMessage(e)),
  });
  const remove = useMutation({
    mutationFn: () => unwrap(api.DELETE("/api/contacts/{id}", { params: { path: { id: c.id } } })),
    onSuccess: async () => {
      toast.success(`${c.displayName} silindi.`);
      await refresh();
      router.push(basePath.includes("tedarikciler") ? "/panel/cari-kasa?sekme=tedarikciler" : "/panel/musteriler");
    },
    onError: (e) => toast.error(errorMessage(e)),
  });

  const write = can(role, "writeContacts");
  const manage = can(role, "manageContacts");
  if (!write) return null;

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="outline" size="icon" aria-label="Diğer işlemler">
          <MoreHorizontal aria-hidden />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="min-w-52">
        <DropdownMenuItem asChild className="min-h-11">
          <Link href={`${basePath}/${c.id}/duzenle`}>
            <Pencil aria-hidden />
            Düzenle
          </Link>
        </DropdownMenuItem>
        {manage && (
          <DropdownMenuItem className="min-h-11" onSelect={() => archive.mutate()}>
            {c.isArchived ? <ArchiveRestore aria-hidden /> : <Archive aria-hidden />}
            {c.isArchived ? "Arşivden çıkar" : "Arşivle"}
          </DropdownMenuItem>
        )}
        {manage && !c.hasLedgerEntries && (
          <DropdownMenuItem
            className="min-h-11 text-danger-600"
            onSelect={() => window.confirm(`${c.displayName} silinsin mi? Bu işlem geri alınamaz.`) && remove.mutate()}
          >
            <Trash2 aria-hidden />
            Sil
          </DropdownMenuItem>
        )}
        {manage && c.hasLedgerEntries && (
          <p className="px-2 py-2 text-xs text-neutral-600">
            Cari hareketi olduğu için silinemez; arşivleyebilirsiniz.
          </p>
        )}
      </DropdownMenuContent>
    </DropdownMenu>
  );
}

function Row({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="flex flex-col gap-0.5 border-b border-border py-3 sm:flex-row sm:gap-4">
      <dt className="w-40 shrink-0 text-sm text-neutral-600">{label}</dt>
      <dd className="text-sm text-black">{children}</dd>
    </div>
  );
}

function Overview({ contact: c }: { contact: Contact }) {
  const address = [c.address, c.district, c.city].filter(Boolean).join(", ");
  return (
    <div className="grid gap-8 lg:grid-cols-2">
      <dl>
        <Row label="Telefon">{c.phone ? displayPhone(c.phone) : "—"}</Row>
        <Row label="E-posta">{c.email ?? "—"}</Row>
        <Row label="Doğum günü">{c.birthDate ? formatDay(c.birthDate) : "—"}</Row>
        <Row label="Adres">{address || "—"}</Row>
        {(c.taxNumber || c.taxOffice) && (
          <Row label="Vergi bilgisi">{[c.taxOffice, c.taxNumber].filter(Boolean).join(" · ")}</Row>
        )}
        <Row label="Kayıt tarihi">{formatDate(c.createdAt)}</Row>
        {c.lastVisitAt && <Row label="Son ziyaret">{formatDate(c.lastVisitAt)}</Row>}
      </dl>
      <section className="flex flex-col gap-2">
        <h2 className="flex items-center gap-2 text-base font-semibold text-black">
          <ShieldCheck aria-hidden className="size-5" />
          İletişim izinleri (KVKK)
        </h2>
        <ul className="flex flex-col">
          {c.consents.map((x) => (
            <li key={x.channel} className="flex items-center justify-between gap-3 border-b border-border py-3">
              <span className="text-sm text-black">{consentNames[x.channel]}</span>
              <span className="text-right text-sm">
                <span className="font-semibold text-black">{x.granted ? "✓ İzin var" : "İzin yok"}</span>
                {x.at && x.source && (
                  <span className="block text-xs text-neutral-600">
                    {formatDateTime(x.at)}, {consentSourceNames[x.source]}
                  </span>
                )}
              </span>
            </li>
          ))}
        </ul>
        <p className="text-xs text-neutral-600">
          İzinleri değiştirmek için Düzenle&apos;yi kullanın; her değişiklik tarih ve kaynakla saklanır.
        </p>
      </section>
    </div>
  );
}

const timelineIcons: Record<string, typeof StickyNote> = {
  created: UserPlus,
  note: StickyNote,
  reminder: BellRing,
  consent: ShieldCheck,
  ledger: FileText,
  collection: HandCoins,
  payment_request: Link2,
  payment_claimed: Link2,
  payment_confirmed: HandCoins,
  statement_shared: FileText,
};

function Timeline({ id }: { id: string }) {
  const timeline = useInfiniteQuery({
    queryKey: ["timeline", id],
    queryFn: ({ pageParam }) =>
      unwrap(
        api.GET("/api/contacts/{id}/timeline", { params: { path: { id }, query: { before: pageParam ?? undefined } } }),
      ),
    initialPageParam: null as string | null,
    getNextPageParam: (last) => last.nextBefore ?? undefined,
  });

  if (timeline.isPending)
    return (
      <p role="status" className="text-sm text-neutral-600">
        Yükleniyor…
      </p>
    );
  if (timeline.isError)
    return (
      <p role="alert" className="text-sm text-danger-600">
        {errorMessage(timeline.error)}
      </p>
    );
  const items = timeline.data.pages.flatMap((p) => p.items);

  return (
    <div className="flex flex-col gap-3">
      <ol className="flex flex-col">
        {items.map((item, i) => (
          <TimelineRow key={`${item.kind}-${item.refId ?? i}-${item.at}`} item={item} />
        ))}
      </ol>
      <p className="text-xs text-neutral-600">
        Satışlar, randevular ve gönderilen mesajlar ilgili modüllerle burada görünecek.
      </p>
      {timeline.hasNextPage && (
        <Button
          variant="outline"
          className="self-start"
          onClick={() => timeline.fetchNextPage()}
          disabled={timeline.isFetchingNextPage}
        >
          Daha eski
        </Button>
      )}
    </div>
  );
}

function TimelineRow({ item }: { item: TimelineItem }) {
  const Icon = timelineIcons[item.kind] ?? FileText;
  return (
    <li className="flex gap-3 border-b border-border py-3">
      <span className="mt-0.5 flex size-8 shrink-0 items-center justify-center rounded-full bg-neutral-100">
        <Icon aria-hidden className="size-4 text-black" />
      </span>
      <div className="flex min-w-0 flex-1 flex-col gap-0.5">
        <span className="flex flex-wrap items-baseline justify-between gap-x-3">
          <span className="text-sm font-semibold text-black">{item.title}</span>
          {item.amount != null && (
            <span className="text-sm font-semibold text-black tabular-nums">
              {item.kind === "ledger" || item.kind === "collection" ? (item.amount > 0 ? "Borç " : "Alacak ") : ""}
              {formatMoney(Math.abs(item.amount))}
            </span>
          )}
        </span>
        {item.detail && <span className="text-sm whitespace-pre-line text-neutral-700">{item.detail}</span>}
        <span className="text-xs text-neutral-600">{formatDateTime(item.at)}</span>
      </div>
    </li>
  );
}

function Notes({ id }: { id: string }) {
  const { me, role } = useSession();
  const queryClient = useQueryClient();
  const [body, setBody] = useState("");
  const notes = useQuery({
    queryKey: ["notes", id],
    queryFn: () => unwrap(api.GET("/api/contacts/{id}/notes", { params: { path: { id } } })),
  });
  const refresh = async () => {
    await queryClient.invalidateQueries({ queryKey: ["notes", id] });
    await queryClient.invalidateQueries({ queryKey: ["timeline", id] });
  };

  const add = useMutation({
    mutationFn: () =>
      unwrap(api.POST("/api/contacts/{id}/notes", { params: { path: { id } }, body: { body: body.trim() } })),
    onSuccess: async () => {
      setBody("");
      await refresh();
    },
    onError: (e) => toast.error(errorMessage(e)),
  });
  const remove = useMutation({
    mutationFn: (noteId: string) =>
      unwrap(api.DELETE("/api/contacts/{id}/notes/{noteId}", { params: { path: { id, noteId } } })),
    onSuccess: refresh,
    onError: (e) => toast.error(errorMessage(e)),
  });

  return (
    <div className="flex max-w-2xl flex-col gap-4">
      {can(role, "writeContacts") && (
        <div className="flex flex-col gap-2">
          <Textarea
            rows={3}
            value={body}
            onChange={(e) => setBody(e.target.value)}
            placeholder="Not yazın (ör. Saçını kısa istiyor, boya 6.0)"
            maxLength={2000}
            aria-label="Yeni not"
          />
          <Button className="self-start" onClick={() => add.mutate()} disabled={!body.trim() || add.isPending}>
            Notu ekle
          </Button>
        </div>
      )}
      {notes.data?.length === 0 && <p className="text-sm text-neutral-600">Henüz not yok.</p>}
      <ul className="flex flex-col">
        {notes.data?.map((n) => (
          <li key={n.id} className="flex gap-3 border-b border-border py-3">
            <div className="flex min-w-0 flex-1 flex-col gap-1">
              <p className="text-sm whitespace-pre-line text-black">{n.body}</p>
              <span className="text-xs text-neutral-600">
                {n.authorName ?? "—"} · {formatDateTime(n.createdAt)}
              </span>
            </div>
            {can(role, "writeContacts") && (n.authorId === me.user.id || can(role, "manageContacts")) && (
              <Button
                variant="ghost"
                size="icon"
                aria-label="Notu sil"
                onClick={() => window.confirm("Not silinsin mi?") && remove.mutate(n.id)}
              >
                <Trash2 aria-hidden />
              </Button>
            )}
          </li>
        ))}
      </ul>
    </div>
  );
}
