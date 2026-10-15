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
- **SaleLines / QuoteLines** (ortak taban `DocumentLine`): ProductId (opsiyonel; serbest kalem), Description (ad kopyası), Unit, Quantity, UnitPrice (KDV dahil), LineDiscount, DiscountType/DiscountValue (girilen satır indirimi: TL/%), AllocatedDiscount (toplam indirimden pay), VatRate, VatAmount, LineTotal, SortOrder. SaleLines.StaffMemberId (Sprint 5A; randevudan çevrilen satışta hizmet satırına randevunun personeli; Sprint 7B'den itibaren her satırda seçilir ve prim buna yazılır).
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

## Otomasyon, anket, ziyaret (Sprint 6B, migration `Sprint6bAutomations`)

- **Automations:** Type (Birthday/WinBack/VisitThanks/Survey/DueReminder; işletme başına bir, filtreli benzersiz), IsEnabled (varsayılan kapalı; satır yokken varsayılanlar gösterilir), Body (400; `{ad}`, `{isletme}`, ankette `{link}`, vadede `{tutar}` ve `{link}`), SendAtLocal (`time`, günlük türler; 09:00-21:00), InactiveDays (X, 7-730), RepeatDays (Y, 7-730), DelayMinutes (teşekkür/anket, 0-4320), EnabledAt (açılmadan önceki ziyaretlere gitmez), LastRunOn (günlük tur bir kez, koşullu UPDATE).
- **AutomationSends:** Type, ContactId, Key (doğum günü: yıl; geri kazanma/vade: gün; teşekkür/anket: randevu Id), MessageLogId, SkipReason; benzersiz (BusinessId, Type, ContactId, Key) → aynı kişiye aynı anahtarla bir kez.
- **Surveys:** AppointmentId (işletmede benzersiz), ContactId, StaffMemberId, CodeHash (`binary(32)`, global benzersiz; kod 10 karakter), ProtectedCode, ExpiresAt (+7 gün), Score (CHECK 1-5; tek kez, koşullu UPDATE), AnsweredAt, Comment (1000; 1-3 puanda, 24 saat içinde, bir kez), CommentedAt, GoogleClickedAt. Gitmeyen anket satırı silinir.
- **Contacts:** VisitCount, FirstVisitOn (`date`), AvgVisitIntervalDays (`decimal(7,2)`); farklı İstanbul günleri (aynı gün satış + randevu = 1). `ContactVisits.RecomputeAsync` ve veri adımı `BackfillVisitStatsSql` (testli). Risk indeksi (BusinessId, VisitCount, LastVisitAt) INCLUDE AvgVisitIntervalDays. Hazır segment `at-risk` (Kaybedilme riski).

## Personel (Sprint 5A + Sprint 7A, migration `Sprint7aStaffTime`; prim Sprint 7B, migration `Sprint7bCommissions`)

- **StaffMembers (Sprint 5A):** UserId (opsiyonel; işletmenin aktif üyesi, filtreli benzersiz `BusinessId + UserId`), FullName, Title, Phone (E.164), Color (takvim: Black/DarkGray/Gray/LightGray/Blue/Outline), ShowInBooking, IsActive, SortOrder. **Sprint 7A:** StartDate (`date`), BirthDate (`date`, isteğe bağlı; yaş kuralı için), Notes (1000), Salary (`decimal(18,2)`, CHECK ≥ 0; personel yanıtında yok, ayrı uç `/api/staff/{id}/salary`, Sahip yazar, Sahip ve Mali Müşavir görür), SalaryBasis (Gross/Net), LeaveAccrualFrom (`date`; işe başlama tarihi girildiği gün, otomatik yıllık izin hakkı bu günden sonraki yıl dönümlerinde). TC kimlik no yok. Personel rolüne StartDate/BirthDate/Notes null döner.
- **CashTransactions.StaffMemberId (7A):** maaş giderinin personeli (sadece maaş kategorisinde; ters kayıt taşır). Filtreli indeks `(BusinessId, StaffMemberId, Date)`.
- **Businesses.OvertimeWeeklyHours (7A):** `decimal(4,1)`, varsayılan 45 (migration), CHECK 1-80.
- **TimeEntries (7A):** StaffMemberId, ClockInAt, ClockOutAt (UTC, dakikaya yuvarlı), InSource / OutSource (Self/Manager), ForgottenAt ("çıkış unutuldu"), Note (200). CHECK çıkış > giriş ve en fazla 24 saat. Filtreli benzersiz `UX_TimeEntries_Open` `(BusinessId, StaffMemberId)` (`ClockOutAt IS NULL AND ForgottenAt IS NULL AND IsDeleted = 0`): açık mesaiye ikinci giriş veritabanında da engellenir; unutulmuş kayıt yeni girişi engellemez. İndeksler `(BusinessId, ClockInAt)` INCLUDE (StaffMemberId, ClockOutAt, ForgottenAt), `(BusinessId, StaffMemberId, ClockInAt)`.
- **LeaveTypes (7A):** Name (50, işletmede benzersiz), IsPaid, SystemKey (`annual`: yıllık izin bakiyesinden düşer, işletmede bir tane, silinemez ve pasife alınamaz), IsActive, SortOrder. Varsayılanlar (kurulumda; mevcut işletmelere `BackfillLeaveTypesSql`, testli): Yıllık izin (ücretli), Mazeret izni (ücretli), Ücretsiz izin, Hastalık/rapor (ücretsiz; ücreti çoğunlukla SGK öder). Taslaktaki `AnnualDays` yerine yasal hesap (`AnnualLeaveRule`).
- **LeaveRequests (7A):** StaffMemberId, LeaveTypeId, StartDate, EndDate (`date`), HalfDay (None/Morning/Afternoon; CHECK sadece tek günde), Days (`decimal(4,1)`, CHECK ≥ 0,5; kapalı gün, tatil ve personelin çalışmadığı gün sayılmaz), Status (Pending/Approved/Rejected/Cancelled), Note (300; sağlık bilgisi yazılmaz), RequestedBy, DecidedBy/At, DecisionNote, CancelledBy/At. İndeksler `(BusinessId, StaffMemberId, StartDate)`, `(BusinessId, Status, StartDate)`.
- **LeaveBalanceEntries (7A, sadece eklenir):** StaffMemberId, Kind (Opening/Accrual/Adjustment/Usage/UsageReversal), Days (`decimal(5,1)`, işaretli, ≠ 0), ServiceYear (Accrual), LeaveRequestId, Note. Filtreli benzersiz `UX_LeaveBalanceEntries_Accrual` (hizmet yılı başına bir) ve `UX_LeaveBalanceEntries_Request` (iznin kullanımı ve iadesi birer kez). Bakiye = Σ Days (devreder; eksi = avans izin). Taslaktaki `LeaveBalances (Entitled, Used)` yerine.
- **StaffTimeOffs.LeaveRequestId (7A):** onaylı izinden gelen kapalı zaman (Kind = Leave, not kopyalanmaz); izin iptalinde silinir, kapalı zaman ekranından silinemez (409 `leave_managed`). Kapalı zaman formunda "İzin" türü yok (400).
- **CommissionRules (7B):** StaffMemberId (opsiyonel; boşsa herkes), Target (All/Service/Product/Category/Item), CategoryId (sadece Category), ProductId (sadece Item; ürün veya hizmet), Type (Percent/Fixed), Value (`decimal(18,2)`; yüzde 0-100, sabit = adet başına TL), IsActive. CHECK: türe göre değer ve hedefe göre alan dolu/boş. Filtreli benzersiz `UX_CommissionRules_Active` `(BusinessId, StaffMemberId, Target, CategoryId, ProductId)` (aktif ve silinmemiş; NULL'lar eşit: aynı kural iki kez girilmez). Silinen personelin kuralları da silinir (soft delete). Taslaktaki `AppliesTo` yerine `Target`.
- **Commissions (7B, sadece eklenir):** StaffMemberId, SaleId, SaleLineId, CommissionRuleId, RuleText (300; kuralın o anki metni), Base (KDV hariç, indirim sonrası satır tutarı), Type, Rate, Quantity (`decimal(18,3)`), Amount (işaretli; CHECK Accrual > 0, Reversal < 0 ve ReversalOfId dolu), Kind (Accrual/Reversal), ReversalOfId, OccurredOn (İstanbul günü: tahakkukta satış günü, iptalde iptal günü), Period (`char(7)`, yyyy-MM). Benzersiz `UX_Commissions_Line` `(BusinessId, SaleLineId, Kind)`; indeksler `(BusinessId, Period, StaffMemberId)` INCLUDE Amount, `(BusinessId, OccurredOn)` INCLUDE (StaffMemberId, Amount), `(BusinessId, SaleId)`. Geçmiş satışlara prim yazılmadı.
- **SaleLines.StaffMemberId (7B'den itibaren):** her satış satırında seçilebilir (aktif personel; hızlı satışta varsayılan satışı yapanın bağlı personel kaydı); boşsa personelsiz satır (prim yok). Teklif satırlarında yok.
- **ContactConsents.MessageLogId (7B):** çıkış linkiyle geri alınan iznin bağlandığı pazarlama SMS'i (müşteriye son 7 günde giden en son; FK, filtreli indeks). **Campaigns.OptOutsTracked (7B):** çıkış kampanyaya bağlanabiliyor mu (migration'da mevcut kampanyalar false, yeni kampanyalar true; raporda false ise "—").

## Abonelik (kiracı dışı tablolar dahil)

- **Plans (global):** Code (Cirak/Kalfa/Usta), Name, MonthlyPrice, YearlyPrice, MaxUsers, MaxContacts, MaxAppointmentsPerMonth, Features (JSON).
- **Subscriptions:** BusinessId, PlanId, Status (Trial/Active/PastDue/Cancelled/Expired), Period (Monthly/Yearly), CurrentPeriodEnd, CancelAtPeriodEnd, ProviderSubscriptionRef, RenewalReminderSentAt.
- **Payments:** SubscriptionId, Amount, Status, ProviderRef, PaidAt.
- Abonelik bitince `Business` salt okunur moda geçer (yazma endpoint'leri 402/403 döner, okuma ve dışa aktarma çalışır).
