/**
 * Telefonda (lg altı) pencere ve alt sayfa açılınca ilk alana otomatik odaklanılmaz: klavye açılıp pencerenin düğmelerini
 * örtüyordu (Sprint 7B). Odak pencerenin kendisinde kalır (odak tuzağı ve ekran okuyucu duyurusu çalışır); masaüstünde
 * Radix'in varsayılanı (ilk odaklanabilir öğe) geçerli.
 */
export function phoneSafeOpenAutoFocus(event: Event) {
  if (event.defaultPrevented || typeof window.matchMedia !== "function" || window.matchMedia("(min-width: 1024px)").matches) return;
  event.preventDefault();
  (event.currentTarget as HTMLElement | null)?.focus({ preventScroll: true });
}
