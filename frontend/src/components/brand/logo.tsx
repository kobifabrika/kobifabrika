import { cn } from "cn";
import Image from "next/image";

/**
 * KobiFabrika logosu: beyaz "KOBI" + mavi "FABRIKA", vektör (public/marka/logo-yazi.svg; logo.png'den bir kez izlendi,
 * 2026-10-21). Sadece koyu zeminde (#000a1d / #0a1f3c / siyah) kullanılır; beyaz zeminde `Wordmark`. Yükseklik className ile
 * (ör. h-6), genişlik orana göre (10,34:1). SVG her yoğunlukta keskin; Next SVG'yi optimize etmeden olduğu gibi sunar.
 */
export function Logo({ className, priority = false }: { className?: string; priority?: boolean }) {
  return (
    <Image
      src="/marka/logo-yazi.svg"
      width={5792}
      height={560}
      alt="KobiFabrika"
      priority={priority}
      unoptimized
      className={cn("h-6 w-auto", className)}
    />
  );
}
