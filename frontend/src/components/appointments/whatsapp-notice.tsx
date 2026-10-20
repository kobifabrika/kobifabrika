"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import { MessageCircle } from "lucide-react";

import { useSession } from "@/components/providers/session-provider";
import { Button } from "@/components/ui/button";
import { API_URL, api, errorMessage, unwrap } from "@/lib/api/client";
import type { WhatsAppNoticeKind } from "@/lib/api/types";

import { appointmentsKey } from "./appointment-bits";

export type WhatsAppKind = WhatsAppNoticeKind;

/** SMS sağlayıcısı açık mı (pilotta kapalı: bildirimler WhatsApp ve e-postayla). */
export function useSmsEnabled(): boolean {
  return useSession().me.platform.smsEnabled;
}

const labels: Record<WhatsAppKind, string> = {
  Confirmed: "Onayı WhatsApp'tan bildir",
  Reminder: "WhatsApp'tan hatırlat",
  Moved: "Yeni saati WhatsApp'tan bildir",
  Cancelled: "İptali WhatsApp'tan bildir",
  Rejected: "WhatsApp'tan bildir",
  Survey: "Anket linkini WhatsApp'tan gönder",
};

/** Dokunma kaydı: sayfa WhatsApp'a geçse de istek tamamlanır (keepalive). "Hatırlatıldı" ve zaman çizelgesi için. */
export function recordWhatsApp(appointmentId: string, kind: WhatsAppKind, onDone?: () => void) {
  void fetch(`${API_URL}/api/appointments/${appointmentId}/whatsapp-notices`, {
    method: "POST",
    credentials: "include",
    keepalive: true,
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ kind }),
  })
    .then(() => onDone?.())
    .catch(() => undefined);
}

/**
 * Müşteriye WhatsApp'tan bildirim düğmesi. Mesaj görünür olur olmaz POST ile hazırlanır (iptal linki token'ı üretilebilir);
 * düğme gerçek bir link olur (iPhone Safari, await sonrası açılan pencereyi engeller). Anket türünde link survey-link'ten gelir.
 */
export function WhatsAppNoticeButton({
  appointmentId,
  kind,
  label,
  variant = "outline",
}: {
  appointmentId: string;
  kind: WhatsAppKind;
  label?: string;
  variant?: "default" | "outline";
}) {
  const queryClient = useQueryClient();
  const message = useQuery({
    queryKey: [...appointmentsKey, appointmentId, "whatsapp", kind],
    queryFn: () =>
      kind === "Survey"
        ? unwrap(api.POST("/api/appointments/{id}/survey-link", { params: { path: { id: appointmentId } } }))
        : unwrap(
            api.POST("/api/appointments/{id}/whatsapp-message", {
              params: { path: { id: appointmentId } },
              body: { kind },
            }),
          ),
    staleTime: 0,
    gcTime: 0,
    retry: false,
  });
  const text = label ?? labels[kind];

  if (message.isError) return <p className="text-sm text-neutral-600">{errorMessage(message.error)}</p>;
  if (!message.data)
    return (
      <Button variant={variant} disabled>
        <MessageCircle aria-hidden />
        {text}
      </Button>
    );
  return (
    <Button asChild variant={variant}>
      <a
        href={message.data.url}
        target="_blank"
        rel="noopener noreferrer"
        onClick={() =>
          recordWhatsApp(appointmentId, kind, () => {
            void queryClient.invalidateQueries({ queryKey: [...appointmentsKey, "tomorrow"] });
            void queryClient.invalidateQueries({ queryKey: ["timeline"] });
          })
        }
      >
        <MessageCircle aria-hidden />
        {text}
      </a>
    </Button>
  );
}
