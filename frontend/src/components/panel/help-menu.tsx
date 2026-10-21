"use client";

import { useMutation } from "@tanstack/react-query";
import { BookOpen, CircleHelp, Mail, MessageCircle, MessageSquarePlus } from "lucide-react";
import { usePathname } from "next/navigation";
import { useState, type FormEvent } from "react";
import { toast } from "sonner";

import { ResponsivePanel } from "@/components/appointments/appointment-bits";
import { Field, FormMessage } from "@/components/forms/field";
import { useSession } from "@/components/providers/session-provider";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { Textarea } from "@/components/ui/textarea";
import { API_URL, ApiError, errorMessage, fieldErrors } from "@/lib/api/client";
import { supportEmail, supportWhatsappDisplay, supportWhatsappLink } from "@/content/support";

const minLength = 10;
const maxLength = 2000;
const maxScreenshotBytes = 5 * 1024 * 1024;

/** Destek WhatsApp'ında hazır metin: işletme, slug ve ekran (yol) — destek ekibi hangi ekrandan yazıldığını görür. */
export function useSupportLinks() {
  const { business } = useSession();
  const pathname = usePathname();
  return {
    whatsapp: supportWhatsappLink(
      `Merhaba, ${business.name} (${business.slug}) için yardım istiyorum. Ekran: ${pathname}`,
    ),
    email: `mailto:${supportEmail}?subject=${encodeURIComponent(`${business.name} · destek`)}`,
  };
}

/** Masaüstü üst çubuğu "?" menüsü: WhatsApp destek, e-posta, yardım makaleleri, geri bildirim. Telefonda Menü'de aynı bölüm. */
export function HelpMenu() {
  const [feedback, setFeedback] = useState(false);
  const links = useSupportLinks();
  return (
    <>
      <DropdownMenu>
        <DropdownMenuTrigger
          aria-label="Yardım"
          className="hidden size-9 items-center justify-center rounded-md text-white hover:bg-navy-800 lg:flex"
        >
          <CircleHelp aria-hidden className="size-5" />
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end" className="w-72">
          <DropdownMenuLabel className="text-xs text-neutral-600">Yardım ve destek</DropdownMenuLabel>
          {links.whatsapp && (
            <DropdownMenuItem asChild>
              <a href={links.whatsapp} target="_blank" rel="noopener noreferrer">
                <MessageCircle aria-hidden />
                <span className="flex flex-col">
                  <span>WhatsApp destek</span>
                  <span className="text-xs text-neutral-600 tabular-nums">{supportWhatsappDisplay}</span>
                </span>
              </a>
            </DropdownMenuItem>
          )}
          <DropdownMenuItem asChild>
            <a href={links.email}>
              <Mail aria-hidden />
              <span className="flex flex-col">
                <span>E-posta</span>
                <span className="text-xs text-neutral-600">{supportEmail}</span>
              </span>
            </a>
          </DropdownMenuItem>
          <DropdownMenuItem asChild>
            <a href="/yardim" target="_blank" rel="noopener">
              <BookOpen aria-hidden /> Yardım makaleleri
            </a>
          </DropdownMenuItem>
          <DropdownMenuSeparator />
          <DropdownMenuItem onSelect={() => setFeedback(true)}>
            <MessageSquarePlus aria-hidden /> Geri bildirim gönder
          </DropdownMenuItem>
        </DropdownMenuContent>
      </DropdownMenu>
      {feedback && <FeedbackDialog open onOpenChange={setFeedback} />}
    </>
  );
}

/**
 * Geri bildirim: metin (10-2000) ve isteğe bağlı ekran görüntüsü (PNG/JPG/WebP, 5 MB). Sayfa yolu ve ekran boyutu otomatik eklenir;
 * rol ve tarayıcıyı sunucu ekler. Görüntüyü sadece KobiFabrika ekibi görür, 90 gün sonra silinir.
 */
export function FeedbackDialog({ open, onOpenChange }: { open: boolean; onOpenChange: (open: boolean) => void }) {
  const pathname = usePathname();
  const [body, setBody] = useState("");
  const [file, setFile] = useState<File | null>(null);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [error, setError] = useState<string | null>(null);

  const send = useMutation({
    mutationFn: async () => {
      const form = new FormData();
      form.set("body", body.trim());
      form.set("path", pathname);
      form.set("viewport", `${window.innerWidth}x${window.innerHeight}`);
      if (file) form.set("screenshot", file);
      const res = await fetch(`${API_URL}/api/feedback`, { method: "POST", credentials: "include", body: form });
      if (!res.ok) throw new ApiError(res.status, await res.json().catch(() => ({ title: "Gönderilemedi." })));
    },
    onSuccess: () => {
      toast.success("Geri bildiriminiz alındı. Teşekkür ederiz!");
      setBody("");
      setFile(null);
      onOpenChange(false);
    },
    onError: (e) => {
      const fields = fieldErrors(e);
      setErrors(fields);
      setError(Object.keys(fields).length ? null : errorMessage(e));
    },
  });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    const next: Record<string, string> = {};
    const text = body.trim();
    if (text.length < minLength) next.body = `En az ${minLength} karakter yazın.`;
    if (file && file.size > maxScreenshotBytes) next.screenshot = "Ekran görüntüsü en fazla 5 MB olabilir.";
    if (file && !["image/png", "image/jpeg", "image/webp"].includes(file.type))
      next.screenshot = "Ekran görüntüsü PNG, JPG veya WebP olmalı.";
    setErrors(next);
    setError(null);
    if (Object.keys(next).length === 0) send.mutate();
  };

  return (
    <ResponsivePanel
      open={open}
      onOpenChange={onOpenChange}
      title="Geri bildirim gönder"
      description="Hata, öneri ya da istek: hepsini okuyoruz."
    >
      <form onSubmit={submit} noValidate className="flex flex-col gap-4">
        <Field label="Mesajınız" error={errors.body} hint={`${body.trim().length}/${maxLength}`}>
          <Textarea rows={5} maxLength={maxLength} value={body} onChange={(e) => setBody(e.target.value)} />
        </Field>
        <Field
          label="Ekran görüntüsü (isteğe bağlı)"
          error={errors.screenshot}
          hint="PNG, JPG veya WebP; en fazla 5 MB."
        >
          <input
            type="file"
            accept="image/png,image/jpeg,image/webp"
            onChange={(e) => setFile(e.target.files?.[0] ?? null)}
            className="min-h-11 w-full rounded-lg border border-border px-3 py-2 text-base file:mr-3 file:min-h-9 file:rounded-md file:border-0 file:bg-neutral-100 file:px-3 file:font-semibold"
          />
        </Field>
        <p className="text-sm text-neutral-600">
          Ekran görüntüsünde müşteri bilgisi varsa gizleyin. Görüntüyü sadece KobiFabrika destek ekibi görür, 90 gün
          sonra silinir.
        </p>
        {error && <FormMessage>{error}</FormMessage>}
        <Button type="submit" disabled={send.isPending}>
          Gönder
        </Button>
      </form>
    </ResponsivePanel>
  );
}
