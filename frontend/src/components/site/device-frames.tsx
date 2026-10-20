import { cn } from "cn";
import Image from "next/image";

/**
 * Gerçek ekran görüntüsü için kendi çerçevelerimiz (stok görsel yok; karar 2026-10-17). Görüntüler demo işletmenin ("Işıltı
 * Kuaför") ekranları: `npm run demo:shots`. next/image WebP/AVIF ve ekran genişliğine uygun boyut sunar; alt metin ekranda ne
 * olduğunu anlatır.
 */
export function LaptopFrame({ src, alt, className, priority = false }: { src: string; alt: string; className?: string; priority?: boolean }) {
  return (
    <figure className={cn("min-w-0", className)}>
      <div className="rounded-t-xl border-[10px] border-b-0 border-navy-950 bg-navy-950">
        <Image
          src={src}
          alt={alt}
          width={1280}
          height={800}
          priority={priority}
          sizes="(min-width: 1024px) 720px, (min-width: 768px) 90vw, 100vw"
          className="block h-auto w-full rounded-t-sm bg-white"
        />
      </div>
      <div aria-hidden className="mx-[-4%] h-3 rounded-b-xl bg-navy-800" />
      <figcaption className="mt-3 text-xs text-neutral-600">Örnek ekran: demo işletme</figcaption>
    </figure>
  );
}

export function PhoneFrame({
  src,
  alt,
  className,
  priority = false,
  caption = true,
  dark = false,
}: {
  src: string;
  alt: string;
  className?: string;
  priority?: boolean;
  caption?: boolean;
  dark?: boolean;
}) {
  return (
    <figure className={cn("w-[200px] shrink-0 sm:w-[240px]", className)}>
      <div className={cn("overflow-hidden rounded-[2rem] border-[8px] bg-white", dark ? "border-navy-800" : "border-navy-950")}>
        <Image
          src={src}
          alt={alt}
          width={750}
          height={1624}
          priority={priority}
          sizes="240px"
          className="block h-auto w-full"
        />
      </div>
      {caption && (
        <figcaption className={cn("mt-3 text-xs", dark ? "text-neutral-300" : "text-neutral-600")}>Örnek ekran: telefonda</figcaption>
      )}
    </figure>
  );
}
