import type { ReactNode, SVGProps } from "react";

import type { SocialKey } from "@/content/site";

/**
 * Sosyal medya ikonları: lucide-react 1.x marka ikonlarını içermediği için tek renk, çizgi stilinde (currentColor) elle çizildi.
 * Renkli marka logosu kullanılmaz (docs/04-tasarim.md: ikonlar tek renk).
 */
const paths: Record<SocialKey, ReactNode> = {
  instagram: (
    <>
      <rect x="2" y="2" width="20" height="20" rx="5" />
      <path d="M16 11.37A4 4 0 1 1 12.63 8 4 4 0 0 1 16 11.37z" />
      <path d="M17.5 6.5h.01" />
    </>
  ),
  facebook: <path d="M18 2h-3a5 5 0 0 0-5 5v3H7v4h3v8h4v-8h3l1-4h-4V7a1 1 0 0 1 1-1h3z" />,
  youtube: (
    <>
      <path d="M22.54 6.42a2.78 2.78 0 0 0-1.94-2C18.88 4 12 4 12 4s-6.88 0-8.6.46a2.78 2.78 0 0 0-1.94 2A29 29 0 0 0 1 11.75a29 29 0 0 0 .46 5.33A2.78 2.78 0 0 0 3.4 19c1.72.46 8.6.46 8.6.46s6.88 0 8.6-.46a2.78 2.78 0 0 0 1.94-2 29 29 0 0 0 .46-5.25 29 29 0 0 0-.46-5.33z" />
      <path d="m9.75 15.02 5.75-3.27-5.75-3.27v6.54z" />
    </>
  ),
  tiktok: (
    <>
      <path d="M15 3v11.5a4.5 4.5 0 1 1-4.5-4.5" />
      <path d="M15 3c.4 2.6 2.4 4.6 5 5" />
    </>
  ),
};

export function SocialIcon({ name, ...props }: { name: SocialKey } & SVGProps<SVGSVGElement>) {
  return (
    <svg
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={2}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden
      focusable="false"
      {...props}
    >
      {paths[name]}
    </svg>
  );
}
