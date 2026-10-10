"use client";

import type { ComponentProps } from "react";

import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog";
import { kvkkSections, kvkkTitle } from "@/content/kvkk-aydinlatma";

/** KVKK aydınlatma metni onayı: kutucuk + metni açan bağlantı. */
export function KvkkConsent({ error, ...input }: ComponentProps<"input"> & { error?: string }) {
  return (
    <div className="flex flex-col gap-1">
      <div className="flex min-h-11 items-start gap-3 py-2">
        <input
          id="kvkk"
          type="checkbox"
          className="mt-0.5 size-5 shrink-0 accent-brand-500"
          aria-invalid={error ? true : undefined}
          aria-describedby={error ? "kvkk-error" : undefined}
          {...input}
        />
        <label htmlFor="kvkk" className="text-sm leading-6 text-neutral-700">
          <KvkkDialog /> okudum, anladım.
        </label>
      </div>
      {error && (
        <p id="kvkk-error" role="alert" className="text-sm text-danger-600">
          {error}
        </p>
      )}
    </div>
  );
}

function KvkkDialog() {
  return (
    <Dialog>
      <DialogTrigger asChild>
        <button type="button" className="font-semibold text-brand-500 underline underline-offset-2">
          KVKK aydınlatma metnini
        </button>
      </DialogTrigger>
      <DialogContent className="max-h-[85vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>{kvkkTitle}</DialogTitle>
          <DialogDescription>Kayıt olmadan önce lütfen okuyun.</DialogDescription>
        </DialogHeader>
        <div className="flex flex-col gap-4 text-sm leading-6 text-neutral-700">
          {kvkkSections.map((s) => (
            <section key={s.heading}>
              <h3 className="font-semibold text-brand-900">{s.heading}</h3>
              <p>{s.body}</p>
            </section>
          ))}
        </div>
        <DialogFooter>
          <DialogClose asChild>
            <Button type="button" className="w-full sm:w-auto">
              Kapat
            </Button>
          </DialogClose>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
