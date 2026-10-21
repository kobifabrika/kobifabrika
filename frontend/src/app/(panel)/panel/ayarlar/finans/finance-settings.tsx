"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Pencil, Plus } from "lucide-react";
import { useState, type ReactNode } from "react";
import { toast } from "sonner";

import { selectClass } from "@/components/contacts/contact-bits";
import { QueryError } from "@/components/feedback/query-error";
import { Field, FormMessage } from "@/components/forms/field";
import { expenseCategoriesKey, useExpenseCategories } from "@/components/finance/expenses";
import { cashAccountsKey, useFinanceRefresh } from "@/components/finance/finance-dialogs";
import { meQueryKey, useSession } from "@/components/providers/session-provider";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { NumberInput } from "@/components/ui/number-input";
import { Switch } from "@/components/ui/switch";
import { api, errorMessage, fieldErrors, unwrap } from "@/lib/api/client";
import type { CashAccountType, ExpenseCategory, ExpenseCategoryKind } from "@/lib/api/types";
import { can } from "@/lib/permissions";
import { useIsDesktop } from "@/lib/use-media";

function SettingsCard({ title, description, children }: { title: string; description?: string; children: ReactNode }) {
  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-black">{title}</CardTitle>
        {description && <CardDescription>{description}</CardDescription>}
      </CardHeader>
      <CardContent>{children}</CardContent>
    </Card>
  );
}

/** Ayarlar → Finans: varsayılan hesaplar, veresiye vadesi, otomatik vade bildirimi, gelir-gider kategorileri. */
export function FinanceSettings() {
  const { role } = useSession();
  return (
    <div className="flex flex-col gap-4">
      {role === "Accountant" && <p className="text-sm text-neutral-600">Mali Müşavir olarak bu ayarları görüntüleyebilirsiniz; değiştiremezsiniz.</p>}
      <DefaultAccountsCard readOnly={!can(role, "manageFinance")} />
      <DueSettingsCard readOnly={!can(role, "manageBusiness")} />
      <CategoriesCard readOnly={!can(role, "manageFinance")} />
    </div>
  );
}

function DefaultAccountsCard({ readOnly }: { readOnly: boolean }) {
  const refresh = useFinanceRefresh();
  const accounts = useQuery({ queryKey: [...cashAccountsKey], queryFn: () => unwrap(api.GET("/api/cash-accounts")) });
  const save = useMutation({
    mutationFn: (id: string) => unwrap(api.POST("/api/cash-accounts/{id}/default", { params: { path: { id } } })),
    onSuccess: async () => {
      toast.success("Varsayılan hesap değişti.");
      await refresh();
    },
    onError: (e) => toast.error(errorMessage(e)),
  });

  const rows: { type: CashAccountType; label: string; hint: string }[] = [
    { type: "Cash", label: "Nakit satışlar", hint: "Nakit tahsilat ve satışların gireceği kasa" },
    { type: "Bank", label: "Kart ve havale satışları", hint: "Kartla ve havaleyle gelen paranın gireceği banka hesabı" },
  ];

  return (
    <SettingsCard title="Varsayılan hesaplar" description="Satış ekranında istenirse başka hesap da seçilebilir.">
      {accounts.isPending ? (
        <p role="status" className="text-sm text-neutral-600">
          Yükleniyor…
        </p>
      ) : accounts.isError ? (
        <QueryError error={accounts.error} onRetry={accounts.refetch} />
      ) : (
        <div className="grid gap-4 sm:grid-cols-2">
          {rows.map((row) => {
            const options = accounts.data.filter((a) => a.type === row.type);
            const current = options.find((a) => a.isDefault);
            return (
              <Field key={row.type} label={row.label} hint={row.hint}>
                <select
                  className={selectClass}
                  value={current?.id ?? ""}
                  disabled={readOnly || save.isPending}
                  onChange={(e) => e.target.value && save.mutate(e.target.value)}
                >
                  {!current && <option value="">Seçin</option>}
                  {options.map((a) => (
                    <option key={a.id} value={a.id}>
                      {a.name}
                    </option>
                  ))}
                </select>
              </Field>
            );
          })}
        </div>
      )}
    </SettingsCard>
  );
}

function DueSettingsCard({ readOnly }: { readOnly: boolean }) {
  const queryClient = useQueryClient();
  const business = useQuery({ queryKey: ["business"], queryFn: () => unwrap(api.GET("/api/business")) });
  const [days, setDays] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const save = useMutation({
    mutationFn: (body: { defaultDueDays: number; dueDigestEnabled: boolean }) => unwrap(api.PUT("/api/business/finance", { body })),
    onSuccess: async (next) => {
      queryClient.setQueryData(["business"], next);
      await queryClient.invalidateQueries({ queryKey: meQueryKey });
      setDays(null);
      setError(null);
      toast.success("Finans ayarları kaydedildi.");
    },
    onError: (e) => setError(fieldErrors(e).defaultDueDays ?? errorMessage(e)),
  });

  if (business.isPending)
    return (
      <SettingsCard title="Veresiye ve vade">
        <p role="status" className="text-sm text-neutral-600">
          Yükleniyor…
        </p>
      </SettingsCard>
    );
  if (business.isError) return <QueryError error={business.error} onRetry={business.refetch} />;

  const b = business.data;
  const value = days ?? String(b.defaultDueDays);
  const submitDays = () => {
    const n = Number(value);
    if (!/^\d+$/.test(value) || n > 365) return setError("Vade 0 ile 365 gün arasında olmalı.");
    save.mutate({ defaultDueDays: n, dueDigestEnabled: b.dueDigestEnabled });
  };

  return (
    <SettingsCard title="Veresiye ve vade" description="Veresiye satışta vade tarihi bu kadar gün sonrası olarak önerilir; satışta değiştirilebilir.">
      <div className="flex flex-col gap-5">
        <div className="flex flex-wrap items-end gap-3">
          <Field label="Varsayılan vade (gün)" error={error ?? undefined} className="w-40">
            <NumberInput kind="integer" value={value} disabled={readOnly} onChange={(e) => setDays(e.target.value)} />
          </Field>
          {!readOnly && (
            <Button variant="outline" onClick={submitDays} disabled={save.isPending || days === null}>
              Vadeyi kaydet
            </Button>
          )}
        </div>
        <label className="flex min-h-14 items-center justify-between gap-4 border-t border-border pt-4">
          <span className="flex flex-col">
            <span className="text-sm font-semibold text-black">Otomatik vade bildirimi</span>
            <span className="text-sm text-neutral-600">
              {"Her sabah 09:00'da vadesi o gün gelen alacaklar için size ve yöneticilere bildirim gelir. Müşteriye otomatik SMS ileride eklenecek."}
            </span>
          </span>
          <Switch
            checked={b.dueDigestEnabled}
            disabled={readOnly || save.isPending}
            onCheckedChange={(on) => save.mutate({ defaultDueDays: b.defaultDueDays, dueDigestEnabled: on })}
          />
        </label>
        {error && days === null && <FormMessage>{error}</FormMessage>}
      </div>
    </SettingsCard>
  );
}

function CategoriesCard({ readOnly }: { readOnly: boolean }) {
  const categories = useExpenseCategories(true);
  const [adding, setAdding] = useState<ExpenseCategoryKind | null>(null);

  return (
    <SettingsCard title="Gelir-gider kategorileri" description="Kullanılan kategori silinmez; pasife alınır, eski kayıtlar korunur.">
      {categories.isPending ? (
        <p role="status" className="text-sm text-neutral-600">
          Yükleniyor…
        </p>
      ) : categories.isError ? (
        <QueryError error={categories.error} onRetry={categories.refetch} />
      ) : (
        <div className="grid gap-6 lg:grid-cols-2">
          {(
            [
              ["Expense", "Gider kategorileri"],
              ["Income", "Gelir kategorileri"],
            ] as const
          ).map(([kind, title]) => (
            <section key={kind} className="flex flex-col gap-2">
              <h3 className="text-sm font-semibold text-black">{title}</h3>
              <ul className="flex flex-col divide-y divide-border border-y border-border">
                {categories.data
                  .filter((c) => c.kind === kind)
                  .map((c) => (
                    <CategoryRow key={c.id} category={c} readOnly={readOnly} />
                  ))}
              </ul>
              {!readOnly &&
                (adding === kind ? (
                  <CategoryEditor kind={kind} onDone={() => setAdding(null)} />
                ) : (
                  <Button variant="ghost" className="self-start" onClick={() => setAdding(kind)}>
                    <Plus aria-hidden />
                    {kind === "Expense" ? "Gider kategorisi ekle" : "Gelir kategorisi ekle"}
                  </Button>
                ))}
            </section>
          ))}
        </div>
      )}
    </SettingsCard>
  );
}

function useCategoryRefresh() {
  const queryClient = useQueryClient();
  return () => queryClient.invalidateQueries({ queryKey: expenseCategoriesKey });
}

function CategoryRow({ category, readOnly }: { category: ExpenseCategory; readOnly: boolean }) {
  const refresh = useCategoryRefresh();
  const [editing, setEditing] = useState(false);
  const toggle = useMutation({
    mutationFn: () =>
      unwrap(
        category.isActive
          ? api.POST("/api/expense-categories/{id}/deactivate", { params: { path: { id: category.id } } })
          : api.POST("/api/expense-categories/{id}/activate", { params: { path: { id: category.id } } }),
      ),
    onSuccess: async () => {
      toast.success(category.isActive ? "Kategori pasife alındı." : "Kategori tekrar açıldı.");
      await refresh();
    },
    onError: (e) => toast.error(errorMessage(e)),
  });

  if (editing)
    return (
      <li className="py-2">
        <CategoryEditor kind={category.kind} category={category} onDone={() => setEditing(false)} />
      </li>
    );

  return (
    <li className="flex min-h-12 items-center gap-2 py-1">
      <span className="flex min-w-0 flex-1 flex-wrap items-center gap-2">
        <span className={`text-sm ${category.isActive ? "text-black" : "text-neutral-600"}`}>{category.name}</span>
        {category.isSalary && <Badge variant="outline">Sadece Sahip girer</Badge>}
        {!category.isActive && <Badge variant="outline">Pasif</Badge>}
      </span>
      {!readOnly && (
        <>
          <Button variant="ghost" size="icon" aria-label={`${category.name} adını değiştir`} onClick={() => setEditing(true)}>
            <Pencil aria-hidden />
          </Button>
          <Button variant="ghost" onClick={() => toggle.mutate()} disabled={toggle.isPending}>
            {category.isActive ? "Pasife al" : "Aç"}
          </Button>
        </>
      )}
    </li>
  );
}

function CategoryEditor({ kind, category, onDone }: { kind: ExpenseCategoryKind; category?: ExpenseCategory; onDone: () => void }) {
  // Telefonda pencere açılınca klavye açılıp düğmeleri örtmesin; masaüstünde ilk alana odaklanılır.
  const isDesktop = useIsDesktop();
  const refresh = useCategoryRefresh();
  const [name, setName] = useState(category?.name ?? "");
  const [error, setError] = useState<string | null>(null);
  const save = useMutation({
    mutationFn: () =>
      unwrap(
        category
          ? api.PUT("/api/expense-categories/{id}", { params: { path: { id: category.id } }, body: { name: name.trim(), kind } })
          : api.POST("/api/expense-categories", { body: { name: name.trim(), kind } }),
      ),
    onSuccess: async () => {
      toast.success(category ? "Kategori güncellendi." : "Kategori eklendi.");
      await refresh();
      onDone();
    },
    onError: (e) => setError(fieldErrors(e).name ?? errorMessage(e)),
  });

  return (
    <div className="flex flex-col gap-2">
      <Field label={category ? "Kategori adı" : "Yeni kategori adı"} error={error ?? undefined}>
        <Input value={name} onChange={(e) => setName(e.target.value)} maxLength={100} autoFocus={isDesktop} />
      </Field>
      <div className="flex gap-2">
        <Button onClick={() => (name.trim() ? save.mutate() : setError("Kategori adı girin."))} disabled={save.isPending}>
          Kategoriyi kaydet
        </Button>
        <Button variant="ghost" onClick={onDone}>
          Vazgeç
        </Button>
      </div>
    </div>
  );
}
