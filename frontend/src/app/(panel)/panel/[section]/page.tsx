import type { Metadata } from "next";
import { notFound } from "next/navigation";

import { Card, CardContent } from "@/components/ui/card";
import { allNavItems, findNavItem } from "@/lib/navigation";

// Panel layout'u gibi: içerik istemcide oturum doğrulanınca render edilir.
export const instant = false;

/** Menüdeki her ekran için geçici iskelet; ekranlar ilgili sprintlerde gerçek sayfalarla değişir. */
export function generateStaticParams() {
  // Sprint 1-5 ekranları (Ayarlar, Müşteriler, Cari ve Kasa, Satışlar, Ürünler, Stok, Raporlar) kendi klasöründe; burada sadece henüz gelmemiş ekranlar.
  return allNavItems.filter((item) => item.slug && (item.sprint ?? 0) > 5).map((item) => ({ section: item.slug }));
}

export async function generateMetadata({ params }: PageProps<"/panel/[section]">): Promise<Metadata> {
  const { section } = await params;
  return { title: findNavItem(section)?.label ?? "Bulunamadı" };
}

export default async function SectionPage({ params }: PageProps<"/panel/[section]">) {
  const { section } = await params;
  const item = findNavItem(section);
  if (!item) notFound();

  const Icon = item.icon;
  return (
    <div className="mx-auto flex max-w-6xl flex-col gap-6">
      <h1 className="text-2xl font-bold tracking-tight text-black">{item.label}</h1>
      <Card>
        <CardContent className="flex flex-col items-center gap-3 py-10 text-center">
          <Icon aria-hidden className="size-10 text-brand" />
          <p className="text-base font-semibold text-black">Bu ekran hazırlanıyor</p>
          <p className="text-sm text-neutral-600">Sprint {item.sprint} ile gelecek.</p>
        </CardContent>
      </Card>
    </div>
  );
}
