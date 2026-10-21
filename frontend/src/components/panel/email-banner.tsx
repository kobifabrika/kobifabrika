"use client";

import { MailWarning } from "lucide-react";
import { useEffect, useState } from "react";
import { toast } from "sonner";

import { useSession } from "@/components/providers/session-provider";
import { Button } from "@/components/ui/button";
import { api, errorMessage, unwrap } from "@/lib/api/client";

const COOLDOWN_SECONDS = 60;

/** E-posta doğrulanmadıysa panelin üstünde uyarı + "Tekrar gönder" (60 sn'de bir; API de sınırlar). */
export function EmailBanner() {
  const { me } = useSession();
  const [cooldown, setCooldown] = useState(0);
  const [sending, setSending] = useState(false);

  useEffect(() => {
    if (cooldown <= 0) return;
    const timer = setTimeout(() => setCooldown((s) => s - 1), 1000);
    return () => clearTimeout(timer);
  }, [cooldown]);

  if (me.user.emailConfirmed) return null;

  const resend = async () => {
    setSending(true);
    try {
      await unwrap(api.POST("/api/auth/resend-confirmation"));
      toast.success("Doğrulama e-postasını tekrar gönderdik.");
    } catch (e) {
      toast.error(errorMessage(e));
    } finally {
      setSending(false);
      setCooldown(COOLDOWN_SECONDS);
    }
  };

  return (
    <div role="status" className="border-b border-border bg-neutral-50 px-4 shadow-[inset_3px_0_0_var(--color-brand)] py-3 lg:px-8">
      <div className="mx-auto flex max-w-6xl flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
        <p className="flex items-start gap-2 text-sm text-black">
          <MailWarning aria-hidden className="mt-0.5 size-5 shrink-0" />
          <span>
            <strong>E-postanızı doğrulayın.</strong> {me.user.email} adresine bir bağlantı gönderdik. Ekip daveti için doğrulama
            gerekiyor.
          </span>
        </p>
        <Button
          variant="outline"
          className="shrink-0"
          disabled={sending || cooldown > 0}
          onClick={resend}
        >
          {cooldown > 0 ? `Tekrar gönder (${cooldown})` : "Tekrar gönder"}
        </Button>
      </div>
    </div>
  );
}
