import type { Metadata } from "next";
import { cacheLife } from "next/cache";
import { connection } from "next/server";
import { Suspense } from "react";

import { BookingRoute } from "@/components/booking/booking-route";
import { API_URL } from "@/lib/api/client";

/** Başlık için işletme adı (sunucuda, kısa önbellekli). Sayfa kapalı/bulunamadıysa null. */
async function businessName(slug: string): Promise<string | null> {
  "use cache";
  cacheLife("minutes");
  try {
    const res = await fetch(`${API_URL}/api/public/booking/${encodeURIComponent(slug)}`, { signal: AbortSignal.timeout(3000) });
    if (!res.ok) return null;
    return ((await res.json()) as { name?: string }).name ?? null;
  } catch {
    return null;
  }
}

export async function generateMetadata({ params }: PageProps<"/r/[slug]">): Promise<Metadata> {
  const { slug } = await params;
  const name = await businessName(slug);
  return {
    title: { absolute: name ? `${name} · Online randevu` : "Online randevu" },
    robots: { index: false, follow: false },
    referrer: "no-referrer",
  };
}

/**
 * Başlık adresteki işletmeye göre (generateMetadata params okur); sayfa bu yüzden istek anında render edilir. İşaretçi olmadan
 * Next geliştirmede "URL data in generateMetadata" hatası verir.
 */
async function RequestTime() {
  await connection();
  return null;
}

/** İşletmenin herkese açık online randevu sayfası (kobifabrika.com/r/{slug}). Oturum gerekmez. */
export default function BookingPage() {
  return (
    <>
      <Suspense>
        <RequestTime />
      </Suspense>
      <Suspense>
        <BookingRoute />
      </Suspense>
    </>
  );
}
