import { Bell, Check, MessageSquareText, Plus, Search } from "lucide-react";
import type { ReactNode } from "react";
import { cn } from "cn";

import { Wordmark } from "@/components/brand/wordmark";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { navGroups } from "@/lib/navigation";

/*
 * Tanıtım sitesindeki ürün ekranları: resim değil, panelin gerçek bileşenleriyle (Card, Badge, Button, menü)
 * çizilmiş örnekler. Veriler örnektir; her çerçevenin altında "Örnek ekran" yazar.
 * Ekran okuyucu için her çerçeve tek görsel (role="img" + açıklama); içindeki etkileşimsiz öğeler gizlenir.
 */

// ── Çerçeveler ──────────────────────────────────────────────────────────────

function Caption({ children }: { children: ReactNode }) {
  return <figcaption className="mt-3 text-xs text-neutral-600">{children}</figcaption>;
}

/** Tarayıcı penceresi: ince siyah çizgi, adres çubuğu, gölge yok. */
export function BrowserFrame({
  label,
  caption = "Örnek ekran — isimler ve tutarlar temsilîdir.",
  url = "app.kobifabrika.com/panel",
  className,
  children,
}: {
  label: string;
  caption?: string;
  url?: string;
  className?: string;
  children: ReactNode;
}) {
  return (
    <figure className={className}>
      <div role="img" aria-label={label} className="overflow-hidden rounded-lg border border-black bg-white">
        <div aria-hidden className="flex h-9 items-center gap-3 border-b border-black px-3">
          <span className="flex gap-1.5">
            <span className="size-2.5 rounded-full border border-black" />
            <span className="size-2.5 rounded-full border border-black" />
            <span className="size-2.5 rounded-full border border-black" />
          </span>
          <span className="flex-1 truncate rounded-sm bg-neutral-100 px-2 py-0.5 text-xs text-neutral-600">{url}</span>
        </div>
        <div aria-hidden inert>
          {children}
        </div>
      </div>
      <Caption>{caption}</Caption>
    </figure>
  );
}

/** Telefon çerçevesi: düz, eğik değil. */
export function PhoneFrame({
  label,
  caption = "Örnek ekran",
  className,
  children,
}: {
  label: string;
  caption?: string;
  className?: string;
  children: ReactNode;
}) {
  return (
    <figure className={cn("w-[17.5rem] shrink-0", className)}>
      <div role="img" aria-label={label} className="rounded-[2.25rem] border-[3px] border-black bg-black p-1.5">
        <div aria-hidden inert className="flex h-[33rem] flex-col overflow-hidden rounded-[1.85rem] bg-white">
          <div className="flex h-7 shrink-0 items-center justify-between px-5 text-xs font-semibold">
            <span className="tabular-nums">14:12</span>
            <span className="h-4 w-16 rounded-full bg-black" />
            <span>5G</span>
          </div>
          {children}
        </div>
      </div>
      <Caption>{caption}</Caption>
    </figure>
  );
}

// ── Örnek veriler ───────────────────────────────────────────────────────────

const today = [
  { time: "10:00", name: "Elif Arslan", service: "Fön", staff: "Derya", status: "Geldi" },
  { time: "11:30", name: "Murat Kaya", service: "Sakal tıraşı", staff: "Serkan", status: "Geldi" },
  { time: "14:30", name: "Ayşe Yılmaz", service: "Saç kesimi", staff: "Derya", status: "Onaylı" },
  { time: "15:15", name: "Hakan Öztürk", service: "Saç + sakal", staff: "Serkan", status: "Online" },
  { time: "16:45", name: "Zeynep Çelik", service: "Boya", staff: "Derya", status: "Onaylı" },
];

function StatusBadge({ status }: { status: string }) {
  if (status === "Geldi")
    return (
      <Badge variant="outline" className="h-6 gap-1 border-black text-xs">
        <Check aria-hidden /> Geldi
      </Badge>
    );
  return (
    <Badge variant="secondary" className="h-6 text-xs">
      {status}
    </Badge>
  );
}

// ── Panel "Bugün" ekranı (masaüstü) ─────────────────────────────────────────

export function TodayScreen() {
  const items = navGroups[0].items.slice(0, 7);
  return (
    <div className="flex h-[30rem] text-left">
      <div className="hidden w-48 shrink-0 flex-col bg-black px-2 py-4 md:flex">
        <Wordmark onDark className="px-2 text-lg" />
        <ul className="mt-6 flex flex-col gap-0.5">
          {items.map(({ slug, label, icon: Icon }, i) => (
            <li
              key={slug || "bugun"}
              className={cn(
                "flex h-9 items-center gap-2.5 rounded-md px-2 text-sm",
                i === 0 ? "bg-brand font-semibold text-black" : "text-neutral-300",
              )}
            >
              <Icon className="size-4" /> {label}
            </li>
          ))}
        </ul>
      </div>
      <div className="flex min-w-0 flex-1 flex-col">
        <div className="flex h-12 items-center justify-between gap-3 bg-black px-4">
          <span className="flex h-8 w-56 items-center gap-2 rounded-md bg-neutral-800 px-2 text-xs text-neutral-300">
            <Search className="size-3.5" /> Müşteri, ürün, satış ara…
          </span>
          <span className="flex items-center gap-3 text-neutral-300">
            <Bell className="size-4" />
            <span className="flex size-7 items-center justify-center rounded-full bg-neutral-800 text-xs font-bold text-white">
              DK
            </span>
          </span>
        </div>
        <div className="flex-1 overflow-hidden p-5">
          <div className="flex items-baseline justify-between">
            <p className="font-heading text-xl font-bold">Günaydın, Derya.</p>
            <p className="text-xs text-neutral-600">Salı, 13 Ekim</p>
          </div>
          <div className="mt-4 grid grid-cols-4 divide-x divide-neutral-200 border-y border-neutral-200">
            {[
              ["Bugünkü satış", "8.450 TL"],
              ["Tahsilat", "6.200 TL"],
              ["Açık veresiye", "14.780 TL"],
              ["Randevu", "12"],
            ].map(([label, value]) => (
              <div key={label} className="px-3 py-3 first:pl-0">
                <p className="text-xs text-neutral-600">{label}</p>
                <p className="mt-0.5 text-lg font-bold tabular-nums">{value}</p>
              </div>
            ))}
          </div>
          <div className="mt-4 grid grid-cols-[1.4fr_1fr] gap-4">
            <Card size="sm" className="gap-1">
              <CardHeader>
                <CardTitle className="font-semibold">Bugünün randevuları</CardTitle>
              </CardHeader>
              <CardContent>
                <ul className="divide-y divide-border">
                  {today.slice(0, 4).map((a) => (
                    <li key={a.time} className="flex items-center gap-3 py-1.5">
                      <span className="w-10 text-sm font-semibold tabular-nums">{a.time}</span>
                      <span className="min-w-0 flex-1 truncate text-sm">
                        {a.name} <span className="text-neutral-600">— {a.service}</span>
                      </span>
                      <StatusBadge status={a.status} />
                    </li>
                  ))}
                </ul>
              </CardContent>
            </Card>
            <Card size="sm" className="gap-1">
              <CardHeader>
                <CardTitle className="font-semibold">Tahsil edilecek</CardTitle>
              </CardHeader>
              <CardContent>
                <ul className="divide-y divide-border text-sm">
                  {[
                    ["Hasan Demir", "1.250 TL"],
                    ["Gül Market", "3.400 TL"],
                    ["Selin Aydın", "780 TL"],
                  ].map(([n, v]) => (
                    <li key={n} className="flex justify-between py-1.5">
                      <span>{n}</span>
                      <span className="font-semibold tabular-nums">{v}</span>
                    </li>
                  ))}
                </ul>
              </CardContent>
            </Card>
          </div>
        </div>
      </div>
    </div>
  );
}

// ── Telefonda randevu günü ──────────────────────────────────────────────────

export function PhoneAgenda() {
  return (
    <div className="flex min-h-0 flex-1 flex-col">
      <div className="flex h-12 items-center justify-between bg-black px-4">
        <Wordmark onDark className="text-base" />
        <Bell className="size-4 text-neutral-300" />
      </div>
      <div className="px-4 pt-4">
        <p className="font-heading text-lg font-bold">Randevular</p>
        <div className="mt-2 flex gap-1 text-xs">
          {["Pzt 12", "Sal 13", "Çar 14", "Per 15"].map((d, i) => (
            <span
              key={d}
              className={cn(
                "flex-1 rounded-md border py-1.5 text-center",
                i === 1 ? "border-black bg-black font-semibold text-white" : "border-neutral-200",
              )}
            >
              {d}
            </span>
          ))}
        </div>
      </div>
      <ul className="mt-3 flex min-h-0 flex-1 flex-col overflow-hidden px-4">
        {today.map((a) => (
          <li key={a.time} className="flex gap-3 border-t border-neutral-200 py-2.5">
            <span className="w-10 pt-0.5 text-sm font-semibold tabular-nums">{a.time}</span>
            <span className="min-w-0 flex-1">
              <span className="block truncate text-sm font-semibold">{a.name}</span>
              <span className="block truncate text-xs text-neutral-600">
                {a.service} · {a.staff}
              </span>
            </span>
            <StatusBadge status={a.status} />
          </li>
        ))}
      </ul>
      <div className="flex shrink-0 items-center justify-around border-t border-neutral-200 py-2 text-xs text-neutral-600">
        <span>Bugün</span>
        <span className="font-semibold text-black">Randevu</span>
        <span className="flex size-10 items-center justify-center rounded-full bg-black text-white">
          <Plus className="size-5" />
        </span>
        <span>Müşteri</span>
        <span>Menü</span>
      </div>
    </div>
  );
}

// ── Modül parçaları ─────────────────────────────────────────────────────────

/** Randevu: hatırlatma SMS'i giden randevu. */
export function ReminderShot() {
  return (
    <div className="p-5 text-left">
      <p className="text-xs font-semibold tracking-wide text-neutral-600 uppercase">Salı, 13 Ekim</p>
      <div className="mt-3 rounded-md border border-neutral-200">
        {today.slice(1, 4).map((a) => (
          <div key={a.time} className="flex items-center gap-3 border-b border-neutral-200 px-3 py-2.5 last:border-b-0">
            <span className="w-11 text-sm font-semibold tabular-nums">{a.time}</span>
            <span className="min-w-0 flex-1 text-sm">
              <span className="font-semibold">{a.name}</span>{" "}
              <span className="block text-neutral-600 sm:inline">
                <span className="hidden sm:inline">— </span>
                {a.service}
              </span>
            </span>
            <StatusBadge status={a.status} />
          </div>
        ))}
      </div>
      <div className="mt-4 flex gap-3 rounded-md bg-neutral-100 p-3">
        <MessageSquareText className="mt-0.5 size-4 shrink-0" />
        <div className="text-sm">
          <p className="font-semibold">Hatırlatma gönderildi · dün 18:00</p>
          <p className="mt-1 text-neutral-600">
            Ayşe Hanım, yarın 14:30&apos;da saç kesimi randevunuz var. Gelemeyecekseniz İPTAL yazın. — Derya Kuaför
          </p>
        </div>
      </div>
    </div>
  );
}

/** Cari: veresiye defteri yerine müşteri hesabı. */
export function LedgerShot() {
  const rows = [
    ["2 Eki", "Satış — 2 kg kıyma, 1 kg kuşbaşı", "+1.850 TL"],
    ["5 Eki", "Tahsilat — nakit", "−1.000 TL"],
    ["9 Eki", "Satış — sucuk, kaşar", "+400 TL"],
  ];
  return (
    <div className="p-5 text-left">
      <div className="flex items-start justify-between gap-3">
        <div>
          <p className="font-heading text-lg font-bold">Hasan Demir</p>
          <p className="text-xs text-neutral-600">+90 532 *** ** 18 · Veresiye limiti 3.000 TL</p>
        </div>
        <div className="text-right">
          <p className="text-xs text-neutral-600">Bakiye</p>
          <p className="text-xl font-bold whitespace-nowrap tabular-nums">1.250 TL</p>
        </div>
      </div>
      <table className="mt-4 w-full text-sm">
        <tbody>
          {rows.map(([d, t, v]) => (
            <tr key={d} className="border-t border-neutral-200">
              <td className="w-14 py-2 text-neutral-600 tabular-nums">{d}</td>
              <td className="py-2">{t}</td>
              <td className="py-2 text-right font-semibold whitespace-nowrap tabular-nums">{v}</td>
            </tr>
          ))}
        </tbody>
      </table>
      <div className="mt-4 flex gap-2">
        <Button size="sm" className="h-9 px-3">
          Tahsilat al
        </Button>
        <Button size="sm" variant="outline" className="h-9 px-3">
          Hatırlatma gönder
        </Button>
      </div>
    </div>
  );
}

/** Kampanya: uzun süredir gelmeyenlere geri kazanma mesajı. */
export function WinBackShot() {
  return (
    <div className="p-5 text-left">
      <p className="font-heading text-lg font-bold">Geri kazanma mesajı</p>
      <dl className="mt-3 grid grid-cols-3 border-y border-neutral-200 text-sm">
        {[
          ["45 gündür gelmeyen", "38 kişi"],
          ["SMS izni olan", "31 kişi"],
          ["Tahmini maliyet", "31 SMS"],
        ].map(([k, v]) => (
          <div key={k} className="border-r border-neutral-200 px-2 py-2 first:pl-0 last:border-r-0">
            <dt className="text-xs text-neutral-600">{k}</dt>
            <dd className="font-semibold tabular-nums">{v}</dd>
          </div>
        ))}
      </dl>
      <div className="mt-4 rounded-md border border-neutral-200 p-3 text-sm">
        Sizi özledik! Bu hafta gelene saç bakımı %20 indirimli. Randevu: kobifabrika.com/r/derya-kuafor
        <span className="mt-2 block text-xs text-neutral-600">Listeden çıkmak için RET yazıp gönderin.</span>
      </div>
      <p className="mt-3 flex items-center gap-1.5 text-xs text-neutral-600">
        <Check className="size-3.5 text-black" /> İzni olmayan 7 kişiye gönderilmez.
      </p>
    </div>
  );
}

/** Personel: ay sonu mesai ve prim. */
export function StaffShot() {
  const rows = [
    ["Derya K.", "176 sa", "38.400 TL", "1.920 TL"],
    ["Serkan T.", "168 sa", "31.250 TL", "1.560 TL"],
    ["Merve A.", "92 sa", "12.800 TL", "640 TL"],
  ];
  return (
    <div className="p-5 text-left">
      <div className="flex items-baseline justify-between">
        <p className="font-heading text-lg font-bold">Eylül özeti</p>
        <p className="text-xs text-neutral-600">Prim: satışın %5&apos;i</p>
      </div>
      <table className="mt-3 w-full text-sm">
        <thead>
          <tr className="text-left text-xs text-neutral-600">
            <th className="pb-2 font-medium">Personel</th>
            <th className="pb-2 font-medium">Mesai</th>
            <th className="pb-2 text-right font-medium">Satış</th>
            <th className="pb-2 text-right font-medium">Prim</th>
          </tr>
        </thead>
        <tbody className="tabular-nums">
          {rows.map(([n, h, s, p]) => (
            <tr key={n} className="border-t border-neutral-200">
              <td className="py-2 font-semibold">{n}</td>
              <td className="py-2">{h}</td>
              <td className="py-2 text-right">{s}</td>
              <td className="py-2 text-right font-semibold">{p}</td>
            </tr>
          ))}
        </tbody>
      </table>
      <p className="mt-3 text-xs text-neutral-600">Merve A. · 16-17 Eylül yıllık izin (onaylandı)</p>
    </div>
  );
}
