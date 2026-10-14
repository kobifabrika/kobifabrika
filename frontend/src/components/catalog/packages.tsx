"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { cn } from "cn";
import { Layers, Plus } from "lucide-react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState, type FormEvent } from "react";
import { toast } from "sonner";

import { EmptyState, selectClass } from "@/components/contacts/contact-bits";
import { QueryError } from "@/components/feedback/query-error";
import { Field, FormMessage } from "@/components/forms/field";
import { useSession } from "@/components/providers/session-provider";
import { useServices } from "@/components/staff/staff-services";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Switch } from "@/components/ui/switch";
import { api, errorMessage, fieldErrors, unwrap } from "@/lib/api/client";
import type { ServicePackage } from "@/lib/api/types";
import { formatMoney } from "@/lib/format";
import { can } from "@/lib/permissions";
import { parseMoneyInput } from "@/lib/validation/rules";

export const packagesKey = ["service-packages"] as const;

/** Paket tanımları (pasifler dahil isteğe bağlı). Hızlı satış aktif paketleri arar. */
export function usePackages(includeInactive = false) {
  return useQuery({
    queryKey: [...packagesKey, includeInactive],
    queryFn: () => unwrap(api.GET("/api/service-packages", { params: { query: { includeInactive } } })),
  });
}

/** "10 seans · 90 gün geçerli". */
export function packageSummary(p: Pick<ServicePackage, "sessionCount" | "validityDays">): string {
  return `${p.sessionCount} seans · ${p.validityDays} gün geçerli`;
}

/** Ürünler → Paketler: seanslı paket tanımları. Sahip ve Yönetici ekler/düzenler; diğerleri görür. */
export function PackageList() {
  const { role } = useSession();
  const manage = can(role, "manageCatalog");
  const [showInactive, setShowInactive] = useState(false);
  const list = usePackages(showInactive);

  return (
    <div className="flex flex-col gap-5">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-bold tracking-tight text-black">Paketler</h1>
        {manage && (
          <Button asChild>
            <Link href="/panel/urunler/paketler/yeni">
              <Plus aria-hidden />
              Yeni paket
            </Link>
          </Button>
        )}
      </div>
      <p className="text-sm text-neutral-600">
        Seanslı paketi hızlı satıştan müşteriye satın; seanslar randevu tamamlanınca düşer. Kalan seanslar müşteri kartında görünür.
      </p>
      <label className="flex min-h-11 items-center gap-2 text-sm text-black">
        <Switch checked={showInactive} onCheckedChange={setShowInactive} />
        Pasifleri göster
      </label>

      {list.isPending ? (
        <p role="status" className="text-sm text-neutral-600">
          Yükleniyor…
        </p>
      ) : list.isError ? (
        <QueryError error={list.error} onRetry={list.refetch} />
      ) : list.data.length === 0 ? (
        <EmptyState icon={<Layers className="size-10" />} title="Henüz paket yok">
          <p className="text-sm text-neutral-600">Ör. &quot;10 seans lazer&quot;: hizmet, seans sayısı, fiyat ve geçerlilik süresi.</p>
          {manage && (
            <Button asChild>
              <Link href="/panel/urunler/paketler/yeni">İlk paketi tanımla</Link>
            </Button>
          )}
        </EmptyState>
      ) : (
        <ul className="flex flex-col divide-y divide-border border-y border-border">
          {list.data.map((p) => (
            <li key={p.id}>
              <Link
                href={`/panel/urunler/paketler/${p.id}`}
                className="flex min-h-16 items-center justify-between gap-3 px-1 py-3 hover:bg-neutral-50"
              >
                <span className="flex min-w-0 flex-col">
                  <span className={cn("truncate text-base font-semibold", p.isActive ? "text-black" : "text-neutral-600")}>
                    {p.name}
                    {!p.isActive && <span className="ml-2 text-xs font-normal">Pasif</span>}
                  </span>
                  <span className="truncate text-sm text-neutral-600">
                    {p.serviceName} · {packageSummary(p)}
                    {p.soldCount > 0 && ` · ${p.soldCount} satıldı`}
                  </span>
                </span>
                <span className="shrink-0 text-base font-semibold text-black tabular-nums">{formatMoney(p.price)}</span>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

/** Paket tanımı (yeni veya düzenleme). Değişiklik satılmış paketleri etkilemez. */
export function PackageForm({ initial }: { initial?: ServicePackage }) {
  const router = useRouter();
  const queryClient = useQueryClient();
  const services = useServices();
  const [name, setName] = useState(initial?.name ?? "");
  const [productId, setProductId] = useState(initial?.productId ?? "");
  const [sessions, setSessions] = useState(String(initial?.sessionCount ?? 10));
  const [price, setPrice] = useState(initial ? String(initial.price).replace(".", ",") : "");
  const [validity, setValidity] = useState(String(initial?.validityDays ?? 90));
  const [active, setActive] = useState(initial?.isActive ?? true);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [error, setError] = useState<string | null>(null);

  const refresh = () => queryClient.invalidateQueries({ queryKey: packagesKey });
  const save = useMutation({
    mutationFn: () => {
      const body = {
        name: name.trim(),
        productId,
        sessionCount: Number(sessions),
        price: parseMoneyInput(price)!.value,
        validityDays: Number(validity),
        isActive: active,
      };
      return unwrap(
        initial
          ? api.PUT("/api/service-packages/{id}", { params: { path: { id: initial.id } }, body })
          : api.POST("/api/service-packages", { body }),
      );
    },
    onSuccess: async () => {
      await refresh();
      toast.success(initial ? "Paket kaydedildi." : "Paket tanımlandı.");
      router.push("/panel/urunler?sekme=paketler");
    },
    onError: (e) => {
      const fields = fieldErrors(e);
      setErrors(fields);
      setError(Object.keys(fields).length ? null : errorMessage(e));
    },
  });
  const remove = useMutation({
    mutationFn: () => unwrap(api.DELETE("/api/service-packages/{id}", { params: { path: { id: initial!.id } } })),
    onSuccess: async (r) => {
      await refresh();
      toast.success(r.deleted ? "Paket silindi." : "Paket satıldığı için silinmedi, pasife alındı.");
      router.push("/panel/urunler?sekme=paketler");
    },
    onError: (e) => toast.error(errorMessage(e)),
  });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    const next: Record<string, string> = {};
    if (!name.trim()) next.name = "Paket adını girin (ör. 10 seans lazer).";
    if (!productId) next.productId = "Hizmet seçin.";
    const s = Number(sessions);
    if (!Number.isInteger(s) || s < 1 || s > 500) next.sessionCount = "Seans sayısı 1 ile 500 arasında olmalı.";
    const p = parseMoneyInput(price);
    if (!p || p.value < 0 || p.decimals > 2) next.price = "Fiyatı rakamla girin (ör. 3.000,00).";
    const v = Number(validity);
    if (!Number.isInteger(v) || v < 1 || v > 1095) next.validityDays = "Geçerlilik 1 ile 1095 gün arasında olmalı.";
    setErrors(next);
    setError(null);
    if (Object.keys(next).length === 0) save.mutate();
  };

  const serviceList = (services.data ?? []).filter((s) => s.isActive || s.id === initial?.productId);
  return (
    <form onSubmit={submit} noValidate className="flex max-w-xl flex-col gap-4">
      <Field label="Paket adı" error={errors.name}>
        <Input value={name} maxLength={200} onChange={(e) => setName(e.target.value)} placeholder="10 seans lazer" />
      </Field>
      <Field label="Hizmet" error={errors.productId} hint="Seanslar bu hizmetin randevularında kullanılır.">
        <select className={selectClass} value={productId} onChange={(e) => setProductId(e.target.value)}>
          <option value="">Seçin</option>
          {serviceList.map((s) => (
            <option key={s.id} value={s.id}>
              {s.name}
            </option>
          ))}
        </select>
      </Field>
      <div className="grid grid-cols-2 gap-4">
        <Field label="Seans sayısı" error={errors.sessionCount}>
          <Input type="number" inputMode="numeric" min={1} max={500} value={sessions} onChange={(e) => setSessions(e.target.value)} />
        </Field>
        <Field label="Geçerlilik (gün)" error={errors.validityDays} hint="Satış gününden itibaren.">
          <Input type="number" inputMode="numeric" min={1} max={1095} value={validity} onChange={(e) => setValidity(e.target.value)} />
        </Field>
      </div>
      <Field label="Fiyat (KDV dahil)" error={errors.price} hint="KDV oranı hizmetinki.">
        <Input inputMode="decimal" value={price} onChange={(e) => setPrice(e.target.value)} placeholder="3.000,00" />
      </Field>
      <label className="flex min-h-11 items-center gap-3 text-sm text-black">
        <Switch checked={active} onCheckedChange={setActive} />
        Satışa açık
      </label>
      {error && <FormMessage>{error}</FormMessage>}
      <div className="flex flex-wrap gap-2">
        <Button type="submit" disabled={save.isPending}>
          {initial ? "Kaydet" : "Paketi tanımla"}
        </Button>
        <Button asChild variant="ghost">
          <Link href="/panel/urunler?sekme=paketler">Vazgeç</Link>
        </Button>
        {initial && (
          <Button
            type="button"
            variant="ghost"
            className="text-danger-600"
            disabled={remove.isPending}
            onClick={() => window.confirm(`${initial.name} silinsin mi? Satıldıysa pasife alınır.`) && remove.mutate()}
          >
            Sil
          </Button>
        )}
      </div>
    </form>
  );
}

/** /panel/urunler/paketler/[id]: paket düzenleme. */
export function PackageEdit({ id }: { id: string }) {
  const pkg = useQuery({
    queryKey: [...packagesKey, "detail", id],
    queryFn: () => unwrap(api.GET("/api/service-packages/{id}", { params: { path: { id } } })),
  });
  const { role } = useSession();
  if (pkg.isPending)
    return (
      <p role="status" className="text-sm text-neutral-600">
        Yükleniyor…
      </p>
    );
  if (pkg.isError) return <QueryError error={pkg.error} onRetry={pkg.refetch} />;
  if (!can(role, "manageCatalog"))
    return (
      <dl className="flex flex-col gap-2 text-sm">
        <dt className="text-neutral-600">Hizmet</dt>
        <dd className="font-semibold text-black">{pkg.data.serviceName}</dd>
        <dt className="text-neutral-600">Seans ve geçerlilik</dt>
        <dd className="font-semibold text-black">{packageSummary(pkg.data)}</dd>
        <dt className="text-neutral-600">Fiyat</dt>
        <dd className="font-semibold text-black">{formatMoney(pkg.data.price)}</dd>
      </dl>
    );
  return <PackageForm initial={pkg.data} />;
}
