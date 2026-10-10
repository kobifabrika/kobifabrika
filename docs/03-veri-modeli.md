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

- **Businesses:** Name, Slug (benzersiz, randevu linki), Sector (Service/Retail/General), Phone, Email, Address, City, District, LogoUrl, TimeZone, Iban, GoogleReviewUrl, WhatsappNumber, Settings (JSON), PlanId, SmsBalance.
- **AspNetUsers (Identity) + AppUser alanları:** FullName, Phone. **BusinessMemberships:** UserId, BusinessId, Role (Owner/Manager/Staff/Accountant), IsActive. (Bir kullanıcı ileride birden çok işletmeye üye olabilir.)
- **Invitations:** Email, Role, Token, ExpiresAt, AcceptedAt.
- **WorkingHours:** DayOfWeek, OpensAt, ClosesAt, IsClosed (işletme geneli). **Holidays:** Date, Name.
- **Notifications:** UserId, Type, Title, Body, Link, ReadAt.
- **AuditLogs:** UserId, Entity, EntityId, Action, Changes (JSON), At.
- **Consents:** ContactId, Channel (Sms/Email/Call), Granted, Source, At, IysStatus.

## Müşteriler

- **Contacts:** IsCustomer, IsSupplier, FirstName, LastName, CompanyName, Phone, Email, BirthDate, Address, Source, Notes, TaxNumber, TaxOffice, Balance (hesaplanmış, önbellek), LastVisitAt, AvgVisitIntervalDays, NoShowCount.
- **Tags, ContactTags.**
- **ContactNotes:** ContactId, Body.
- **Reminders:** ContactId (opsiyonel), AssignedUserId, DueAt, Title, DoneAt.
- **Segments:** Name, Definition (JSON filtre), IsSystem.

## Katalog ve stok

- **ProductCategories:** Name, ParentId.
- **Products:** Type (Product/Service), Name, CategoryId, Unit, VatRate, PurchasePrice, SalePrice, Barcode, DurationMinutes (hizmet), TrackStock, CriticalStockLevel, StockQuantity (önbellek), IsActive.
- **StockMovements:** ProductId, Type (In/Out/Adjustment/Sale), Quantity, UnitCost, RefType, RefId, Note.

## Satış ve teklif

- **Sales:** No (işletme içinde sıralı), ContactId (opsiyonel), StaffMemberId (opsiyonel), Date, SubTotal, DiscountTotal, VatTotal, Total, PaymentType (Cash/Card/Transfer/Credit), Status (Completed/Cancelled), AppointmentId (opsiyonel).
- **SaleLines:** SaleId, ProductId, Description, Quantity, UnitPrice, DiscountRate, VatRate, LineTotal, StaffMemberId.
- **Quotes:** No, ContactId, Date, ValidUntil, Status (Draft/Sent/Accepted/Rejected/Converted), PublicToken, totals, Notes, SaleId.
- **QuoteLines:** Sale ile aynı yapı.

## Finans

- **CashAccounts:** Name, Type (Cash/Bank), Iban, Balance (önbellek).
- **CashTransactions:** CashAccountId, Type (Collection/Payment/TransferIn/TransferOut/Expense/Income), Amount, Date, ContactId, ExpenseCategoryId, Description, RefType, RefId.
- **ExpenseCategories:** Name, Kind (Expense/Income).
- **LedgerEntries (cari hareket):** ContactId, Date, DueDate, Debit, Credit, Description, RefType (Sale/Collection/Payment/Opening), RefId. Bakiye = Σ(Debit − Credit).
- **PaymentRequests (tahsilat linki):** ContactId, Amount, PublicToken, Status (Open/ClaimedPaid/Confirmed/Cancelled), ClaimedAt, ConfirmedAt, CashTransactionId.

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
