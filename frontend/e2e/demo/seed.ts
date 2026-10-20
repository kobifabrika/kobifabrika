import { createContact, createProduct, createSale, line, ownerWithBusiness } from "../support/api";
import { at, book } from "../support/appointments";
import { accounts, addDebit, addExpense, categories, istanbulDay } from "../support/finance";

/**
 * "Işıltı Kuaför" demo işletmesi (site görüntüleri ve rol turu ortak). Normal API uçlarıyla kurulur; bütün veriler uydurmadır:
 * adlar yaygın Türkçe adlardan, telefonlar +90 5xx test aralığından rastgele. 3 personel, 12 hizmet, 60 müşteri, bugün ve yarın
 * randevular, satışlar (nakit, kart, veresiye), vadeli borçlar, gider, açık online randevu sayfası.
 */
const firstNames = ["Ayşe", "Fatma", "Zeynep", "Elif", "Merve", "Esra", "Büşra", "Selin", "Derya", "Gamze", "Hande", "Seda", "Burcu", "Ceren", "Deniz"];
const lastNames = ["Yılmaz", "Kaya", "Demir", "Şahin", "Çelik", "Yıldız", "Aydın", "Öztürk", "Arslan", "Doğan", "Kılıç", "Aslan"];
const menFirst = ["Mehmet", "Ahmet", "Mustafa", "Emre", "Burak", "Can", "Murat", "Onur"];

const services: [string, number, number][] = [
  ["Kadın saç kesimi", 45, 450],
  ["Erkek saç kesimi", 30, 300],
  ["Fön", 30, 250],
  ["Saç boyama", 120, 1200],
  ["Röfle", 150, 1800],
  ["Keratin bakım", 120, 1500],
  ["Sakal tıraşı", 20, 150],
  ["Manikür", 45, 350],
  ["Pedikür", 60, 400],
  ["Kaş alma", 15, 100],
  ["Cilt bakımı", 60, 900],
  ["Gelin başı", 180, 3000],
];

export async function seedIsilti() {
  const owner = await ownerWithBusiness({ name: "Işıltı Kuaför", sector: "HairBarber", confirmEmail: true, fullName: "Ayşe Işık" });
  const week = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];
  await owner.put("/api/business/working-hours", {
    days: week.map((day) => ({ day, isClosed: false, opensAt: "09:00", closesAt: "20:00" })),
  });

  // Personel (renkli takvim kolonları) ve hizmetler.
  const staffSpec = [
    { fullName: "Elif Kaya", title: "Saç uzmanı", color: "Blue" },
    { fullName: "Mert Demir", title: "Berber", color: "Black" },
    { fullName: "Zeynep Arslan", title: "Güzellik uzmanı", color: "DarkGray" },
  ];
  const staff = [];
  for (const s of staffSpec) {
    const created = await owner.post<{ id: string }>("/api/staff", { ...s, phone: null, showInBooking: true, userId: null, isActive: true });
    await owner.put(`/api/staff/${created.id}/working-hours`, {
      days: week.map((day) => ({ day, ranges: [{ start: "09:00", end: "20:00" }] })),
    });
    staff.push(created.id);
  }
  const svc: { id: string; minutes: number; price: number }[] = [];
  for (const [name, minutes, price] of services) {
    const created = await owner.post<{ id: string }>("/api/products", {
      type: "Service",
      name,
      categoryId: null,
      unit: "Piece",
      vatRate: 20,
      pricesIncludeVat: true,
      salePrice: price,
      purchasePrice: null,
      barcode: null,
      durationMinutes: minutes,
      trackStock: false,
      criticalStockLevel: null,
      initialStock: null,
      notes: null,
    });
    svc.push({ id: created.id, minutes, price });
  }
  const shampoo = await createProduct(owner, { name: "Argan şampuan 400 ml", salePrice: 380, initialStock: 24, criticalStockLevel: 5 });

  // 60 müşteri.
  const contacts: string[] = [];
  for (let i = 0; i < 60; i++) {
    const man = i % 5 === 4;
    const first = man ? menFirst[i % menFirst.length]! : firstNames[i % firstNames.length]!;
    const last = lastNames[(i * 7) % lastNames.length]!;
    const c = await createContact(owner, {
      firstName: first,
      lastName: last,
      consents: { sms: i % 3 !== 0, email: false, call: true },
    });
    contacts.push(c.id);
  }

  // Bugünün ve yarının randevuları (personel başına sıralı saatler).
  const today = istanbulDay();
  const tomorrow = istanbulDay(1);
  const todayPlan: [number, string, number][] = [
    [0, "10:00", 0],
    [0, "11:00", 3],
    [0, "14:00", 4],
    [1, "10:30", 1],
    [1, "11:00", 6],
    [1, "13:30", 1],
    [1, "16:00", 1],
    [2, "12:00", 7],
    [2, "15:00", 10],
    [2, "17:30", 8],
  ];
  for (const [i, [s, time, service]] of todayPlan.entries()) {
    await book(owner, { contactId: contacts[i]!, staffId: staff[s]!, serviceId: svc[service]!.id, startAt: at(today, time) });
  }
  const tomorrowPlan: [number, string, number][] = [
    [0, "10:00", 5],
    [1, "10:00", 1],
    [1, "11:30", 6],
    [2, "14:00", 10],
    [0, "16:00", 0],
  ];
  for (const [i, [s, time, service]] of tomorrowPlan.entries()) {
    await book(owner, { contactId: contacts[20 + i]!, staffId: staff[s]!, serviceId: svc[service]!.id, startAt: at(tomorrow, time) });
  }

  // Satışlar (nakit, kart, veresiye) ve vadeli borçlar.
  await createSale(owner, [line(svc[0]!.id), line(shampoo.id)], [{ method: "Card", amount: 830 }], contacts[30]!);
  await createSale(owner, [line(svc[1]!.id), line(svc[6]!.id)], [{ method: "Cash", amount: 450 }], contacts[31]!);
  await createSale(owner, [line(svc[3]!.id)], [{ method: "Credit", amount: 1200 }], contacts[32]!);
  await createSale(owner, [line(svc[7]!.id), line(svc[8]!.id)], [{ method: "Cash", amount: 750 }], contacts[33]!);
  await addDebit(owner, contacts[34]!, 1250, -3);
  await addDebit(owner, contacts[35]!, 860, 0);
  await addDebit(owner, contacts[36]!, 2400, -12);
  const acc = await accounts(owner);
  const cats = await categories(owner);
  await addExpense(owner, { categoryId: Object.values(cats)[0]!, amount: 650, cashAccountId: acc["Nakit Kasa"]!.id, description: "Bakım malzemesi" });

  const settings = await owner.put<{ slug: string }>("/api/business/booking-settings", {
    enabled: true,
    autoConfirm: false,
    minNoticeMinutes: 60,
    maxDaysAhead: 30,
    slotMinutes: 30,
    reminderHoursBefore: 24,
    customerCancelHours: 2,
  });

  return { owner, staff, svc, contacts, slug: settings.slug, today, tomorrow };
}
