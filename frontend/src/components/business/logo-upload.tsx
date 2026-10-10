"use client";

import { ImageUp, Trash2 } from "lucide-react";
import { useRef, useState } from "react";

import { FormMessage } from "@/components/forms/field";
import { Button } from "@/components/ui/button";
import { API_URL, ApiError, fileUrl, type ProblemDetails } from "@/lib/api/client";

const MAX_BYTES = 2 * 1024 * 1024;
const ACCEPT = "image/png,image/jpeg,image/webp";

type Props = {
  logoUrl: string | null;
  /** "/api/setup/logo" veya "/api/business/logo" */
  endpoint: "/api/setup/logo" | "/api/business/logo";
  onChange: (logoUrl: string | null) => void;
  disabled?: boolean;
};

/** Logo yükleme (PNG/JPG/WebP, en fazla 2 MB). Dosya seçilince hemen yüklenir. */
export function LogoUpload({ logoUrl, endpoint, onChange, disabled }: Props) {
  const input = useRef<HTMLInputElement>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const upload = async (file: File) => {
    setError(null);
    if (file.size > MAX_BYTES) return setError("Logo en fazla 2 MB olabilir.");
    if (!ACCEPT.split(",").includes(file.type)) return setError("Logo PNG, JPG veya WebP olmalı.");

    setBusy(true);
    try {
      const body = new FormData();
      body.append("file", file);
      // multipart gövde openapi-fetch tiplerinde yok; doğrudan fetch.
      const response = await fetch(`${API_URL}${endpoint}`, { method: "POST", body, credentials: "include" });
      const data = (await response.json()) as { logoUrl?: string } & ProblemDetails;
      if (!response.ok) throw new ApiError(response.status, data);
      onChange(data.logoUrl ?? null);
    } catch (e) {
      setError(e instanceof ApiError ? (e.problem.errors?.file?.[0] ?? e.message) : "Logo yüklenemedi. Tekrar deneyin.");
    } finally {
      setBusy(false);
      if (input.current) input.current.value = "";
    }
  };

  const remove = async () => {
    setBusy(true);
    setError(null);
    try {
      const response = await fetch(`${API_URL}${endpoint}`, { method: "DELETE", credentials: "include" });
      if (!response.ok) throw new Error();
      onChange(null);
    } catch {
      setError("Logo kaldırılamadı. Tekrar deneyin.");
    } finally {
      setBusy(false);
    }
  };

  const src = fileUrl(logoUrl);

  return (
    <div className="flex flex-col gap-3">
      <div className="flex items-center gap-4">
        <div className="flex size-24 shrink-0 items-center justify-center overflow-hidden rounded-lg border border-border bg-neutral-50">
          {src ? (
            // eslint-disable-next-line @next/next/no-img-element -- API'den gelen kullanıcı dosyası; next/image optimizasyonu gerekmez.
            <img src={src} alt="İşletme logosu" className="size-full object-contain" />
          ) : (
            <ImageUp aria-hidden className="size-8 text-neutral-400" />
          )}
        </div>
        <div className="flex flex-col gap-2">
          <input
            ref={input}
            type="file"
            accept={ACCEPT}
            className="sr-only"
            id="logo-file"
            disabled={disabled || busy}
            onChange={(e) => e.target.files?.[0] && upload(e.target.files[0])}
          />
          <Button type="button" variant="outline" disabled={disabled || busy} onClick={() => input.current?.click()}>
            {busy ? "Yükleniyor…" : src ? "Logoyu değiştir" : "Logo seç"}
          </Button>
          {src && !disabled && (
            <Button type="button" variant="ghost" className="text-danger-600" disabled={busy} onClick={remove}>
              <Trash2 aria-hidden className="size-4" /> Kaldır
            </Button>
          )}
        </div>
      </div>
      <p className="text-xs text-neutral-600">PNG, JPG veya WebP; en fazla 2 MB. Kare logo en iyi görünür.</p>
      {error && <FormMessage>{error}</FormMessage>}
    </div>
  );
}
