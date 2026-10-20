import { cn } from "cn";
import Image from "next/image";

/**
 * KobiFabrika logosu (public/marka/logo.png'den `npm run marka` ile kırpılmış, saydam). Beyaz "KOBI" + mavi "FABRIKA": sadece
 * koyu zeminde (#000a1d / siyah) kullanılır; beyaz zeminde `Wordmark`. Yükseklik className ile (ör. h-6), genişlik orana göre.
 * next/image tarayıcıya WebP/AVIF ve ekran yoğunluğuna uygun boyut sunar. Boyut 640×62 (`npm run marka` çıktısı).
 */
export function Logo({ className, priority = false }: { className?: string; priority?: boolean }) {
  return (
    <Image
      src="/marka/logo-yazi.png"
      width={640}
      height={62}
      alt="KobiFabrika"
      priority={priority}
      sizes="(min-width: 768px) 400px, 260px"
      className={cn("h-6 w-auto", className)}
    />
  );
}
