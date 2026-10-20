import type { SVGProps } from "react";

/**
 * "Kimler için" bölümünün çizgi ikonları (kendi çizimimiz; lucide'de sektör ikonları yok). Tek renk (currentColor), 1,75 çizgi,
 * 48×48 görünüm alanı. Süs; anlam yanındaki başlıkta.
 */
function Svg(props: SVGProps<SVGSVGElement>) {
  return (
    <svg
      viewBox="0 0 48 48"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.75}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden
      {...props}
    />
  );
}

export type SectorIconName = "kuafor" | "guzellik" | "studyo" | "kafe" | "dukkan";

export function SectorIcon({ name, ...props }: { name: SectorIconName } & SVGProps<SVGSVGElement>) {
  switch (name) {
    case "kuafor":
      // Makas
      return (
        <Svg {...props}>
          <circle cx="13" cy="35" r="5" />
          <circle cx="35" cy="35" r="5" />
          <path d="M16.5 31.5 34 8M31.5 31.5 14 8" />
          <circle cx="24" cy="21" r="1" fill="currentColor" />
        </Svg>
      );
    case "guzellik":
      // El aynası ve parıltı
      return (
        <Svg {...props}>
          <circle cx="20" cy="18" r="11" />
          <circle cx="20" cy="18" r="7" />
          <path d="M27.5 26 39 40.5" />
          <path d="M36 7v6M33 10h6" />
        </Svg>
      );
    case "studyo":
      // Pilates matı üstünde esneyen kişi
      return (
        <Svg {...props}>
          <circle cx="15" cy="13" r="3.5" />
          <path d="M17 18 26 26l9-8" />
          <path d="M26 26v8l-7 6M26 34l9 6" />
          <path d="M6 42h36" />
        </Svg>
      );
    case "kafe":
      // Buharı tüten fincan
      return (
        <Svg {...props}>
          <path d="M9 21h24v9a10 10 0 0 1-10 10h-4A10 10 0 0 1 9 30z" />
          <path d="M33 24h3a5 5 0 0 1 0 10h-3.5" />
          <path d="M17 8c-2 2.5 2 4 0 7M24 8c-2 2.5 2 4 0 7" />
          <path d="M6 43h30" />
        </Svg>
      );
    case "dukkan":
      // Tenteli dükkân
      return (
        <Svg {...props}>
          <path d="M7 19 10 8h28l3 11" />
          <path d="M7 19c0 2.8 2.2 5 5 5s5-2.2 5-5c0 2.8 2.2 5 5 5h4c2.8 0 5-2.2 5-5 0 2.8 2.2 5 5 5s5-2.2 5-5z" />
          <path d="M10 24v17h28V24" />
          <path d="M20 41V31h8v10" />
        </Svg>
      );
  }
}
