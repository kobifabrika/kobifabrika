"use client";

import { useQuery } from "@tanstack/react-query";
import { Download } from "lucide-react";
import { useParams } from "next/navigation";
import { useEffect } from "react";

import { PublicMessage, PublicShell } from "@/components/public/public-shell";
import { Button } from "@/components/ui/button";
import { API_URL, ApiError, api, unwrap } from "@/lib/api/client";
import { formatDayShort, formatMoney } from "@/lib/format";

/** Ekstre müşteriye gider; bakiye onun bakışıyla: pozitif = borcu var. */
export function customerBalance(value: number) {
  if (value > 0) return `Borcunuz ${formatMoney(value)}`;
  if (value < 0) return `Alacağınız ${formatMoney(-value)}`;
  return "Borcunuz yok";
}

/** Ekstre usulü bakiye: borç bakiyesi (B), alacak bakiyesi (A). */
function signed(value: number) {
  return `${formatMoney(Math.abs(value))}${value > 0 ? " (B)" : value < 0 ? " (A)" : ""}`;
}

export function StatementView() {
  const { token } = useParams<{ token: string }>();
  const statement = useQuery({
    queryKey: ["public", "statement", token],
    queryFn: () => unwrap(api.GET("/api/public/statements/{token}", { params: { path: { token } } })),
    retry: false,
  });

  useEffect(() => {
    if (statement.data) document.title = `${statement.data.businessName} · Cari hesap ekstresi`;
  }, [statement.data]);

  if (statement.isPending) {
    return (
      <PublicShell>
        <p role="status" className="py-10 text-sm text-neutral-600">
          Yükleniyor…
        </p>
      </PublicShell>
    );
  }

  if (statement.isError) {
    const gone = statement.error instanceof ApiError && statement.error.status === 410;
    return (
      <PublicShell>
        <PublicMessage title={gone ? "Bu bağlantının süresi doldu" : "Ekstre bulunamadı"}>
          {gone
            ? "Güncel ekstre için işletmeden yeni bir bağlantı isteyin."
            : "Bağlantıyı kontrol edin veya işletmeden yenisini isteyin."}
        </PublicMessage>
      </PublicShell>
    );
  }

  const s = statement.data;
  const range = s.from
    ? `${formatDayShort(s.from)} – ${formatDayShort(s.to)}`
    : `${formatDayShort(s.to)} tarihine kadar`;
  const rows = [...s.items].reverse();

  return (
    <PublicShell businessName={s.businessName} logoUrl={s.logoUrl} phone={s.businessPhone}>
      <section className="flex flex-col gap-1">
        <h1 className="text-2xl font-bold tracking-tight text-black">Cari hesap ekstresi</h1>
        <p className="text-base text-neutral-700">
          Sayın <span className="font-semibold text-black">{s.contactName}</span> · {range}
        </p>
      </section>

      <section className="flex flex-col gap-1 border-l-2 border-brand bg-neutral-50 px-4 py-4">
        <span className="text-sm text-neutral-700">Son bakiye</span>
        <span className="text-3xl font-bold text-black tabular-nums">{customerBalance(s.closingBalance)}</span>
      </section>

      <Button asChild variant="outline" className="self-start">
        <a href={`${API_URL}/api/public/statements/${token}/pdf`} target="_blank" rel="noopener noreferrer">
          <Download aria-hidden />
          PDF indir
        </a>
      </Button>

      <section className="flex flex-col gap-2">
        <h2 className="text-lg font-semibold text-black">Hareketler</h2>
        {rows.length === 0 ? (
          <p className="text-sm text-neutral-600">Bu dönemde hareket yok.</p>
        ) : (
          <ul className="flex flex-col divide-y divide-border border-y border-border">
            {rows.map((r, i) => (
              <li key={i} className="flex items-start gap-3 py-3">
                <div className="flex min-w-0 flex-1 flex-col">
                  <span className="text-sm font-semibold text-black">{r.description}</span>
                  <span className="text-xs text-neutral-600">{formatDayShort(r.date)}</span>
                </div>
                <div className="flex flex-col items-end text-right tabular-nums">
                  <span className="text-sm font-semibold text-black">
                    {r.debit > 0 ? `Borç ${formatMoney(r.debit)}` : `Alacak ${formatMoney(r.credit)}`}
                  </span>
                  <span className="text-xs text-neutral-600">Bakiye {signed(r.balance)}</span>
                </div>
              </li>
            ))}
            {s.from && (
              <li className="flex justify-between py-3 text-sm text-neutral-700">
                <span>Devreden bakiye</span>
                <span className="tabular-nums">{signed(s.carriedBalance)}</span>
              </li>
            )}
          </ul>
        )}
        <p className="text-xs text-neutral-600">
          (B): borç bakiyesi, (A): alacak bakiyesi. Sorunuz varsa doğrudan işletmeyle iletişime geçin.
        </p>
      </section>
    </PublicShell>
  );
}
