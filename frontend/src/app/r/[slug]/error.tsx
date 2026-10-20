"use client";

import { useParams } from "next/navigation";

import { ErrorView } from "@/components/feedback/error-view";

/** Online randevu sayfasında beklenmeyen hata (müşteri görür): tekrar dene veya sayfanın başına dön. */
export default function BookingError({ error, reset }: { error: Error & { digest?: string }; reset: () => void }) {
  const { slug } = useParams<{ slug: string }>();
  return <ErrorView error={error} reset={reset} home={{ href: `/r/${slug}`, label: "Randevu sayfasının başına dön" }} />;
}
