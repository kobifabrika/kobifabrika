"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { cn } from "cn";
import { CalendarPlus, Check, ChevronLeft, ChevronRight, Clock, UserRound } from "lucide-react";
import Link from "next/link";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useEffect, useMemo, useRef, useState, type FormEvent, type ReactNode } from "react";
import { toast } from "sonner";

import { PublicMessage } from "@/components/public/public-shell";
import { Field, FormMessage } from "@/components/forms/field";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { ApiError, api, errorMessage, fieldErrors, problemCode, unwrap } from "@/lib/api/client";
import type { PublicBooking, PublicBookingCreated, PublicBookingService, PublicBookingStaff } from "@/lib/api/types";
import {
  buildIcs,
  groupSlots,
  isBookingStep,
  isMobile,
  slotToUtc,
  stripBounds,
  stripLabel,
  weekFrom,
  type BookingStep,
} from "@/lib/booking";
import { longDayLabel } from "@/lib/calendar";
import { addDays } from "@/lib/date-ranges";
import { formatMoney, istanbulParts } from "@/lib/format";

import { BookingShell } from "./booking-shell";

type Choice = {
  service?: PublicBookingService;
  /** null: "Fark etmez". */
  staff?: PublicBookingStaff | null;
  date?: string;
  time?: string;
};

type Person = {
  firstName: string;
  lastName: string;
  phone: string;
  email: string;
  marketing: boolean;
  website: string;
};

const stepTitles: Record<BookingStep, string> = {
  hizmet: "Hizmet seçin",
  personel: "Personel seçin",
  saat: "Gün ve saat seçin",
  bilgiler: "Bilgileriniz",
  kod: "Telefon doğrulama",
  onay: "Randevunuz alındı",
};

/** SMS kapalıyken (pilot) kod adımı yok: randevu talebi doğrudan gönderilir, işletme onaylar. */
const numberedSteps = (verify: boolean): BookingStep[] =>
  verify ? ["hizmet", "personel", "saat", "bilgiler", "kod"] : ["hizmet", "personel", "saat", "bilgiler"];

export const bookingKey = (slug: string) => ["public", "booking", slug] as const;

/**
 * Online randevu (/r/[slug]): hizmet → personel ("Fark etmez" ilk) → gün/saat → bilgiler → SMS kodu → onay. SMS kapalıyken
 * (verificationRequired false) kod adımı yok: bilgilerden sonra talep gönderilir, durum her zaman "onay bekliyor". Adım ?adim= ile
 * adreste; tarayıcının geri tuşu adımlar arasında çalışır. Seçimler sayfada tutulur; sayfa yenilenirse baştan başlar.
 */
export function BookingFlow({ slug }: { slug: string }) {
  const queryClient = useQueryClient();
  const router = useRouter();
  const pathname = usePathname();
  const params = useSearchParams();
  const page = useQuery({
    queryKey: bookingKey(slug),
    queryFn: () => unwrap(api.GET("/api/public/booking/{slug}", { params: { path: { slug } } })),
    retry: false,
  });
  const [choice, setChoice] = useState<Choice>({});
  const [person, setPerson] = useState<Person>({
    firstName: "",
    lastName: "",
    phone: "",
    email: "",
    marketing: false,
    website: "",
  });
  // Bot tuzağı: sayfa açıldıktan 3 saniyeden kısa sürede gelen gönderim sunucuda kayıt açmaz.
  const openedAt = useRef<number | null>(null);
  useEffect(() => {
    openedAt.current = Date.now();
  }, []);
  const [verification, setVerification] = useState<{ id: string; sentAt: number } | null>(null);
  const [created, setCreated] = useState<PublicBookingCreated | null>(null);

  const requested = params.get("adim");
  const wanted: BookingStep = isBookingStep(requested) ? requested : "hizmet";
  // Eksik seçimle ileri adıma gelinirse (sayfa yenilendi) ilk eksik adıma dönülür.
  const step: BookingStep =
    wanted === "onay" && !created
      ? "hizmet"
      : wanted !== "hizmet" && !choice.service
        ? "hizmet"
        : ["saat", "bilgiler", "kod"].includes(wanted) && choice.staff === undefined
          ? "personel"
          : ["bilgiler", "kod"].includes(wanted) && !choice.time
            ? "saat"
            : wanted === "kod" && !verification
              ? "bilgiler"
              : wanted;

  const go = (next: BookingStep, replace = false) => {
    const url = next === "hizmet" ? pathname : `${pathname}?adim=${next}`;
    if (replace) router.replace(url, { scroll: false });
    else router.push(url, { scroll: false });
    window.scrollTo({ top: 0 });
  };

  useEffect(() => {
    if (page.data) document.title = `${page.data.name} · Online randevu`;
  }, [page.data]);

  if (page.isPending)
    return (
      <BookingShell>
        <p role="status" className="py-10 text-sm text-neutral-600">
          Yükleniyor…
        </p>
      </BookingShell>
    );

  if (page.isError) {
    const missing = page.error instanceof ApiError && page.error.status === 404;
    return (
      <BookingShell>
        <PublicMessage title={missing ? "Online randevu sayfası bulunamadı" : "Sayfa açılamadı"}>
          {missing
            ? "İşletme online randevuyu kapatmış olabilir. Randevu için işletmeyi arayın."
            : errorMessage(page.error)}
        </PublicMessage>
      </BookingShell>
    );
  }

  const b = page.data;
  if (!b.available)
    return (
      <BookingShell
        name={b.name}
        logoUrl={b.logoUrl}
        address={[b.address, b.district, b.city]}
        phone={b.phone}
        phoneE164={b.phoneE164}
      >
        <PublicMessage title="Şu an online randevu alınmıyor">
          {b.phone ? `Randevu için işletmeyi arayın: ${b.phone}` : "Randevu için işletmeyle iletişime geçin."}
        </PublicMessage>
      </BookingShell>
    );
  const numbered = numberedSteps(b.verificationRequired);
  const index = numbered.indexOf(step);
  const summary = <Summary business={b} choice={choice} />;

  return (
    <BookingShell
      name={b.name}
      logoUrl={b.logoUrl}
      address={[b.address, b.district, b.city]}
      phone={b.phone}
      phoneE164={b.phoneE164}
      aside={
        step !== "onay" && choice.service ? (
          <div className="sticky top-6 border border-border p-4">{summary}</div>
        ) : undefined
      }
    >
      <div className="flex flex-col gap-1">
        {index >= 0 && (
          <p className="text-sm text-neutral-600">
            Adım {index + 1}/{numbered.length}
          </p>
        )}
        <h1 className="text-xl font-bold text-black">{stepTitles[step]}</h1>
      </div>

      {index > 0 && (
        <button
          type="button"
          onClick={() => router.back()}
          className="flex min-h-11 w-fit items-center gap-1 text-sm font-semibold text-neutral-700 hover:text-black"
        >
          <ChevronLeft aria-hidden className="size-4" />
          Geri
        </button>
      )}

      {step !== "hizmet" && step !== "onay" && choice.service && (
        <div className="border border-border p-3 lg:hidden">{summary}</div>
      )}

      {step === "hizmet" && (
        <ServiceStep
          business={b}
          onPick={(service) => {
            setChoice({ service });
            go("personel");
          }}
        />
      )}
      {step === "personel" && (
        <StaffStep
          staff={b.staff.filter((s) => s.serviceIds.includes(choice.service!.id))}
          onPick={(staff) => {
            setChoice((c) => ({ ...c, staff, date: undefined, time: undefined }));
            go("saat");
          }}
        />
      )}
      {step === "saat" && (
        <TimeStep
          slug={slug}
          business={b}
          choice={choice}
          onPick={(date, time) => {
            setChoice((c) => ({ ...c, date, time }));
            go(verification ? "kod" : "bilgiler");
          }}
        />
      )}
      {step === "bilgiler" && b.verificationRequired && (
        <PersonStep
          slug={slug}
          business={b}
          value={person}
          onChange={setPerson}
          onSent={(id) => {
            setVerification({ id, sentAt: Date.now() });
            go("kod");
          }}
        />
      )}
      {step === "bilgiler" && !b.verificationRequired && (
        <RequestStep
          slug={slug}
          business={b}
          choice={choice}
          value={person}
          onChange={setPerson}
          elapsedSeconds={() => (openedAt.current ? Math.floor((Date.now() - openedAt.current) / 1000) : 0)}
          onSlotTaken={() => {
            void queryClient.invalidateQueries({ queryKey: ["public", "booking", slug, "slots"] });
            setChoice((c) => ({ ...c, time: undefined }));
            go("saat", true);
          }}
          onDone={(result) => {
            setCreated(result);
            go("onay", true);
          }}
        />
      )}
      {step === "kod" && verification && (
        <CodeStep
          slug={slug}
          choice={choice}
          person={person}
          verification={verification}
          onResent={(id) => setVerification({ id, sentAt: Date.now() })}
          onSlotTaken={() => {
            // Dolan saat listede kalmasın: boş saatler önbellekten değil sunucudan yeniden gelir (staleTime 30 sn).
            void queryClient.invalidateQueries({ queryKey: ["public", "booking", slug, "slots"] });
            setChoice((c) => ({ ...c, time: undefined }));
            go("saat", true);
          }}
          onNewCode={() => {
            setVerification(null);
            go("bilgiler", true);
          }}
          onDone={(result) => {
            setCreated(result);
            setVerification(null);
            go("onay", true);
          }}
        />
      )}
      {step === "onay" && created && (
        <Confirmation slug={slug} business={b} created={created} emailGiven={!!person.email.trim()} />
      )}
    </BookingShell>
  );
}

function Summary({ choice }: { business: PublicBooking; choice: Choice }) {
  if (!choice.service) return null;
  return (
    <dl className="flex flex-col gap-2 text-sm">
      <div className="flex flex-col">
        <dt className="text-xs text-neutral-600">Hizmet</dt>
        <dd className="font-semibold text-black">
          {choice.service.name} · {choice.service.durationMinutes} dk
          {choice.service.price != null && (
            <span className="font-normal text-neutral-700"> · {formatMoney(choice.service.price)}</span>
          )}
        </dd>
      </div>
      {choice.staff !== undefined && (
        <div className="flex flex-col">
          <dt className="text-xs text-neutral-600">Personel</dt>
          <dd className="font-semibold text-black">{choice.staff?.fullName ?? "Fark etmez"}</dd>
        </div>
      )}
      {choice.date && choice.time && (
        <div className="flex flex-col">
          <dt className="text-xs text-neutral-600">Gün ve saat</dt>
          <dd className="font-semibold text-black tabular-nums">
            {longDayLabel(choice.date)} · {choice.time}
          </dd>
        </div>
      )}
    </dl>
  );
}

function OptionButton({
  children,
  onClick,
  selected,
}: {
  children: ReactNode;
  onClick: () => void;
  selected?: boolean;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        "flex min-h-14 w-full items-center justify-between gap-3 rounded-lg border px-4 py-3 text-left",
        selected ? "border-black bg-neutral-50" : "border-border bg-white hover:border-black",
      )}
    >
      {children}
    </button>
  );
}

function ServiceStep({ business, onPick }: { business: PublicBooking; onPick: (s: PublicBookingService) => void }) {
  if (business.services.length === 0)
    return (
      <PublicMessage title="Şu anda online randevuya açık hizmet yok">Randevu için işletmeyi arayın.</PublicMessage>
    );
  return (
    <ul className="flex flex-col gap-2" aria-label="Hizmetler">
      {business.services.map((s) => (
        <li key={s.id}>
          <OptionButton onClick={() => onPick(s)}>
            <span className="flex min-w-0 flex-col">
              <span className="text-base font-semibold text-black">{s.name}</span>
              <span className="flex items-center gap-1 text-sm text-neutral-600">
                <Clock aria-hidden className="size-4" />
                {s.durationMinutes} dk
              </span>
            </span>
            {s.price != null && (
              <span className="shrink-0 text-base font-semibold text-black tabular-nums">{formatMoney(s.price)}</span>
            )}
          </OptionButton>
        </li>
      ))}
    </ul>
  );
}

function StaffStep({ staff, onPick }: { staff: PublicBookingStaff[]; onPick: (s: PublicBookingStaff | null) => void }) {
  return (
    <ul className="flex flex-col gap-2" aria-label="Personel">
      <li>
        <OptionButton onClick={() => onPick(null)}>
          <span className="flex flex-col">
            <span className="text-base font-semibold text-black">Fark etmez</span>
            <span className="text-sm text-neutral-600">Seçtiğiniz saatte boş olan personel</span>
          </span>
        </OptionButton>
      </li>
      {staff.map((s) => (
        <li key={s.id}>
          <OptionButton onClick={() => onPick(s)}>
            <span className="flex items-center gap-3">
              <UserRound aria-hidden className="size-5 text-neutral-600" />
              <span className="flex flex-col">
                <span className="text-base font-semibold text-black">{s.fullName}</span>
                {s.title && <span className="text-sm text-neutral-600">{s.title}</span>}
              </span>
            </span>
          </OptionButton>
        </li>
      ))}
    </ul>
  );
}

function TimeStep({
  slug,
  business,
  choice,
  onPick,
}: {
  slug: string;
  business: PublicBooking;
  choice: Choice;
  onPick: (date: string, time: string) => void;
}) {
  const [from, setFrom] = useState(choice.date && choice.date >= business.today ? choice.date : business.today);
  const [day, setDay] = useState<string | null>(choice.date ?? null);
  const slots = useQuery({
    queryKey: ["public", "booking", slug, "slots", choice.service!.id, choice.staff?.id ?? null, from],
    queryFn: () =>
      unwrap(
        api.GET("/api/public/booking/{slug}/slots", {
          params: {
            path: { slug },
            query: { serviceId: choice.service!.id, staffId: choice.staff?.id, from, days: 7 },
          },
        }),
      ),
    retry: false,
  });
  const days = useMemo(
    () => slots.data?.days ?? weekFrom(from).map((date) => ({ date, times: [] as string[] })),
    [slots.data, from],
  );
  const firstFree = days.find((d) => d.times.length > 0)?.date ?? null;
  const selected = day && days.some((d) => d.date === day) ? day : firstFree;
  const times = days.find((d) => d.date === selected)?.times ?? [];
  const bounds = stripBounds(from, business.today, business.lastDay);
  const shift = (n: number) => {
    setFrom(addDays(from, n * 7));
    setDay(null);
  };

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center gap-2">
        <Button
          variant="outline"
          size="icon"
          aria-label="Önceki hafta"
          disabled={!bounds.prev}
          onClick={() => shift(-1)}
        >
          <ChevronLeft aria-hidden />
        </Button>
        <div role="radiogroup" aria-label="Gün" className="grid flex-1 grid-cols-7 gap-1">
          {days.map((d) => {
            const label = stripLabel(d.date);
            const empty = d.times.length === 0;
            return (
              <button
                key={d.date}
                type="button"
                role="radio"
                aria-checked={selected === d.date}
                aria-label={`${longDayLabel(d.date)}${empty ? ", boş saat yok" : ""}`}
                disabled={empty}
                onClick={() => setDay(d.date)}
                className={cn(
                  "flex min-h-14 flex-col items-center justify-center rounded-lg border text-sm",
                  selected === d.date ? "border-link bg-link text-white" : "border-border bg-white text-black",
                  empty && "border-dashed text-neutral-400",
                )}
              >
                <span className="text-xs">{label.weekday}</span>
                <span className="font-semibold tabular-nums">{label.day}</span>
              </button>
            );
          })}
        </div>
        <Button
          variant="outline"
          size="icon"
          aria-label="Sonraki hafta"
          disabled={!bounds.next}
          onClick={() => shift(1)}
        >
          <ChevronRight aria-hidden />
        </Button>
      </div>

      {slots.isPending ? (
        <p role="status" className="text-sm text-neutral-600">
          Boş saatler yükleniyor…
        </p>
      ) : slots.isError ? (
        <FormMessage>{errorMessage(slots.error)}</FormMessage>
      ) : !firstFree ? (
        <div className="flex flex-col items-start gap-3 border border-dashed border-neutral-300 px-4 py-6">
          <p className="text-base font-semibold text-black">Bu hafta boş saat yok</p>
          {bounds.next ? (
            <Button variant="outline" onClick={() => shift(1)}>
              Sonraki hafta
              <ChevronRight aria-hidden />
            </Button>
          ) : (
            <p className="text-sm text-neutral-600">Randevu için işletmeyi arayın.</p>
          )}
        </div>
      ) : (
        <div className="flex flex-col gap-4">
          <p className="text-base font-semibold text-black">{longDayLabel(selected!)}</p>
          {groupSlots(times).map((g) => (
            <section key={g.label} className="flex flex-col gap-2" aria-label={g.label}>
              <h2 className="text-sm font-semibold text-neutral-700">{g.label}</h2>
              <div className="grid grid-cols-3 gap-2 sm:grid-cols-4">
                {g.times.map((t) => (
                  <button
                    key={t}
                    type="button"
                    onClick={() => onPick(selected!, t)}
                    className={cn(
                      "min-h-11 rounded-lg border text-base font-semibold tabular-nums",
                      choice.date === selected && choice.time === t
                        ? "border-link bg-link text-white"
                        : "border-border bg-white text-black hover:border-black",
                    )}
                  >
                    {t}
                  </button>
                ))}
              </div>
            </section>
          ))}
        </div>
      )}
    </div>
  );
}

function PersonStep({
  slug,
  business,
  value,
  onChange,
  onSent,
}: {
  slug: string;
  business: PublicBooking;
  value: Person;
  onChange: (p: Person) => void;
  onSent: (verificationId: string) => void;
}) {
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [error, setError] = useState<string | null>(null);
  const send = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST("/api/public/booking/{slug}/verifications", {
          params: { path: { slug } },
          body: { phone: value.phone, firstName: value.firstName },
        }),
      ),
    onSuccess: (r) => onSent(r.verificationId),
    onError: (e) => {
      const fields = fieldErrors(e);
      setErrors(fields);
      setError(Object.keys(fields).length ? null : codeError(e, business));
    },
  });
  const submit = (e: FormEvent) => {
    e.preventDefault();
    const next: Record<string, string> = {};
    if (!value.firstName.trim()) next.firstName = "Adınızı girin.";
    if (!value.lastName.trim()) next.lastName = "Soyadınızı girin.";
    if (!isMobile(value.phone)) next.phone = "Cep telefonu numaranızı girin (ör. 0532 123 45 67).";
    setErrors(next);
    setError(null);
    if (Object.keys(next).length === 0) send.mutate();
  };
  const set = (patch: Partial<Person>) => onChange({ ...value, ...patch });

  return (
    <form onSubmit={submit} noValidate className="flex flex-col gap-4">
      <div className="grid gap-4 sm:grid-cols-2">
        <Field label="Ad" error={errors.firstName}>
          <Input
            autoComplete="given-name"
            maxLength={100}
            value={value.firstName}
            onChange={(e) => set({ firstName: e.target.value })}
          />
        </Field>
        <Field label="Soyad" error={errors.lastName}>
          <Input
            autoComplete="family-name"
            maxLength={100}
            value={value.lastName}
            onChange={(e) => set({ lastName: e.target.value })}
          />
        </Field>
      </div>
      <Field label="Cep telefonu" error={errors.phone} hint="Doğrulama kodu bu numaraya SMS ile gelir.">
        <Input
          type="tel"
          inputMode="tel"
          autoComplete="tel-national"
          placeholder="05xx xxx xx xx"
          maxLength={20}
          value={value.phone}
          onChange={(e) => set({ phone: e.target.value })}
        />
      </Field>
      <p className="text-sm text-neutral-700">
        Kişisel verileriniz randevunuz için işlenir:{" "}
        <a href={`/r/${slug}/aydinlatma`} target="_blank" rel="noopener" className="font-semibold text-black underline">
          KVKK aydınlatma metni
        </a>
        .
      </p>
      <label className="flex min-h-11 items-start gap-3 text-sm text-black">
        <Checkbox
          className="mt-0.5"
          checked={value.marketing}
          onCheckedChange={(v) => set({ marketing: v === true })}
        />
        <span>Kampanya ve fırsatlardan SMS ile haberdar olmak istiyorum.</span>
      </label>
      {error && <FormMessage>{error}</FormMessage>}
      <Button type="submit" size="lg" disabled={send.isPending}>
        Kod gönder
      </Button>
    </form>
  );
}

/** Kod gönderme/doğrulama hatalarının kullanıcı metni (sınır aşımında işletme telefonu). */
function codeError(e: unknown, business: PublicBooking): string {
  if (e instanceof ApiError && e.problem.detail) {
    const code = problemCode(e);
    if (code === "booking_sms_limit" && business.phone && !e.problem.detail.includes(business.phone))
      return `${e.problem.detail} ${business.phone}`;
    return e.problem.detail;
  }
  return errorMessage(e);
}

function CodeStep({
  slug,
  choice,
  person,
  verification,
  onResent,
  onSlotTaken,
  onNewCode,
  onDone,
}: {
  slug: string;
  choice: Choice;
  person: Person;
  verification: { id: string; sentAt: number };
  onResent: (id: string) => void;
  onSlotTaken: () => void;
  onNewCode: () => void;
  onDone: (created: PublicBookingCreated) => void;
}) {
  const [code, setCode] = useState("");
  const [error, setError] = useState<{ text: string; renew: boolean } | null>(null);
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(timer);
  }, []);
  const wait = Math.max(0, 60 - Math.floor((now - verification.sentAt) / 1000));

  const book = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST("/api/public/booking/{slug}/appointments", {
          params: { path: { slug } },
          body: {
            verificationId: verification.id,
            code: code.trim(),
            serviceId: choice.service!.id,
            staffId: choice.staff?.id ?? null,
            startAt: slotToUtc(choice.date!, choice.time!),
            firstName: person.firstName.trim(),
            lastName: person.lastName.trim(),
            phone: person.phone,
            marketingConsent: person.marketing,
          },
        }),
      ),
    onSuccess: onDone,
    onError: (e) => {
      const c = problemCode(e);
      if (c === "slot_taken" || c === "slot_invalid") {
        toast.error(errorMessage(e));
        onSlotTaken();
        return;
      }
      setError({
        text: errorMessage(e),
        renew:
          c === "code_expired" ||
          c === "code_used" ||
          c === "code_attempts" ||
          (e instanceof ApiError && e.status === 429),
      });
    },
  });
  const resend = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST("/api/public/booking/{slug}/verifications", {
          params: { path: { slug } },
          body: { phone: person.phone, firstName: person.firstName },
        }),
      ),
    onSuccess: (r) => {
      onResent(r.verificationId);
      setCode("");
      setError(null);
      toast.success("Yeni kod gönderildi.");
    },
    onError: (e) => setError({ text: errorMessage(e), renew: false }),
  });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    if (!/^\d{6}$/.test(code.trim())) {
      setError({ text: "SMS ile gelen 6 haneli kodu girin.", renew: false });
      return;
    }
    setError(null);
    book.mutate();
  };

  return (
    <form onSubmit={submit} noValidate className="flex flex-col gap-4">
      <p className="text-base text-neutral-700">
        <span className="font-semibold text-black tabular-nums">{person.phone}</span> numarasına gelen 6 haneli kodu
        girin.
      </p>
      <Field label="Doğrulama kodu">
        <Input
          inputMode="numeric"
          autoComplete="one-time-code"
          pattern="\d{6}"
          maxLength={6}
          value={code}
          onChange={(e) => setCode(e.target.value.replace(/\D/g, ""))}
          className="text-center text-xl tracking-[0.5em] tabular-nums"
          autoFocus
        />
      </Field>
      {error && <FormMessage>{error.text}</FormMessage>}
      <Button type="submit" size="lg" disabled={book.isPending}>
        Randevuyu onayla
      </Button>
      <div className="flex flex-wrap gap-2">
        {error?.renew ? (
          <Button type="button" variant="outline" onClick={onNewCode}>
            Yeni kod iste
          </Button>
        ) : (
          <Button type="button" variant="ghost" disabled={wait > 0 || resend.isPending} onClick={() => resend.mutate()}>
            {wait > 0 ? `Tekrar gönder (${wait} sn)` : "Kodu tekrar gönder"}
          </Button>
        )}
      </div>
    </form>
  );
}

function Confirmation({
  slug,
  business,
  created,
  emailGiven,
}: {
  slug: string;
  business: PublicBooking;
  created: PublicBookingCreated;
  emailGiven: boolean;
}) {
  const when = istanbulParts(created.startAt);
  const end = istanbulParts(created.endAt);
  const pending = created.status === "Pending";
  const download = () => {
    const ics = buildIcs({
      uid: created.manageToken.slice(0, 16),
      title: `${created.serviceName} · ${business.name}`,
      startAt: created.startAt,
      endAt: created.endAt,
      location: [business.address, business.district, business.city].filter(Boolean).join(", ") || null,
      description: `Personel: ${created.staffName}`,
    });
    const url = URL.createObjectURL(new Blob([ics], { type: "text/calendar;charset=utf-8" }));
    const a = document.createElement("a");
    a.href = url;
    a.download = "randevu.ics";
    a.click();
    URL.revokeObjectURL(url);
  };
  return (
    <div className="flex flex-col gap-5">
      <section className="flex items-start gap-3 border-l-2 border-brand bg-neutral-50 px-4 py-4">
        <Check aria-hidden className="mt-0.5 size-5 shrink-0 text-black" />
        <div className="flex flex-col gap-1">
          <p className="text-base font-semibold text-black">
            {pending ? "Randevu talebiniz alındı." : "Randevunuz onaylandı."}
          </p>
          <p className="text-sm text-neutral-700">
            {confirmationNote(business.verificationRequired, pending, emailGiven)}
          </p>
        </div>
      </section>
      <dl className="flex flex-col gap-3 text-base">
        <div>
          <dt className="text-sm text-neutral-600">Hizmet</dt>
          <dd className="font-semibold text-black">{created.serviceName}</dd>
        </div>
        <div>
          <dt className="text-sm text-neutral-600">Personel</dt>
          <dd className="font-semibold text-black">{created.staffName}</dd>
        </div>
        <div>
          <dt className="text-sm text-neutral-600">Gün ve saat</dt>
          <dd className="font-semibold text-black tabular-nums">
            {longDayLabel(when.date)} · {when.time}-{end.time}
          </dd>
        </div>
      </dl>
      <div className="flex flex-col gap-2 sm:flex-row">
        <Button variant="outline" onClick={download}>
          <CalendarPlus aria-hidden />
          Takvime ekle
        </Button>
        <Button asChild variant="ghost">
          <Link href={`/r/${slug}/randevu/${created.manageToken}`}>Randevuyu görüntüle veya iptal et</Link>
        </Button>
      </div>
    </div>
  );
}

/** Onay ekranındaki bilgi satırı: SMS açık/kapalı ve e-posta girilip girilmediğine göre. */
export function confirmationNote(sms: boolean, pending: boolean, emailGiven: boolean): string {
  if (sms)
    return pending ? "İşletme onayladığında SMS ile bilgi vereceğiz." : "Randevu bilgileri SMS ile de gönderildi.";
  if (!pending)
    return emailGiven ? "Randevu bilgileri e-postanıza da gönderildi." : "Randevu saatinde sizi bekliyoruz.";
  return emailGiven
    ? "İşletme onayladığında e-postayla bilgi vereceğiz. Talebinizin bir kopyası e-postanıza gönderildi."
    : "İşletme randevunuzu onaylayınca size telefonla veya WhatsApp'tan haber verir.";
}

const emailPattern = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

/**
 * SMS kapalıyken bilgiler adımı: ad, soyad, cep telefonu (zorunlu), e-posta (isteğe bağlı) ve görünmez bot tuzağı alanı. Gönderince
 * randevu talebi açılır (kod yok); işletme panelden onaylar.
 */
function RequestStep({
  slug,
  business,
  choice,
  value,
  onChange,
  elapsedSeconds,
  onSlotTaken,
  onDone,
}: {
  slug: string;
  business: PublicBooking;
  choice: Choice;
  value: Person;
  onChange: (p: Person) => void;
  elapsedSeconds: () => number;
  onSlotTaken: () => void;
  onDone: (created: PublicBookingCreated) => void;
}) {
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [error, setError] = useState<string | null>(null);
  const hasEmail = !!value.email.trim();
  const book = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST("/api/public/booking/{slug}/appointments", {
          params: { path: { slug } },
          body: {
            verificationId: null,
            code: null,
            serviceId: choice.service!.id,
            staffId: choice.staff?.id ?? null,
            startAt: slotToUtc(choice.date!, choice.time!),
            firstName: value.firstName.trim(),
            lastName: value.lastName.trim(),
            phone: value.phone,
            email: value.email.trim() || null,
            marketingConsent: hasEmail && value.marketing,
            website: value.website || null,
            elapsedSeconds: elapsedSeconds(),
          },
        }),
      ),
    onSuccess: onDone,
    onError: (e) => {
      const c = problemCode(e);
      if (c === "slot_taken" || c === "slot_invalid") {
        toast.error(errorMessage(e));
        onSlotTaken();
        return;
      }
      const fields = fieldErrors(e);
      setErrors(fields);
      setError(Object.keys(fields).length ? null : codeError(e, business));
    },
  });
  const submit = (e: FormEvent) => {
    e.preventDefault();
    const next: Record<string, string> = {};
    if (!value.firstName.trim()) next.firstName = "Adınızı girin.";
    if (!value.lastName.trim()) next.lastName = "Soyadınızı girin.";
    if (!isMobile(value.phone)) next.phone = "Cep telefonu numaranızı girin (ör. 0532 123 45 67).";
    if (hasEmail && !emailPattern.test(value.email.trim())) next.email = "Geçerli bir e-posta adresi girin.";
    setErrors(next);
    setError(null);
    if (Object.keys(next).length === 0) book.mutate();
  };
  const set = (patch: Partial<Person>) => onChange({ ...value, ...patch });

  return (
    <form onSubmit={submit} noValidate className="relative flex flex-col gap-4">
      <div className="grid gap-4 sm:grid-cols-2">
        <Field label="Ad" error={errors.firstName}>
          <Input
            autoComplete="given-name"
            maxLength={100}
            value={value.firstName}
            onChange={(e) => set({ firstName: e.target.value })}
          />
        </Field>
        <Field label="Soyad" error={errors.lastName}>
          <Input
            autoComplete="family-name"
            maxLength={100}
            value={value.lastName}
            onChange={(e) => set({ lastName: e.target.value })}
          />
        </Field>
      </div>
      <Field label="Cep telefonu" error={errors.phone} hint="İşletme randevunuz için bu numaradan size ulaşır.">
        <Input
          type="tel"
          inputMode="tel"
          autoComplete="tel-national"
          placeholder="05xx xxx xx xx"
          maxLength={20}
          value={value.phone}
          onChange={(e) => set({ phone: e.target.value })}
        />
      </Field>
      <Field
        label="E-posta (isteğe bağlı)"
        error={errors.email}
        hint="Girerseniz randevu bilgileri e-postanıza da gelir."
      >
        <Input
          type="email"
          inputMode="email"
          autoComplete="email"
          maxLength={256}
          value={value.email}
          onChange={(e) => set({ email: e.target.value })}
        />
      </Field>
      {/* Bot tuzağı: insanlar görmez ve doldurmaz; doluysa sunucu kayıt açmaz. */}
      <div aria-hidden className="absolute top-0 -left-[9999px] size-px overflow-hidden">
        <label>
          Web sitesi
          <input
            tabIndex={-1}
            autoComplete="off"
            name="website"
            value={value.website}
            onChange={(e) => set({ website: e.target.value })}
          />
        </label>
      </div>
      <p className="text-sm text-neutral-700">
        Kişisel verileriniz randevunuz için işlenir:{" "}
        <a href={`/r/${slug}/aydinlatma`} target="_blank" rel="noopener" className="font-semibold text-black underline">
          KVKK aydınlatma metni
        </a>
        .
      </p>
      {hasEmail && (
        <label className="flex min-h-11 items-start gap-3 text-sm text-black">
          <Checkbox
            className="mt-0.5"
            checked={value.marketing}
            onCheckedChange={(v) => set({ marketing: v === true })}
          />
          <span>Kampanya ve fırsatlardan e-posta ile haberdar olmak istiyorum.</span>
        </label>
      )}
      {error && <FormMessage>{error}</FormMessage>}
      <Button type="submit" size="lg" disabled={book.isPending}>
        Randevu talebini gönder
      </Button>
      <p className="text-sm text-neutral-600">Randevunuz işletme onayladığında kesinleşir.</p>
    </form>
  );
}
