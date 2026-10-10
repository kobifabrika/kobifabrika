import { CalendarCheck, Smartphone, Wallet } from "lucide-react";
import Link from "next/link";

import { Backdrop } from "@/components/brand/backdrop";
import { Wordmark } from "@/components/brand/wordmark";

const benefits = [
  { icon: Wallet, text: "Cari, kasa, stok ve satış tek yerde" },
  { icon: CalendarCheck, text: "Randevu ve kampanyayla müşteri geri gelir" },
  { icon: Smartphone, text: "10 dakikada kurulum, telefondan kullanım" },
];

/**
 * Giriş/kayıt/şifre/davet düzeni — koyu tema (docs/04-tasarim.md). Masaüstünde solda marka alanı, sağda form kartı.
 * 768px altında marka alanı üstte ince bant olur, form tam genişlik; sayfa kaydırılabilir.
 */
export default function AuthLayout({ children }: LayoutProps<"/">) {
  return (
    <div className="dark flex min-h-screen flex-col bg-background text-foreground md:flex-row">
      <section className="relative flex shrink-0 items-center justify-between overflow-hidden border-b border-border px-4 py-3 md:w-[45%] md:flex-col md:items-start md:justify-center md:border-r md:border-b-0 md:px-12 md:py-12 lg:w-1/2 lg:px-16">
        <Backdrop className="hidden md:block" />
        <Link href="/" className="relative rounded-md" aria-label="KobiFabrika ana sayfa">
          <Wordmark onDark className="text-2xl md:text-5xl" />
        </Link>
        <div className="relative hidden md:mt-6 md:block">
          <p className="max-w-md text-2xl leading-snug font-semibold text-white">
            Paranı takip et, <span className="text-brand-100">müşterini geri getir.</span>
          </p>
          <ul className="mt-8 flex flex-col gap-4">
            {benefits.map(({ icon: Icon, text }) => (
              <li key={text} className="flex items-center gap-3 text-base text-neutral-300">
                <span className="flex size-10 shrink-0 items-center justify-center rounded-lg border border-border bg-card">
                  <Icon aria-hidden className="size-5 text-brand-300" />
                </span>
                {text}
              </li>
            ))}
          </ul>
        </div>
        <p className="relative hidden text-xs font-semibold tracking-wider text-neutral-400 uppercase md:absolute md:bottom-8 md:left-12 md:block lg:left-16">
          KobiFabrika Yazılım A.Ş.
        </p>
      </section>

      <main className="flex flex-1 items-start justify-center px-4 py-8 md:items-center md:px-8 md:py-12">
        <div className="w-full max-w-md rounded-2xl border border-border bg-card p-5 shadow-2xl shadow-black/40 sm:p-8">
          {children}
        </div>
      </main>
    </div>
  );
}
