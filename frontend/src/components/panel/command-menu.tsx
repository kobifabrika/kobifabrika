"use client";

import { useQuery } from "@tanstack/react-query";
import { Command as CommandPrimitive } from "cmdk";
import {
  Activity,
  BellRing,
  Clock,
  FileSpreadsheet,
  PackagePlus,
  Search,
  UserRound,
  Users,
  type LucideIcon,
} from "lucide-react";
import { useRouter } from "next/navigation";
import { useEffect, useMemo, useState } from "react";

import { useSession } from "@/components/providers/session-provider";
import { CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList } from "@/components/ui/command";
import { Dialog, DialogContent, DialogDescription, DialogTitle } from "@/components/ui/dialog";
import { api, unwrap } from "@/lib/api/client";
import { panelHref, visibleNavGroups, visibleQuickActions } from "@/lib/navigation";
import { can } from "@/lib/permissions";

type PageLink = { label: string; href: string; icon: LucideIcon; keywords?: string };

const groupLabels: Record<string, string> = { customer: "Müşteriler", product: "Ürünler", sale: "Satışlar" };

/** Türkçe büyük/küçük harf ve aksan duyarsız eşleşme ("ayar" → "Ayarlar", "calisma" → "Çalışma"). */
export function normalizeSearch(text: string) {
  return text
    .toLocaleLowerCase("tr-TR")
    .replace(/ı/g, "i")
    .normalize("NFD")
    .replace(/\p{Mn}/gu, "");
}

function useDebounced<T>(value: T, ms: number) {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), ms);
    return () => clearTimeout(timer);
  }, [value, ms]);
  return debounced;
}

/**
 * Ctrl+K / ⌘K genel arama: sayfalar ve hızlı işlemler istemcide eşleşir; müşteriler (ad, telefon, e-posta),
 * ürünler (ad, barkod) ve satışlar (SAT numarası, müşteri) API'den gelir (GET /api/search).
 */
export function CommandMenu() {
  const router = useRouter();
  const { role, business } = useSession();
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const debounced = useDebounced(query.trim(), 250);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key.toLowerCase() === "k" && (e.ctrlKey || e.metaKey)) {
        e.preventDefault();
        setOpen((o) => !o);
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, []);

  const pages = useMemo<PageLink[]>(() => {
    const links: PageLink[] = visibleNavGroups(role, business.modules)
      .flatMap((g) => g.items)
      .map((item) => ({ label: item.label, href: panelHref(item.slug), icon: item.icon }));
    for (const action of visibleQuickActions(role, business.modules)) {
      links.push({ label: action.label, href: action.href, icon: action.icon, keywords: action.keywords });
    }
    if (can(role, "manageCatalog"))
      links.push({
        label: "Yeni ürün/hizmet",
        href: "/panel/urunler/yeni",
        icon: PackagePlus,
        keywords: "ürün hizmet ekle katalog",
      });
    if (can(role, "writeContacts")) {
      links.push({
        label: "Excel'den müşteri aktar",
        href: "/panel/musteriler/ice-aktar",
        icon: FileSpreadsheet,
        keywords: "içe aktar excel",
      });
    }
    links.push({ label: "Hatırlatmalar", href: "/panel/hatirlatmalar", icon: BellRing, keywords: "görev hatırlatma" });
    if (can(role, "viewSettings"))
      links.push({
        label: "Çalışma saatleri",
        href: "/panel/ayarlar/calisma-saatleri",
        icon: Clock,
        keywords: "ayarlar",
      });
    if (can(role, "manageTeam"))
      links.push({
        label: "Ekip ve davetler",
        href: "/panel/ayarlar/ekip",
        icon: Users,
        keywords: "ayarlar personel kullanıcı davet",
      });
    if (can(role, "viewAuditLog"))
      links.push({
        label: "Aktivite günlüğü",
        href: "/panel/ayarlar/aktivite",
        icon: Activity,
        keywords: "ayarlar log kayıt",
      });
    links.push({ label: "Hesabım", href: "/panel/hesabim", icon: UserRound, keywords: "profil şifre" });
    // Aynı adres bir kez (liste anahtarı ve cmdk değeri adres; hızlı işlemle elle eklenen çakışınca React uyarı veriyordu).
    return links.filter((link, i) => links.findIndex((l) => l.href === link.href) === i);
  }, [role, business.modules]);

  const matchedPages = useMemo(() => {
    const q = normalizeSearch(query.trim());
    return q ? pages.filter((p) => normalizeSearch(`${p.label} ${p.keywords ?? ""}`).includes(q)) : pages;
  }, [pages, query]);

  const search = useQuery({
    queryKey: ["search", debounced],
    queryFn: () => unwrap(api.GET("/api/search", { params: { query: { q: debounced } } })),
    enabled: open && debounced.length >= 2,
  });

  const go = (href: string) => {
    setOpen(false);
    setQuery("");
    router.push(href);
  };

  const groups = search.data?.groups ?? [];

  return (
    <>
      <button
        type="button"
        onClick={() => setOpen(true)}
        className="hidden h-9 w-full max-w-md items-center gap-2 rounded-lg border border-white/15 bg-white/10 px-3 text-left text-sm text-neutral-300 transition-colors outline-none hover:bg-white/15 focus-visible:border-brand focus-visible:ring-1 focus-visible:ring-brand lg:flex"
      >
        <Search aria-hidden className="size-4" />
        <span className="flex-1">Müşteri, ürün, satış veya sayfa ara…</span>
        <kbd className="rounded border border-white/20 px-1.5 font-sans text-xs text-neutral-300">Ctrl K</kbd>
      </button>
      <button
        type="button"
        onClick={() => setOpen(true)}
        aria-label="Ara"
        className="flex size-11 items-center justify-center rounded-lg text-foreground hover:bg-white/10 lg:hidden"
      >
        <Search aria-hidden className="size-5" />
      </button>

      <Dialog open={open} onOpenChange={setOpen}>
        <DialogContent className="top-4 translate-y-0 overflow-hidden p-0 sm:top-1/4" showCloseButton={false}>
          <DialogTitle className="sr-only">Ara</DialogTitle>
          <DialogDescription className="sr-only">Müşteri, ürün, satış veya sayfa arayın.</DialogDescription>
          <CommandPrimitive shouldFilter={false} className="flex flex-col bg-popover p-1">
            <CommandInput value={query} onValueChange={setQuery} placeholder="Müşteri, ürün, satış veya sayfa ara…" />
            <CommandList className="max-h-[60vh]">
              <CommandEmpty>Sonuç bulunamadı.</CommandEmpty>
              {matchedPages.length > 0 && (
                <CommandGroup heading="Sayfalar">
                  {matchedPages.map(({ label, href, icon: Icon }) => (
                    <CommandItem key={href} value={href} onSelect={() => go(href)}>
                      <Icon aria-hidden className="text-neutral-600" />
                      {label}
                    </CommandItem>
                  ))}
                </CommandGroup>
              )}
              {groups.map((group) => (
                <CommandGroup key={group.type} heading={groupLabels[group.type] ?? group.type}>
                  {group.items.map((item) => (
                    <CommandItem key={item.id} value={`${group.type}-${item.id}`} onSelect={() => go(item.href)}>
                      <span className="flex flex-col">
                        <span>{item.title}</span>
                        {item.subtitle && <span className="text-xs text-neutral-600">{item.subtitle}</span>}
                      </span>
                    </CommandItem>
                  ))}
                </CommandGroup>
              ))}
              {debounced.length >= 2 && search.isFetching && (
                <p role="status" className="px-3 py-2 text-xs text-neutral-600">
                  Aranıyor…
                </p>
              )}
            </CommandList>
            <p className="flex items-center gap-1 border-t border-border px-3 py-2 text-xs text-neutral-600">
              <Search aria-hidden className="size-3.5" /> Müşteri (ad, telefon), ürün (ad, barkod) veya satış
              numarasıyla arayın.
            </p>
          </CommandPrimitive>
        </DialogContent>
      </Dialog>
    </>
  );
}
