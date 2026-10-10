import type { Metadata } from "next";
import { AlertTriangle, CalendarDays, HandCoins, ReceiptText, type LucideIcon } from "lucide-react";

import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";

// Panel layout'u gibi: içerik istemcide oturum doğrulanınca render edilir.
export const instant = false;

export const metadata: Metadata = { title: "Bugün" };

const stats: { label: string; icon: LucideIcon }[] = [
  { label: "Bugünkü satış", icon: ReceiptText },
  { label: "Bugünkü tahsilat", icon: HandCoins },
  { label: "Bugünün randevuları", icon: CalendarDays },
  { label: "Vadesi gelen alacak", icon: AlertTriangle },
];

/** Bugün ekranı iskeleti. Gerçek veriler Sprint 4'te (docs/05-sprintler.md). */
export default function TodayPage() {
  return (
    <div className="mx-auto flex max-w-6xl flex-col gap-6">
      <div>
        <h1 className="text-2xl font-bold tracking-tight text-black">Bugün</h1>
        <p className="mt-1 text-sm text-neutral-600">İşletmenizde bugün olanlar burada görünecek.</p>
      </div>

      <section aria-label="Özet" className="grid grid-cols-2 gap-3 lg:grid-cols-4 lg:gap-4">
        {stats.map(({ label, icon: Icon }) => (
          <Card key={label} className="gap-2 py-4">
            <CardHeader className="flex flex-row items-center justify-between gap-2 px-4">
              <CardTitle className="text-xs font-medium text-neutral-600">{label}</CardTitle>
              <Icon aria-hidden className="size-5 shrink-0 text-neutral-600" />
            </CardHeader>
            <CardContent className="px-4">
              <p className="text-2xl font-bold text-black tabular-nums">—</p>
            </CardContent>
          </Card>
        ))}
      </section>

      <div className="grid gap-4 lg:grid-cols-2">
        <Card>
          <CardHeader>
            <CardTitle className="text-black">Bugünün randevuları</CardTitle>
          </CardHeader>
          <CardContent>
            <p className="text-sm text-neutral-600">Henüz randevu yok.</p>
          </CardContent>
        </Card>
        <Card>
          <CardHeader>
            <CardTitle className="text-black">Kritik stok</CardTitle>
          </CardHeader>
          <CardContent>
            <p className="text-sm text-neutral-600">Kritik seviyede ürün yok.</p>
          </CardContent>
        </Card>
      </div>
    </div>
  );
}
