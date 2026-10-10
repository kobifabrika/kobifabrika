"use client";

import { useQuery } from "@tanstack/react-query";
import { Command as CommandPrimitive } from "cmdk";
import { Activity, Clock, Search, Settings, UserRound, Users, type LucideIcon } from "lucide-react";
import { useRouter } from "next/navigation";
import { useEffect, useMemo, useState } from "react";

import { useSession } from "@/components/providers/session-provider";
import { CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList } from "@/components/ui/command";
import { Dialog, DialogContent, DialogDescription, DialogTitle } from "@/components/ui/dialog";
import { api, unwrap } from "@/lib/api/client";
import { panelHref, visibleNavGroups } from "@/lib/navigation";
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
 * Ctrl+K / ⌘K genel arama. Sprint 1'de iskelet: sayfalar istemcide eşleşir; müşteri/ürün/satış
 * sonuçları API'den gelir (GET /api/search) ve ileriki sprintlerde dolar.
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
    if (can(role, "viewSettings")) links.push({ label: "Çalışma saatleri", href: "/panel/ayarlar/calisma-saatleri", icon: Clock, keywords: "ayarlar" });
    if (can(role, "manageTeam")) links.push({ label: "Ekip ve davetler", href: "/panel/ayarlar/ekip", icon: Users, keywords: "ayarlar personel kullanıcı davet" });
    if (can(role, "viewAuditLog")) links.push({ label: "Aktivite günlüğü", href: "/panel/ayarlar/aktivite", icon: Activity, keywords: "ayarlar log kayıt" });
    links.push({ label: "Hesabım", href: "/panel/hesabim", icon: UserRound, keywords: "profil şifre" });
    return links;
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
        className="hidden h-11 w-full max-w-md items-center gap-2 rounded-lg border border-border bg-muted px-3 text-left text-sm text-muted-foreground hover:border-primary lg:flex"
      >
        <Search aria-hidden className="size-4" />
        <span className="flex-1">Müşteri, ürün, satış veya sayfa ara…</span>
        <kbd className="rounded border border-border bg-background px-1.5 text-xs text-muted-foreground">Ctrl K</kbd>
      </button>
      <button
        type="button"
        onClick={() => setOpen(true)}
        aria-label="Ara"
        className="flex size-11 items-center justify-center rounded-md text-foreground hover:bg-muted lg:hidden"
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
                      <Icon aria-hidden className="text-brand-500" />
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
              <Settings aria-hidden className="size-3.5" /> Müşteri, ürün ve satış araması ilgili ekranlar gelince çalışacak.
            </p>
          </CommandPrimitive>
        </DialogContent>
      </Dialog>
    </>
  );
}
