import { cn } from "cn";
import type { ReactNode } from "react";

/**
 * Alt sayfaların başlık bandı (2026-10-21): açık sis zemin, koyu (#000a1d) yazı, mavi küçük etiket çizgisi. Renk geçişi yok (sadece
 * ana sayfanın ilk ekranında). `compact`: içerik ilk ekranda görünsün diye kısa bant (Fiyatlar: kartlar 1366×768'de ilk ekranda).
 */
export function PageIntro({
  label,
  title,
  compact = false,
  children,
}: {
  label: string;
  title: string;
  compact?: boolean;
  children?: ReactNode;
}) {
  return (
    <section className="border-b border-neutral-200 bg-neutral-100">
      <div className={cn("mx-auto max-w-6xl px-4 sm:px-6", compact ? "py-6 sm:py-8" : "py-12 sm:py-16")}>
        <p className="flex items-center gap-2 text-sm font-semibold text-link">
          <span aria-hidden className="h-0.5 w-5 rounded-full bg-brand" />
          {label}
        </p>
        <h1
          className={cn(
            "max-w-3xl leading-[1.08] font-semibold tracking-tight text-navy-950",
            compact ? "mt-2 text-3xl sm:text-4xl" : "mt-3 text-4xl sm:text-5xl",
          )}
        >
          {title}
        </h1>
        {children && <div className={cn("max-w-2xl text-neutral-600", compact ? "mt-3 text-base" : "mt-5 text-lg")}>{children}</div>}
      </div>
    </section>
  );
}
