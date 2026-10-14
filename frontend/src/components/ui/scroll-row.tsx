"use client";

import { cn } from "cn";
import Link from "next/link";
import { useEffect, useRef, type ComponentProps, type ReactNode, type Ref } from "react";

/**
 * Telefonda yatay kaydırılan satır (filtre çipleri ve sekmeler). Uygulamadaki bütün çip ve sekme satırları bunu kullanır.
 * - Kaydırma çubuğu gizli (`no-scrollbar`; kaydırma dokunma/fareyle çalışır).
 * - Kenara kadar uzanır (-mx-4) ama ilk ve son öğe sayfa boşluğu kadar içeride durur: son öğenin sağındaki boşluk içteki
 *   satırın kendi dolgusu (`pr-4`); kaydırılan kapsayıcının sağ dolgusu bazı tarayıcılarda (WebKit) yutulduğu için.
 * - Kenarda solma yok (gradyan yasak, docs/04-tasarim.md); kesik görünen öğe sadece kaydırma sürerken olur.
 */
export function ScrollRow({
  children,
  className,
  innerClassName,
  as: Tag = "div",
  ref,
  ...props
}: {
  children: ReactNode;
  className?: string;
  innerClassName?: string;
  as?: "div" | "nav";
  ref?: Ref<HTMLDivElement>;
} & Omit<ComponentProps<"div">, "children" | "className" | "ref">) {
  return (
    <Tag {...props} ref={ref} className={cn("no-scrollbar -mx-4 overflow-x-auto pl-4 lg:mx-0 lg:pl-0", className)}>
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
        active ? "border-black bg-black text-white" : "border-border bg-background text-black hover:border-black",
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
        active ? "border-black bg-black text-white" : "border-border bg-background text-black hover:border-black",
        className,
      )}
      {...props}
    />
  );
}

export type TabItem = { key: string; label: string; href: string };

/** Sekme satırı: alt çizgi mavi, telefonda yatay kaydırma (Ayarlar, müşteri kartı, Cari ve Kasa, Raporlar…). */
export function TabNav({ tabs, active, label }: { tabs: TabItem[]; active: string; label: string }) {
  // Aktif sekme satırın dışında kalıyorsa (telefonda sağdaki sekmeler) görünür alana kaydırılır; sayfa dikey kaymaz.
  const row = useRef<HTMLDivElement>(null);
  useEffect(() => {
    const el = row.current;
    const current = el?.querySelector<HTMLElement>("[aria-current=page]");
    if (!el || !current) return;
    const left = current.offsetLeft - el.offsetLeft;
    if (left < el.scrollLeft || left + current.offsetWidth > el.scrollLeft + el.clientWidth) {
      el.scrollLeft = Math.max(0, left - (el.clientWidth - current.offsetWidth) / 2);
    }
  }, [active]);
  return (
    <ScrollRow ref={row} as="nav" aria-label={label} innerClassName="gap-1 border-b border-border">
      {tabs.map((t) => (
        <Link
          key={t.key}
          href={t.href}
          scroll={false}
          aria-current={t.key === active ? "page" : undefined}
          className={cn(
            "-mb-px flex min-h-11 shrink-0 items-center border-b-2 px-3 text-sm font-semibold whitespace-nowrap",
            t.key === active ? "border-brand text-black" : "border-transparent text-neutral-600 hover:text-black",
          )}
        >
          {t.label}
        </Link>
      ))}
    </ScrollRow>
  );
}
