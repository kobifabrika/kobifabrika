"use client";

import type { ReactNode } from "react";

import { fileUrl } from "@/lib/api/client";

/**
 * İşletmenin müşterisinin açtığı sayfaların çerçevesi: sade beyaz zemin, üstte işletme logosu ve adı.
 * KobiFabrika sadece en altta küçük "KobiFabrika ile gönderildi" yazısıyla görünür.
 */
export function PublicShell({
  businessName,
  logoUrl,
  phone,
  children,
}: {
  businessName?: string;
  logoUrl?: string | null;
  phone?: string | null;
  children: ReactNode;
}) {
  const logo = fileUrl(logoUrl);
  return (
    <div className="flex min-h-screen flex-col bg-white">
      <main className="mx-auto flex w-full max-w-2xl flex-1 flex-col gap-6 px-4 py-6 sm:py-10">
        {businessName && (
          <header className="flex items-center gap-3 border-b border-black pb-4">
            {logo && (
              // eslint-disable-next-line @next/next/no-img-element -- logo API'den gelir; boyutu bilinmez.
              <img src={logo} alt="" className="h-12 w-auto max-w-28 object-contain" />
            )}
            <div className="flex min-w-0 flex-col">
              <span className="truncate text-lg font-bold text-black">{businessName}</span>
              {phone && (
                <a
                  href={`tel:${phone.replace(/\s/g, "")}`}
                  className="w-fit text-sm text-neutral-700 underline decoration-neutral-300 underline-offset-2"
                >
                  {phone}
                </a>
              )}
            </div>
          </header>
        )}
        {children}
      </main>
      <footer className="px-4 pb-6 text-center text-xs text-neutral-600">KobiFabrika ile gönderildi</footer>
    </div>
  );
}

export function PublicMessage({ title, children }: { title: string; children?: ReactNode }) {
  return (
    <div className="flex flex-col gap-2 py-10">
      <h1 className="text-xl font-bold text-black">{title}</h1>
      {children && <p className="text-base text-neutral-700">{children}</p>}
    </div>
  );
}
