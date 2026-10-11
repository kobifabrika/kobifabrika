import { expect, type Locator, type Page, type TestInfo } from "@playwright/test";

import { isDesktop } from "./test";

/** Ana menü: masaüstünde sol menü, telefonda alt çubuktaki "Menü" paneli (açar). */
export async function mainMenu(page: Page, info: TestInfo): Promise<Locator> {
  if (!isDesktop(info)) {
    await page.getByRole("navigation", { name: "Alt menü" }).getByRole("button", { name: "Menü" }).click();
  }
  const nav = page.getByRole("navigation", { name: "Ana menü" });
  await expect(nav).toBeVisible();
  return nav;
}

/** Telefonda açılan menü panelini kapatır. */
export async function closeMenu(page: Page, info: TestInfo) {
  if (!isDesktop(info)) await page.keyboard.press("Escape");
}

/** Sağ üstteki kullanıcı menüsünden çıkış. */
export async function logout(page: Page) {
  await page.getByRole("button", { name: /Kullanıcı menüsü/ }).click();
  await page.getByRole("menuitem", { name: "Çıkış yap" }).click();
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
