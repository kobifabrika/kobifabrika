import Link from "next/link";
import { ArrowRight, Check, Minus, Plus } from "lucide-react";
import type { ReactNode } from "react";
import { cn } from "cn";

import {
  BrowserFrame,
  LedgerShot,
  PhoneAgenda,
  PhoneFrame,
  ReminderShot,
  StaffShot,
  TodayScreen,
  WinBackShot,
} from "@/components/site/product-shots";
import { Button } from "@/components/ui/button";
import { allPlansInclude, formatPlanPrice, planFootnote, plans } from "@/content/plans";

// Metinler taslak; kesin metinler Sprint 8'de (docs/05-sprintler.md). Fiyatlar: src/content/plans.ts.
// Tasarım ilkeleri: docs/04-tasarim.md → "Tanıtım sitesi". Her bölümde en fazla bir mavi öğe.

const container = "mx-auto max-w-6xl px-4 sm:px-6";

/** Bölüm başlığı: küçük gri etiket + büyük başlık + kısa metin. Sola hizalı. */
function SectionHeading({
  id,
  label,
  title,
  text,
  onDark = false,
}: {
  id: string;
  label: string;
  title: ReactNode;
  text?: string;
  onDark?: boolean;
}) {
  return (
    <div className="max-w-2xl">
      <p className={cn("text-sm font-semibold", onDark ? "text-neutral-300" : "text-neutral-600")}>{label}</p>
      <h2 id={id} className="mt-3 text-3xl leading-[1.1] font-bold tracking-tight sm:text-5xl">
        {title}
      </h2>
      {text && <p className={cn("mt-5 text-lg", onDark ? "text-neutral-300" : "text-neutral-700")}>{text}</p>}
    </div>
  );
}

/** Önemli kelimenin altında mavi çizgi (beyaz zeminde mavi yazı yasak; çizgi serbest). */
function Underline({ children }: { children: ReactNode }) {
  return (
    <span className="underline decoration-brand decoration-[0.09em] underline-offset-[0.14em] [text-decoration-skip-ink:none]">
      {children}
    </span>
  );
}

// ── İçerik ──────────────────────────────────────────────────────────────────

type Mark = "yes" | "no" | "some";
const comparison: { row: string; marks: [Mark, Mark, Mark] }[] = [
  { row: "Cari, kasa ve veresiye", marks: ["yes", "no", "yes"] },
  { row: "Satış, teklif ve stok", marks: ["yes", "no", "yes"] },
  { row: "Online randevu ve SMS hatırlatma", marks: ["no", "yes", "yes"] },
  { row: "Gelmeyen müşteriye geri kazanma mesajı", marks: ["no", "some", "yes"] },
  { row: "Mesai, izin ve prim", marks: ["no", "no", "yes"] },
  { row: "Randevu ve borç aynı müşteri kartında", marks: ["no", "no", "yes"] },
];

const features: {
  id: string;
  module: string;
  title: string;
  text: string;
  points: string[];
  url: string;
  label: string;
  shot: ReactNode;
}[] = [
  {
    id: "cari",
    module: "Cari ve kasa",
    title: "Veresiye defterini kapat.",
    text: "Kim ne aldı, ne ödedi, ne kadar borcu kaldı; her müşterinin hesabı ayrı. Borç hatırlatması tek dokunuşla SMS olarak gider.",
    points: ["Kasa ve banka hareketleri", "Tahsilat ve ödeme takibi", "Mali müşavire Excel olarak"],
    url: "app.kobifabrika.com/panel/cari",
    label: "Örnek müşteri hesabı: Hasan Demir, bakiye 1.250 TL, son satış ve tahsilatlar.",
    shot: <LedgerShot />,
  },
  {
    id: "randevu",
    module: "Randevu",
    title: "Boş koltuk kalmasın.",
    text: "Müşteri randevusunu senin sayfandan kendisi alır. Bir gün önce hatırlatma SMS'i gider; gelemeyecekse iptal eder, saat başkasına açılır.",
    points: ["Personel bazında takvim", "Online randevu sayfası", "Otomatik SMS hatırlatma"],
    url: "app.kobifabrika.com/panel/randevular",
    label: "Örnek randevu listesi: Ayşe Yılmaz — Saç kesimi, 14:30; gönderilen hatırlatma SMS'i.",
    shot: <ReminderShot />,
  },
  {
    id: "kampanya",
    module: "Kampanya",
    title: "Gelmeyen müşteriyi geri çağır.",
    text: "Uzun süredir uğramayanları KobiFabrika bulur. Tek mesajla geri çağırırsın; izni olmayana gitmez, her mesajda çıkış yolu vardır.",
    points: ["Boş saate kampanya", "Memnuniyet anketi ve Google yorum", "İYS ve KVKK'ya uygun"],
    url: "app.kobifabrika.com/panel/kampanyalar",
    label: "Örnek geri kazanma mesajı: 45 gündür gelmeyen 38 kişiden SMS izni olan 31 kişiye.",
    shot: <WinBackShot />,
  },
  {
    id: "personel",
    module: "Personel",
    title: "Ay sonu hesabıyla uğraşma.",
    text: "Giriş-çıkış ve izin talebi telefondan. Satıştan prim kendiliğinden hesaplanır; maaş bilgisini sadece işletme sahibi görür.",
    points: ["Mesai ve vardiya", "İzin talebi ve onayı", "Satıştan prim hesabı"],
    url: "app.kobifabrika.com/panel/personel",
    label: "Örnek ay özeti: üç personelin mesai saati, satışı ve primi.",
    shot: <StaffShot />,
  },
];

const otherModules = [
  { title: "Satış ve teklif", text: "Hızlı satış, iskonto, parçalı ödeme. Teklifi PDF gönder, onaylanınca satışa çevir." },
  { title: "Stok", text: "Ürün ve hizmet listesi, giriş-çıkış hareketleri, azalan ürün uyarısı." },
  { title: "Müşteriler", text: "Randevusu, alışverişi ve borcu aynı kartta. Listeni Excel'den aktar." },
  { title: "Raporlar", text: "Satış, alacak, gelir-gider ve personel performansı. Hepsi Excel'e çıkar." },
];

const differentiators = [
  { title: "Tek uygulama, iki iş", text: "Para takibi ve müşteri kazanma aynı yerde; iki program, iki abonelik yok." },
  { title: "TL, sabit fiyat", text: "Fiyat TL; kur farkı yok, satışından komisyon almayız." },
  { title: "Şeffaf abonelik", text: "Yenilemeden 7 gün önce hatırlatma, tek tıkla iptal, sürpriz zam yok." },
  { title: "Veri senin", text: "Abonelik bitse de verini salt okunur görür, Excel'e aktarırsın." },
  { title: "Aracısız destek", text: "Panelden WhatsApp ile doğrudan ekibe yazarsın; bayi ve çağrı merkezi yok." },
  { title: "Kalıcı ücretsiz paket", text: "Çırak paketi süresiz ücretsiz; işin büyüyünce üst pakete geçersin." },
  { title: "10 dakikada kurulum", text: "Telefondan kurulur, telefondan kullanılır; uygulama indirmen gerekmez." },
  { title: "Mali müşavir ücretsiz", text: "Mali müşavirin salt okunur kullanıcı olarak girer, ayrıca ödeme yapmazsın." },
  { title: "İzinsiz mesaj yok", text: "SMS izni olmayan müşteriye pazarlama mesajı gitmez; çıkış her mesajda var." },
];

const sectorList = [
  "Kuaför ve berber",
  "Güzellik ve bakım",
  "Klinik ve sağlık",
  "Spor salonu",
  "Oto servis",
  "Mağaza ve market",
  "Kafe ve restoran",
  "Diğer işletmeler",
];

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

// ── Sayfa ───────────────────────────────────────────────────────────────────

export default function HomePage() {
  return (
    <>
      {/* a) Giriş: başlık, çağrı, ürün ekranı */}
      <section aria-labelledby="baslik" className="border-b border-neutral-200">
        <div className={cn(container, "kf-fade pt-12 pb-16 sm:pt-20 lg:pb-24")}>
          <p className="text-sm font-semibold text-neutral-600">Esnaf ve küçük işletme için</p>
          <h1
            id="baslik"
            className="mt-4 max-w-4xl text-[2.6rem] leading-[1.02] font-bold tracking-[-0.035em] sm:text-6xl lg:text-[5.25rem]"
          >
            Veresiye defterini kapat. Gelmeyen müşteriyi geri çağır.
          </h1>
          <p className="mt-6 max-w-2xl text-lg text-neutral-700 sm:text-xl">
            Kasa, cari, stok ve satış; randevu, SMS ve Google yorum; mesai, izin ve prim. Hepsi telefonunda, tek
            uygulamada.
          </p>
          <div className="mt-8 flex flex-col gap-3 sm:flex-row sm:items-center sm:gap-6">
            <Button asChild size="lg" variant="accent" className="sm:px-6">
              <Link href="/kayit">
                Ücretsiz başla <ArrowRight aria-hidden data-icon="inline-end" />
              </Link>
            </Button>
            <Link
              href="#paketler"
              className="inline-flex min-h-11 items-center text-base font-semibold underline underline-offset-4 hover:no-underline"
            >
              Paketleri gör
            </Link>
          </div>
          <p className="mt-5 text-sm text-neutral-600">
            Çırak paketi süresiz ücretsiz · Kurulum 10 dakika · Tek tıkla iptal
          </p>

          <div className="mt-14 flex items-end gap-6 lg:mt-16">
            <BrowserFrame
              className="hidden min-w-0 flex-1 md:block"
              label="Örnek panel ekranı: Bugün. Günün satışı 8.450 TL, açık veresiye 14.780 TL, randevular ve tahsil edilecek müşteriler."
            >
              <TodayScreen />
            </BrowserFrame>
            <PhoneFrame
              className="lg:mb-0"
              label="Örnek telefon ekranı: günün randevuları. 14:30 Ayşe Yılmaz — Saç kesimi."
              caption="Örnek ekran — telefonda randevu listesi"
            >
              <PhoneAgenda />
            </PhoneFrame>
          </div>
        </div>
      </section>

      {/* b) İki program yerine tek uygulama */}
      <section aria-labelledby="tek-uygulama" className="border-b border-neutral-200">
        <div className={cn(container, "grid gap-12 py-20 sm:py-28 lg:grid-cols-[1fr_1.25fr] lg:gap-16")}>
          <SectionHeading
            id="tek-uygulama"
            label="Neden tek uygulama"
            title="Bugün iki program, iki abonelik ödüyorsun."
            text="Muhasebe programında randevu yok, randevu programında veresiye yok. Müşterin iki ayrı listede, şifren iki ayrı yerde."
          />
          {/* Dar ekranda tablo yatay kayar; klavyeyle de kaydırılabilsin diye odaklanabilir bölge. */}
          <div
            role="region"
            aria-label="Karşılaştırma tablosu"
            tabIndex={0}
            className="-mx-4 overflow-x-auto px-4 focus-visible:outline-2 sm:mx-0 sm:px-0"
          >
            <table className="w-full min-w-[22rem] text-sm sm:text-base">
              <caption className="sr-only">Ön muhasebe programı, randevu programı ve KobiFabrika karşılaştırması</caption>
              <thead>
                <tr className="border-b border-black text-left align-bottom">
                  <th scope="col" className="py-3 pr-3 font-medium text-neutral-600">
                    <span className="sr-only">Özellik</span>
                  </th>
                  <th scope="col" className="w-[17%] px-2 py-3 text-xs font-semibold text-neutral-600 sm:text-sm">
                    Muhasebe programı
                  </th>
                  <th scope="col" className="w-[17%] px-2 py-3 text-xs font-semibold text-neutral-600 sm:text-sm">
                    Randevu programı
                  </th>
                  <th scope="col" className="w-[20%] px-2 py-3 text-xs font-bold sm:text-sm">
                    KobiFabrika
                  </th>
                </tr>
              </thead>
              <tbody>
                {comparison.map(({ row, marks }) => (
                  <tr key={row} className="border-b border-neutral-200">
                    <th scope="row" className="py-3.5 pr-3 text-left font-normal">
                      {row}
                    </th>
                    {marks.map((m, i) => (
                      <td key={i} className={cn("px-2 py-3.5", i === 2 && "bg-neutral-100")}>
                        <MarkCell mark={m} />
                      </td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      </section>

      {/* c) Modüller: ürün parçalarıyla */}
      <section aria-labelledby="moduller">
        <div className={cn(container, "pt-20 sm:pt-28")}>
          <SectionHeading
            id="moduller"
            label="Modüller"
            title={
              <>
                Defter, ajanda ve hesap makinesi <Underline>tek yerde</Underline>.
              </>
            }
            text="Sektörünü seçince menü sana göre ayarlanır; kullanmadığın modülü kapatırsın."
          />
        </div>
        <ol className={cn(container, "mt-14 flex flex-col")}>
          {features.map((f, i) => (
            <li
              key={f.id}
              className="grid gap-8 border-t border-black py-12 sm:py-16 lg:grid-cols-[1fr_1.3fr] lg:gap-16"
            >
              <div>
                <p className="text-sm font-semibold text-neutral-600 tabular-nums">
                  {String(i + 1).padStart(2, "0")} — {f.module}
                </p>
                <h3 className="mt-3 text-3xl leading-tight font-bold tracking-tight sm:text-4xl">{f.title}</h3>
                <p className="mt-4 max-w-md text-base leading-7 text-neutral-700">{f.text}</p>
                <ul className="mt-6 flex flex-col gap-2.5">
                  {f.points.map((p) => (
                    <li key={p} className="flex items-center gap-2.5 text-base">
                      <Check aria-hidden className="size-4 shrink-0" strokeWidth={2.5} /> {p}
                    </li>
                  ))}
                </ul>
              </div>
              <BrowserFrame label={f.label} url={f.url} className="w-full min-w-0 lg:max-w-xl lg:justify-self-end">
                {f.shot}
              </BrowserFrame>
            </li>
          ))}
        </ol>
        <div className={cn(container, "pb-20 sm:pb-28")}>
          <div className="border-t border-black pt-10">
            <h3 className="text-xl font-bold">Ayrıca</h3>
            <dl className="mt-6 grid gap-x-10 gap-y-8 sm:grid-cols-2 lg:grid-cols-4">
              {otherModules.map((m) => (
                <div key={m.title} className="border-t border-neutral-200 pt-4">
                  <dt className="font-semibold">{m.title}</dt>
                  <dd className="mt-1 text-sm leading-6 text-neutral-700">{m.text}</dd>
                </div>
              ))}
            </dl>
          </div>
        </div>
      </section>

      {/* d) Farkımız — siyah bölüm (1/2) */}
      <section aria-labelledby="farkimiz" className="dark bg-black text-white">
        <div className={cn(container, "py-20 sm:py-28")}>
          <SectionHeading
            id="farkimiz"
            label="Farkımız"
            onDark
            title={
              <>
                Esnafın derdini <Underline>dinleyerek</Underline> yaptık.
              </>
            }
          />
          <ol className="mt-14 grid gap-x-10 sm:grid-cols-2 lg:grid-cols-3">
            {differentiators.map((d, i) => (
              <li key={d.title} className="border-t border-neutral-800 py-6">
                <p className="text-sm text-neutral-400 tabular-nums">{String(i + 1).padStart(2, "0")}</p>
                <h3 className="mt-2 text-lg font-semibold">{d.title}</h3>
                <p className="mt-1 text-sm leading-6 text-neutral-300">{d.text}</p>
              </li>
            ))}
          </ol>
        </div>
      </section>

      {/* e) Sektörler */}
      <section aria-labelledby="sektorler" className="border-b border-neutral-200">
        <div className={cn(container, "grid gap-10 py-20 sm:py-24 lg:grid-cols-[1fr_1.4fr]")}>
          <SectionHeading
            id="sektorler"
            label="Sektörler"
            title="Hangi işi yapıyorsan ona göre açılır."
            text="Kurulumda sektörünü seçersin; menü, hizmet listesi ve ekranlar ona göre gelir."
          />
          <ul className="grid grid-cols-2 gap-x-8 self-end">
            {sectorList.map((s) => (
              <li key={s} className="border-b border-neutral-200 py-3.5 text-base font-medium sm:text-lg">
                {s}
              </li>
            ))}
          </ul>
        </div>
      </section>

      {/* f) Paketler */}
      <section aria-labelledby="paketler">
        <div className={cn(container, "py-20 sm:py-28")}>
          <SectionHeading
            id="paketler"
            label="Paketler"
            title="Ücretsiz başla, işin büyüyünce geç."
            text="TL, sabit ve komisyonsuz fiyat. Lansman fiyatlarını çok yakında açıklıyoruz."
          />
          <ul className="mt-14 grid border-t border-black lg:grid-cols-3 lg:divide-x lg:divide-neutral-200">
            {plans.map((plan) => (
              <li
                key={plan.id}
                className={cn(
                  "flex flex-col border-b border-neutral-200 py-8 last:border-b-0 lg:border-b-0 lg:px-8 lg:first:pl-0 lg:last:pr-0",
                  plan.highlighted && "relative",
                )}
              >
                {plan.highlighted && (
                  <span aria-hidden className="absolute -top-px right-0 left-0 h-1 bg-brand lg:right-8 lg:left-8" />
                )}
                <div className="flex items-baseline justify-between gap-3">
                  <h3 className="text-2xl font-bold">{plan.name}</h3>
                  {plan.highlighted && (
                    <span className="rounded-sm border border-black px-2 py-0.5 text-xs font-semibold">Önerilen</span>
                  )}
                </div>
                <p className="mt-1 text-sm text-neutral-600">{plan.tagline}</p>
                <p
                  className={cn(
                    "mt-6 font-heading font-bold tracking-tight",
                    plan.monthlyPrice === null ? "text-2xl" : "text-4xl",
                  )}
                >
                  {formatPlanPrice(plan)}
                  {!!plan.monthlyPrice && <span className="text-base font-medium text-neutral-600"> / ay</span>}
                </p>
                <p className="mt-1 text-sm text-neutral-600">{plan.priceNote}</p>
                <ul className="mt-6 flex flex-1 flex-col gap-3">
                  {plan.features.map((f) => (
                    <li key={f} className="flex items-start gap-2.5 text-sm">
                      <Check aria-hidden className="mt-0.5 size-4 shrink-0" strokeWidth={2.5} /> {f}
                    </li>
                  ))}
                </ul>
                <Button asChild size="lg" variant={plan.highlighted ? "default" : "outline"} className="mt-8 w-full">
                  <Link href="/kayit">{plan.cta}</Link>
                </Button>
              </li>
            ))}
          </ul>
          <div className="mt-10 grid gap-6 border-t border-neutral-200 pt-8 lg:grid-cols-[1fr_2fr]">
            <p className="font-semibold">Bütün paketlerde</p>
            <div>
              <ul className="grid gap-2 sm:grid-cols-2">
                {allPlansInclude.map((t) => (
                  <li key={t} className="flex items-start gap-2.5 text-sm">
                    <Check aria-hidden className="mt-0.5 size-4 shrink-0" strokeWidth={2.5} /> {t}
                  </li>
                ))}
              </ul>
              <p className="mt-4 text-xs text-neutral-600">{planFootnote}</p>
            </div>
          </div>
        </div>
      </section>

      {/* g) SSS */}
      <section aria-labelledby="sss" className="border-t border-neutral-200">
        <div className={cn(container, "grid gap-10 py-20 sm:py-28 lg:grid-cols-[1fr_1.6fr]")}>
          <SectionHeading id="sss" label="SSS" title="Sık sorulan sorular" text="Aklına takılan başka bir şey varsa bize yaz." />
          <div className="border-b border-neutral-200">
            {faqs.map(({ q, a }) => (
              <details key={q} className="group border-t border-neutral-200">
                <summary className="flex min-h-16 cursor-pointer list-none items-center justify-between gap-4 py-4 text-base font-semibold sm:text-lg [&::-webkit-details-marker]:hidden">
                  {q}
                  <Plus
                    aria-hidden
                    className="size-5 shrink-0 transition-transform group-open:rotate-45 motion-reduce:transition-none"
                  />
                </summary>
                <p className="max-w-2xl pb-6 text-base leading-7 text-neutral-700">{a}</p>
              </details>
            ))}
          </div>
        </div>
      </section>

      {/* h) Son çağrı — siyah bölüm (2/2) */}
      <section aria-labelledby="son-cagri" className="dark bg-black text-white">
        <div className={cn(container, "py-20 sm:py-28")}>
          <h2 id="son-cagri" className="max-w-3xl text-4xl leading-[1.05] font-bold tracking-[-0.035em] sm:text-6xl">
            Defteri bugün kapat.
          </h2>
          <p className="mt-5 max-w-xl text-lg text-neutral-300">
            10 dakikada kur, Çırak paketiyle ücretsiz kullan. İstediğin zaman tek tıkla iptal.
          </p>
          <div className="mt-8 flex flex-col gap-3 sm:flex-row sm:items-center sm:gap-6">
            <Button asChild size="lg" variant="accent" className="sm:px-6">
              <Link href="/kayit">
                Ücretsiz başla <ArrowRight aria-hidden data-icon="inline-end" />
              </Link>
            </Button>
            <Link
              href="/giris"
              className="inline-flex min-h-11 items-center text-base font-semibold underline underline-offset-4 hover:no-underline"
            >
              Giriş yap
            </Link>
          </div>
        </div>
      </section>
    </>
  );
}

function MarkCell({ mark }: { mark: Mark }) {
  if (mark === "yes")
    return (
      <>
        <Check aria-hidden className="size-5" strokeWidth={2.5} />
        <span className="sr-only">Var</span>
      </>
    );
  if (mark === "some") return <span className="text-xs text-neutral-600 sm:text-sm">Bazısında</span>;
  return (
    <>
      <Minus aria-hidden className="size-5 text-neutral-400" />
      <span className="sr-only">Yok</span>
    </>
  );
}
