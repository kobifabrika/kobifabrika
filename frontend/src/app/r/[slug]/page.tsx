import type { Metadata } from "next";
import { Suspense } from "react";

import { Wordmark } from "@/components/brand/wordmark";

export const metadata: Metadata = { title: "Online randevu" };

/** İşletmenin herkese açık online randevu sayfası (kobifabrika.com/r/{slug}). Sprint 5'te yapılacak. */
export default function BookingPage({ params }: PageProps<"/r/[slug]">) {
  return (
    <main className="flex min-h-screen flex-col items-center justify-center gap-4 bg-background px-4 text-center">
      <Suspense fallback={<p className="text-sm text-neutral-600">Yükleniyor…</p>}>
        <BookingPlaceholder params={params} />
      </Suspense>
      <p className="text-xs text-neutral-600">
        <Wordmark className="text-xs" /> ile randevu
      </p>
    </main>
  );
}

async function BookingPlaceholder({ params }: { params: PageProps<"/r/[slug]">["params"] }) {
  const { slug } = await params;
  return (
    <div>
      <h1 className="text-2xl font-bold text-black">Online randevu</h1>
      <p className="mt-2 text-sm text-neutral-600">
        <span className="font-semibold text-black">{slug}</span> için online randevu sayfası yakında burada.
      </p>
    </div>
  );
}
