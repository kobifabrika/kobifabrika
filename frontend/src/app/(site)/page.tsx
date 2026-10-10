import Link from "next/link";
import {
  BadgeTurkishLira,
  CalendarCheck,
  Download,
  Headset,
  Layers,
  Gift,
  ShieldCheck,
  Smartphone,
  UserCheck,
} from "lucide-react";

import { Button } from "@/components/ui/button";

// Taslak tanıtım sayfası. Kesin metinler Sprint 8'de (docs/05-sprintler.md).
const differentiators = [
  { icon: Layers, title: "Tek uygulama, iki iş", text: "Para takibi ve müşteri kazanma aynı yerde." },
  {
    icon: CalendarCheck,
    title: "Müşteri kazandıran program",
    text: "Geri kazanma mesajı, boş saat kampanyası, memnuniyetten Google yoruma.",
  },
  { icon: BadgeTurkishLira, title: "TL, sabit, komisyonsuz fiyat", text: "Sürpriz yok, kur farkı yok." },
  {
    icon: ShieldCheck,
    title: "Şeffaf abonelik",
    text: "Yenilemeden 7 gün önce hatırlatma, tek tıkla iptal, sürpriz zam yok.",
  },
  { icon: Download, title: "Veri senindir", text: "Abonelik bitse de salt okunur erişim ve Excel dışa aktarma." },
  { icon: Headset, title: "Gerçek destek", text: "Panelden WhatsApp ile destek; bayi ve aracı yok." },
  { icon: Gift, title: "Kalıcı ücretsiz paket", text: "Çırak paketi süresiz ücretsiz." },
  { icon: Smartphone, title: "10 dakikada kurulum", text: "Telefondan kurulur, telefondan kullanılır." },
  { icon: UserCheck, title: "Mali müşavir ücretsiz", text: "Salt okunur kullanıcı olarak ekleyin." },
];

export default function HomePage() {
  return (
    <>
      <section className="bg-brand-900 text-white">
        <div className="mx-auto max-w-6xl px-4 py-16 sm:py-24">
          <p className="text-sm font-semibold tracking-wider text-brand-300 uppercase">KOBİ ve esnaf için</p>
          <h1 className="mt-3 max-w-3xl text-3xl leading-tight font-bold sm:text-5xl sm:leading-tight">
            Paranı takip eden, müşteri getiren, personelini yöneten tek uygulama.
          </h1>
          <p className="mt-5 max-w-2xl text-lg text-sidebar-foreground">
            Cari, kasa, veresiye, stok, satış ve teklif; randevu, SMS kampanyası ve Google yorum; mesai, izin ve prim.
            Hepsi KobiFabrika&apos;da.
          </p>
          <div className="mt-8 flex flex-col gap-3 sm:flex-row">
            <Button asChild size="lg" className="bg-brand-300 text-brand-900 hover:bg-brand-100">
              <Link href="/kayit">Ücretsiz başla</Link>
            </Button>
            <Button
              asChild
              size="lg"
              variant="outline"
              className="border-white/40 bg-transparent text-white hover:bg-white/10 hover:text-white"
            >
              <Link href="/giris">Giriş yap</Link>
            </Button>
          </div>
        </div>
      </section>

      <section className="mx-auto max-w-6xl px-4 py-16">
        <h2 className="text-2xl font-bold text-brand-900 sm:text-3xl">Bizi farklı kılan</h2>
        <ul className="mt-8 grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {differentiators.map(({ icon: Icon, title, text }) => (
            <li key={title} className="rounded-lg border border-border bg-white p-5">
              <Icon aria-hidden className="size-6 text-brand-500" />
              <h3 className="mt-3 text-lg font-semibold text-brand-900">{title}</h3>
              <p className="mt-1 text-sm text-neutral-600">{text}</p>
            </li>
          ))}
        </ul>
      </section>
    </>
  );
}
