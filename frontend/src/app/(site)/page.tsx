import Link from "next/link";
import {
  ArrowRight,
  BadgeTurkishLira,
  CalendarDays,
  ChartColumn,
  Check,
  ChevronDown,
  Download,
  Gift,
  Headset,
  IdCard,
  Layers,
  Megaphone,
  Boxes,
  Receipt,
  ShieldCheck,
  Smartphone,
  Star,
  UserCheck,
  Users,
  Wallet,
  X,
  type LucideIcon,
} from "lucide-react";
import { cn } from "cn";

import { Backdrop } from "@/components/brand/backdrop";
import { Wordmark } from "@/components/brand/wordmark";
import { sectors } from "@/components/business/sectors";
import { PanelPreview } from "@/components/site/panel-preview";
import { Button } from "@/components/ui/button";
import { allPlansInclude, formatPlanPrice, planFootnote, plans } from "@/content/plans";

// Metinler taslak; kesin metinler Sprint 8'de (docs/05-sprintler.md). Fiyatlar: src/content/plans.ts.

const modules: { icon: LucideIcon; title: string; text: string }[] = [
  { icon: CalendarDays, title: "Randevu", text: "Online randevu sayfası, personel takvimi ve SMS hatırlatma. Gelmeyen müşteri azalır." },
  { icon: Receipt, title: "Satış ve Teklif", text: "Hızlı satış, iskonto ve parçalı ödeme. Teklifi PDF olarak gönder, onaylanınca satışa çevir." },
  { icon: Wallet, title: "Cari ve Kasa", text: "Veresiye, tahsilat ve ödeme takibi. Kimin ne kadar borcu var, kasada ne var, tek bakışta." },
  { icon: Users, title: "Müşteriler", text: "Her müşterinin randevusu, alışverişi ve borcu aynı kartta. Excel'den içe aktar." },
  { icon: Megaphone, title: "Kampanya ve SMS", text: "Uzun süredir gelmeyene geri kazanma mesajı, boş saate kampanya. İzinsiz kimseye gitmez." },
  { icon: IdCard, title: "Personel, Mesai, İzin", text: "Giriş-çıkış, izin talebi ve onayı, satıştan prim hesabı. Ay sonu hesap hazır." },
  { icon: Boxes, title: "Stok", text: "Ürün ve hizmet listesi, stok hareketleri ve kritik stok uyarısı." },
  { icon: ChartColumn, title: "Raporlar", text: "Satış, alacak, gelir-gider ve personel performansı. Hepsi Excel'e aktarılır." },
];

const differentiators: { icon: LucideIcon; title: string; text: string }[] = [
  { icon: Layers, title: "Tek uygulama, iki iş", text: "Para takibi ve müşteri kazanma aynı yerde; iki program, iki abonelik yok." },
  { icon: Star, title: "Müşteri kazandıran program", text: "Geri kazanma mesajı, boş saat kampanyası, memnuniyetten Google yoruma." },
  { icon: BadgeTurkishLira, title: "TL, sabit, komisyonsuz fiyat", text: "Fiyat TL; kur farkı ve satıştan komisyon yok." },
  { icon: ShieldCheck, title: "Şeffaf abonelik", text: "Yenilemeden 7 gün önce hatırlatma, tek tıkla iptal, sürpriz zam yok." },
  { icon: Download, title: "Veri senindir", text: "Abonelik bitse de salt okunur erişim ve Excel dışa aktarma sürer." },
  { icon: Headset, title: "Gerçek destek", text: "Panelden WhatsApp ile doğrudan ekibe ulaşırsın; bayi ve aracı yok." },
  { icon: Gift, title: "Kalıcı ücretsiz paket", text: "Çırak paketi süresiz ücretsiz; büyüdükçe üst pakete geçersin." },
  { icon: Smartphone, title: "10 dakikada kurulum", text: "Telefondan kurulur, telefondan kullanılır; uygulama indirmek gerekmez." },
  { icon: UserCheck, title: "Mali müşavir ücretsiz", text: "Mali müşavirin salt okunur kullanıcı olarak girer, ücret ödemezsin." },
];

// Sektör şeridi: sihirbazdaki sektörlerle aynı ikonlar, kısa adlar.
const sectorStrip = (
  [
    ["HairBarber", "Kuaför/Berber"],
    ["BeautyCare", "Güzellik"],
    ["ClinicHealth", "Klinik"],
    ["SportsFitness", "Spor"],
    ["AutoService", "Oto Servis"],
    ["RetailStore", "Mağaza"],
    ["CafeRestaurant", "Kafe"],
  ] as const
).map(([value, label]) => ({ label, icon: sectors.find((s) => s.value === value)!.icon }));

const faqs: { q: string; a: string }[] = [
  {
    q: "Verilerim güvende mi?",
    a: "Verileriniz şifreli bağlantıyla taşınır, her işletmenin verisi diğerlerinden ayrı tutulur ve her gece yedeklenir. KVKK'ya uygun çalışırız; verileriniz satılmaz, reklam için kullanılmaz.",
  },
  {
    q: "İstediğim zaman iptal edebilir miyim?",
    a: "Evet. Panelden tek tıkla iptal edersiniz; yenilemeden 7 gün önce SMS ve e-postayla hatırlatırız. Abonelik bitse de verileriniz silinmez, salt okunur görür ve Excel'e aktarırsınız.",
  },
  {
    q: "Mali müşavirim sisteme girebilir mi?",
    a: "Evet, ücretsiz. Mali müşaviriniz salt okunur kullanıcı olarak girer, raporları ve hareketleri Excel olarak indirir. Kullanıcı sayınıza dahil edilmez.",
  },
  {
    q: "Telefondan çalışır mı?",
    a: "Evet. KobiFabrika önce telefon için tasarlandı; uygulama indirmeden tarayıcıdan açılır. Bilgisayarda ve tablette de aynı hesapla çalışırsınız.",
  },
  {
    q: "e-Fatura ve e-Arşiv var mı?",
    a: "e-Fatura ve e-Arşiv Faz 2'de geliyor. Şimdilik satış ve teklif belgelerini PDF olarak müşterinize gönderebilirsiniz.",
  },
  {
    q: "Destek nasıl alırım?",
    a: "Panelden WhatsApp ile doğrudan KobiFabrika ekibine yazarsınız; bayi veya çağrı merkezi aracı yoktur. Sık sorulanlar için yardım makaleleri de panelde.",
  },
  {
    q: "Mevcut müşteri ve ürün listemi aktarabilir miyim?",
    a: "Evet. Excel şablonunu indirip doldurun, müşteri ve ürünleriniz birkaç dakikada içeri aktarılır.",
  },
  {
    q: "Ücretsiz paket gerçekten ücretsiz mi?",
    a: "Evet. Çırak paketi süresiz ücretsizdir. İşiniz büyüyünce Kalfa veya Usta'ya geçer, 14 gün ücretsiz denersiniz.",
  },
];

function SectionHeading({ eyebrow, title, text, id }: { eyebrow: string; title: string; text?: string; id: string }) {
  return (
    <div className="kf-reveal max-w-2xl">
      <p className="text-sm font-semibold tracking-wider text-brand-300 uppercase">{eyebrow}</p>
      <h2 id={id} className="mt-2 text-3xl font-bold tracking-tight text-white sm:text-4xl">
        {title}
      </h2>
      {text && <p className="mt-4 text-lg text-neutral-300">{text}</p>}
    </div>
  );
}

/** Koyu zeminde cam kart. */
const glassCard = "rounded-xl border border-white/10 bg-white/[0.04] backdrop-blur-sm";

export default function HomePage() {
  return (
    <>
      {/* a) Hero */}
      <section className="relative overflow-hidden">
        <Backdrop />
        <div className="relative mx-auto flex max-w-6xl flex-col gap-14 px-4 pt-14 pb-20 sm:pt-20 lg:gap-16 lg:pt-24 lg:pb-36">
          <div className="mx-auto flex max-w-3xl flex-col items-center text-center">
            <p className="inline-flex items-center gap-2 rounded-full border border-white/10 bg-white/[0.04] px-3 py-1 text-sm text-neutral-300">
              <span aria-hidden className="size-2 rounded-full bg-brand-100" />
              KOBİ ve esnaf için tek uygulama
            </p>
            <h1 className="mt-5 text-4xl leading-[1.1] font-extrabold tracking-tight text-white sm:text-5xl lg:text-6xl">
              Paranı takip et, <span className="text-brand-100">müşterini geri getir.</span>
            </h1>
            <p className="mt-5 max-w-2xl text-lg text-neutral-300">
              Ön muhasebe, randevu, kampanya ve personel tek uygulamada. Cari, kasa, veresiye ve stok; online randevu,
              SMS kampanyası ve Google yorum; mesai, izin ve prim.
            </p>
            <div className="mt-8 flex w-full flex-col gap-3 sm:w-auto sm:flex-row">
              <Button asChild size="lg" className="hover:bg-brand-100">
                <Link href="/kayit">
                  Ücretsiz başla <ArrowRight aria-hidden data-icon="inline-end" />
                </Link>
              </Button>
              <Button asChild size="lg" variant="outline" className="border-white/30 text-white">
                <Link href="/giris">Giriş yap</Link>
              </Button>
            </div>
            <ul className="mt-6 flex flex-wrap justify-center gap-x-5 gap-y-2 text-sm text-neutral-400">
              {["Çırak paketi süresiz ücretsiz", "10 dakikada kurulum", "Tek tıkla iptal"].map((t) => (
                <li key={t} className="flex items-center gap-1.5">
                  <Check aria-hidden className="size-4 text-brand-300" /> {t}
                </li>
              ))}
            </ul>
          </div>
          <div className="mx-auto w-full max-w-5xl">
            <PanelPreview />
          </div>
        </div>
      </section>

      {/* b) İki program yerine tek uygulama */}
      <section aria-labelledby="tek-uygulama" className="border-t border-border">
        <div className="mx-auto max-w-6xl px-4 py-20 sm:py-24">
          <SectionHeading
            id="tek-uygulama"
            eyebrow="İki program yerine tek uygulama"
            title="Esnaf bugün iki ayrı program alıyor."
            text="Muhasebe programında randevu yok, randevu programında cari yok. İki abonelik, iki şifre, iki ayrı müşteri listesi."
          />
          <div className="mt-12 grid items-center gap-4 lg:grid-cols-[1fr_auto_1.1fr]">
            <div className="kf-reveal grid gap-4 sm:grid-cols-2 lg:grid-cols-1">
              <CompareCard
                title="Ön muhasebe programı"
                has={["Cari ve kasa", "Fatura ve stok"]}
                lacks={["Randevu yok", "SMS kampanyası yok", "Mesai ve izin yok"]}
              />
              <CompareCard
                title="Randevu programı"
                has={["Randevu ve takvim", "SMS hatırlatma"]}
                lacks={["Cari hesap yok", "Kasa ve veresiye yok"]}
              />
            </div>
            <div aria-hidden className="flex justify-center">
              <ArrowRight className="size-8 rotate-90 text-brand-300 lg:rotate-0" />
            </div>
            <div className="kf-reveal relative overflow-hidden rounded-xl border border-brand-300/40 bg-card p-6 shadow-[0_0_60px_-15px_var(--color-brand-300)] sm:p-8">
              <Wordmark onDark className="text-2xl" />
              <p className="mt-2 text-lg font-semibold text-white">Hepsi tek uygulamada, tek müşteri kartında.</p>
              <p className="mt-2 text-neutral-300">
                Randevuya gelen müşterinin veresiyesi, aldığı ürünler ve kampanya izni aynı kartta. Satış yapınca kasa ve
                stok kendiliğinden güncellenir.
              </p>
              <ul className="mt-5 grid gap-2 sm:grid-cols-2">
                {["Cari, kasa, veresiye", "Satış, teklif, stok", "Randevu ve hatırlatma", "Kampanya ve Google yorum", "Personel, mesai, izin", "Tek abonelik, TL fiyat"].map(
                  (t) => (
                    <li key={t} className="flex items-center gap-2 text-sm text-white">
                      <Check aria-hidden className="size-4 shrink-0 text-brand-100" /> {t}
                    </li>
                  ),
                )}
              </ul>
            </div>
          </div>
        </div>
      </section>

      {/* c) Modüller */}
      <section aria-labelledby="moduller" className="relative border-t border-border">
        <div className="mx-auto max-w-6xl px-4 py-20 sm:py-24">
          <SectionHeading
            id="moduller"
            eyebrow="Modüller"
            title="İşletmenin ihtiyacı olan her şey."
            text="Sektörünü seçince menü sana göre ayarlanır; kullanmadığın modülü kapatırsın."
          />
          <ul className="mt-12 grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
            {modules.map(({ icon: Icon, title, text }) => (
              <li key={title} className={cn(glassCard, "kf-reveal p-5 transition-colors hover:border-brand-300/40")}>
                <span className="flex size-11 items-center justify-center rounded-lg bg-brand-300/10">
                  <Icon aria-hidden className="size-6 text-brand-300" />
                </span>
                <h3 className="mt-4 text-lg font-semibold text-white">{title}</h3>
                <p className="mt-1 text-sm leading-6 text-neutral-400">{text}</p>
              </li>
            ))}
          </ul>
        </div>
      </section>

      {/* d) Bizi farklı kılan */}
      <section aria-labelledby="farkimiz" className="relative overflow-hidden border-t border-border">
        <Backdrop grid={false} className="opacity-60" />
        <div className="relative mx-auto max-w-6xl px-4 py-20 sm:py-24">
          <SectionHeading id="farkimiz" eyebrow="Bizi farklı kılan" title="Esnafın derdini dinleyerek yaptık." />
          <ol className="mt-12 grid gap-x-8 gap-y-8 sm:grid-cols-2 lg:grid-cols-3">
            {differentiators.map(({ icon: Icon, title, text }, i) => (
              <li key={title} className="kf-reveal flex gap-4">
                <span className="flex size-11 shrink-0 items-center justify-center rounded-full border border-brand-300/30 text-brand-300">
                  <Icon aria-hidden className="size-5" />
                </span>
                <div>
                  <h3 className="font-semibold text-white">
                    <span className="sr-only">{i + 1}. </span>
                    {title}
                  </h3>
                  <p className="mt-1 text-sm leading-6 text-neutral-400">{text}</p>
                </div>
              </li>
            ))}
          </ol>
        </div>
      </section>

      {/* e) Sektörler */}
      <section aria-labelledby="sektorler" className="border-y border-border bg-card/50">
        <div className="mx-auto max-w-6xl px-4 py-10">
          <h2 id="sektorler" className="text-center text-sm font-semibold tracking-wider text-neutral-400 uppercase">
            Her sektöre uygun şablon
          </h2>
          <ul className="mt-6 flex flex-wrap justify-center gap-3">
            {sectorStrip.map(({ label, icon: Icon }) => (
              <li
                key={label}
                className="flex min-h-11 items-center gap-2 rounded-full border border-white/10 bg-white/[0.04] px-4 text-sm font-medium text-white"
              >
                <Icon aria-hidden className="size-5 text-brand-300" />
                {label}
              </li>
            ))}
          </ul>
        </div>
      </section>

      {/* f) Paketler */}
      <section aria-labelledby="paketler">
        <div className="mx-auto max-w-6xl px-4 py-20 sm:py-24">
          <SectionHeading
            id="paketler"
            eyebrow="Paketler"
            title="Ücretsiz başla, büyüdükçe geç."
            text="TL, sabit ve komisyonsuz fiyat. Lansman fiyatlarını çok yakında açıklıyoruz."
          />
          <ul className="mt-12 grid gap-4 lg:grid-cols-3">
            {plans.map((plan) => (
              <li
                key={plan.id}
                className={cn(
                  "kf-reveal relative flex flex-col rounded-2xl border p-6 sm:p-8",
                  plan.highlighted
                    ? "border-brand-300/60 bg-card shadow-[0_0_60px_-20px_var(--color-brand-300)]"
                    : "border-white/10 bg-white/[0.03]",
                )}
              >
                {plan.highlighted && (
                  <span className="absolute -top-3 left-6 rounded-full bg-brand-300 px-3 py-0.5 text-xs font-bold text-brand-900">
                    Önerilen
                  </span>
                )}
                <h3 className="text-xl font-bold text-white">{plan.name}</h3>
                <p className="mt-1 text-sm text-neutral-400">{plan.tagline}</p>
                <p className={cn("mt-6 font-extrabold tracking-tight text-white", plan.monthlyPrice === null ? "text-2xl" : "text-3xl")}>
                  {formatPlanPrice(plan)}
                  {!!plan.monthlyPrice && <span className="text-base font-medium text-neutral-400"> / ay</span>}
                </p>
                <p className="mt-1 text-sm text-brand-300">{plan.priceNote}</p>
                <ul className="mt-6 flex flex-1 flex-col gap-3">
                  {plan.features.map((f) => (
                    <li key={f} className="flex items-start gap-2 text-sm text-neutral-300">
                      <Check aria-hidden className="mt-0.5 size-4 shrink-0 text-brand-100" /> {f}
                    </li>
                  ))}
                </ul>
                <Button
                  asChild
                  size="lg"
                  variant={plan.highlighted ? "default" : "outline"}
                  className={cn("mt-8 w-full", plan.highlighted ? "hover:bg-brand-100" : "border-white/30 text-white")}
                >
                  <Link href="/kayit">{plan.cta}</Link>
                </Button>
              </li>
            ))}
          </ul>
          <div className={cn(glassCard, "kf-reveal mt-6 p-5 sm:p-6")}>
            <p className="text-sm font-semibold text-white">Bütün paketlerde</p>
            <ul className="mt-3 grid gap-2 sm:grid-cols-2">
              {allPlansInclude.map((t) => (
                <li key={t} className="flex items-start gap-2 text-sm text-neutral-300">
                  <Check aria-hidden className="mt-0.5 size-4 shrink-0 text-brand-300" /> {t}
                </li>
              ))}
            </ul>
            <p className="mt-4 text-xs text-neutral-400">{planFootnote}</p>
          </div>
        </div>
      </section>

      {/* g) SSS */}
      <section aria-labelledby="sss" className="border-t border-border">
        <div className="mx-auto grid max-w-6xl gap-10 px-4 py-20 sm:py-24 lg:grid-cols-[1fr_1.6fr]">
          <SectionHeading id="sss" eyebrow="SSS" title="Sık sorulan sorular" text="Aklına takılan başka bir şey varsa bize yaz." />
          <div className="flex flex-col gap-3">
            {faqs.map(({ q, a }) => (
              <details key={q} className={cn(glassCard, "group kf-reveal")}>
                <summary className="flex min-h-14 cursor-pointer list-none items-center justify-between gap-4 rounded-xl px-5 py-3 text-base font-semibold text-white outline-ring [&::-webkit-details-marker]:hidden">
                  {q}
                  <ChevronDown aria-hidden className="size-5 shrink-0 text-brand-300 transition-transform group-open:rotate-180" />
                </summary>
                <p className="px-5 pb-5 text-sm leading-6 text-neutral-300">{a}</p>
              </details>
            ))}
          </div>
        </div>
      </section>

      {/* h) Son çağrı */}
      <section className="relative overflow-hidden border-t border-border">
        <Backdrop />
        <div className="kf-reveal relative mx-auto flex max-w-3xl flex-col items-center px-4 py-20 text-center sm:py-28">
          <h2 className="text-3xl font-bold tracking-tight text-white sm:text-5xl">
            İşini tek uygulamadan yönet. <span className="text-brand-100">Bugün başla.</span>
          </h2>
          <p className="mt-5 text-lg text-neutral-300">10 dakikada kur, Çırak paketiyle ücretsiz kullan. İstediğin zaman tek tıkla iptal.</p>
          <div className="mt-8 flex w-full flex-col gap-3 sm:w-auto sm:flex-row">
            <Button asChild size="lg" className="hover:bg-brand-100">
              <Link href="/kayit">
                Ücretsiz başla <ArrowRight aria-hidden data-icon="inline-end" />
              </Link>
            </Button>
            <Button asChild size="lg" variant="outline" className="border-white/30 text-white">
              <Link href="/giris">Giriş yap</Link>
            </Button>
          </div>
        </div>
      </section>
    </>
  );
}

function CompareCard({ title, has, lacks }: { title: string; has: string[]; lacks: string[] }) {
  return (
    <div className={cn(glassCard, "p-5")}>
      <h3 className="font-semibold text-white">{title}</h3>
      <ul className="mt-3 flex flex-col gap-2 text-sm">
        {has.map((t) => (
          <li key={t} className="flex items-center gap-2 text-neutral-300">
            <Check aria-hidden className="size-4 shrink-0 text-brand-300" /> {t}
          </li>
        ))}
        {lacks.map((t) => (
          <li key={t} className="flex items-center gap-2 text-neutral-400">
            <X aria-hidden className="size-4 shrink-0 text-neutral-400" /> {t}
          </li>
        ))}
      </ul>
    </div>
  );
}
