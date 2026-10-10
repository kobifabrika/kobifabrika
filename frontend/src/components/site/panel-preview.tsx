import { Bell, CalendarDays, HandCoins, Menu, Plus, ReceiptText, Search, TrendingUp, Users, type LucideIcon } from "lucide-react";
import { cn } from "cn";

import { Wordmark } from "@/components/brand/wordmark";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { navGroups } from "@/lib/navigation";

/*
 * Tanıtım sitesindeki panel önizlemesi: resim değil, panelin gerçek bileşenleriyle (Card, Badge, Wordmark, menü)
 * çizilmiş örnek ekran. Veriler örnektir. Ekran okuyucuya tek bir görsel olarak okunur.
 */

const appointments = [
  { time: "10:00", name: "Ayşe K.", service: "Saç kesimi + fön", status: "Geldi" },
  { time: "11:30", name: "Mehmet D.", service: "Sakal tıraşı", status: "Onaylı" },
  { time: "13:00", name: "Zeynep A.", service: "Boya", status: "Onaylı" },
  { time: "15:30", name: "Can Y.", service: "Saç kesimi", status: "Online" },
];

const stats: { label: string; value: string; icon: LucideIcon }[] = [
  { label: "Satış", value: "8.450 ₺", icon: ReceiptText },
  { label: "Tahsilat", value: "6.200 ₺", icon: HandCoins },
  { label: "Randevu", value: "12", icon: CalendarDays },
  { label: "Yeni müşteri", value: "3", icon: Users },
];

// Son 7 günün cirosu (örnek), en yüksek güne göre yüzde.
const week = [
  { day: "Pzt", value: 52 },
  { day: "Sal", value: 64 },
  { day: "Çar", value: 48 },
  { day: "Per", value: 72 },
  { day: "Cum", value: 88 },
  { day: "Cmt", value: 100 },
  { day: "Paz", value: 30 },
];

function StatusBadge({ status }: { status: string }) {
  return (
    <Badge
      variant="outline"
      className={cn(
        "h-6 text-xs",
        status === "Geldi" && "border-transparent bg-success-50 text-success-600",
        status === "Online" && "border-transparent bg-neutral-100 text-brand-500",
      )}
    >
      {status}
    </Badge>
  );
}

function AppointmentList({ items }: { items: typeof appointments }) {
  return (
    <ul className="flex flex-col divide-y divide-border">
      {items.map((a) => (
        <li key={a.time} className="flex items-center gap-3 py-2">
          <span className="w-11 shrink-0 text-sm font-semibold text-brand-700 tabular-nums">{a.time}</span>
          <span className="flex min-w-0 flex-1 flex-col">
            <span className="truncate text-sm font-semibold text-brand-900">{a.name}</span>
            <span className="truncate text-xs text-neutral-600">{a.service}</span>
          </span>
          <StatusBadge status={a.status} />
        </li>
      ))}
    </ul>
  );
}

function DesktopPreview() {
  const items = navGroups[0].items.slice(0, 7);
  return (
    <div className="overflow-hidden rounded-xl border border-white/10 bg-neutral-50 shadow-2xl shadow-black/50">
      <div className="flex h-8 items-center gap-1.5 bg-brand-900 px-3">
        <span className="size-2.5 rounded-full bg-neutral-700" />
        <span className="size-2.5 rounded-full bg-neutral-700" />
        <span className="size-2.5 rounded-full bg-neutral-700" />
      </div>
      <div className="flex">
        <div className="hidden w-48 shrink-0 flex-col gap-1 border-t border-sidebar-border bg-sidebar px-2 py-4 lg:flex">
          <Wordmark onDark className="mb-3 px-2 text-lg" />
          {items.map(({ label, icon: Icon }, i) => (
            <span
              key={label}
              className={cn(
                "flex items-center gap-2 rounded-md px-2 py-1.5 text-xs",
                i === 0 ? "bg-sidebar-accent font-semibold text-white" : "text-sidebar-foreground",
              )}
            >
              <Icon className={cn("size-4", i === 0 ? "text-sidebar-primary" : "text-neutral-400")} />
              {label}
            </span>
          ))}
        </div>
        <div className="flex min-w-0 flex-1 flex-col gap-4 p-5 pr-80">
          <div className="flex items-center justify-between gap-2">
            <div>
              <p className="text-lg font-bold text-brand-900">Günaydın, Ayşe.</p>
              <p className="text-xs text-neutral-600">Bugün 12 randevun var.</p>
            </div>
            <span className="flex h-8 items-center gap-2 rounded-lg border border-input bg-white px-2 text-xs text-neutral-600">
              <Search className="size-3.5" /> Ara…
            </span>
          </div>
          <div className="grid grid-cols-2 gap-2 xl:grid-cols-4">
            {stats.map(({ label, value, icon: Icon }) => (
              <Card key={label} size="sm" className="gap-1 py-2">
                <CardHeader className="flex flex-row items-center justify-between gap-1 px-3">
                  <CardTitle className="truncate text-xs font-medium text-neutral-600">{label}</CardTitle>
                  <Icon className="size-4 shrink-0 text-brand-500" />
                </CardHeader>
                <CardContent className="px-3">
                  <p className="text-base font-bold text-brand-900 tabular-nums">{value}</p>
                </CardContent>
              </Card>
            ))}
          </div>
          <div className="grid grid-cols-5 gap-2">
            <Card size="sm" className="col-span-3">
              <CardHeader>
                <CardTitle className="text-sm font-semibold text-brand-700">Bugünün randevuları</CardTitle>
              </CardHeader>
              <CardContent>
                <AppointmentList items={appointments.slice(0, 3)} />
              </CardContent>
            </Card>
            <Card size="sm" className="col-span-2">
              <CardHeader>
                <CardTitle className="text-sm font-semibold text-brand-700">Haftalık ciro</CardTitle>
              </CardHeader>
              <CardContent className="flex flex-1 items-end gap-1.5">
                {week.map((d) => (
                  <span key={d.day} className="flex flex-1 flex-col items-center gap-1">
                    <span
                      className={cn("w-full rounded-sm", d.value === 100 ? "bg-brand-500" : "bg-brand-300/70")}
                      style={{ height: `${Math.round(d.value * 0.9)}px` }}
                    />
                    <span className="text-xs text-neutral-600">{d.day[0]}</span>
                  </span>
                ))}
              </CardContent>
            </Card>
          </div>
        </div>
      </div>
    </div>
  );
}

function PhonePreview({ className }: { className?: string }) {
  return (
    <div
      className={cn(
        "w-[17rem] overflow-hidden rounded-[2rem] border-[6px] border-neutral-800 bg-neutral-50 shadow-2xl shadow-black/60",
        className,
      )}
    >
      <div className="flex h-11 items-center justify-between bg-brand-900 px-4">
        <Wordmark onDark className="text-base" />
        <span className="flex items-center gap-2 text-white">
          <Bell className="size-4" />
          <span className="flex size-6 items-center justify-center rounded-full bg-brand-300 text-xs font-bold text-brand-900">
            AK
          </span>
        </span>
      </div>
      <div className="flex flex-col gap-2.5 p-3">
        <Card size="sm" className="gap-1 bg-brand-900 text-white ring-0">
          <CardHeader className="flex flex-row items-center justify-between">
            <CardTitle className="text-xs font-medium text-neutral-300">Bugünkü ciro</CardTitle>
            <span className="flex items-center gap-1 text-xs font-semibold text-brand-100">
              <TrendingUp className="size-3.5" /> %12
            </span>
          </CardHeader>
          <CardContent>
            <p className="text-2xl font-bold tabular-nums">8.450 ₺</p>
            <p className="text-xs text-neutral-400">Nakit 2.250 ₺ · Kart 6.200 ₺</p>
          </CardContent>
        </Card>
        <Card size="sm" className="gap-1">
          <CardHeader className="flex flex-row items-center justify-between">
            <CardTitle className="text-sm font-semibold text-brand-700">Bugünün randevuları</CardTitle>
            <span className="text-xs font-semibold text-brand-500">12</span>
          </CardHeader>
          <CardContent>
            <AppointmentList items={appointments} />
          </CardContent>
        </Card>
      </div>
      <div className="flex items-center justify-around border-t border-border bg-white px-2 py-1.5 text-neutral-600">
        <CalendarDays className="size-5 text-brand-500" />
        <ReceiptText className="size-5" />
        <span className="flex size-9 items-center justify-center rounded-full bg-brand-500 text-white">
          <Plus className="size-5" />
        </span>
        <Users className="size-5" />
        <Menu className="size-5" />
      </div>
    </div>
  );
}

/** Masaüstünde ekran + üstüne binen telefon; telefonda sadece telefon ekranı. */
export function PanelPreview() {
  return (
    <div role="img" aria-label="KobiFabrika panelinden örnek ekran: bugünkü ciro, randevular ve haftalık satış" className="light relative text-foreground">
      <div className="hidden lg:block">
        <DesktopPreview />
      </div>
      <PhonePreview className="mx-auto lg:absolute lg:right-6 lg:-bottom-16" />
    </div>
  );
}
