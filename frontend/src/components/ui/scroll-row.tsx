"use client";

import { cn } from "cn";
import Link from "next/link";
import { useEffect, useRef, type ComponentProps, type ReactNode } from "react";

/**
 * Telefonda yatay kaydırılan satır (filtre çipleri ve sekmeler). Uygulamadaki bütün çip ve sekme satırları bunu kullanır.
 * - Kaydırma çubuğu gizli (`no-scrollbar`; kaydırma dokunma/fareyle çalışır).
 * - Kenara kadar uzanır (-mx-4) ama ilk ve son öğe sayfa boşluğu kadar içeride durur: son öğenin sağındaki boşluk içteki
 *   satırın kendi dolgusu (`pr-4`); kaydırılan kapsayıcının sağ dolgusu bazı tarayıcılarda (WebKit) yutulduğu için.
 * - Kenarda solma yok (gradyan yasak, docs/04-tasarim.md); kesik görünen öğe sadece kaydırma sürerken olur.
 * - Seçili öğe (çip, sekme, personel) ilk açılışta ve activeKey değişince görünür alana kaydırılır.
 */
/** Seçili öğe (aria-pressed / aria-checked / aria-current) satırın dışında kalıyorsa görünür alana kaydırılır; sayfa dikey kaymaz. */
function revealActive(row: HTMLElement | null) {
  const current = row?.querySelector<HTMLElement>("[aria-pressed=true], [aria-checked=true], [aria-current=page], [aria-current=true]");
  if (!row || !current) return;
  const left = current.offsetLeft - row.offsetLeft;
  if (left < row.scrollLeft || left + current.offsetWidth > row.scrollLeft + row.clientWidth) {
    row.scrollLeft = Math.max(0, left - (row.clientWidth - current.offsetWidth) / 2);
  }
}

export function ScrollRow({
  children,
  className,
  innerClassName,
  as: Tag = "div",
  activeKey,
  ...props
}: {
  children: ReactNode;
  className?: string;
  innerClassName?: string;
  as?: "div" | "nav";
  /** Değişince seçili öğe yeniden görünür alana kaydırılır (verilmezse sadece ilk açılışta). */
  activeKey?: string | number | null;
} & Omit<ComponentProps<"div">, "children" | "className" | "ref">) {
  const row = useRef<HTMLDivElement>(null);
  useEffect(() => revealActive(row.current), [activeKey]);
  return (
    <Tag {...props} ref={row} className={cn("no-scrollbar -mx-4 overflow-x-auto pl-4 lg:mx-0 lg:pl-0", className)}>
      <div className={cn("flex w-max min-w-full gap-2 pr-4 lg:pr-0", innerClassName)}>{children}</div>
    </Tag>
  );
}

const chipClass = "min-h-11 shrink-0 rounded-full border px-4 text-sm font-medium whitespace-nowrap";

/** Filtre çipi: seçili siyah, seçili değil çerçeveli (aria-pressed). */
export function Chip({
  active,
  className,
  ...props
}: { active: boolean } & ComponentProps<"button">) {
  return (
    <button
      type="button"
      aria-pressed={active}
      className={cn(
        chipClass,
        active ? "border-link bg-link text-white" : "border-border bg-background text-black hover:border-black",
        className,
      )}
      {...props}
    />
  );
}

/** Çip görünümünde link (sorgu parametresiyle süzülen listeler). */
export function ChipLink({ active, className, ...props }: { active: boolean } & ComponentProps<typeof Link>) {
  return (
    <Link
      aria-current={active ? "true" : undefined}
      className={cn(
        chipClass,
        "inline-flex items-center",
        active ? "border-link bg-link text-white" : "border-border bg-background text-black hover:border-black",
        className,
      )}
      {...props}
    />
  );
}

export type TabItem = { key: string; label: string; href: string };

/** Sekme satırı: alt çizgi mavi, telefonda yatay kaydırma (Ayarlar, müşteri kartı, Cari ve Kasa, Raporlar…). */
export function TabNav({ tabs, active, label }: { tabs: TabItem[]; active: string; label: string }) {
  return (
    <ScrollRow as="nav" aria-label={label} activeKey={active} innerClassName="gap-1 border-b border-border">
      {tabs.map((t) => (
        <Link
          key={t.key}
          href={t.href}
          scroll={false}
          aria-current={t.key === active ? "page" : undefined}
          className={cn(
            "-mb-px flex min-h-11 shrink-0 items-center border-b-2 px-3 text-sm font-semibold whitespace-nowrap",
            t.key === active ? "border-link text-link" : "border-transparent text-neutral-600 hover:text-black",
          )}
        >
          {t.label}
        </Link>
      ))}
    </ScrollRow>
  );
}
