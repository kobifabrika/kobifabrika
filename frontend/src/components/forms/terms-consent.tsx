"use client";

import type { ComponentProps } from "react";

const link = "font-semibold text-link-text underline underline-offset-2";

/**
 * Kayıt onayı (karar 2026-10-17): "Kullanım koşullarını ve Veri işleme sözleşmesini kabul ediyorum" (davetle gelen çalışanda sadece
 * Kullanım koşulları; veri işleme sözleşmesinin tarafı işletmedir). KVKK aydınlatma metni onay değil, bilgilendirme bağlantısıdır.
 * Metinler yeni sekmede açılır; form kaybolmaz.
 */
export function TermsConsent({ error, dpa = true, ...input }: ComponentProps<"input"> & { error?: string; dpa?: boolean }) {
  return (
    <div className="flex flex-col gap-1">
      <div className="flex min-h-11 items-start gap-3 py-2">
        <input
          id="terms"
          type="checkbox"
          className="mt-0.5 size-5 shrink-0 accent-black dark:accent-brand"
          aria-invalid={error ? true : undefined}
          aria-describedby={error ? "terms-error" : undefined}
          {...input}
        />
        <label htmlFor="terms" className="text-sm leading-6 text-neutral-700 dark:text-neutral-300">
          <a href="/kullanim-kosullari" target="_blank" rel="noopener" className={link}>
            Kullanım koşullarını
          </a>
          {dpa && (
            <>
              {" "}
              ve{" "}
              <a href="/veri-isleme-sozlesmesi" target="_blank" rel="noopener" className={link}>
                Veri işleme sözleşmesini
              </a>
            </>
          )}{" "}
          kabul ediyorum.
        </label>
      </div>
      {error && (
        <p id="terms-error" role="alert" className="text-sm text-danger-600 dark:self-start dark:rounded-md dark:bg-danger-50 dark:px-2 dark:py-1">
          {error}
        </p>
      )}
      <p className="text-sm text-neutral-600 dark:text-neutral-300">
        Kişisel verileriniz{" "}
        <a href="/kvkk" target="_blank" rel="noopener" className={link}>
          KVKK aydınlatma metni
        </a>{" "}
        kapsamında işlenir.
      </p>
    </div>
  );
}
