# Veri modeli (Faz 1 taslağı)

Bu bir taslaktır; her sprint ilgili tabloları EF Core migration ile oluşturur ve gerekirse bu dosyayı günceller.

## Terim sözlüğü (kod ↔ arayüz)

| Kod | Arayüz |
|---|---|
| Business | İşletme |
| Contact (IsCustomer / IsSupplier) | Müşteri / Tedarikçi (cari) |
| Product (Type: Product / Service) | Ürün / Hizmet |
| Sale, SaleLine | Satış, satış kalemi |
| Quote | Teklif |
| LedgerEntry | Cari hareket |
| CashAccount | Kasa / Banka hesabı |
| CashTransaction | Tahsilat / Ödeme / Transfer |
| PaymentRequest | Tahsilat linki |
| Appointment | Randevu |
| ServicePackage, CustomerPackage | Paket tanımı, müşterinin aldığı paket (seans) |
| Campaign, Automation | Kampanya, otomasyon |
| Survey | Memnuniyet anketi |
| StaffMember | Personel |
| TimeEntry | Mesai kaydı |
| LeaveRequest | İzin talebi |
| CommissionRule | Prim kuralı |
| Subscription, Plan | Abonelik, paket |

## Ortak alanlar

Her kiracı tablosunda: `Id` (Guid), `BusinessId` (Guid, FK, indeksli), `CreatedAt`, `CreatedBy`, `UpdatedAt`, `IsDeleted`.
Para: `decimal(18,2)`. Oran: `decimal(5,2)`. Tarih: `datetime2` (UTC). Kısa metinler `nvarchar(n)`.
Benzersiz indeksler `BusinessId`'yi içerir (ör. `BusinessId + Phone`).

## Çekirdek

- **Businesses:** Name, Slug (benzersiz, randevu linki), Sector, Template (Service/Retail/General, sektörden türer), Modules (int bayrak: Appointments=1, Stock=2), SetupCompletedAt (null → kurulum bitmedi), Phone, Email, Address, City, District, LogoUrl, TimeZone, Iban (TR, boşluksuz; tahsilat linki için), IbanHolderName (Sprint 2), GoogleReviewUrl, WhatsappNumber, Settings (JSON), PlanId (Sprint 8), SmsBalance.
  - Sektör → şablon: HairBarber (Kuaför/Berber), BeautyCare (Güzellik ve Bakım), ClinicHealth (Klinik/Sağlık), SportsFitness (Spor ve Fitness), AutoService (Oto Servis) → **Service**; RetailStore (Perakende/Mağaza), CafeRestaurant (Kafe/Restoran) → **Retail**; Other (Diğer) → **General**. Kod: `Domain/Businesses/SectorCatalog.cs`.
  - Varsayılan modüller: Service → sadece Randevular; Retail → sadece Stok; General → ikisi de. Ayarlar'dan değiştirilir; sektör sonradan değişince modüller korunur.
- **AspNetUsers (Identity) + AppUser alanları:** FullName, PhoneNumber (E.164), CreatedAt, LastBusinessId (girişte açılacak işletme; cookie'ye claim olarak yazılır). **BusinessMemberships:** UserId, BusinessId, Role (Owner/Manager/Staff/Accountant), IsActive; benzersiz (UserId, BusinessId). Kiracı filtresi dışında (`[TenantExempt]`); sorgular UserId veya BusinessId ile açıkça süzülür.
- **UserConsents (kiracı dışı, kullanıcı düzeyi):** UserId, Kind (KvkkPrivacyNotice), Version (ör. "2026-10"), AcceptedAt, IpAddress. Kayıtta ve davetle kayıtta yazılır.
- **Invitations:** Email, Role, TokenHash (`binary(32)`, SHA-256; ham token sadece e-postadaki linkte), ExpiresAt (7 gün), InvitedByUserId, AcceptedAt, AcceptedByUserId, RevokedAt. Durum (Pending/Accepted/Revoked/Expired) hesaplanır. Benzersiz TokenHash.
- **WorkingHours:** DayOfWeek (tinyint), IsClosed, OpensAt, ClosesAt (`time`, işletmenin yerel saati); benzersiz (BusinessId, DayOfWeek). Varsayılan: Pzt-Cmt 09:00-19:00, Pazar kapalı. **Holidays:** Date, Name (Sprint 5).
- **Notifications:** UserId, Type (ör. `invitation.accepted`, `team.role_changed`, `team.joined`), Title, Body, Link, ReadAt.
- **AuditLogs:** UserId, Action (Login, InvitationSent, InvitationResent, InvitationRevoked, InvitationAccepted, RoleChanged, MemberRemoved, BusinessUpdated, SlugChanged, WorkingHoursUpdated, LogoChanged, ModulesChanged), EntityType, EntityId, Changes (`json`), IpAddress, At. Sadece eklenir: güncelleme/silme `AppDbContext`'te engellenir; soft delete yok.

## Müşteriler (Sprint 2, migration `Sprint2Contacts`)

- **Contacts:** IsCustomer, IsSupplier (ikisi birden olabilir), FirstName, LastName, CompanyName (ad/soyad/firmadan biri zorunlu), DisplayName (aranır), Phone (E.164; benzersiz `BusinessId + Phone`, filtre: Phone dolu ve silinmemiş), Email, BirthDate (`date`), Address, City, District, Source (Instagram/Google/Referral/WalkIn/Facebook/TikTok/Other), Notes (kısa sabit not), TaxNumber, TaxOffice, Balance (önbellek; sadece SQL'de atomik `Balance = Balance + @delta`), LastVisitAt (Sprint 3/5'te dolar), IsArchived, ArchivedAt, SmsConsent/EmailConsent/CallConsent (güncel izin; varsayılan kapalı). AvgVisitIntervalDays, NoShowCount Sprint 5/6'da.
- **ContactConsents** (izin geçmişi, sadece eklenir): ContactId, Channel (Sms/Email/Call), Granted, Source (Panel/Import/OnlineBooking), At, IysStatus.
- **Tags:** Name (benzersiz, işletme içinde), Color (Gray/Black/Blue/Outline). **ContactTags:** ContactId, TagId (soft delete yok).
- **ContactNotes:** ContactId, Body.
- **Reminders:** ContactId (opsiyonel), AssignedUserId, DueAt (UTC), Title, DoneAt, NotifiedAt (Hangfire bildirimi bir kez).
- **Segments:** Name, Definition (`json`: type, tagIds, source, balance, inactiveDays). Hazır 4 segment kodda (`inactive-60`, `debtors`, `birthday-this-month`, `new-30`), tabloda değil.
- **ImportJobs:** FileName, Status, Headers (`json`), Rows (`json`, ≤5000), Result (`json`), ExpiresAt (24 saat; saatlik iş siler).

## Katalog ve stok (Sprint 3, migration `Sprint3Sales`)

Miktarlar `decimal(18,3)` (kg, lt, m için).

- **VatRates (global, işletmeye ait değil):** Rate (PK, `decimal(5,2)`), Name, SortOrder, IsActive. Seed: %0, %1, %10, %20. Products.VatRate buna FK.
- **ProductCategories:** Name (işletme içinde benzersiz), ParentId (şimdilik kullanılmaz). Kurulumda sektöre göre örnek kategoriler.
- **Products:** Type (Product/Service), Name, CategoryId, Unit (Piece/Kg/Liter/Meter/Package/Hour/Session), VatRate, PricesIncludeVat (varsayılan true), PurchasePrice, SalePrice, Barcode (benzersiz `BusinessId + Barcode`, filtreli), DurationMinutes (hizmet), TrackStock (hizmette hep false), CriticalStockLevel, StockQuantity (önbellek; sadece `StockService` ile atomik), LowStockNotifiedAt, IsActive, Notes.
- **StockMovements:** ProductId, Type (In/Out/Adjustment/Sale/SaleCancel), Quantity (işaretli), QuantityAfter, UnitCost, Date (`date`), SaleId, Note. Silinmez.
- **DocumentSequences:** Kind (Sale/Quote), Year, LastValue; benzersiz (BusinessId, Kind, Year). Soft delete yok. Numara belgeyle aynı transaction'da `MERGE … OUTPUT` ile alınır.

## Satış ve teklif (Sprint 3)

Tutarlar KDV dahil; KDV satır tutarının içinden ayrılır. Hesap: `Domain/Sales/SaleCalculator.cs` (frontend `src/lib/sales.ts` aynı algoritma ve test vektörleri).

- **Sales:** Year, Seq, No (SAT-2026-000001), ContactId (opsiyonel), Date (UTC), SubTotal (brüt), LineDiscountTotal, DiscountTotal, DiscountType/DiscountValue (girilen indirim: TL/%), VatTotal, Total, Status (Completed/Cancelled), CancelledAt, CancelledBy, CancelReason, QuoteId, Note. StaffMemberId ve AppointmentId Sprint 5/7'de.
- **SaleLines / QuoteLines** (ortak taban `DocumentLine`): ProductId (opsiyonel; serbest kalem), Description (ad kopyası), Unit, Quantity, UnitPrice (KDV dahil), LineDiscount, DiscountType/DiscountValue (girilen satır indirimi: TL/%), AllocatedDiscount (toplam indirimden pay), VatRate, VatAmount, LineTotal, SortOrder.
- **SalePayments:** SaleId, Method (Cash/Card/Transfer/Credit), Amount, CashAccountId, CashTransactionId, LedgerEntryId. Bölünmüş ödemede birden fazla; toplam = satış tutarı.
- **Businesses.ReceiptWidth:** fiş genişliği (mm), 58 veya 80 (varsayılan 80).
- **Quotes:** Year, Seq, No (TKL-2026-0001), ContactId, Date, ValidUntil (varsayılan +15 gün), Status (Draft/Sent/Accepted/Rejected/Converted), TokenHash + ProtectedToken (tahsilat linkiyle aynı), SentAt, LastViewedAt, RespondedAt, ResponseNote, RespondedVia (Link/Panel), toplamlar, DiscountType/DiscountValue (girilen toplam indirim), Notes, SaleId.
- Mevcut tablolara: **CashTransactions.SaleId** ve türler `SaleIncome`/`SaleRefund`; **LedgerEntries.RefType** `Sale`/`SaleCancel`; **ImportJobs.Kind** (Contacts/Products).

## Finans (Sprint 2 + Sprint 4, migration `Sprint4Finance`)

- **CashAccounts:** Name (işletme içinde benzersiz), Type (Cash/Bank), Iban, IbanHolderName, Balance (önbellek, atomik), IsDefault (**tip başına bir varsayılan**; filtreli benzersiz indeks), IsActive (bakiyesi 0 ve varsayılan olmayan hesap pasife alınır; pasif hesaba hareket yazılmaz). Her işletmede "Nakit Kasa" ve "Banka" (kurulumda ikisi de varsayılan; mevcut işletmelerde en eski banka migration'da varsayılan yapıldı).
- **CashTransactions:** CashAccountId, Type (Collection/Payment/TransferIn/TransferOut/Expense/Income/SaleIncome/SaleRefund/OpeningIn/OpeningOut), Amount (> 0, check), Date (`date`), ContactId, Description, PaymentRequestId, SaleId, ExpenseCategoryId (Expense/Income'da zorunlu, check), DocumentNo, TransferId (transferin iki satırı), **ters kayıt:** ReversalOfId, IsReversal (check: ters kayıtsa ReversalOfId dolu), ReversedAt, ReversedBy. Silinmez; ters kayıt aynı tip ve tutarla bugünün tarihiyle yazılır, bakiyeye ters yönde işler, toplamlarda kendi tipinden düşülür. Bir hareketin en fazla bir ters kaydı (filtreli benzersiz `BusinessId + ReversalOfId`). İndeksler: `(BusinessId, Date, Type)` INCLUDE (Amount, IsReversal, ExpenseCategoryId, CashAccountId), `(BusinessId, TransferId)`.
- **ExpenseCategories:** Name (tür içinde benzersiz), Kind (Expense/Income), SystemKey (`salary`: maaş kuralı ad değişse de çalışır), IsActive, SortOrder. Varsayılanlar: Kira, Elektrik-Su-Doğalgaz, İnternet-Telefon, Maaş, Malzeme, Vergi-SGK, Diğer; gelir: Diğer gelir (kurulumda; mevcut işletmelere `BackfillExpenseCategoriesSql`).
- **LedgerEntries (cari hareket):** ContactId, Date (`date`), DueDate, Debit, Credit (biri > 0, check), Description, RefType (Opening/Manual/Collection/Sale/SaleCancel/Payment/PaymentReversal/CollectionReversal), RefId. Bakiye = Σ(Debit − Credit); pozitif = müşteri borçlu (panelde "Alacak"). Veresiye satışta DueDate = satış günü + `DefaultDueDays` (satışta değiştirilebilir); eski veresiye satışlara migration'da satış tarihi + 30. Ödeme ve ters kayıtlarda RefId kasa hareketinin Id'si.
- **DueReminderLogs:** ContactId, Channel (WhatsApp/Sms), Amount, OldestDueDate, SentAt (CreatedBy = gönderen). Panelde WhatsApp/SMS linkine tıklanınca yazılır.
- **Businesses (Sprint 4):** DefaultDueDays (0-365, varsayılan 30), DueDigestEnabled (varsayılan açık), LastDueDigestOn (`date`; günlük bildirim bir kez).
- **Vade hesabı (FIFO, `Domain/Finance/ReceivableAllocator.cs`):** borç kalemleri vadeye (yoksa hareket tarihine) göre sıralanır, alacaklar en eskiden düşülür; satış iptali önce kendi satışını kapatır; tahsilatın ters kaydı geri aldığı tahsilatı nötrler (yeni borç kalemi sayılmaz). Sadece bakiyesi > 0 olan cariler, iki sorguda (`Infrastructure/Finance/ReceivableQueries.cs`).
- **PaymentRequests (tahsilat linki):** ContactId, Amount, Note, TokenHash (`binary(32)`, SHA-256; ham token sadece linkte), ExpiresAt (30 gün), Status (Open/ClaimedPaid/Confirmed/Cancelled), ClaimedAt, ClaimNote, ConfirmedAt, ConfirmedBy, CashTransactionId, CancelledAt.
- **StatementShares (ekstre linki):** ContactId, From, To, TokenHash, ExpiresAt (30 gün), Channel (Link/WhatsApp/Email), LastViewedAt.

## Randevu

- **Appointments:** ContactId, StaffMemberId, ProductId (hizmet), StartAt, EndAt, Status (Pending/Confirmed/Completed/NoShow/Cancelled), Source (Panel/Online), Note, CustomerPackageId (opsiyonel), SaleId (opsiyonel), ReminderSentAt.
- **StaffWorkingHours:** StaffMemberId, DayOfWeek, StartsAt, EndsAt.
- **ServicePackages:** Name, ProductId (hizmet), SessionCount, Price, ValidityDays.
- **CustomerPackages:** ContactId, ServicePackageId, SaleId, TotalSessions, UsedSessions, ExpiresAt.
- **BookingSettings:** AutoConfirm, MinNoticeHours, MaxDaysAhead, SlotMinutes, ReminderHoursBefore.

## Pazarlama

- **Campaigns:** Name, Channel (Sms/Email/WhatsappLink), SegmentId, Body, ScheduledAt, SentAt, Status, RecipientCount, CreditCost, Kind (Manual/EmptySlot).
- **CampaignRecipients:** CampaignId, ContactId, Status (Queued/Sent/Failed/Skipped-NoConsent), MessageLogId.
- **Automations:** Type (Birthday/WinBack/AfterVisitThanks/Survey/DueReminder), IsEnabled, Settings (JSON: gün sayısı, mesaj şablonu).
- **MessageLogs:** Channel, To, Body, Provider, ProviderMessageId, Status, CreditCost, ContactId, RefType, RefId.
- **SmsCreditTransactions:** Amount (+/−), Reason, RefId.
- **Surveys:** AppointmentId, ContactId, PublicToken, Score (1-5), Comment, AnsweredAt, RedirectedToGoogle.

## Personel

- **StaffMembers:** UserId (opsiyonel), FullName, Title, Phone, StartDate, Salary (sadece Owner), Color (takvim), ShowInBooking, IsActive.
- **TimeEntries:** StaffMemberId, ClockInAt, ClockOutAt, Source (Self/Manager), Note.
- **LeaveTypes:** Name, IsPaid, AnnualDays.
- **LeaveRequests:** StaffMemberId, LeaveTypeId, StartDate, EndDate, Days, Status (Pending/Approved/Rejected), ApprovedBy, Note.
- **LeaveBalances:** StaffMemberId, Year, LeaveTypeId, Entitled, Used.
- **CommissionRules:** StaffMemberId (opsiyonel, boşsa herkes), AppliesTo (Service/Product/All), ProductId/CategoryId (opsiyonel), Type (Percent/Fixed), Value.
- **Commissions:** StaffMemberId, SaleLineId, Amount, Period (yyyy-MM).

## Abonelik (kiracı dışı tablolar dahil)

- **Plans (global):** Code (Cirak/Kalfa/Usta), Name, MonthlyPrice, YearlyPrice, MaxUsers, MaxContacts, MaxAppointmentsPerMonth, Features (JSON).
- **Subscriptions:** BusinessId, PlanId, Status (Trial/Active/PastDue/Cancelled/Expired), Period (Monthly/Yearly), CurrentPeriodEnd, CancelAtPeriodEnd, ProviderSubscriptionRef, RenewalReminderSentAt.
- **Payments:** SubscriptionId, Amount, Status, ProviderRef, PaidAt.
- Abonelik bitince `Business` salt okunur moda geçer (yazma endpoint'leri 402/403 döner, okuma ve dışa aktarma çalışır).
