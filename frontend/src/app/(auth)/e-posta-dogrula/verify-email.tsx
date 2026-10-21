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
      <Result ok={false} title="E-posta doğrulanamadı" text={`${message} Panelde "Tekrar gönder" ile yeni bağlantı isteyebilirsiniz.`} />
    );
  }

  if (verify.isSuccess) {
    return <Result ok title="E-postanız doğrulandı" text="Teşekkürler! Artık ekip arkadaşlarınızı davet edebilirsiniz." />;
  }

  return (
    <p role="status" className="mt-8 text-sm text-neutral-600">
      Doğrulanıyor…
    </p>
  );
}

function Result({ ok, title, text }: { ok: boolean; title: string; text: string }) {
  const Icon = ok ? CircleCheck : CircleX;
  return (
    <div className="mt-8 flex flex-col items-start gap-4" role={ok ? "status" : "alert"}>
      <Icon aria-hidden className={ok ? "size-10 text-brand" : "size-10 text-danger-600"} />
      <h2 className="text-xl font-bold text-black">{title}</h2>
      <p className="text-sm text-neutral-600">{text}</p>
      <Button asChild size="lg" className="w-full">
        <Link href="/panel">Panele git</Link>
      </Button>
    </div>
  );
}
