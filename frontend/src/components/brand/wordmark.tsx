import { cn } from "cn";

/**
 * Logo gelene kadar "KobiFabrika" yazısı (docs/04-tasarim.md).
 * `onDark`: koyu zeminde (brand-900) kullanılıyorsa; brand-300 sadece koyu zeminde yazı olabilir.
 */
export function Wordmark({ className, onDark = false }: { className?: string; onDark?: boolean }) {
  return (
    <span
      className={cn(
        "font-extrabold tracking-tight select-none",
        onDark ? "text-white" : "text-brand-900",
        className,
      )}
    >
      Kobi<span className={onDark ? "text-brand-300" : "text-brand-500"}>Fabrika</span>
    </span>
  );
}
