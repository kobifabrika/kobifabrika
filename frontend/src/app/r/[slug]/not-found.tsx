import { StatusPage } from "@/components/feedback/status-page";

/** Online randevu altında olmayan adres (müşteri görür). */
export default function BookingNotFound() {
  return (
    <StatusPage code="404" title="Randevu sayfası bulunamadı">
      Bağlantı eksik ya da eski olabilir. Randevu için işletmeden güncel linki isteyin veya işletmeyi arayın.
    </StatusPage>
  );
}
