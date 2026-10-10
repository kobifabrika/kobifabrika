"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { CircleCheck, CircleX } from "lucide-react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { useEffect, useRef } from "react";

import { meQueryKey } from "@/components/providers/session-provider";
import { Button } from "@/components/ui/button";
import { api, errorMessage, fieldErrors, unwrap } from "@/lib/api/client";

/** E-postadaki linkle açılır; sayfa açılınca doğrulama isteği bir kez gönderilir. */
export function VerifyEmail() {
  const params = useSearchParams();
  const queryClient = useQueryClient();
  const userId = params.get("u") ?? "";
  const token = params.get("t") ?? "";
  const started = useRef(false);

  const verify = useMutation({
    mutationFn: () => unwrap(api.POST("/api/auth/confirm-email", { body: { userId, token } })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: meQueryKey }),
  });

  useEffect(() => {
    if (started.current || !userId || !token) return;
    started.current = true;
    verify.mutate();
  }, [userId, token, verify]);

  if (!userId || !token || verify.isError) {
    const message = verify.isError ? (fieldErrors(verify.error).token ?? errorMessage(verify.error)) : "Bağlantı eksik veya bozuk.";
    return (
      <Result ok={false} title="E-posta doğrulanamadı" text={`${message} Panelde "Tekrar gönder" ile yeni bağlantı isteyebilirsin.`} />
    );
  }

  if (verify.isSuccess) {
    return <Result ok title="E-postan doğrulandı" text="Teşekkürler! Artık ekip arkadaşlarını davet edebilirsin." />;
  }

  return (
    <p role="status" className="mt-8 text-sm text-muted-foreground">
      Doğrulanıyor…
    </p>
  );
}

function Result({ ok, title, text }: { ok: boolean; title: string; text: string }) {
  const Icon = ok ? CircleCheck : CircleX;
  return (
    <div className="mt-8 flex flex-col items-start gap-4" role={ok ? "status" : "alert"}>
      {/* Durum renkleri koyu zeminde yeterli kontrast vermez; ikon açık daire içinde. */}
      <span className={ok ? "rounded-full bg-success-50 p-2 text-success-600" : "rounded-full bg-danger-50 p-2 text-danger-600"}>
        <Icon aria-hidden className="size-8" />
      </span>
      <h2 className="text-xl font-bold text-foreground">{title}</h2>
      <p className="text-sm text-muted-foreground">{text}</p>
      <Button asChild size="lg" className="w-full">
        <Link href="/panel">Panele git</Link>
      </Button>
    </div>
  );
}
