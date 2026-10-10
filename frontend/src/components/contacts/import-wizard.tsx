"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { cn } from "cn";
import { CheckCircle2, Download, FileSpreadsheet, Upload } from "lucide-react";
import Link from "next/link";
import { useRef, useState } from "react";

import { Notice, selectClass } from "@/components/contacts/contact-bits";
import { contactsKey } from "@/components/contacts/contact-list";
import { FormMessage } from "@/components/forms/field";
import { Button } from "@/components/ui/button";
import { API_URL, api, errorMessage, unwrap, type ProblemDetails } from "@/lib/api/client";
import type { ImportField, ImportRow } from "@/lib/api/types";

type Upload = {
  jobId: string;
  fileName: string;
  headers: string[];
  rowCount: number;
  fields: ImportField[];
  mapping: Record<string, number>;
};
type Preview = { total: number; ok: number; errors: number; duplicates: number; rows: ImportRow[] };
type Result = { added: number; duplicates: number; errors: number; warnings: number };

const steps = ["Dosya", "Sütunlar", "Önizleme", "Sonuç"] as const;

/** Excel'den müşteri aktarma: şablon → yükle → sütun eşle → önizle (hatalı satırlar nedeniyle) → kaydet → rapor. */
export function ImportWizard() {
  const queryClient = useQueryClient();
  const [step, setStep] = useState(0);
  const [upload, setUpload] = useState<Upload | null>(null);
  const [mapping, setMapping] = useState<Record<string, number>>({});
  const [preview, setPreview] = useState<Preview | null>(null);
  const [result, setResult] = useState<Result | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [filter, setFilter] = useState<"all" | "Error" | "Duplicate">("all");
  const fileRef = useRef<HTMLInputElement>(null);

  const onFile = async (file: File | undefined) => {
    if (!file) return;
    setError(null);
    setBusy(true);
    try {
      const body = new FormData();
      body.append("file", file);
      const response = await fetch(`${API_URL}/api/contacts/import`, { method: "POST", body, credentials: "include" });
      const data = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error((data as ProblemDetails).detail ?? "Dosya yüklenemedi.");
      setUpload(data as Upload);
      setMapping((data as Upload).mapping);
      setStep(1);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Dosya yüklenemedi.");
    } finally {
      setBusy(false);
      if (fileRef.current) fileRef.current.value = "";
    }
  };

  const runPreview = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST("/api/contacts/import/{jobId}/preview", {
          params: { path: { jobId: upload!.jobId }, query: { pageSize: 200 } },
          body: { mapping },
        }),
      ),
    onSuccess: (p) => {
      setPreview(p);
      setFilter(p.errors > 0 ? "Error" : "all");
      setStep(2);
      setError(null);
    },
    onError: (e) => setError(errorMessage(e)),
  });

  const commit = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST("/api/contacts/import/{jobId}/commit", {
          params: { path: { jobId: upload!.jobId } },
          body: { mapping },
        }),
      ),
    onSuccess: async (r) => {
      setResult(r);
      setStep(3);
      setError(null);
      await queryClient.invalidateQueries({ queryKey: contactsKey });
      await queryClient.invalidateQueries({ queryKey: ["tags"] });
    },
    onError: (e) => setError(errorMessage(e)),
  });

  const nameMapped = mapping.firstName !== undefined || mapping.companyName !== undefined;
  const usedColumns = new Set(Object.values(mapping));

  return (
    <div className="flex max-w-3xl flex-col gap-6">
      <ol className="flex gap-2 text-xs font-semibold" aria-label="Adımlar">
        {steps.map((s, i) => (
          <li
            key={s}
            aria-current={i === step ? "step" : undefined}
            className={cn(
              "flex-1 border-t-2 pt-2",
              i <= step ? "border-brand text-black" : "border-neutral-200 text-neutral-600",
            )}
          >
            {i + 1}. {s}
          </li>
        ))}
      </ol>

      {error && <FormMessage>{error}</FormMessage>}

      {step === 0 && (
        <section className="flex flex-col gap-5">
          <div className="flex flex-col gap-2">
            <h2 className="text-lg font-semibold text-black">1. Şablonu indirin ve doldurun</h2>
            <p className="text-sm text-neutral-700">
              Kendi listeniz de olur; bir sonraki adımda sütunları eşlersiniz. En fazla 5000 satır. Aynı telefon zaten
              kayıtlıysa o satır atlanır.
            </p>
            <Button asChild variant="outline" className="self-start">
              <a href={`${API_URL}/api/contacts/import/template.xlsx`} download>
                <Download aria-hidden />
                Şablonu indir (.xlsx)
              </a>
            </Button>
          </div>
          <div className="flex flex-col gap-2">
            <h2 className="text-lg font-semibold text-black">2. Dosyayı yükleyin</h2>
            <label
              className={cn(
                "flex min-h-32 cursor-pointer flex-col items-center justify-center gap-2 border border-dashed border-neutral-400 px-4 text-center hover:border-black",
                busy && "pointer-events-none opacity-60",
              )}
            >
              <Upload aria-hidden className="size-8 text-black" />
              <span className="text-sm font-semibold text-black">{busy ? "Yükleniyor…" : "Excel dosyası seçin"}</span>
              <span className="text-xs text-neutral-600">.xlsx, en fazla 5 MB</span>
              <input
                ref={fileRef}
                type="file"
                accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
                className="sr-only"
                onChange={(e) => onFile(e.target.files?.[0])}
              />
            </label>
          </div>
        </section>
      )}

      {step === 1 && upload && (
        <section className="flex flex-col gap-4">
          <div className="flex items-center gap-2 text-sm text-black">
            <FileSpreadsheet aria-hidden className="size-5" />
            <span className="font-semibold">{upload.fileName}</span>
            <span className="text-neutral-600">· {upload.rowCount} satır</span>
          </div>
          <p className="text-sm text-neutral-700">
            Her alanın dosyanızdaki hangi sütundan alınacağını seçin. Başlıklara göre önerdik.
          </p>
          <ul className="flex flex-col divide-y divide-border border-y border-border">
            {upload.fields.map((f) => (
              <li key={f.key} className="grid grid-cols-[8rem_1fr] items-center gap-3 py-2 sm:grid-cols-[12rem_1fr]">
                <label htmlFor={`map-${f.key}`} className="text-sm font-semibold text-black">
                  {f.label}
                </label>
                <select
                  id={`map-${f.key}`}
                  className={selectClass}
                  value={mapping[f.key] ?? ""}
                  onChange={(e) => {
                    const next = { ...mapping };
                    if (e.target.value === "") delete next[f.key];
                    else next[f.key] = Number(e.target.value);
                    setMapping(next);
                  }}
                >
                  <option value="">— Aktarılmasın —</option>
                  {upload.headers.map((h, i) => (
                    <option key={i} value={i} disabled={usedColumns.has(i) && mapping[f.key] !== i}>
                      {h || `${i + 1}. sütun`}
                    </option>
                  ))}
                </select>
              </li>
            ))}
          </ul>
          {!nameMapped && <Notice>Ad veya Firma alanını bir sütunla eşleyin.</Notice>}
          <div className="flex gap-2">
            <Button variant="ghost" onClick={() => setStep(0)}>
              Geri
            </Button>
            <Button onClick={() => runPreview.mutate()} disabled={!nameMapped || runPreview.isPending}>
              {runPreview.isPending ? "Kontrol ediliyor…" : "Önizle"}
            </Button>
          </div>
        </section>
      )}

      {step === 2 && preview && (
        <section className="flex flex-col gap-4">
          <dl className="grid grid-cols-2 gap-3 sm:grid-cols-4">
            {(
              [
                ["Toplam", preview.total],
                ["Aktarılacak", preview.ok],
                ["Hatalı", preview.errors],
                ["Tekrar (atlanacak)", preview.duplicates],
              ] as const
            ).map(([label, value]) => (
              <div key={label} className="border-t-2 border-black pt-2">
                <dt className="text-xs text-neutral-600">{label}</dt>
                <dd className="text-2xl font-bold text-black tabular-nums">{value}</dd>
              </div>
            ))}
          </dl>

          {(preview.errors > 0 || preview.duplicates > 0) && (
            <Notice>
              Hatalı ve tekrar eden satırlar aktarılmaz; düzeltmek isterseniz dosyada düzeltip yeniden yükleyin. Hazır
              satırları şimdi aktarabilirsiniz.
            </Notice>
          )}

          <div className="flex gap-2" role="group" aria-label="Satır süzgeci">
            {(
              [
                ["all", "Hepsi"],
                ["Error", `Hatalı (${preview.errors})`],
                ["Duplicate", `Tekrar (${preview.duplicates})`],
              ] as const
            ).map(([key, label]) => (
              <button
                key={key}
                type="button"
                aria-pressed={filter === key}
                onClick={() => setFilter(key)}
                className={cn(
                  "min-h-11 rounded-full border px-4 text-sm font-medium",
                  filter === key ? "border-black bg-black text-white" : "border-border text-black hover:border-black",
                )}
              >
                {label}
              </button>
            ))}
          </div>

          <ul className="flex max-h-[28rem] flex-col divide-y divide-border overflow-y-auto border-y border-border">
            {preview.rows
              .filter((r) => filter === "all" || r.status === filter)
              .map((r) => (
                <li key={r.rowNumber} className="flex gap-3 py-2">
                  <span className="w-12 shrink-0 text-xs text-neutral-600 tabular-nums">{r.rowNumber}. satır</span>
                  <div className="flex min-w-0 flex-1 flex-col">
                    <span className="text-sm font-semibold text-black">{r.name ?? "(adsız)"}</span>
                    {r.messages.map((m) => (
                      <span
                        key={m}
                        className={cn("text-xs", r.status === "Error" ? "text-danger-600" : "text-neutral-700")}
                      >
                        {m}
                      </span>
                    ))}
                  </div>
                  <span className="shrink-0 text-xs font-semibold text-black">
                    {r.status === "Ok" ? "✓ Hazır" : r.status === "Error" ? "Hatalı" : "Atlanacak"}
                  </span>
                </li>
              ))}
          </ul>
          {preview.rows.length < preview.total && filter === "all" && (
            <p className="text-xs text-neutral-600">İlk {preview.rows.length} satır gösteriliyor (önce hatalılar).</p>
          )}

          <div className="flex gap-2">
            <Button variant="ghost" onClick={() => setStep(1)}>
              Geri
            </Button>
            <Button onClick={() => commit.mutate()} disabled={preview.ok === 0 || commit.isPending}>
              {commit.isPending ? "Aktarılıyor…" : `${preview.ok} kaydı aktar`}
            </Button>
          </div>
        </section>
      )}

      {step === 3 && result && upload && (
        <section className="flex flex-col gap-4">
          <p className="flex items-center gap-2 text-lg font-semibold text-black">
            <CheckCircle2 aria-hidden className="size-6 text-brand" />
            {result.added} kayıt eklendi.
          </p>
          <ul className="text-sm text-neutral-700">
            {result.duplicates > 0 && <li>{result.duplicates} satır aynı telefon nedeniyle atlandı.</li>}
            {result.errors > 0 && <li>{result.errors} satır hatalı olduğu için aktarılmadı.</li>}
            {result.warnings > 0 && <li>{result.warnings} satırda uyarı var (ör. açılış bakiyesi aktarılmadı).</li>}
          </ul>
          <div className="flex flex-wrap gap-2">
            <Button asChild>
              <Link href="/panel/musteriler">Müşterilere git</Link>
            </Button>
            <Button asChild variant="outline">
              <a href={`${API_URL}/api/contacts/import/${upload.jobId}/report.xlsx`} download>
                <Download aria-hidden />
                Raporu indir
              </a>
            </Button>
          </div>
        </section>
      )}
    </div>
  );
}
