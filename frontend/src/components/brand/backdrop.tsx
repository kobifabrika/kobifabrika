import { cn } from "cn";

/**
 * Koyu zemin dekoru: ince çizgi ızgara + hafif turkuaz ışıma. Sadece süs; ekran okuyucudan gizli.
 * Ebeveyn `relative` ve `overflow-hidden` olmalı; içerik bunun üstünde `relative` ile durur.
 */
export function Backdrop({ className, grid = true }: { className?: string; grid?: boolean }) {
  return (
    <div aria-hidden className={cn("pointer-events-none absolute inset-0 overflow-hidden", className)}>
      {grid && (
        <div className="kf-grid absolute inset-0 [mask-image:radial-gradient(ellipse_at_top,black_20%,transparent_70%)]" />
      )}
      <div className="absolute -top-40 left-1/2 size-[36rem] -translate-x-1/2 rounded-full bg-brand-300/15 blur-3xl" />
      <div className="absolute -right-32 bottom-0 size-80 rounded-full bg-brand-100/10 blur-3xl" />
    </div>
  );
}
