import { NextResponse, type NextRequest } from "next/server";

/** API'nin oturum cookie'si (backend Program.cs: Cookie.Name). */
const AUTH_COOKIE = "kf.auth";

/**
 * Hızlı ön kontrol: oturum cookie'si yoksa panel ve kurulum sayfaları giriş ekranına yönlenir.
 * Cookie'nin geçerliliğini API kontrol eder (PanelGate → GET /api/auth/me); burası sadece gereksiz
 * yüklemeyi önler. Geliştirmede cookie localhost'a, canlıda .kobifabrika.com'a yazılır.
 */
export function proxy(request: NextRequest) {
  if (request.cookies.has(AUTH_COOKIE)) return NextResponse.next();

  const login = new URL("/giris", request.url);
  if (request.nextUrl.pathname.startsWith("/panel")) {
    login.searchParams.set("donus", request.nextUrl.pathname + request.nextUrl.search);
  }
  return NextResponse.redirect(login);
}

export const config = {
  matcher: ["/panel/:path*", "/kurulum"],
};
