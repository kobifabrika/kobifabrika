"use client";

import { useQuery } from "@tanstack/react-query";
import { ChevronLeft } from "lucide-react";
import Link from "next/link";
import { useParams } from "next/navigation";

import { PublicMessage } from "@/components/public/public-shell";
import { LegalSections } from "@/components/site/legal-page";
import { customerNoticeSections, legalDocs } from "@/content/legal";
import { api, unwrap } from "@/lib/api/client";

import { BookingShell } from "./booking-shell";
import { bookingKey } from "./booking-flow";

/** /r/[slug]/aydinlatma: Müşteri aydınlatma şablonu, işletmenin adı, adresi ve telefonuyla doldurulmuş. */
export function CustomerNotice() {
  const { slug } = useParams<{ slug: string }>();
  const page = useQuery({
    queryKey: bookingKey(slug),
    queryFn: () => unwrap(api.GET("/api/public/booking/{slug}", { params: { path: { slug } } })),
    retry: false,
  });
  if (page.isPending)
    return (
      <BookingShell>
        <p role="status" className="py-10 text-sm text-neutral-600">
          Yükleniyor…
        </p>
      </BookingShell>
    );
  if (page.isError)
    return (
      <BookingShell>
        <PublicMessage title="Sayfa bulunamadı">Bu işletmenin online randevu sayfası kapalı olabilir.</PublicMessage>
      </BookingShell>
    );
  const b = page.data;
  const address = [b.address, b.district, b.city].filter(Boolean).join(", ") || null;
  return (
    <BookingShell name={b.name} logoUrl={b.logoUrl} address={[b.address, b.district, b.city]} phone={b.phone} phoneE164={b.phoneE164}>
      <Link href={`/r/${slug}`} className="flex min-h-11 w-fit items-center gap-1 text-sm font-semibold text-neutral-700 hover:text-black">
        <ChevronLeft aria-hidden className="size-4" />
        Online randevuya dön
      </Link>
      <h1 className="text-xl font-bold text-black">Kişisel verilerin işlenmesine ilişkin aydınlatma</h1>
      <p className="text-sm text-neutral-600">Sürüm {legalDocs.sablon.version}</p>
      <LegalSections sections={customerNoticeSections({ name: b.name, address, contact: b.phone })} />
    </BookingShell>
  );
}
