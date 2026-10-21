"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft } from "lucide-react";
import Link from "next/link";
import { useParams, useSearchParams } from "next/navigation";
import { useState, type FormEvent, type ReactNode } from "react";
import { toast } from "sonner";

import { selectClass } from "@/components/contacts/contact-bits";
import { QueryError } from "@/components/feedback/query-error";
import { Field, FormMessage } from "@/components/forms/field";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Switch } from "@/components/ui/switch";
import { TabNav } from "@/components/ui/scroll-row";
import { Textarea } from "@/components/ui/textarea";
import { api, errorMessage, fieldErrors, unwrap } from "@/lib/api/client";
import { platformActionNames, roleNames, type AdminBusinessDetail, type Role } from "@/lib/api/types";
import { displayPhone } from "@/lib/contacts";
import { formatDate, formatDateTime, formatRelative } from "@/lib/format";

import { adminKey } from "./platform-overview";
import { planLabel } from "./platform-businesses";

const tabs = [
  { key: "ozet", label: "Özet ve paket" },
  { key: "kullanicilar", label: "Kullanıcılar" },
  { key: "kredi", label: "Kredi" },
  { key: "notlar", label: "Notlar" },
  { key: "gunluk", label: "Günlük" },
] as const;

const planOptions = [
  { code: "cirak", name: "Çırak" },
  { code: "kalfa", name: "Kalfa" },
  { code: "usta", name: "Usta" },
] as const;

/**
 * /platform/isletmeler/[id]?sekme=: işletmenin paketi, pilotu, denemesi, salt okunur modu, kullanıcıları, SMS kredisi, destek notları
 * ve platform günlüğü. Her işlem platform günlüğüne ve işletmenin aktivite günlüğüne yazılır (API). Müşteri verisi yok.
 */
export function PlatformBusinessDetail() {
  const { id } = useParams<{ id: string }>();
  const params = useSearchParams();
  const active = tabs.find((t) => t.key === params.get("sekme"))?.key ?? "ozet";
  const key = [...adminKey, "business", id];
  const query = useQuery({
    queryKey: key,
    queryFn: () => unwrap(api.GET("/api/admin/businesses/{id}", { params: { path: { id } } })),
  });

  return (
    <div className="flex flex-col gap-4">
      <Link
        href="/platform/isletmeler"
        className="flex min-h-11 w-fit items-center gap-1 text-sm font-semibold text-neutral-700 hover:text-black"
      >
        <ArrowLeft aria-hidden className="size-4" />
        İşletmeler
      </Link>
      {query.isPending ? (
        <p role="status" className="text-sm text-neutral-600">
          Yükleniyor…
        </p>
      ) : query.isError ? (
        <QueryError error={query.error} onRetry={query.refetch} />
      ) : (
        <>
          <div className="flex flex-col gap-1">
            <h1 className="text-2xl font-bold tracking-tight text-black">{query.data.summary.name}</h1>
            <p className="text-sm text-neutral-600">
              {planLabel(query.data.summary)} · /r/{query.data.summary.slug}
            </p>
          </div>
          <TabNav
            label="İşletme sekmeleri"
            active={active}
            tabs={tabs.map((t) => ({ ...t, href: `?sekme=${t.key}` }))}
          />
          {active === "ozet" && <SummaryTab detail={query.data} />}
          {active === "kullanicilar" && <UsersTab id={id} />}
          {active === "kredi" && <CreditTab detail={query.data} />}
          {active === "notlar" && <NotesTab id={id} />}
          {active === "gunluk" && <ActivityTab id={id} />}
        </>
      )}
    </div>
  );
}

function Row({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="flex flex-col gap-0.5 sm:flex-row sm:gap-3">
      <dt className="text-sm text-neutral-600 sm:w-44 sm:shrink-0">{label}</dt>
      <dd className="text-sm text-black tabular-nums">{children}</dd>
    </div>
  );
}

/** Ayrıntı ve liste yenilensin (paket, pilot, deneme, salt okunur, kredi değişince). */
function useAdminRefresh() {
  const queryClient = useQueryClient();
  return () => queryClient.invalidateQueries({ queryKey: adminKey });
}

function SummaryTab({ detail }: { detail: AdminBusinessDetail }) {
  const s = detail.summary;
  const refresh = useAdminRefresh();
  const id = s.id;
  const [plan, setPlan] = useState(s.planCode);
  const [days, setDays] = useState("14");
  const [reason, setReason] = useState(detail.readOnlyReason ?? "");
  const done = (text: string) => async () => {
    await refresh();
    toast.success(text);
  };
  const onError = (e: unknown) => toast.error(errorMessage(e));

  const changePlan = useMutation({
    mutationFn: () =>
      unwrap(api.PUT("/api/admin/businesses/{id}/plan", { params: { path: { id } }, body: { planCode: plan } })),
    onSuccess: done("Paket değişti."),
    onError,
  });
  const pilot = useMutation({
    mutationFn: (isPilot: boolean) =>
      unwrap(api.PUT("/api/admin/businesses/{id}/pilot", { params: { path: { id } }, body: { isPilot } })),
    onSuccess: done("Pilot durumu değişti."),
    onError,
  });
  const readOnly = useMutation({
    mutationFn: (isReadOnly: boolean) =>
      unwrap(
        api.PUT("/api/admin/businesses/{id}/read-only", {
          params: { path: { id } },
          body: { isReadOnly, reason: reason.trim() || null },
        }),
      ),
    onSuccess: done("Salt okunur durumu değişti."),
    onError,
  });
  const extend = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST("/api/admin/businesses/{id}/trial/extend", { params: { path: { id } }, body: { days: Number(days) } }),
      ),
    onSuccess: done("Deneme uzatıldı."),
    onError,
  });
  const end = useMutation({
    mutationFn: () => unwrap(api.POST("/api/admin/businesses/{id}/trial/end", { params: { path: { id } } })),
    onSuccess: done("Deneme bitirildi."),
    onError,
  });

  return (
    <div className="grid gap-4 lg:grid-cols-2">
      <Card className="gap-3 py-4">
        <CardHeader className="px-4">
          <CardTitle className="text-base font-semibold text-black">Özet</CardTitle>
        </CardHeader>
        <CardContent className="px-4">
          <dl className="flex flex-col gap-2">
            <Row label="Sahip">{s.ownerName ? `${s.ownerName} · ${s.ownerEmail}` : "—"}</Row>
            <Row label="Telefon">{detail.phone ? displayPhone(detail.phone) : "—"}</Row>
            <Row label="Şehir">{[detail.district, s.city].filter(Boolean).join(", ") || "—"}</Row>
            <Row label="Kayıt">{formatDate(s.createdAt)}</Row>
            <Row label="Son giriş">{s.lastLoginAt ? formatRelative(s.lastLoginAt) : "—"}</Row>
            <Row label="Kurulum">{s.setupCompleted ? "Tamamlandı" : "Bitmedi"}</Row>
            <Row label="Kullanıcı / personel">
              {s.users} / {detail.staffMembers}
            </Row>
            <Row label="Müşteri">{s.contacts}</Row>
            <Row label="Randevu (bu ay / toplam)">
              {s.appointmentsThisMonth} / {detail.appointmentsTotal}
            </Row>
            <Row label="Satış (toplam)">{detail.salesTotal}</Row>
            <Row label="Online randevu">{detail.bookingEnabled ? "Açık" : "Kapalı"}</Row>
            <Row label="İstediği paket">
              {detail.requestedPlan
                ? `${detail.requestedPlan}${detail.requestedPeriod ? ` (${detail.requestedPeriod})` : ""}`
                : "—"}
            </Row>
            <Row label="Açık geri bildirim">{detail.openFeedback}</Row>
          </dl>
        </CardContent>
      </Card>

      <Card className="gap-4 py-4">
        <CardHeader className="px-4">
          <CardTitle className="text-base font-semibold text-black">Paket</CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-5 px-4">
          <label className="flex min-h-11 items-center justify-between gap-4">
            <span className="flex flex-col">
              <span className="text-sm font-semibold text-black">Pilot</span>
              <span className="text-sm text-neutral-600">Süresiz, limitsiz, bütün özellikler.</span>
            </span>
            <Switch
              aria-label="Pilot"
              checked={s.status === "Pilot"}
              disabled={pilot.isPending}
              onCheckedChange={(v) => pilot.mutate(v)}
            />
          </label>

          <div className="flex flex-col gap-2">
            <Field label="Paket (deneme bitince geçerli)">
              <select className={selectClass} value={plan} onChange={(e) => setPlan(e.target.value)}>
                {planOptions.map((p) => (
                  <option key={p.code} value={p.code}>
                    {p.name}
                  </option>
                ))}
              </select>
            </Field>
            <Button
              variant="outline"
              className="self-start"
              disabled={plan === s.planCode || changePlan.isPending}
              onClick={() => changePlan.mutate()}
            >
              Paketi değiştir
            </Button>
          </div>

          <div className="flex flex-col gap-2">
            <p className="text-sm font-semibold text-black">
              Deneme {s.trialEndsAt ? `· bitiş ${formatDateTime(s.trialEndsAt)}` : "· yok"}
            </p>
            <div className="flex flex-wrap items-end gap-2">
              <Field label="Gün">
                <Input
                  type="number"
                  inputMode="numeric"
                  min={1}
                  max={365}
                  className="w-24"
                  value={days}
                  onChange={(e) => setDays(e.target.value)}
                />
              </Field>
              <Button variant="outline" disabled={extend.isPending} onClick={() => extend.mutate()}>
                Denemeyi uzat
              </Button>
              <Button variant="outline" disabled={end.isPending || s.status !== "Trial"} onClick={() => end.mutate()}>
                Denemeyi bitir
              </Button>
            </div>
          </div>

          <div className="flex flex-col gap-2 border-t border-border pt-4">
            <Field label="Salt okunur gerekçesi (isteğe bağlı)">
              <Input maxLength={500} value={reason} onChange={(e) => setReason(e.target.value)} />
            </Field>
            <label className="flex min-h-11 items-center justify-between gap-4">
              <span className="flex flex-col">
                <span className="text-sm font-semibold text-black">Salt okunur</span>
                <span className="text-sm text-neutral-600">
                  Panelde ve herkese açık sayfalarda yazma kapanır; okuma ve Excel çalışır.
                </span>
              </span>
              <Switch
                aria-label="Salt okunur"
                checked={s.isReadOnly}
                disabled={readOnly.isPending}
                onCheckedChange={(v) => readOnly.mutate(v)}
              />
            </label>
          </div>
        </CardContent>
      </Card>
    </div>
  );
}

function UsersTab({ id }: { id: string }) {
  const query = useQuery({
    queryKey: [...adminKey, "business", id, "users"],
    queryFn: () => unwrap(api.GET("/api/admin/businesses/{id}/users", { params: { path: { id } } })),
  });
  if (query.isPending) return <p className="text-sm text-neutral-600">Yükleniyor…</p>;
  if (query.isError) return <QueryError error={query.error} onRetry={query.refetch} />;
  return (
    <ul className="flex flex-col divide-y divide-border border-y border-border">
      {query.data.map((u) => (
        <li key={u.userId} className="flex flex-col gap-0.5 py-3">
          <span className="text-sm font-semibold text-black">
            {u.fullName} <span className="font-normal text-neutral-600">· {roleNames[u.role as Role]}</span>
            {!u.isActive && <span className="font-normal text-neutral-600"> · pasif</span>}
          </span>
          <span className="text-xs text-neutral-600">
            {u.email}
            {!u.emailConfirmed && " (doğrulanmadı)"}
            {u.phone && ` · ${displayPhone(u.phone)}`}
          </span>
          <span className="text-xs text-neutral-600">
            Katıldı {formatDate(u.joinedAt)} · son giriş {u.lastLoginAt ? formatRelative(u.lastLoginAt) : "yok"}
          </span>
        </li>
      ))}
    </ul>
  );
}

function CreditTab({ detail }: { detail: AdminBusinessDetail }) {
  const refresh = useAdminRefresh();
  const [amount, setAmount] = useState("");
  const [note, setNote] = useState("");
  const [errors, setErrors] = useState<Record<string, string>>({});
  const topUp = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST("/api/admin/businesses/{id}/sms-credits", {
          params: { path: { id: detail.summary.id } },
          body: { amount: Number(amount), note: note.trim() },
        }),
      ),
    onSuccess: async (r) => {
      toast.success(
        `${Number(amount).toLocaleString("tr-TR")} kredi yüklendi. Yeni bakiye ${r.balance.toLocaleString("tr-TR")}.`,
      );
      setAmount("");
      setNote("");
      setErrors({});
      await refresh();
    },
    onError: (e) => {
      setErrors(fieldErrors(e));
      toast.error(errorMessage(e));
    },
  });
  const submit = (e: FormEvent) => {
    e.preventDefault();
    topUp.mutate();
  };
  return (
    <form onSubmit={submit} noValidate className="flex max-w-md flex-col gap-4">
      <p className="text-sm text-black">
        SMS bakiyesi:{" "}
        <span className="font-semibold tabular-nums">{detail.summary.smsBalance.toLocaleString("tr-TR")}</span>
      </p>
      <p className="text-sm text-neutral-600">
        SMS gönderimi pilot döneminde kapalı; yüklenen kredi SMS açılınca kullanılır.
      </p>
      <Field label="Kredi" error={errors.amount}>
        <Input type="number" inputMode="numeric" min={1} value={amount} onChange={(e) => setAmount(e.target.value)} />
      </Field>
      <Field label="Not" error={errors.note}>
        <Input maxLength={200} value={note} onChange={(e) => setNote(e.target.value)} />
      </Field>
      <Button type="submit" className="self-start" disabled={topUp.isPending}>
        Yükle
      </Button>
    </form>
  );
}

function NotesTab({ id }: { id: string }) {
  const queryClient = useQueryClient();
  const key = [...adminKey, "business", id, "notes"];
  const query = useQuery({
    queryKey: key,
    queryFn: () => unwrap(api.GET("/api/admin/businesses/{id}/notes", { params: { path: { id } } })),
  });
  const [body, setBody] = useState("");
  const [error, setError] = useState<string | null>(null);
  const add = useMutation({
    mutationFn: () =>
      unwrap(api.POST("/api/admin/businesses/{id}/notes", { params: { path: { id } }, body: { body: body.trim() } })),
    onSuccess: async () => {
      setBody("");
      setError(null);
      await queryClient.invalidateQueries({ queryKey: key });
    },
    onError: (e) => setError(errorMessage(e)),
  });
  return (
    <div className="flex flex-col gap-4">
      <form
        onSubmit={(e) => {
          e.preventDefault();
          if (!body.trim()) setError("Not yazın.");
          else add.mutate();
        }}
        noValidate
        className="flex flex-col gap-2"
      >
        <Field label="Destek notu (işletme görmez)">
          <Textarea rows={3} maxLength={2000} value={body} onChange={(e) => setBody(e.target.value)} />
        </Field>
        {error && <FormMessage>{error}</FormMessage>}
        <Button type="submit" className="self-start" disabled={add.isPending}>
          Not ekle
        </Button>
      </form>
      {query.isPending ? (
        <p className="text-sm text-neutral-600">Yükleniyor…</p>
      ) : query.isError ? (
        <QueryError error={query.error} onRetry={query.refetch} />
      ) : query.data.length === 0 ? (
        <p className="text-sm text-neutral-600">Not yok.</p>
      ) : (
        <ul className="flex flex-col divide-y divide-border border-y border-border">
          {query.data.map((n) => (
            <li key={n.id} className="flex flex-col gap-1 py-3">
              <p className="text-sm whitespace-pre-line text-black">{n.body}</p>
              <span className="text-xs text-neutral-600">
                {n.authorName} · {formatDateTime(n.createdAt)}
              </span>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

function ActivityTab({ id }: { id: string }) {
  const query = useQuery({
    queryKey: [...adminKey, "business", id, "activity"],
    queryFn: () => unwrap(api.GET("/api/admin/businesses/{id}/activity", { params: { path: { id } } })),
  });
  if (query.isPending) return <p className="text-sm text-neutral-600">Yükleniyor…</p>;
  if (query.isError) return <QueryError error={query.error} onRetry={query.refetch} />;
  if (query.data.length === 0) return <p className="text-sm text-neutral-600">Platform işlemi yok.</p>;
  return (
    <ul className="flex flex-col divide-y divide-border border-y border-border">
      {query.data.map((a) => (
        <li key={a.id} className="flex flex-col gap-0.5 py-3">
          <span className="text-sm font-semibold text-black">{platformActionNames[a.action]}</span>
          {a.details && <span className="text-xs break-all text-neutral-700">{a.details}</span>}
          <span className="text-xs text-neutral-600">
            {a.adminName} · {formatDateTime(a.at)}
          </span>
        </li>
      ))}
    </ul>
  );
}
