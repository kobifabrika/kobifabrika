import { cn } from "cn";
import type { ReactNode } from "react";

/**
 * Alt sayfaların koyu lacivert başlık bandı (ana sayfanın girişiyle aynı dil).
 * `compact`: içerik ilk ekranda görünsün diye kısa bant (Fiyatlar: paket kartları 1366×768'de de ilk ekranda).
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
    <section className="dark bg-navy-950 text-white">
      <div className={cn("mx-auto max-w-6xl px-4 sm:px-6", compact ? "py-6 sm:py-8" : "py-12 sm:py-16")}>
        <p className="text-sm font-semibold text-cyan">{label}</p>
        <h1
          className={cn(
            "max-w-3xl leading-[1.05] font-bold tracking-tight",
            compact ? "mt-2 text-3xl sm:text-4xl" : "mt-3 text-4xl sm:text-5xl",
          )}
        >
          {title}
        </h1>
        {children && (
          <div className={cn("max-w-2xl text-neutral-300", compact ? "mt-3 text-base" : "mt-5 text-lg")}>{children}</div>
        )}
      </div>
    </section>
  );
}
