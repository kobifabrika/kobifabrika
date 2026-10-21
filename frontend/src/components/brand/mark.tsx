import { cn } from "cn";

import { Wordmark } from "@/components/brand/wordmark";

/**
 * Beyaz zeminde site markası: sadece "KobiFabrika" yazısı (2026-10-21: KF işareti kaldırıldı; işaret yalnızca favicon ve
 * uygulama ikonlarında, public/marka/isaret.svg). Koyu zeminde `Wordmark onDark` veya `Logo`.
 */
export function SiteBrand({ className }: { className?: string }) {
  return <Wordmark className={cn("text-[1.375rem] text-ink", className)} />;
}
