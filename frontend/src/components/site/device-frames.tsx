import { cn } from "cn";
import Image from "next/image";

/**
 * Gerçek ekran görüntüsü için kendi çerçevelerimiz (2026-10-21, Stripe tarzı): ince kenarlıklı tarayıcı penceresi ve telefon,
 * çok hafif yumuşak gölge. Görüntüler demo işletmenin ("Işıltı Kuaför") ekranları, 2x yoğunlukta: `npm run demo:shots`. next/image
 * ekran genişliğine ve yoğunluğuna uygun boyut sunar (kalite 85: ekrandaki yazı net kalsın); alt metin ekranda ne olduğunu anlatır.
 */
export const softShadow = "shadow-[0_1px_2px_color-mix(in_srgb,var(--color-navy-950)_6%,transparent),0_18px_40px_-18px_color-mix(in_srgb,var(--color-navy-950)_28%,transparent)]";

export function BrowserFrame({
  src,
  alt,
  className,
  sizes = "(min-width: 1024px) 620px, calc(100vw - 56px)",
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
      <div className={cn("overflow-hidden rounded-xl border border-neutral-200 bg-white", softShadow)}>
        <div aria-hidden className="flex h-7 items-center gap-1.5 border-b border-neutral-200 bg-neutral-100 px-3">
          <span className="size-2.5 rounded-full bg-neutral-200" />
          <span className="size-2.5 rounded-full bg-neutral-200" />
          <span className="size-2.5 rounded-full bg-neutral-200" />
        </div>
        <Image
          src={src}
          alt={alt}
          width={2560}
          height={1600}
          quality={85}
          sizes={sizes}
          className="block h-auto w-full"
        />
      </div>
      {caption && <figcaption className="mt-3 text-xs text-neutral-600">Örnek ekran: demo işletme</figcaption>}
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
      <div className={cn("overflow-hidden rounded-[2rem] border-[7px] border-navy-950 bg-white", softShadow)}>
        <Image
          src={src}
          alt={alt}
          width={750}
          height={1624}
          quality={85}
          priority={priority}
          sizes="240px"
          className="block h-auto w-full"
        />
      </div>
      {caption && <figcaption className="mt-3 text-xs text-neutral-600">Örnek ekran: telefonda</figcaption>}
    </figure>
  );
}
