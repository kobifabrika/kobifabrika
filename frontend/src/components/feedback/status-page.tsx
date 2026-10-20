import { cn } from "cn";
import Link from "next/link";
import type { ReactNode } from "react";

import { Logo } from "@/components/brand/logo";

export type StatusLink = { href: string; label: string };

/**
 * 404 ve hata sayfalarının ortak görünümü: koyu lacivert bant + logo, büyük kod, ne olduğu, ne yapılabileceği (düğme ve
 * bağlantılar). `bare`: panel içinde (menü ve üst çubuk zaten var) sadece içerik.
 */
export function StatusPage({
  code,
  title,
  children,
  actions,
  links = [],
  bare = false,
}: {
  code: string;
  title: string;
  children: ReactNode;
  actions?: ReactNode;
  links?: StatusLink[];
  bare?: boolean;
}) {
  const body = (
    <div className={cn("mx-auto flex w-full max-w-xl flex-col gap-4", bare ? "py-10" : "px-4 py-12 sm:py-16")}>
      <p className="font-heading text-6xl font-bold text-link tabular-nums">{code}</p>
      <h1 className="text-2xl font-bold tracking-tight text-black sm:text-3xl">{title}</h1>
      <div className="text-base leading-7 text-neutral-700">{children}</div>
      {actions && <div className="flex flex-col gap-3 sm:flex-row">{actions}</div>}
      {links.length > 0 && (
        <nav aria-label="Yararlı bağlantılar" className="mt-2 border-t border-neutral-200 pt-4">
          <ul className="flex flex-wrap gap-x-6">
            {links.map((l) => (
              <li key={l.href}>
                <Link href={l.href} className="inline-flex min-h-11 items-center font-semibold text-link underline underline-offset-4">
                  {l.label}
                </Link>
              </li>
            ))}
          </ul>
        </nav>
      )}
    </div>
  );
  if (bare) return body;
  return (
    <div className="flex min-h-screen flex-col bg-white">
      <header className="dark bg-navy-950">
        <div className="mx-auto flex h-16 max-w-6xl items-center px-4 sm:px-6">
          <Link href="/" className="flex min-h-11 items-center rounded-md" aria-label="KobiFabrika ana sayfa">
            <Logo className="h-5" />
          </Link>
        </div>
      </header>
      <main className="flex-1">{body}</main>
    </div>
  );
}
