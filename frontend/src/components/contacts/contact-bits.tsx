"use client";

import { cn } from "cn";
import { MessageCircle, Phone } from "lucide-react";
import type { ReactNode } from "react";

import type { Tag, TagColor } from "@/lib/api/types";
import { telLink, waLink } from "@/lib/contacts";
import { balanceLabel } from "@/lib/format";

/** Native select; panel formlarındaki diğer seçimlerle aynı görünüm (44px, 16px yazı). */
export const selectClass =
  "h-11 w-full rounded-lg border border-input bg-background px-3 text-base text-foreground outline-none focus-visible:border-foreground focus-visible:ring-3 focus-visible:ring-ring aria-invalid:border-destructive disabled:bg-muted disabled:text-muted-foreground";

/** Etiket renkleri sadece 3 renk kuralından: siyah, gri, mavi zemin (yazı siyah), çerçeve. */
export const tagColorClass: Record<TagColor, string> = {
  Gray: "bg-neutral-100 text-black border-transparent",
  Black: "bg-black text-white border-transparent",
  Blue: "bg-brand text-black border-transparent",
  Outline: "bg-white text-black border-neutral-400",
};

export function TagBadge({ tag, className }: { tag: Pick<Tag, "name" | "color">; className?: string }) {
  return (
    <span
      className={cn(
        "inline-flex h-6 items-center rounded-full border px-2 text-xs font-medium whitespace-nowrap",
        tagColorClass[tag.color],
        className,
      )}
    >
      {tag.name}
    </span>
  );
}

export function TagList({ tags, max = 3 }: { tags: Tag[]; max?: number }) {
  if (tags.length === 0) return null;
  const rest = tags.length - max;
  return (
    <span className="flex flex-wrap items-center gap-1">
      {tags.slice(0, max).map((t) => (
        <TagBadge key={t.id} tag={t} />
      ))}
      {rest > 0 && <span className="text-xs text-neutral-600">+{rest}</span>}
    </span>
  );
}

/** "Alacak 1.250,00 TL" / "Borç 300,00 TL"; renk değil kelime (alacakta kalın). */
export function Balance({ value, className }: { value: number; className?: string }) {
  return (
    <span className={cn("tabular-nums", value > 0 ? "font-semibold text-black" : "text-neutral-700", className)}>
      {balanceLabel(value)}
    </span>
  );
}

/** Tek dokunuşla ara ve WhatsApp (en az 44px). */
export function ContactActions({ phone, compact = false }: { phone: string | null | undefined; compact?: boolean }) {
  if (!phone) return null;
  const base =
    "inline-flex min-h-11 items-center justify-center gap-2 rounded-lg border border-border bg-background px-3 text-sm font-semibold text-black hover:border-black";
  return (
    <span className="flex gap-2">
      <a href={telLink(phone)} className={cn(base, compact && "min-w-11 px-0")} aria-label="Ara">
        <Phone aria-hidden className="size-4" />
        {!compact && "Ara"}
      </a>
      <a
        href={waLink(phone)}
        target="_blank"
        rel="noopener noreferrer"
        className={cn(base, compact && "min-w-11 px-0")}
        aria-label="WhatsApp"
      >
        <MessageCircle aria-hidden className="size-4" />
        {!compact && "WhatsApp"}
      </a>
    </span>
  );
}

export { TabNav, type TabItem } from "@/components/ui/scroll-row";

/** Boş liste/ekran açıklaması. */
export function EmptyState({ icon, title, children }: { icon: ReactNode; title: string; children?: ReactNode }) {
  return (
    <div className="flex flex-col items-start gap-3 border border-dashed border-neutral-300 px-5 py-8">
      <span className="text-neutral-400">{icon}</span>
      <p className="text-base font-semibold text-black">{title}</p>
      {children}
    </div>
  );
}

/** Uyarı kutusu: gri zemin + solda ince mavi çizgi (sarı yok; docs/04-tasarim.md). */
export function Notice({ children, className }: { children: ReactNode; className?: string }) {
  return (
    <div className={cn("border-l-2 border-brand bg-neutral-100 px-3 py-2 text-sm text-black", className)}>
      {children}
    </div>
  );
}
