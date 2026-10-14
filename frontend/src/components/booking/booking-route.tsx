"use client";

import { useParams } from "next/navigation";

import { BookingFlow } from "./booking-flow";
import { ManageAppointment } from "./manage-appointment";

/** /r/[slug]: adres parametresinden online randevu akışı. */
export function BookingRoute() {
  const { slug } = useParams<{ slug: string }>();
  return <BookingFlow slug={slug} />;
}

/** /r/[slug]/randevu/[token]: müşterinin randevu sayfası. */
export function ManageRoute() {
  const { token } = useParams<{ token: string }>();
  return <ManageAppointment token={token} />;
}
