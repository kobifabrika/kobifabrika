import { cn } from "cn";
import Image from "next/image";

/**
 * Gerçek ekran görüntüsü için kendi çerçevelerimiz (2026-10-21, Stripe tarzı): ince kenarlıklı tarayıcı penceresi ve telefon,
 * çok hafif yumuşak gölge. Görüntüler demo işletmenin ("Işıltı Kuaför") ekranları: `npm run demo:shots`. next/image WebP/AVIF
 * ve ekran genişliğine uygun boyut sunar; alt metin ekranda ne olduğunu anlatır.
 */
export const softShadow = "shadow-[0_1px_2px_rgb(10_31_60/0.06),0_18px_40px_-18px_rgb(10_31_60/0.28)]";

export function BrowserFrame({
  src,
  alt,
  className,
  sizes = "(min-width: 1024px) 640px, 100vw",
  caption = true,
}: {
  src: string;
  alt: string;
  className?: string;
  sizes?: string;
  caption?: boolean;
}) {
  return (
    <figure className={cn("min-w-0", className)}>
      <div className={cn("overflow-hidden rounded-xl border border-slate-200 bg-white", softShadow)}>
        <div aria-hidden className="flex h-7 items-center gap-1.5 border-b border-slate-200 bg-mist px-3">
          <span className="size-2.5 rounded-full bg-slate-200" />
          <span className="size-2.5 rounded-full bg-slate-200" />
          <span className="size-2.5 rounded-full bg-slate-200" />
        </div>
        <Image src={src} alt={alt} width={1280} height={800} sizes={sizes} className="block h-auto w-full" />
      </div>
      {caption && <figcaption className="mt-3 text-xs text-slate">Örnek ekran: demo işletme</figcaption>}
    </figure>
  );
}

export function PhoneFrame({
  src,
  alt,
  className,
  priority = false,
  caption = true,
}: {
  src: string;
  alt: string;
  className?: string;
  priority?: boolean;
  caption?: boolean;
}) {
  return (
    <figure className={cn("w-[200px] shrink-0 sm:w-[240px]", className)}>
      <div className={cn("overflow-hidden rounded-[2rem] border-[7px] border-ink bg-white", softShadow)}>
        <Image src={src} alt={alt} width={750} height={1624} priority={priority} sizes="240px" className="block h-auto w-full" />
      </div>
      {caption && <figcaption className="mt-3 text-xs text-slate">Örnek ekran: telefonda</figcaption>}
    </figure>
  );
}
