"use client";

import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { cn } from "cn";
import { Download, FileSpreadsheet, Filter, Plus, Search, Users, X } from "lucide-react";
import Link from "next/link";
import { useEffect, useMemo, useState } from "react";
import { toast } from "sonner";

import { Balance, ContactActions, EmptyState, Notice, selectClass, TagList } from "@/components/contacts/contact-bits";
import { QueryError } from "@/components/feedback/query-error";
import { Field } from "@/components/forms/field";
import { useSession } from "@/components/providers/session-provider";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Sheet, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Switch } from "@/components/ui/switch";
import { API_URL, api, errorMessage, unwrap } from "@/lib/api/client";
import {
  sourceNames,
  type ContactFilter,
  type ContactListItem,
  type ContactSource,
  type Segment,
} from "@/lib/api/types";
import { displayPhone } from "@/lib/contacts";
import { formatDate } from "@/lib/format";
import { can } from "@/lib/permissions";

export const contactsKey = ["contacts"] as const;
const PAGE_SIZE = 25;

type Mode = "customer" | "supplier";

type Filters = {
  tagIds: string[];
  source: ContactSource | "";
  balance: "" | "debtors" | "creditors" | "nonzero";
  inactiveDays: "" | "30" | "60" | "90" | "180";
  archived: boolean;
};

const emptyFilters: Filters = { tagIds: [], source: "", balance: "", inactiveDays: "", archived: false };

function useDebounced<T>(value: T, ms: number) {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), ms);
    return () => clearTimeout(timer);
  }, [value, ms]);
  return debounced;
}

/** Kartın adresi: müşteriyse Müşteriler, sadece tedarikçiyse Cari ve Kasa altında. */
export function contactHref(c: Pick<ContactListItem, "id" | "isCustomer">) {
  return c.isCustomer ? `/panel/musteriler/${c.id}` : `/panel/cari-kasa/tedarikciler/${c.id}`;
}

/**
 * Müşteri (veya tedarikçi) listesi: arama, hazır/kayıtlı segment çipleri, filtre paneli, "segment olarak kaydet".
 * Masaüstünde tablo, telefonda kart (tek dokunuşla ara ve WhatsApp). Bakiye Personel'e gösterilmez (API null döner).
 */
export function ContactList({ mode = "customer" }: { mode?: Mode }) {
  const { role } = useSession();
  const [search, setSearch] = useState("");
  const [segment, setSegment] = useState<string>("");
  const [filters, setFilters] = useState<Filters>(emptyFilters);
  const [filterOpen, setFilterOpen] = useState(false);
  const debounced = useDebounced(search.trim(), 300);
  const finance = can(role, "viewFinance");

  const segments = useQuery({
    queryKey: ["segments"],
    queryFn: () => unwrap(api.GET("/api/segments")),
    enabled: mode === "customer",
  });
  const tags = useQuery({ queryKey: ["tags"], queryFn: () => unwrap(api.GET("/api/tags")) });

  const query = {
    search: debounced || undefined,
    type: mode,
    segment: segment || undefined,
    tagIds: filters.tagIds.length ? filters.tagIds : undefined,
    source: filters.source || undefined,
    balance: filters.balance || undefined,
    inactiveDays: filters.inactiveDays ? Number(filters.inactiveDays) : undefined,
    archived: filters.archived,
  };

  const list = useInfiniteQuery({
    queryKey: [...contactsKey, "list", query],
    queryFn: ({ pageParam }) =>
      unwrap(api.GET("/api/contacts", { params: { query: { ...query, page: pageParam, pageSize: PAGE_SIZE } } })),
    initialPageParam: 1,
    getNextPageParam: (last, pages) => (pages.length * PAGE_SIZE < last.total ? pages.length + 1 : undefined),
  });

  const items = list.data?.pages.flatMap((p) => p.items) ?? [];
  const total = list.data?.pages[0]?.total ?? 0;
  const activeFilterCount =
    filters.tagIds.length +
    (filters.source ? 1 : 0) +
    (filters.balance ? 1 : 0) +
    (filters.inactiveDays ? 1 : 0) +
    (filters.archived ? 1 : 0);
  const noFilter = !debounced && !segment && activeFilterCount === 0;
  const title = mode === "customer" ? "Müşteriler" : "Tedarikçiler";
  const newHref = mode === "customer" ? "/panel/musteriler/yeni" : "/panel/musteriler/yeni?tur=tedarikci";
  const exportHref = `${API_URL}/api/contacts/export.xlsx?type=${mode}${filters.archived ? "&archived=true" : ""}${debounced ? `&search=${encodeURIComponent(debounced)}` : ""}`;

  return (
    <div className="flex flex-col gap-5">
      <div className="flex flex-wrap items-center justify-between gap-3">
        {mode === "customer" ? (
          <h1 className="text-2xl font-bold tracking-tight text-black">
            {title}{" "}
            {list.data && <span className="text-base font-normal text-neutral-600 tabular-nums">({total})</span>}
          </h1>
        ) : (
          <h2 className="text-lg font-semibold text-black">
            {title} {list.data && <span className="text-sm font-normal text-neutral-600 tabular-nums">({total})</span>}
          </h2>
        )}
        <div className="flex flex-wrap gap-2">
          {mode === "customer" && can(role, "writeContacts") && (
            <Button asChild variant="outline">
              <Link href="/panel/musteriler/ice-aktar">
                <FileSpreadsheet aria-hidden />
                İçe aktar
              </Link>
            </Button>
          )}
          {can(role, "exportData") && (
            <Button asChild variant="outline">
              <a href={exportHref} download>
                <Download aria-hidden />
                <span className="sr-only sm:not-sr-only">Excel&apos;e aktar</span>
              </a>
            </Button>
          )}
          {can(role, "writeContacts") && (
            <Button asChild>
              <Link href={newHref}>
                <Plus aria-hidden />
                {mode === "customer" ? "Yeni müşteri" : "Yeni tedarikçi"}
              </Link>
            </Button>
          )}
        </div>
      </div>

      <div className="flex gap-2">
        <div className="relative flex-1">
          <Search
            aria-hidden
            className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-neutral-600"
          />
          <Input
            type="search"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Ad, telefon veya e-posta ara"
            aria-label={`${title} içinde ara`}
            className="pl-9"
          />
        </div>
        <Button variant="outline" onClick={() => setFilterOpen(true)} aria-label="Filtre">
          <Filter aria-hidden />
          <span className="hidden sm:inline">Filtre</span>
          {activeFilterCount > 0 && (
            <span className="rounded-full bg-black px-1.5 text-xs text-white tabular-nums">{activeFilterCount}</span>
          )}
        </Button>
      </div>

      {mode === "customer" && (segments.data?.length ?? 0) > 0 && (
        <SegmentChips segments={segments.data!} active={segment} onChange={setSegment} />
      )}

      {segment === "inactive-60" && (
        <Notice>Son ziyaret bilgisi satış ve randevularla dolacak; şimdilik bu liste boş görünebilir.</Notice>
      )}

      {list.isPending ? (
        <p role="status" className="text-sm text-neutral-600">
          Yükleniyor…
        </p>
      ) : list.isError ? (
        <QueryError error={list.error} onRetry={list.refetch} />
      ) : items.length === 0 ? (
        noFilter ? (
          <EmptyState
            icon={<Users className="size-10" />}
            title={mode === "customer" ? "Henüz müşteri yok" : "Henüz tedarikçi yok"}
          >
            {can(role, "writeContacts") && (
              <div className="flex flex-wrap gap-2">
                <Button asChild>
                  <Link href={newHref}>{mode === "customer" ? "İlk müşteriyi ekle" : "Tedarikçi ekle"}</Link>
                </Button>
                {mode === "customer" && (
                  <Button asChild variant="outline">
                    <Link href="/panel/musteriler/ice-aktar">Excel&apos;den aktar</Link>
                  </Button>
                )}
              </div>
            )}
          </EmptyState>
        ) : (
          <p className="text-sm text-neutral-600">Bu aramaya uyan kayıt yok.</p>
        )
      ) : (
        <>
          <ContactTable items={items} finance={finance} />
          <ContactCards items={items} finance={finance} />
          {list.hasNextPage && (
            <Button
              variant="outline"
              className="self-center"
              onClick={() => list.fetchNextPage()}
              disabled={list.isFetchingNextPage}
            >
              {list.isFetchingNextPage ? "Yükleniyor…" : "Daha fazla göster"}
            </Button>
          )}
        </>
      )}

      <FilterSheet
        key={filterOpen ? "open" : "closed"}
        open={filterOpen}
        onOpenChange={setFilterOpen}
        filters={filters}
        onApply={setFilters}
        tags={tags.data ?? []}
        finance={finance}
        canSave={mode === "customer" && can(role, "writeContacts")}
        mode={mode}
        onSaved={(id) => {
          setFilters(emptyFilters);
          setSegment(id);
        }}
      />
    </div>
  );
}

function SegmentChips({
  segments,
  active,
  onChange,
}: {
  segments: Segment[];
  active: string;
  onChange: (v: string) => void;
}) {
  const chip = (key: string, label: string) => (
    <button
      key={key || "all"}
      type="button"
      onClick={() => onChange(key)}
      aria-pressed={active === key}
      className={cn(
        "min-h-11 shrink-0 rounded-full border px-4 text-sm font-medium whitespace-nowrap",
        active === key
          ? "border-black bg-black text-white"
          : "border-border bg-background text-black hover:border-black",
      )}
    >
      {label}
    </button>
  );
  return (
    <div className="-mx-4 overflow-x-auto px-4 lg:mx-0 lg:px-0" role="group" aria-label="Segmentler">
      <div className="flex min-w-max gap-2">
        {chip("", "Tümü")}
        {segments.map((s) => chip(s.code ?? s.id!, s.name))}
      </div>
    </div>
  );
}

function ContactTable({ items, finance }: { items: ContactListItem[]; finance: boolean }) {
  return (
    <div className="hidden overflow-x-auto border-y border-border md:block">
      <table className="w-full text-left text-sm">
        <thead>
          <tr className="border-b border-border text-xs text-neutral-600">
            <th className="py-2 pr-3 font-semibold">Ad</th>
            <th className="py-2 pr-3 font-semibold">Telefon</th>
            <th className="py-2 pr-3 font-semibold">Etiketler</th>
            <th className="py-2 pr-3 font-semibold">Kaynak</th>
            {finance && <th className="py-2 pr-3 text-right font-semibold">Bakiye</th>}
            <th className="py-2 font-semibold">Kayıt</th>
          </tr>
        </thead>
        <tbody>
          {items.map((c) => (
            <tr key={c.id} className="border-b border-border last:border-0 hover:bg-neutral-50">
              <td className="py-2 pr-3">
                <Link
                  href={contactHref(c)}
                  className="flex min-h-11 flex-col justify-center font-semibold text-black hover:underline"
                >
                  {c.displayName}
                  {c.companyName && c.companyName !== c.displayName && (
                    <span className="text-xs font-normal text-neutral-600">{c.companyName}</span>
                  )}
                </Link>
              </td>
              <td className="py-2 pr-3 whitespace-nowrap text-black tabular-nums">{displayPhone(c.phone)}</td>
              <td className="py-2 pr-3">
                <TagList tags={c.tags} />
              </td>
              <td className="py-2 pr-3 text-neutral-700">{c.source ? sourceNames[c.source] : ""}</td>
              {finance && (
                <td className="py-2 pr-3 text-right whitespace-nowrap">
                  {c.balance != null && c.balance !== 0 && <Balance value={c.balance} />}
                </td>
              )}
              <td className="py-2 whitespace-nowrap text-neutral-600">{formatDate(c.createdAt)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function ContactCards({ items, finance }: { items: ContactListItem[]; finance: boolean }) {
  return (
    <ul className="flex flex-col divide-y divide-border border-y border-border md:hidden">
      {items.map((c) => (
        <li key={c.id} className="flex items-center gap-3 py-3">
          <Link href={contactHref(c)} className="flex min-h-11 min-w-0 flex-1 flex-col gap-1">
            <span className="truncate text-base font-semibold text-black">{c.displayName}</span>
            {c.phone && <span className="text-sm text-neutral-700 tabular-nums">{displayPhone(c.phone)}</span>}
            {(c.tags.length > 0 || (finance && !!c.balance)) && (
              <span className="flex flex-wrap items-center gap-2">
                <TagList tags={c.tags} max={2} />
                {finance && c.balance != null && c.balance !== 0 && <Balance value={c.balance} className="text-sm" />}
              </span>
            )}
          </Link>
          <ContactActions phone={c.phone} compact />
        </li>
      ))}
    </ul>
  );
}

const inactiveOptions = [
  { value: "30", label: "30 gündür gelmeyenler" },
  { value: "60", label: "60 gündür gelmeyenler" },
  { value: "90", label: "90 gündür gelmeyenler" },
  { value: "180", label: "6 aydır gelmeyenler" },
] as const;

function FilterSheet({
  open,
  onOpenChange,
  filters,
  onApply,
  tags,
  finance,
  canSave,
  mode,
  onSaved,
}: {
  open: boolean;
  onOpenChange: (v: boolean) => void;
  filters: Filters;
  onApply: (f: Filters) => void;
  tags: { id: string; name: string }[];
  finance: boolean;
  canSave: boolean;
  mode: Mode;
  onSaved: (segmentId: string) => void;
}) {
  const queryClient = useQueryClient();
  // Her açılışta (key değişir) güncel filtrelerle başlar.
  const [draft, setDraft] = useState(filters);
  const [segmentName, setSegmentName] = useState("");

  const definition = useMemo<ContactFilter>(
    () => ({
      type: mode,
      tagIds: draft.tagIds.length ? draft.tagIds : null,
      source: draft.source || null,
      balance: draft.balance || null,
      inactiveDays: draft.inactiveDays ? Number(draft.inactiveDays) : null,
    }),
    [draft, mode],
  );
  const hasCriteria = draft.tagIds.length > 0 || !!draft.source || !!draft.balance || !!draft.inactiveDays;

  const save = useMutation({
    mutationFn: () => unwrap(api.POST("/api/segments", { body: { name: segmentName.trim(), filter: definition } })),
    onSuccess: async (segment) => {
      toast.success(`"${segment.name}" segmenti kaydedildi.`);
      await queryClient.invalidateQueries({ queryKey: ["segments"] });
      setSegmentName("");
      onOpenChange(false);
      onSaved(segment.id!);
    },
    onError: (e) => toast.error(errorMessage(e)),
  });

  const toggleTag = (id: string) =>
    setDraft((d) => ({ ...d, tagIds: d.tagIds.includes(id) ? d.tagIds.filter((t) => t !== id) : [...d.tagIds, id] }));

  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="right" className="w-full max-w-sm gap-0 overflow-y-auto">
        <SheetHeader>
          <SheetTitle>Filtre</SheetTitle>
          <SheetDescription>
            Seçtiklerinizin hepsine uyan kayıtlar listelenir; etiketlerden herhangi biri yeterli.
          </SheetDescription>
        </SheetHeader>
        <div className="flex flex-col gap-5 px-4 pb-4">
          {tags.length > 0 && (
            <fieldset className="flex flex-col gap-2">
              <legend className="mb-1 text-sm font-semibold text-black">Etiketler</legend>
              {tags.map((t) => (
                <label key={t.id} className="flex min-h-11 items-center gap-3 text-sm text-black">
                  <Checkbox checked={draft.tagIds.includes(t.id)} onCheckedChange={() => toggleTag(t.id)} />
                  {t.name}
                </label>
              ))}
            </fieldset>
          )}
          <Field label="Kaynak">
            <select
              className={selectClass}
              value={draft.source}
              onChange={(e) => setDraft({ ...draft, source: e.target.value as ContactSource | "" })}
            >
              <option value="">Hepsi</option>
              {Object.entries(sourceNames).map(([value, label]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </select>
          </Field>
          {finance && (
            <Field label="Bakiye">
              <select
                className={selectClass}
                value={draft.balance}
                onChange={(e) => setDraft({ ...draft, balance: e.target.value as Filters["balance"] })}
              >
                <option value="">Hepsi</option>
                <option value="debtors">Bize borcu olanlar (Alacak)</option>
                <option value="creditors">Bizim borçlu olduklarımız (Borç)</option>
                <option value="nonzero">Bakiyesi olanlar</option>
              </select>
            </Field>
          )}
          <Field label="Son ziyaret">
            <select
              className={selectClass}
              value={draft.inactiveDays}
              onChange={(e) => setDraft({ ...draft, inactiveDays: e.target.value as Filters["inactiveDays"] })}
            >
              <option value="">Hepsi</option>
              {inactiveOptions.map((o) => (
                <option key={o.value} value={o.value}>
                  {o.label}
                </option>
              ))}
            </select>
          </Field>
          <label className="flex min-h-11 items-center justify-between gap-3 text-sm font-semibold text-black">
            Arşivdekileri göster
            <Switch checked={draft.archived} onCheckedChange={(v) => setDraft({ ...draft, archived: v })} />
          </label>

          {canSave && hasCriteria && (
            <div className="flex flex-col gap-2 border-t border-border pt-4">
              <Field
                label="Segment olarak kaydet"
                hint="Bu filtreyi listenin üstünde tek dokunuşla açmak için ad verin."
              >
                <Input
                  value={segmentName}
                  onChange={(e) => setSegmentName(e.target.value)}
                  placeholder="ör. Instagram VIP"
                  maxLength={100}
                />
              </Field>
              <Button variant="outline" disabled={!segmentName.trim() || save.isPending} onClick={() => save.mutate()}>
                Segmenti kaydet
              </Button>
            </div>
          )}
        </div>
        <SheetFooter className="flex-row gap-2 border-t border-border">
          <Button variant="ghost" onClick={() => setDraft(emptyFilters)}>
            <X aria-hidden />
            Temizle
          </Button>
          <Button
            className="flex-1"
            onClick={() => {
              onApply(draft);
              onOpenChange(false);
            }}
          >
            Uygula
          </Button>
        </SheetFooter>
      </SheetContent>
    </Sheet>
  );
}
