using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KobiFabrika.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Sprint6aMarketing : Migration
    {
        /// <summary>
        /// Kurulumu bitmiş, hoş geldin kredisi almamış her işletmeye bir kez 50 SMS kredisi (karar 2026-10-15). Tekrar çalıştırılabilir:
        /// Welcome hareketi olan işletme atlanır (ayrıca filtreli benzersiz indeks). Bakiye ve hareket aynı ifadede tutarlı yazılır.
        /// </summary>
        public const string BackfillWelcomeCreditsSql = """
            DECLARE @granted TABLE ([BusinessId] uniqueidentifier, [Balance] int);
            UPDATE b SET [SmsBalance] = b.[SmsBalance] + 50
            OUTPUT inserted.[Id], inserted.[SmsBalance] INTO @granted
            FROM [Businesses] b
            WHERE b.[SetupCompletedAt] IS NOT NULL AND b.[IsDeleted] = 0
              AND NOT EXISTS (SELECT 1 FROM [SmsCreditTransactions] t WHERE t.[BusinessId] = b.[Id] AND t.[Kind] = 'Welcome');
            INSERT INTO [SmsCreditTransactions] ([Id], [BusinessId], [Amount], [Kind], [BalanceAfter], [Note], [IsDeleted], [CreatedAt])
            SELECT NEWID(), g.[BusinessId], 50, 'Welcome', g.[Balance], N'Hoş geldin kredisi', 0, SYSUTCDATETIME() FROM @granted g;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AnonymizedAt",
                table: "Contacts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AnonymizedBy",
                table: "Contacts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsAnonymized",
                table: "Contacts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "OptOutCode",
                table: "Contacts",
                type: "varchar(8)",
                unicode: false,
                maxLength: 8,
                nullable: true,
                collation: "Latin1_General_100_BIN2");

            migrationBuilder.AlterColumn<string>(
                name: "IysStatus",
                table: "ContactConsents",
                type: "varchar(20)",
                unicode: false,
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IysError",
                table: "ContactConsents",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "IysSentAt",
                table: "ContactConsents",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IysBrandCode",
                table: "Businesses",
                type: "varchar(20)",
                unicode: false,
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SmsLowBalanceNotifiedAt",
                table: "Businesses",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SmsLowBalanceThreshold",
                table: "Businesses",
                type: "int",
                nullable: false,
                defaultValue: 50);

            migrationBuilder.AddColumn<DateTime>(
                name: "SmsOutOfCreditNotifiedAt",
                table: "Businesses",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Campaigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SegmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SegmentCode = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    Filter = table.Column<string>(type: "json", nullable: true),
                    TargetName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Body = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: false),
                    ScheduledAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RecipientsResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RecipientCount = table.Column<int>(type: "int", nullable: false),
                    SentCount = table.Column<int>(type: "int", nullable: false),
                    FailedCount = table.Column<int>(type: "int", nullable: false),
                    SkippedCount = table.Column<int>(type: "int", nullable: false),
                    CreditUsed = table.Column<int>(type: "int", nullable: false),
                    RanOutOfCredit = table.Column<bool>(type: "bit", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Campaigns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Campaigns_Segments_SegmentId",
                        column: x => x.SegmentId,
                        principalTable: "Segments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MarketingDailySends",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContactId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocalDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketingDailySends", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MarketingDailySends_Contacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "Contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SmsCreditTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    BalanceAfter = table.Column<int>(type: "int", nullable: false),
                    MessageLogId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BusinessId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmsCreditTransactions", x => x.Id);
                    table.CheckConstraint("CK_SmsCreditTransactions_Amount", "[Amount] <> 0");
                    table.CheckConstraint("CK_SmsCreditTransactions_BalanceAfter", "[BalanceAfter] >= 0");
                    table.ForeignKey(
                        name: "FK_SmsCreditTransactions_MessageLogs_MessageLogId",
                        column: x => x.MessageLogId,
                        principalTable: "MessageLogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CampaignRecipients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContactId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Phone = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    SkipReason = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    MessageLogId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Segments = table.Column<int>(type: "int", nullable: false),
                    ClaimedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ProcessedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    BusinessId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignRecipients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CampaignRecipients_Campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "Campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CampaignRecipients_Contacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "Contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CampaignRecipients_MessageLogs_MessageLogId",
                        column: x => x.MessageLogId,
                        principalTable: "MessageLogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_MessageLogs_CampaignRecipient",
                table: "MessageLogs",
                column: "RefId",
                unique: true,
                filter: "[RefType] = 'CampaignRecipient'");

            migrationBuilder.CreateIndex(
                name: "IX_Contacts_OptOutCode",
                table: "Contacts",
                column: "OptOutCode",
                unique: true,
                filter: "[OptOutCode] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ContactConsents_IysPending",
                table: "ContactConsents",
                column: "CreatedAt",
                filter: "[IysStatus] = 'Pending'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Businesses_SmsBalance",
                table: "Businesses",
                sql: "[SmsBalance] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Businesses_SmsLowBalanceThreshold",
                table: "Businesses",
                sql: "[SmsLowBalanceThreshold] BETWEEN 0 AND 100000");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignRecipients_BusinessId",
                table: "CampaignRecipients",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignRecipients_BusinessId_CampaignId_Status",
                table: "CampaignRecipients",
                columns: new[] { "BusinessId", "CampaignId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CampaignRecipients_BusinessId_ContactId",
                table: "CampaignRecipients",
                columns: new[] { "BusinessId", "ContactId" });

            migrationBuilder.CreateIndex(
                name: "IX_CampaignRecipients_CampaignId_ContactId",
                table: "CampaignRecipients",
                columns: new[] { "CampaignId", "ContactId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CampaignRecipients_ContactId",
                table: "CampaignRecipients",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignRecipients_MessageLogId",
                table: "CampaignRecipients",
                column: "MessageLogId");

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_BusinessId",
                table: "Campaigns",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_BusinessId_CreatedAt",
                table: "Campaigns",
                columns: new[] { "BusinessId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_Dispatch",
                table: "Campaigns",
                columns: new[] { "Status", "ScheduledAt" },
                filter: "[Status] IN ('Scheduled', 'Sending') AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_SegmentId",
                table: "Campaigns",
                column: "SegmentId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketingDailySends_BusinessId",
                table: "MarketingDailySends",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketingDailySends_BusinessId_ContactId_LocalDate",
                table: "MarketingDailySends",
                columns: new[] { "BusinessId", "ContactId", "LocalDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MarketingDailySends_BusinessId_LocalDate",
                table: "MarketingDailySends",
                columns: new[] { "BusinessId", "LocalDate" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketingDailySends_ContactId",
                table: "MarketingDailySends",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_SmsCreditTransactions_BusinessId_CreatedAt",
                table: "SmsCreditTransactions",
                columns: new[] { "BusinessId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SmsCreditTransactions_BusinessId_Kind_CreatedAt",
                table: "SmsCreditTransactions",
                columns: new[] { "BusinessId", "Kind", "CreatedAt" })
                .Annotation("SqlServer:Include", new[] { "Amount" });

            migrationBuilder.CreateIndex(
                name: "UX_SmsCreditTransactions_Refund",
                table: "SmsCreditTransactions",
                column: "MessageLogId",
                unique: true,
                filter: "[Kind] = 'Refund'");

            migrationBuilder.CreateIndex(
                name: "UX_SmsCreditTransactions_Welcome",
                table: "SmsCreditTransactions",
                column: "BusinessId",
                unique: true,
                filter: "[Kind] = 'Welcome'");

            migrationBuilder.Sql(BackfillWelcomeCreditsSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CampaignRecipients");

            migrationBuilder.DropTable(
                name: "MarketingDailySends");

            migrationBuilder.DropTable(
                name: "SmsCreditTransactions");

            migrationBuilder.DropTable(
                name: "Campaigns");

            migrationBuilder.DropIndex(
                name: "UX_MessageLogs_CampaignRecipient",
                table: "MessageLogs");

            migrationBuilder.DropIndex(
                name: "IX_Contacts_OptOutCode",
                table: "Contacts");

            migrationBuilder.DropIndex(
                name: "IX_ContactConsents_IysPending",
                table: "ContactConsents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Businesses_SmsBalance",
                table: "Businesses");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Businesses_SmsLowBalanceThreshold",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "AnonymizedAt",
                table: "Contacts");

            migrationBuilder.DropColumn(
                name: "AnonymizedBy",
                table: "Contacts");

            migrationBuilder.DropColumn(
                name: "IsAnonymized",
                table: "Contacts");

            migrationBuilder.DropColumn(
                name: "OptOutCode",
                table: "Contacts");

            migrationBuilder.DropColumn(
                name: "IysError",
                table: "ContactConsents");

            migrationBuilder.DropColumn(
                name: "IysSentAt",
                table: "ContactConsents");

            migrationBuilder.DropColumn(
                name: "IysBrandCode",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "SmsLowBalanceNotifiedAt",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "SmsLowBalanceThreshold",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "SmsOutOfCreditNotifiedAt",
                table: "Businesses");

            migrationBuilder.AlterColumn<string>(
                name: "IysStatus",
                table: "ContactConsents",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(20)",
                oldUnicode: false,
                oldMaxLength: 20,
                oldNullable: true);
        }
    }
}
