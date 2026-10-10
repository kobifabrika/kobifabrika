"use client";

import Link from "next/link";
import { useState } from "react";

import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";

/** Giriş formu iskeleti. API bağlantısı Sprint 1'de (cookie auth) eklenecek. */
export function LoginForm() {
  const [showPassword, setShowPassword] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);

  return (
    <form
      className="mt-8 flex flex-col gap-5"
      onSubmit={(e) => {
        e.preventDefault();
        setNotice("Giriş Sprint 1'de bağlanacak.");
      }}
    >
      <div className="flex flex-col gap-2">
        <Label htmlFor="email" className="text-sm font-semibold text-brand-900">
          E-posta
        </Label>
        <Input id="email" name="email" type="email" autoComplete="email" inputMode="email" placeholder="ornek@isletme.com" required />
      </div>

      <div className="flex flex-col gap-2">
        <Label htmlFor="password" className="text-sm font-semibold text-brand-900">
          Şifre
        </Label>
        <div className="relative">
          <Input
            id="password"
            name="password"
            type={showPassword ? "text" : "password"}
            autoComplete="current-password"
            placeholder="Şifreniz"
            className="pr-20"
            required
          />
          <button
            type="button"
            onClick={() => setShowPassword((v) => !v)}
            aria-pressed={showPassword}
            className="absolute inset-y-0 right-0 min-w-16 rounded-r-lg px-3 text-sm font-medium text-brand-500"
          >
            {showPassword ? "Gizle" : "Göster"}
          </button>
        </div>
      </div>

      <div className="flex items-center justify-between gap-3 text-sm">
        <label className="flex min-h-11 items-center gap-2 text-neutral-600">
          <input type="checkbox" name="remember" className="size-5 accent-brand-500" />
          Beni hatırla
        </label>
        <Link href="/sifremi-unuttum" className="flex min-h-11 items-center font-semibold text-brand-500 hover:underline">
          Şifremi unuttum
        </Link>
      </div>

      <Button type="submit" size="lg" className="w-full">
        Giriş yap
      </Button>

      {notice && (
        <p role="status" className="rounded-md bg-neutral-100 px-3 py-2 text-sm text-brand-700">
          {notice}
        </p>
      )}
    </form>
  );
}
