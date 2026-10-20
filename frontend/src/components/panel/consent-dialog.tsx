"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";

import { TermsConsent } from "@/components/forms/terms-consent";
import { FormMessage } from "@/components/forms/field";
import { meQueryKey, useSession } from "@/components/providers/session-provider";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { api, errorMessage, unwrap } from "@/lib/api/client";

/**
 * Kullanım koşulları veya Veri işleme sözleşmesi güncellendiyse Sahip'ten bir kez onay (karar 2026-10-17, soru 14). Pencere
 * kapatılamaz; onaylanınca oturum yenilenir. Diğer roller bilgilendirme dışında onay vermez (API consentRequired yalnızca Sahip'te).
 */
export function ConsentDialog() {
  const { me } = useSession();
  const queryClient = useQueryClient();
  const [checked, setChecked] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const accept = useMutation({
    mutationFn: () => unwrap(api.POST("/api/auth/consents", { body: { termsAccepted: true } })),
    onSuccess: (next) => queryClient.setQueryData(meQueryKey, next),
    onError: (e) => setError(errorMessage(e)),
  });
  if (!me.user.consentRequired) return null;

  return (
    <Dialog open>
      <DialogContent showCloseButton={false} onEscapeKeyDown={(e) => e.preventDefault()} onInteractOutside={(e) => e.preventDefault()}>
        <DialogHeader>
          <DialogTitle>Sözleşmelerimiz güncellendi</DialogTitle>
          <DialogDescription>
            Pilot dönemi için Kullanım koşullarını ve işletmenizin müşteri verilerini nasıl işlediğimizi anlatan Veri işleme
            sözleşmesini yayımladık. Devam etmek için okuyup onaylayın.
          </DialogDescription>
        </DialogHeader>
        <form
          noValidate
          className="flex flex-col gap-4"
          onSubmit={(e) => {
            e.preventDefault();
            if (!checked) setError("Devam etmek için Kullanım koşullarını ve Veri işleme sözleşmesini kabul edin.");
            else accept.mutate();
          }}
        >
          <TermsConsent checked={checked} onChange={(e) => setChecked(e.target.checked)} />
          {error && <FormMessage>{error}</FormMessage>}
          <Button type="submit" disabled={accept.isPending}>
            Onayla ve devam et
          </Button>
        </form>
      </DialogContent>
    </Dialog>
  );
}
