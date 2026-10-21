"use client";

import { RotateCw } from "lucide-react";
import Link from "next/link";
import { useEffect } from "react";

import { StatusPage, type StatusLink } from "@/components/feedback/status-page";
import { Button } from "@/components/ui/button";

/**
 * Beklenmeyen hata ekranı (error.tsx'ler): "Tekrar dene" (reset), dönüş bağlantısı ve hata kodu (destek için). Hata ayrıntısı
 * kullanıcıya gösterilmez.
 */
export function ErrorView({
  error,
  reset,
  home,
  links,
  bare = false,
}: {
  error: Error & { digest?: string };
  reset: () => void;
  home: StatusLink;
  links?: StatusLink[];
  bare?: boolean;
}) {
  useEffect(() => {
    console.error(error);
  }, [error]);
  return (
    <StatusPage
      code="Hata"
      title="Bir şeyler ters gitti"
      bare={bare}
      links={links}
      actions={
        <>
          <Button size="lg" onClick={reset}>
            <RotateCw aria-hidden /> Tekrar dene
          </Button>
          <Button asChild size="lg" variant="outline">
            <Link href={home.href}>{home.label}</Link>
          </Button>
        </>
      }
    >
      <p>Sayfa açılırken beklenmeyen bir hata oluştu. Tekrar deneyin; sorun sürerse bize yazın, birlikte bakalım.</p>
      {error.digest && <p className="mt-2 text-sm text-neutral-600">Hata kodu: {error.digest}</p>}
    </StatusPage>
  );
}
