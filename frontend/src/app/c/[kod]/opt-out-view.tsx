"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Check } from "lucide-react";
import { useParams } from "next/navigation";
import { useEffect } from "react";

import { PublicMessage, PublicShell } from "@/components/public/public-shell";
import { Button } from "@/components/ui/button";
import { api, errorMessage, unwrap } from "@/lib/api/client";

export function OptOutView() {
  const { kod } = useParams<{ kod: string }>();
  const queryClient = useQueryClient();
  const key = ["public", "opt-out", kod];
  const page = useQuery({
    queryKey: key,
    queryFn: () => unwrap(api.GET("/api/public/opt-out/{code}", { params: { path: { code: kod } } })),
    retry: false,
  });
  const stop = useMutation({
    mutationFn: () => unwrap(api.POST("/api/public/opt-out/{code}", { params: { path: { code: kod } } })),
    onSuccess: (data) => queryClient.setQueryData(key, data),
  });

  useEffect(() => {
    if (page.data) document.title = `${page.data.businessName} · SMS'leri durdur`;
  }, [page.data]);

  if (page.isPending)
    return (
      <PublicShell>
        <p role="status" className="py-10 text-sm text-neutral-600">
          Yükleniyor…
        </p>
      </PublicShell>
    );
  if (page.isError)
    return (
      <PublicShell>
        <PublicMessage title="Bu link artık geçerli değil">Linki kontrol edin veya işletmeyle iletişime geçin.</PublicMessage>
      </PublicShell>
    );

  const p = page.data;
  return (
    <PublicShell businessName={p.businessName}>
      {p.subscribed ? (
        <section className="flex max-w-md flex-col gap-4">
          <h1 className="text-xl font-bold text-black">Kampanya SMS&apos;lerini durdurmak istiyor musunuz?</h1>
          <p className="text-base text-neutral-700">
            {p.businessName} size artık kampanya ve fırsat SMS&apos;i göndermez. Randevu hatırlatması gibi bilgilendirme mesajları gelmeye devam
            eder.
          </p>
          <Button className="w-full sm:w-auto" disabled={stop.isPending} onClick={() => stop.mutate()}>
            SMS&apos;leri durdur
          </Button>
          {stop.isError && (
            <p role="alert" className="text-sm text-danger-600">
              {errorMessage(stop.error)}
            </p>
          )}
        </section>
      ) : (
        <section className="flex max-w-md items-start gap-3 border-l-2 border-brand bg-neutral-50 px-4 py-4" role="status">
          <Check aria-hidden className="mt-0.5 size-5 shrink-0 text-black" />
          <p className="text-base text-black">Artık {p.businessName}&apos;den kampanya SMS&apos;i almayacaksınız.</p>
        </section>
      )}
    </PublicShell>
  );
}
