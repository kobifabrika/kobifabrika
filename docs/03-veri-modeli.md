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
| Campaign, CampaignRecipient, Automation | Kampanya, kampanya alıcısı, otomasyon |
| SmsCreditTransaction | SMS kredi hareketi |
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
- **WorkingHours:** DayOfWeek (tinyint), IsClosed, OpensAt, ClosesAt (`time`, işletmenin yerel saati; gece yarısını aşmaz); benzersiz (BusinessId, DayOfWeek). Varsayılan: Pzt-Cmt 09:00-19:00, Pazar kapalı. **Holidays (Sprint 5A):** Date (`date`), Name; filtreli benzersiz (BusinessId, Date).
- **Notifications:** UserId, Type (ör. `invitation.accepted`, `team.role_changed`, `team.joined`), Title, Body, Link, ReadAt.
- **AuditLogs:** UserId, Action (Login, InvitationSent, InvitationResent, InvitationRevoked, InvitationAccepted, RoleChanged, MemberRemoved, BusinessUpdated, SlugChanged, WorkingHoursUpdated, LogoChanged, ModulesChanged), EntityType, EntityId, Changes (`json`), IpAddress, At. Sadece eklenir: güncelleme/silme `AppDbContext`'te engellenir; soft delete yok.

## Müşteriler (Sprint 2, migration `Sprint2Contacts`)

- **Contacts:** IsCustomer, IsSupplier (ikisi birden olabilir), FirstName, LastName, CompanyName (ad/soyad/firmadan biri zorunlu), DisplayName (aranır), Phone (E.164; benzersiz `BusinessId + Phone`, filtre: Phone dolu ve silinmemiş), Email, BirthDate (`date`), Address, City, District, Source (Instagram/Google/Referral/WalkIn/Facebook/TikTok/Other; OnlineBooking sadece sistem yazar, Sprint 5), Notes (kısa sabit not), TaxNumber, TaxOffice, Balance (önbellek; sadece SQL'de atomik `Balance = Balance + @delta`), LastVisitAt (tamamlanmış satış ve randevuların en yenisi; `ContactVisits`), IsArchived, ArchivedAt, SmsConsent/EmailConsent/CallConsent (güncel izin; varsayılan kapalı). Gelmeyen müşteri uyarısı saklanmaz, randevulardan hesaplanır (`NoShowRule`). AvgVisitIntervalDays Sprint 6'da.
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

- **Sales:** Year, Seq, No (SAT-2026-000001), ContactId (opsiyonel), Date (UTC), SubTotal (brüt), LineDiscountTotal, DiscountTotal, DiscountType/DiscountValue (girilen indirim: TL/%), VatTotal, Total, Status (Completed/Cancelled), CancelledAt, CancelledBy, CancelReason, QuoteId, AppointmentId (Sprint 5A; filtreli benzersiz: tamamlanmış satışlar arasında randevu başına bir), Note.
- **SaleLines / QuoteLines** (ortak taban `DocumentLine`): ProductId (opsiyonel; serbest kalem), Description (ad kopyası), Unit, Quantity, UnitPrice (KDV dahil), LineDiscount, DiscountType/DiscountValue (girilen satır indirimi: TL/%), AllocatedDiscount (toplam indirimden pay), VatRate, VatAmount, LineTotal, SortOrder. SaleLines.StaffMemberId (Sprint 5A; randevudan çevrilen satışta hizmet satırına randevunun personeli, Sprint 7'de prim).
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

## Randevu (Sprint 5A, migration `Sprint5aAppointments`)

- **Appointments:** ContactId, StaffMemberId, ProductId (hizmet), StartAt, EndAt (UTC; CHECK bitiş > başlangıç ve en fazla 12 saat), Status (Pending/Confirmed/Completed/NoShow/Cancelled), Source (Panel/Online), Note, SaleId (satışa çevrildiyse; satış iptalinde temizlenir), ConfirmedAt, CompletedAt, NoShowAt, CancelledAt, CancelledBy, CancelSource (Business/Customer), CancelReason, TimeSetAt (saatin en son belirlendiği an; hatırlatma kuralı), ReminderSentAt, CustomerPackageId, PackageSessionUsed, ManageTokenHash (`binary(32)`, filtreli benzersiz; müşterinin iptal linki), ManageToken (Data Protection ile şifreli; aynı link SMS'lerde yeniden kullanılır). İndeksler: (BusinessId, StaffMemberId, StartAt) INCLUDE (EndAt, Status), (BusinessId, StartAt) INCLUDE (Status, StaffMemberId), (BusinessId, ContactId, StartAt). Çakışma kontrolü uygulamada (personel satırı kilidi; `AppointmentBooking`), iptal saati boşaltır. Hatırlatma işi için filtreli indeks `IX_Appointments_ReminderDue` (StartAt; ReminderSentAt boş, Onaylı).
- **StaffMembers** (Personel bölümüne bakın), **StaffWorkingHours:** StaffMemberId, DayOfWeek, StartsAt, EndsAt (yerel saat; gün başına birden çok satır = öğle arası; CHECK `StartsAt < EndsAt`). Personel işletmenin açık saatleri dışında randevu almaz (kesişim); eklenince işletmenin saatleri kopyalanır.
- **StaffServices:** StaffMemberId, ProductId (soft delete yok); personelin satırı yoksa bütün hizmetleri verir.
- **StaffTimeOffs:** StaffMemberId, StartAt, EndAt (UTC), Kind (Break/Leave/Other), Note. Sprint 7'de onaylanan izinler buraya yazılır.
- **Holidays:** Çekirdek bölümünde.
- **Businesses.StaffSeesOnlyOwnAppointments:** Personel rolü sadece kendi randevularını görsün (varsayılan false).

## Online randevu, SMS ve paketler (Sprint 5B, migration `Sprint5bBooking`)

- **Businesses** (ayrı BookingSettings tablosu yerine kolonlar): BookingEnabled (varsayılan false), BookingAutoConfirm (false), BookingMinNoticeMinutes (120; 0-10080), BookingMaxDaysAhead (30; 1-180), BookingSlotMinutes (15; 5/10/15/20/30/60), ReminderHoursBefore (24; 0 = kapalı, en fazla 72), CustomerCancelHours (2; 0 = müşteri iptal edemez, en fazla 72). Varsayılanlar migration'da; CHECK kısıtları.
- **PhoneVerifications:** Phone (E.164, sadece TR cep), CodeHash (`binary(32)`, SHA-256(kod + Id); kod 6 hane), ExpiresAt (+5 dk), Attempts (CHECK 0-3), VerifiedAt, ConsumedAt (tek kullanım), IpAddress, AppointmentId. İndeksler (Phone, CreatedAt) (numara sınırı bütün işletmelerde), (BusinessId, Phone, CreatedAt), (IpAddress, CreatedAt), CreatedAt (7 gün sonra silinir).
- **MessageLogs:** Channel (Sms), To, Body (1000), Purpose (Verification/AppointmentConfirmation/AppointmentReminder/AppointmentMoved/AppointmentCancelled/AppointmentRejected), Provider, ProviderMessageId, Status (Queued/Sent/Failed), Error, Segments, CreditCost (tahmini, segment başına 1), Billable (doğrulama kodu false), ContactId, RefType, RefId, SentAt. İndeksler (BusinessId, Purpose, CreatedAt) (günlük doğrulama üst sınırı), (BusinessId, ContactId, CreatedAt).
- **ServicePackages:** Name, ProductId (hizmet), SessionCount (1-500), Price (KDV dahil, KDV oranı hizmetinki), ValidityDays (1-1095), IsActive. Satılmış paket silinmez, pasife alınır.
- **CustomerPackages:** ContactId, ServicePackageId, ProductId ve Name (kopya), SaleId, SaleLineId, TotalSessions, UsedSessions (CHECK `0 ≤ Used ≤ Total`; sadece atomik UPDATE), ExpiresOn (`date`, son geçerli gün dahil = satış günü + geçerlilik), Status (Active/Cancelled), CancelledAt, CloseNote. Görünen durum hesaplanır: Aktif, Bitti, Süresi doldu, İptal.
- **SaleLines.ServicePackageId:** paket satırı (adet 1, stok etkisi yok); satışla birlikte CustomerPackage açılır.
- **ContactConsents:** online randevudaki SMS pazarlama izni `Source = OnlineBooking` ile yazılır (işaretsizse yazılmaz).

## Pazarlama (Sprint 6A, migration `Sprint6aMarketing`)

- **Businesses:** `SmsBalance` (int, 1 kredi = 1 segment; önbellek, sadece `SmsCredits` ile atomik, CHECK `>= 0`, SaveChanges yazmaz), `SmsLowBalanceThreshold` (50; 0 = kapalı, CHECK 0-100000), `SmsLowBalanceNotifiedAt`, `SmsOutOfCreditNotifiedAt` (bildirim bir kez; yüklemede sıfırlanır), `IysBrandCode` (İYS marka kodu; boşken izinler İYS'ye bildirilmez).
- **SmsCreditTransactions** (sadece eklenir): Amount (int, ≠ 0; artı yükleme/iade, eksi gönderim), Kind (TopUp/Welcome/Send/Refund/Adjustment), BalanceAfter (CHECK ≥ 0), MessageLogId, Note. Filtreli benzersiz: işletme başına bir `Welcome`, gönderim başına bir `Refund`. Σ Amount = bakiye. Veri adımı: kurulumu bitmiş işletmelere bir kez 50 hoş geldin kredisi (`BackfillWelcomeCreditsSql`, testli).
- **MessageLogs:** Status'a `NoCredit` (kredi yetmedi, sağlayıcıya gitmedi); Purpose'a `Campaign`, `Birthday`, `WinBack`, `VisitThanks`, `Survey` (pazarlama) ve `DueReminder` (bilgilendirme). Filtreli benzersiz `UX_MessageLogs_CampaignRecipient` (RefId; RefType = 'CampaignRecipient'): kampanya alıcısına en fazla bir SMS.
- **Campaigns:** Name, Kind (Manual/EmptySlot/RiskList), Status (Scheduled/Sending/Completed/Cancelled), SegmentId / SegmentCode / Filter (json; kayıtlı segmentin o anki kopyası), TargetName, Body (600; `{ad}`, `{isletme}`), ScheduledAt (09:00-21:00'a kaydırılmış), RequestedAt (kaydırıldıysa istenen zaman), StartedAt, RecipientsResolvedAt, CompletedAt, CancelledAt/By, RecipientCount, SentCount, FailedCount, SkippedCount, CreditUsed, RanOutOfCredit. Dağıtıcı indeksi `IX_Campaigns_Dispatch` (Status, ScheduledAt; Scheduled/Sending). Kanal sadece SMS (Faz 1; e-posta kampanyası sonra).
- **CampaignRecipients:** CampaignId, ContactId (benzersiz çift), Phone (gönderim anındaki; KVKK silmede null), Status (Pending/Sending/Sent/Failed/Skipped), SkipReason (NoPhone/NoConsent/OptedOut/DailyLimit/NoCredit/Cancelled/Unavailable/Interrupted), MessageLogId, Segments, ClaimedAt, ProcessedAt.
- **MarketingDailySends:** (BusinessId, ContactId, LocalDate) benzersiz; günde en fazla 1 pazarlama SMS'i (kampanya + otomasyon). Gönderimden önce eklenir, gönderilemezse silinir.
- **Contacts:** `OptOutCode` (8 karakter, bütün işletmelerde benzersiz, büyük/küçük harf duyarlı; /c/{kod}), `IsAnonymized`, `AnonymizedAt`, `AnonymizedBy` (KVKK). **ContactConsents:** Source'a `OptOutLink`, `OptOutSms`, `Iys`, `Anonymization`; `IysStatus` (Pending/Accepted/Rejected/NotRequired; SMS izni değişince Pending), `IysSentAt`, `IysError`; bekleyenler için filtreli indeks.
- **DocumentSequences:** Kind `Anonymous` (yıl 0): "Silinmiş müşteri #N".
- 6B'de: Automations, AutomationSends, Surveys; Contacts ziyaret istatistiği (VisitCount, AvgVisitIntervalDays).

## Personel

- **StaffMembers (Sprint 5A):** UserId (opsiyonel; işletmenin aktif üyesi, filtreli benzersiz `BusinessId + UserId`), FullName, Title, Phone (E.164), Color (takvim: Black/DarkGray/Gray/LightGray/Blue/Outline), ShowInBooking, IsActive, SortOrder. Sprint 7'de: StartDate, Salary (sadece Owner).
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
