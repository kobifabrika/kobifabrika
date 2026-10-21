"use client";

import { MapPin, Phone } from "lucide-react";
import type { ReactNode } from "react";

import { Logo } from "@/components/brand/logo";
import { fileUrl } from "@/lib/api/client";

/** Google Haritalar arama linki (adres + ilçe + il). */
export function mapsLink(parts: (string | null | undefined)[]): string | null {
  const address = parts.filter(Boolean).join(", ");
  return address ? `https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(address)}` : null;
}

/**
 * Online randevu sayfalarının çerçevesi: üstte logo, işletme adı, adres (Haritalar) ve telefon. Masaüstünde içerik sola hizalı
 * 640 px kolon, sağda özet kutusu (aside; lg ve üstü). KobiFabrika sadece altta küçük yazıyla görünür.
 */
export function BookingShell({
  name,
  logoUrl,
  address,
  phone,
  phoneE164,
  aside,
  children,
}: {
  name?: string;
  logoUrl?: string | null;
  address?: (string | null | undefined)[];
  phone?: string | null;
  phoneE164?: string | null;
  aside?: ReactNode;
  children: ReactNode;
}) {
  const logo = fileUrl(logoUrl);
  const map = address ? mapsLink(address) : null;
  const addressText = address?.filter(Boolean).join(", ");
  return (
    <div className="flex min-h-screen flex-col bg-white">
      <div className="mx-auto flex w-full max-w-6xl flex-1 flex-col gap-6 px-4 py-6 sm:py-10">
        {name && (
          <header className="flex items-center gap-3 border-b border-black pb-4">
            {logo && (
              // eslint-disable-next-line @next/next/no-img-element -- logo API'den gelir; boyutu bilinmez.
              <img src={logo} alt="" className="h-12 w-auto max-w-28 object-contain" />
            )}
            <div className="flex min-w-0 flex-col gap-1">
              <span className="truncate text-lg font-bold text-black">{name}</span>
              <div className="flex flex-wrap gap-x-4 gap-y-1">
                {map && (
                  <a
                    href={map}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="inline-flex min-h-6 items-center gap-1 text-sm text-neutral-700 underline decoration-neutral-300 underline-offset-2"
                  >
                    <MapPin aria-hidden className="size-4 shrink-0" />
                    <span className="truncate">{addressText}</span>
                  </a>
                )}
                {phone && (
                  <a
                    href={`tel:${phoneE164 ?? phone.replace(/\s/g, "")}`}
                    className="inline-flex min-h-6 items-center gap-1 text-sm text-neutral-700 underline decoration-neutral-300 underline-offset-2"
                  >
                    <Phone aria-hidden className="size-4 shrink-0" />
                    {phone}
                  </a>
                )}
              </div>
            </div>
          </header>
        )}
        <div className="grid flex-1 gap-8 lg:grid-cols-[minmax(0,640px)_minmax(0,1fr)]">
          <main className="flex min-w-0 flex-col gap-5">{children}</main>
          {aside && <aside className="hidden lg:block">{aside}</aside>}
        </div>
      </div>
      <footer className="flex items-center justify-center gap-1 px-4 pb-6 text-xs text-neutral-600">
        <Logo surface="light" className="h-2.5" /> ile online randevu
      </footer>
    </div>
  );
}
