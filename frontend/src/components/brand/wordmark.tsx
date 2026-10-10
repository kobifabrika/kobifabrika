import { cn } from "cn";

/**
 * Logo gelene kadar "KobiFabrika" yazısı (docs/04-tasarim.md).
 * Siyah zeminde (`onDark`) "Fabrika" vurgu mavisi. Beyaz zeminde mavi yazı olamaz: yazı siyah, "Fabrika"nın altında mavi çizgi.
 */
export function Wordmark({ className, onDark = false }: { className?: string; onDark?: boolean }) {
  return (
    <span className={cn("font-heading font-extrabold tracking-tight select-none", onDark ? "text-white" : "text-black", className)}>
      Kobi
      <span
        className={
          onDark ? "text-brand" : "underline decoration-brand decoration-[0.12em] underline-offset-[0.14em]"
        }
      >
        Fabrika
      </span>
    </span>
  );
}
