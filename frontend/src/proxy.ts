import { NextResponse, type NextRequest } from "next/server";

import { isSiteOnly } from "@/content/site-mode";

/** API'nin oturum cookie'si (backend Program.cs: Cookie.Name). */
const AUTH_COOKIE = "kf.auth";

/**
 * Hızlı ön kontrol: oturum cookie'si yoksa panel, süper admin (/platform) ve kurulum sayfaları giriş ekranına yönlenir.
 * Cookie'nin geçerliliğini API kontrol eder (PanelGate → GET /api/auth/me); burası sadece gereksiz
 * yüklemeyi önler. Geliştirmede cookie localhost'a, canlıda .kobifabrika.com'a yazılır.
 *
 * Sadece-site modunda (content/site-mode.ts) API olmadığı için uygulama yollarının hepsi "Çok yakında" sayfasını gösterir;
 * mod kapalıyken giriş ve herkese açık link yolları olduğu gibi geçer.
 */
export function proxy(request: NextRequest) {
  const { pathname } = request.nextUrl;
  if (isSiteOnly()) return NextResponse.rewrite(new URL("/cok-yakinda", request.url));
  if (!protectedPaths.some((p) => pathname === p || pathname.startsWith(`${p}/`))) return NextResponse.next();
  if (request.cookies.has(AUTH_COOKIE)) return NextResponse.next();

  const login = new URL("/giris", request.url);
  if (pathname.startsWith("/panel") || pathname.startsWith("/platform")) {
    login.searchParams.set("donus", pathname + request.nextUrl.search);
  }
  return NextResponse.redirect(login);
}

/** Oturum isteyen yollar. */
const protectedPaths = ["/panel", "/platform", "/kurulum"];

/**
 * Prefetch istekleri proxy'ye girmez: çıkıştan hemen sonra menüdeki bağlantıların segment prefetch'i cookie'siz gelince
 * /giris'e yönlenip orada olmayan segmenti istiyor ve 404 alıyordu. Prefetch veri taşımaz (PanelGate korur); gerçek
 * gezinme yine buradan geçer. Next bu ayarı derlemede statik okur; değerler satır içinde kalmalı. Sondaki yollar sadece
 * sadece-site modu için: API gerektiren giriş ve link sayfaları (mod kapalıyken proxy bunları olduğu gibi geçirir).
 */
export const config = {
  matcher: [
    {
      source: "/panel/:path*",
      missing: [
        { type: "header", key: "next-router-prefetch" },
        { type: "header", key: "purpose", value: "prefetch" },
      ],
    },
    {
      source: "/platform/:path*",
      missing: [
        { type: "header", key: "next-router-prefetch" },
        { type: "header", key: "purpose", value: "prefetch" },
      ],
    },
    {
      source: "/kurulum",
      missing: [
        { type: "header", key: "next-router-prefetch" },
        { type: "header", key: "purpose", value: "prefetch" },
      ],
    },
    "/giris",
    "/kayit",
    "/sifremi-unuttum",
    "/sifre-sifirla/:path*",
    "/e-posta-dogrula/:path*",
    "/davet/:path*",
    "/r/:path*",
    "/a/:path*",
    "/c/:path*",
    "/ekstre/:path*",
    "/odeme/:path*",
    "/teklif/:path*",
  ],
};
