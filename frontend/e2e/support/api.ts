import {
  expect,
  request,
  type APIRequestContext,
  type APIResponse,
  type Browser,
  type BrowserContext,
} from "@playwright/test";

import { platformEmail } from "../scripts/env.mjs";
import { target } from "./target";
import { linkFromEmail } from "./mailpit";

/**
 * Test verisini API üzerinden hazırlayan yardımcılar (arayüz adımları ayrıca test edilir). Her kullanıcı kendi çerezini tutan
 * ayrı bir istek bağlamına sahiptir; tarayıcıya aynı oturumla geçmek için `signIn(browser)`.
 */

export const PASSWORD = "Sifre1234";

let counter = 0;
/** Her test kendi benzersiz e-postasını kullanır: e2e+<zaman>-<sıra>-<rastgele>@example.test */
export function uniqueEmail(tag = "u"): string {
  counter += 1;
  return `e2e+${tag}-${Date.now().toString(36)}-${counter}-${Math.random().toString(36).slice(2, 7)}@example.test`;
}

/** Benzersiz Türk cep numarası (05xx …). */
export function uniquePhone(): string {
  const n = String(Math.floor(Math.random() * 1e7)).padStart(7, "0");
  return `053${Math.floor(Math.random() * 10)} ${n.slice(0, 3)} ${n.slice(3, 5)} ${n.slice(5)}`;
}

/** Testlerde API yanıtları serbestçe okunur (tipli istemci frontend'de; burada senaryo verisi). */
// eslint-disable-next-line @typescript-eslint/no-explicit-any
type Json = any;

export type Role = "Owner" | "Manager" | "Staff" | "Accountant";

async function ok(res: APIResponse, what: string) {
  expect(res.ok(), `${what}: ${res.status()} ${await res.text()}`).toBe(true);
  const body = await res.text();
  return body ? JSON.parse(body) : null;
}

export class ApiUser {
  constructor(
    readonly ctx: APIRequestContext,
    readonly email: string,
    readonly fullName: string,
  ) {}

  get = async <T = Json>(url: string): Promise<T> => ok(await this.ctx.get(url), `GET ${url}`);
  post = async <T = Json>(url: string, data: unknown = {}): Promise<T> =>
    ok(await this.ctx.post(url, { data }), `POST ${url}`);
  put = async <T = Json>(url: string, data: unknown = {}): Promise<T> =>
    ok(await this.ctx.put(url, { data }), `PUT ${url}`);

  /** Bu kullanıcının oturumuyla yeni bir tarayıcı bağlamı (çerez aktarılır). */
  async browserContext(browser: Browser, options: Parameters<Browser["newContext"]>[0] = {}): Promise<BrowserContext> {
    return browser.newContext({ ...options, storageState: await this.ctx.storageState() });
  }

  /** E-posta doğrulama linkini Mailpit'ten okuyup API'ye doğrulatır (davet göndermek için gerekir). */
  emailConfirmed = false;

  async confirmEmail() {
    if (this.emailConfirmed) return;
    const link = new URL(await linkFromEmail(this.email, /E-posta adresini doğrula/, "/e-posta-dogrula"));
    await this.post("/api/auth/confirm-email", {
      userId: link.searchParams.get("u"),
      token: link.searchParams.get("t"),
    });
    this.emailConfirmed = true;
  }

  async dispose() {
    await this.ctx.dispose();
  }
}

export async function newApiContext(): Promise<APIRequestContext> {
  return request.newContext({ baseURL: target().apiUrl });
}

/** Yeni hesap (oturum açık döner). */
export async function register(options: { email?: string; fullName?: string } = {}): Promise<ApiUser> {
  const ctx = await newApiContext();
  const email = options.email ?? uniqueEmail("sahip");
  const fullName = options.fullName ?? "Deneme Sahip";
  await ok(
    await ctx.post("/api/auth/register", {
      data: { fullName, email, phone: uniquePhone(), password: PASSWORD, termsAccepted: true },
    }),
    "Kayıt",
  );
  return new ApiUser(ctx, email, fullName);
}

export type Sector =
  | "HairBarber"
  | "BeautyCare"
  | "ClinicHealth"
  | "SportsFitness"
  | "AutoService"
  | "RetailStore"
  | "CafeRestaurant"
  | "Other";

/** Kurulumu bitmiş işletmesi olan Sahip. Perakende (stok açık) varsayılan. */
export async function ownerWithBusiness(
  options: { name?: string; sector?: Sector; confirmEmail?: boolean; email?: string; fullName?: string } = {},
): Promise<ApiUser> {
  const owner = await register({ email: options.email, fullName: options.fullName });
  await owner.post("/api/setup/business", {
    name: options.name ?? `Deneme Butik ${Math.random().toString(36).slice(2, 6)}`,
    sector: options.sector ?? "Other",
    phone: "0212 555 44 33",
    city: "İstanbul",
    district: "Kadıköy",
  });
  await owner.post("/api/setup/complete");
  if (options.confirmEmail) await owner.confirmEmail();
  return owner;
}

/** Sahibin ekibine yeni bir üye: davet (e-posta Mailpit'ten) → davet linkinden kayıt. */
export async function addMember(owner: ApiUser, role: Exclude<Role, "Owner">, fullName?: string): Promise<ApiUser> {
  const email = uniqueEmail(role.toLowerCase());
  await owner.confirmEmail(); // davet göndermek için gerekir
  await owner.post("/api/team/invitations", { email, role });
  const token = (await linkFromEmail(email, /davet etti/, "/davet/")).split("/davet/")[1];
  const ctx = await newApiContext();
  const name = fullName ?? `Deneme ${role}`;
  await ok(
    await ctx.post(`/api/invitations/${token}/register`, {
      data: { fullName: name, phone: uniquePhone(), password: PASSWORD, termsAccepted: true },
    }),
    "Davetle kayıt",
  );
  return new ApiUser(ctx, email, name);
}

// --- Kayıt yardımcıları (Sprint 2-3) ---------------------------------------------------------------

export type Contact = { id: string; displayName: string; balance: number };

export function createContact(
  user: ApiUser,
  data: {
    firstName: string;
    lastName?: string;
    phone?: string;
    email?: string | null;
    openingBalance?: { amount: number; direction: "Receivable" | "Payable"; date?: string } | null;
  } & Record<string, unknown>,
): Promise<Contact> {
  return user.post("/api/contacts", {
    lastName: null,
    companyName: null,
    isCustomer: true,
    isSupplier: false,
    phone: uniquePhone(),
    email: null,
    birthDate: null,
    address: null,
    city: null,
    district: null,
    source: null,
    notes: null,
    taxNumber: null,
    taxOffice: null,
    tagIds: null,
    consents: null,
    openingBalance: null,
    ...data,
  });
}

export type Product = { id: string; name: string; stockQuantity: number | null };

export function createProduct(
  user: ApiUser,
  data: { name: string; salePrice: number } & Record<string, unknown>,
): Promise<Product> {
  return user.post("/api/products", {
    type: "Product",
    categoryId: null,
    unit: "Piece",
    vatRate: 20,
    pricesIncludeVat: true,
    purchasePrice: null,
    barcode: null,
    durationMinutes: null,
    trackStock: true,
    criticalStockLevel: null,
    initialStock: 10,
    notes: null,
    ...data,
  });
}

export function line(productId: string | null, quantity = 1, extra: Record<string, unknown> = {}) {
  return { productId, description: null, quantity, unitPrice: null, vatRate: null, discount: null, ...extra };
}

export type Sale = { id: string; no: string; total: number };

export async function createSale(
  user: ApiUser,
  lines: ReturnType<typeof line>[],
  payments: { method: "Cash" | "Card" | "Transfer" | "Credit"; amount: number }[],
  contactId: string | null = null,
): Promise<Sale> {
  const result = await user.post<{ sale: Sale }>("/api/sales", {
    contactId,
    date: null,
    lines,
    discount: null,
    payments: payments.map((p) => ({ ...p, cashAccountId: null })),
    note: null,
  });
  return result.sale;
}

export type Quote = { id: string; no: string; total: number };

export function createQuote(
  user: ApiUser,
  contactId: string,
  lines: ReturnType<typeof line>[],
  discount: { type: "Amount" | "Percent"; value: number } | null = null,
): Promise<Quote> {
  return user.post("/api/quotes", { contactId, date: null, validUntil: null, lines, discount, notes: null });
}

// --- Platform yöneticisi (Sprint 6A) -------------------------------------------------------------------

/**
 * Platform yöneticisi (E2E API'sinde Platform:AdminEmails, scripts/start-api.mjs; adres koşuya ve çifte özel, çiftin bütün
 * testleri aynı hesabı paylaşır; işletmesi de olur). Projeler paralel koştuğu için: önce giriş dener, yoksa
 * kaydolur; aynı anda başka test kaydettiyse girişi ve e-posta doğrulamasını bekler.
 */
export async function platformAdmin(): Promise<ApiUser> {
  const ctx = await newApiContext();
  const PLATFORM_EMAIL = platformEmail(target().sms);
  const user = new ApiUser(ctx, PLATFORM_EMAIL, "KobiFabrika Destek");
  const login = () => ctx.post("/api/auth/login", { data: { email: PLATFORM_EMAIL, password: PASSWORD, rememberMe: false } });
  if (!(await login()).ok()) {
    const reg = await ctx.post("/api/auth/register", {
      data: { fullName: user.fullName, email: PLATFORM_EMAIL, phone: uniquePhone(), password: PASSWORD, termsAccepted: true },
    });
    if (reg.ok()) {
      await user.post("/api/setup/business", { name: "KobiFabrika Destek", sector: "Other", phone: "0212 555 44 33", city: "İstanbul", district: "Kadıköy" });
      await user.post("/api/setup/complete");
      await user.confirmEmail();
    } else {
      await expect.poll(async () => (await login()).ok(), { timeout: 20_000 }).toBe(true);
    }
  }
  await expect
    .poll(async () => (await user.get<{ user: { isPlatformAdmin: boolean }; business: unknown }>("/api/auth/me")), { timeout: 20_000 })
    .toMatchObject({ user: { isPlatformAdmin: true }, business: expect.anything() });
  user.emailConfirmed = true;
  return user;
}
