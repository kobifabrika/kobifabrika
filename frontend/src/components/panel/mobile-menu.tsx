"use client";

import {
  BookOpen,
  Building2,
  Check,
  LogOut,
  Mail,
  Menu,
  MessageCircle,
  MessageSquarePlus,
  ShieldCheck,
  UserRound,
} from "lucide-react";
import Link from "next/link";
import { useState } from "react";

import { FeedbackDialog, useSupportLinks } from "@/components/panel/help-menu";
import { useSwitchBusiness } from "@/components/panel/business-switcher";
import { NavFooter, NavList } from "@/components/panel/sidebar";
import { initials } from "@/components/panel/user-menu";
import { useLogout, useSession } from "@/components/providers/session-provider";
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle, SheetTrigger } from "@/components/ui/sheet";
import { fileUrl } from "@/lib/api/client";
import { roleNames, type Role } from "@/lib/api/types";

const rowClass =
  "flex min-h-11 w-full items-center gap-3 rounded-md px-3 text-left text-sm text-sidebar-foreground transition-colors hover:bg-navy-800";

/**
 * Telefonda sağ üstteki "Menü": kullanıcı menüsüyle birleşik tek düğme (üst çubukta ayrı hamburger + avatar 375 px'te sıkışır).
 * Açılan sağ panelin başlığı işletmenin logosu ve adı; önce kullanıcı (ad, işletme · rol, Hesabım, işletme değiştir, Çıkış), sonra
 * ana menü ve Yardım; en altta küçük KobiFabrika logosu (sol menüyle aynı).
 */
export function MobileMenu() {
  const [open, setOpen] = useState(false);
  const { me, business, role } = useSession();
  const logout = useLogout();
  const switchTo = useSwitchBusiness();
  const close = () => setOpen(false);
  const support = useSupportLinks();
  const [feedback, setFeedback] = useState(false);
  const logo = fileUrl(business.logoUrl);

  return (
    <>
      <Sheet open={open} onOpenChange={setOpen}>
        <SheetTrigger asChild>
          <button
            type="button"
            className="flex size-11 items-center justify-center rounded-lg text-white hover:bg-white/10 lg:hidden"
            aria-label="Menü"
          >
            <Menu aria-hidden className="size-6" />
          </button>
        </SheetTrigger>
        <SheetContent
          side="right"
          className="w-[85%] max-w-xs gap-0 border-sidebar-border bg-sidebar p-0 text-sidebar-foreground"
        >
          <SheetHeader className="border-b border-sidebar-border px-5 py-4">
            <SheetTitle className="flex min-w-0 items-center gap-2.5 pr-10">
              {logo && (
                // eslint-disable-next-line @next/next/no-img-element -- API'den gelen kullanıcı dosyası; next/image optimizasyonu gerekmez.
                <img src={logo} alt="" className="size-8 shrink-0 rounded-md bg-white object-contain" />
              )}
              <span className="truncate text-base font-semibold text-white">{business.name}</span>
            </SheetTitle>
            <SheetDescription className="sr-only">Panel menüsü ve hesap</SheetDescription>
          </SheetHeader>
          <div className="flex-1 overflow-y-auto">
            <section aria-label="Hesap" className="border-b border-sidebar-border px-3 py-4">
              <div className="flex items-center gap-3 px-3 pb-3">
                <span className="flex size-11 shrink-0 items-center justify-center rounded-full bg-neutral-300 text-sm font-bold text-navy-950">
                  {initials(me.user.fullName)}
                </span>
                <span className="flex min-w-0 flex-col">
                  <span className="truncate text-sm font-semibold text-white">{me.user.fullName}</span>
                  <span className="truncate text-xs text-neutral-300">
                    {business.name} · {roleNames[role]}
                  </span>
                </span>
              </div>
              <Link href="/panel/hesabim" onClick={close} className={rowClass}>
                <UserRound aria-hidden className="size-5" /> Hesabım
              </Link>
              {me.user.isPlatformAdmin && (
                <Link href="/platform" onClick={close} className={rowClass}>
                  <ShieldCheck aria-hidden className="size-5" /> Süper admin
                </Link>
              )}
              {me.memberships.length > 1 &&
                me.memberships.map((m) => (
                  <button
                    key={m.businessId}
                    type="button"
                    disabled={m.businessId === business.id}
                    onClick={() => {
                      close();
                      switchTo(m.businessId);
                    }}
                    className={rowClass}
                  >
                    {m.businessId === business.id ? (
                      <Check aria-hidden className="size-5" />
                    ) : (
                      <Building2 aria-hidden className="size-5" />
                    )}
                    <span className="flex flex-col">
                      <span>{m.businessName}</span>
                      <span className="text-xs text-neutral-300">{roleNames[m.role as Role]}</span>
                    </span>
                  </button>
                ))}
              <button
                type="button"
                onClick={() => {
                  // Önce panel kapanır: açık menü linkleri oturum kapandıktan sonra önyüklenmesin (proxy 404).
                  close();
                  logout();
                }}
                className={rowClass}
              >
                <LogOut aria-hidden className="size-5" /> Çıkış yap
              </button>
            </section>
            <div className="px-3 py-5">
              <NavList onNavigate={close} />
            </div>
            <section aria-label="Yardım" className="border-t border-sidebar-border px-3 py-4">
              <p className="px-3 pb-2 text-xs font-semibold text-neutral-300">Yardım</p>
              {support.whatsapp && (
                <a href={support.whatsapp} target="_blank" rel="noopener noreferrer" className={rowClass}>
                  <MessageCircle aria-hidden className="size-5" /> WhatsApp destek
                </a>
              )}
              <a href={support.email} className={rowClass}>
                <Mail aria-hidden className="size-5" /> E-posta ile destek
              </a>
              <a href="/yardim" target="_blank" rel="noopener" className={rowClass}>
                <BookOpen aria-hidden className="size-5" /> Yardım makaleleri
              </a>
              <button
                type="button"
                onClick={() => {
                  close();
                  setFeedback(true);
                }}
                className={rowClass}
              >
                <MessageSquarePlus aria-hidden className="size-5" /> Geri bildirim gönder
              </button>
            </section>
          </div>
          <NavFooter />
        </SheetContent>
      </Sheet>
      {feedback && <FeedbackDialog open onOpenChange={setFeedback} />}
    </>
  );
}
