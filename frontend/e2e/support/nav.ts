import { expect, type Locator, type Page, type TestInfo } from "@playwright/test";

import { isDesktop } from "./test";

/** Ana menü: masaüstünde sol menü, telefonda sağ üstteki "Menü" paneli (açar; Sprint 6A'da alt çubuktan taşındı). */
export async function mainMenu(page: Page, info: TestInfo): Promise<Locator> {
  if (!isDesktop(info)) {
    await page.getByRole("banner").getByRole("button", { name: "Menü" }).click();
  }
  const nav = page.getByRole("navigation", { name: "Ana menü" });
  await expect(nav).toBeVisible();
  return nav;
}

/** Telefonda açılan menü panelini kapatır. */
export async function closeMenu(page: Page, info: TestInfo) {
  if (!isDesktop(info)) await page.keyboard.press("Escape");
}

/** Çıkış: masaüstünde sağ üstteki kullanıcı menüsünden, telefonda sağ üstteki Menü panelinden. */
export async function logout(page: Page) {
  const userMenu = page.getByRole("button", { name: /Kullanıcı menüsü/ });
  if (await userMenu.isVisible()) {
    await userMenu.click();
    await page.getByRole("menuitem", { name: "Çıkış yap" }).click();
  } else {
    await page.getByRole("banner").getByRole("button", { name: "Menü" }).click();
    await page.getByRole("dialog").getByRole("button", { name: "Çıkış yap" }).click();
  }
  await expect(page).toHaveURL(/\/giris/);
}

/** Giriş ekranından giriş. */
export async function login(page: Page, email: string, password: string) {
  await page.goto("/giris");
  await page.getByLabel("E-posta").fill(email);
  await page.getByLabel("Şifre", { exact: true }).fill(password);
  await page.getByRole("button", { name: "Giriş yap" }).click();
}

/** Bildirim (toast) metni. */
export function toast(page: Page, text: string | RegExp): Locator {
  return page.locator("[data-sonner-toast]").filter({ hasText: text }).first();
}
