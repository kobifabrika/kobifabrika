import { cn } from "cn";

import { Wordmark } from "@/components/brand/wordmark";

/**
 * KF işareti (public/marka/isaret.svg ile aynı çizim, satır içi: ağ isteği yok). Siyah kare kendi zeminini taşıdığı için beyaz
 * zeminde de kullanılır. Süs; yanındaki yazı markayı okutur.
 */
export function BrandMark({ className }: { className?: string }) {
  return (
    <svg viewBox="0 0 512 512" aria-hidden className={cn("size-7 shrink-0", className)}>
      <rect width="512" height="512" rx="72" fill="#000000" />
      <g transform="translate(256 256) scale(0.75) translate(-257 -256)">
        <g fill="#ffffff">
          <rect x="64" y="100" width="76" height="312" />
          <polygon points="132,268 222,100 306,100 196,312" />
          <polygon points="178,226 308,412 222,412 132,282" />
        </g>
        <g fill="#00b0ff">
          <rect x="326" y="100" width="76" height="312" />
          <rect x="326" y="100" width="124" height="68" />
          <rect x="326" y="226" width="110" height="64" />
        </g>
      </g>
    </svg>
  );
}

/** Beyaz zeminde site markası: işaret + yazı (koyu zemin logosu `Logo`; docs/04). */
export function SiteBrand({ className }: { className?: string }) {
  return (
    <span className={cn("inline-flex items-center gap-2", className)}>
      <BrandMark />
      <Wordmark className="text-xl text-ink" />
    </span>
  );
}
