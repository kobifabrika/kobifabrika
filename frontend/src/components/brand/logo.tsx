import { cn } from "cn";
import Image from "next/image";

/** Logo SVG'lerinin görünüm kutusu (`npm run marka` yazdırır); genişlik yüksekliğe göre bu oranla. */
export const logoSize = { width: 586, height: 81 };

/**
 * KobiFabrika'nın tek logosu (2026-10-21): "Kobi" + mavi "Fabrika", Schibsted Grotesk ExtraBold, alt çizgi yok. Harfler vektör
 * yol (`npm run marka`, public/marka/logo-*.svg); font yüklenmeden her yerde aynı görünür. `surface`: logonun durduğu zemin.
 * Açık zeminde "Kobi" #000a1d, koyu zeminde beyaz; "Fabrika" ikisinde de #00b0ff (logo kontrast kuralından muaf, docs/04).
 * Yükseklik className ile (ör. h-5), genişlik orana göre.
 */
export function Logo({
  surface,
  className,
  priority = false,
}: {
  surface: "light" | "dark";
  className?: string;
  priority?: boolean;
}) {
  return (
    <Image
      src={surface === "dark" ? "/marka/logo-koyu.svg" : "/marka/logo-acik.svg"}
      width={logoSize.width}
      height={logoSize.height}
      alt="KobiFabrika"
      priority={priority}
      unoptimized
      className={cn("h-5 w-auto", className)}
    />
  );
}
