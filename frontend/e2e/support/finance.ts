import type { ApiUser } from "./api";

/** Sprint 4 finans verisini API ile hazırlama yardımcıları (testler arka kapı kullanmaz; normal uçlar). */

export type Account = { id: string; name: string; type: "Cash" | "Bank"; balance: number; isDefault: boolean };

export async function accounts(user: ApiUser): Promise<Record<string, Account>> {
  const list = await user.get<Account[]>("/api/cash-accounts");
  return Object.fromEntries(list.map((a) => [a.name, a]));
}

export async function categories(user: ApiUser): Promise<Record<string, string>> {
  const list = await user.get<{ id: string; name: string }[]>("/api/expense-categories");
  return Object.fromEntries(list.map((c) => [c.name, c.id]));
}

export function addExpense(
  user: ApiUser,
  data: { categoryId: string; amount: number; cashAccountId: string; kind?: "Expense" | "Income"; description?: string },
) {
  return user.post<{ id: string; accountBalance: number }>("/api/expenses", {
    kind: "Expense",
    date: null,
    description: null,
    documentNo: null,
    contactId: null,
    ...data,
  });
}

/** İstanbul günü ± gün ("YYYY-MM-DD"). */
export function istanbulDay(offset = 0): string {
  const today = new Intl.DateTimeFormat("en-CA", { timeZone: "Europe/Istanbul" }).format(new Date());
  const d = new Date(`${today}T00:00:00Z`);
  d.setUTCDate(d.getUTCDate() + offset);
  return d.toISOString().slice(0, 10);
}

/** Elle borç (vadeli). */
export function addDebit(user: ApiUser, contactId: string, amount: number, dueOffset: number, dateOffset = -30) {
  return user.post(`/api/contacts/${contactId}/ledger`, {
    kind: "Debit",
    amount,
    date: istanbulDay(dateOffset),
    dueDate: istanbulDay(dueOffset),
    description: "Veresiye",
  });
}

export function setIban(user: ApiUser) {
  return user.put("/api/business/payment-info", { iban: "TR400006200000000000000001", ibanHolderName: "Emir Kaya" });
}

/** "12.10.2026" biçimi. */
export const trDay = (iso: string) => iso.split("-").reverse().join(".");
