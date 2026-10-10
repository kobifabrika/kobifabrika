import Link from "next/link";

import { Wordmark } from "@/components/brand/wordmark";

export default function NotFound() {
  return (
    <main className="flex min-h-screen flex-col items-center justify-center gap-4 bg-background px-4 text-center">
      <Wordmark className="text-2xl" />
      <h1 className="text-xl font-bold text-brand-900">Sayfa bulunamadı</h1>
      <p className="text-sm text-neutral-600">Aradığınız sayfa taşınmış veya hiç var olmamış olabilir.</p>
      <Link href="/" className="inline-flex min-h-11 items-center font-semibold text-brand-500 hover:underline">
        Ana sayfaya dön
      </Link>
    </main>
  );
}
