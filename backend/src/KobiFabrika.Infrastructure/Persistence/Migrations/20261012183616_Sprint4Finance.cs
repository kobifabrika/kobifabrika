using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KobiFabrika.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Sprint4Finance : Migration
    {
        /// <summary>
        /// Gelir-gider kategorisi olmayan her işletmeye varsayılanları ekler (ExpenseCategory.DefaultList ile aynı; test edilir).
        /// Tekrar çalıştırılabilir: aynı tür ve addaki kategori varsa eklemez.
        /// </summary>
        public const string BackfillExpenseCategoriesSql = """
            INSERT INTO [ExpenseCategories] ([Id], [BusinessId], [Name], [Kind], [SystemKey], [IsActive], [SortOrder], [IsDeleted], [CreatedAt])
            SELECT NEWID(), b.[Id], c.[Name], c.[Kind], c.[SystemKey], 1, c.[SortOrder], 0, SYSUTCDATETIME()
            FROM [Businesses] b
            CROSS JOIN (VALUES
                (N'Kira', 'Expense', NULL, 1),
                (N'Elektrik-Su-Doğalgaz', 'Expense', NULL, 2),
                (N'İnternet-Telefon', 'Expense', NULL, 3),
                (N'Maaş', 'Expense', 'salary', 4),
                (N'Malzeme', 'Expense', NULL, 5),
                (N'Vergi-SGK', 'Expense', NULL, 6),
                (N'Diğer', 'Expense', NULL, 99),
                (N'Diğer gelir', 'Income', NULL, 99)) c([Name], [Kind], [SystemKey], [SortOrder])
            WHERE NOT EXISTS (SELECT 1 FROM [ExpenseCategories] e
                WHERE e.[BusinessId] = b.[Id] AND e.[Kind] = c.[Kind] AND e.[Name] = c.[Name] AND e.[IsDeleted] = 0);
            """;

        /// <summary>Varsayılan banka hesabı olmayan işletmede en eski banka hesabını varsayılan yapar (kart/havale satışları).</summary>
        public const string BackfillDefaultBankSql = """
            UPDATE a SET [IsDefault] = 1
            FROM [CashAccounts] a
            WHERE a.[Type] = 'Bank' AND a.[IsDeleted] = 0
              AND a.[Id] = (SELECT TOP 1 x.[Id] FROM [CashAccounts] x
                            WHERE x.[BusinessId] = a.[BusinessId] AND x.[Type] = 'Bank' AND x.[IsDeleted] = 0
                            ORDER BY x.[CreatedAt], x.[Id])
              AND NOT EXISTS (SELECT 1 FROM [CashAccounts] d
                              WHERE d.[BusinessId] = a.[BusinessId] AND d.[Type] = 'Bank' AND d.[IsDefault] = 1 AND d.[IsDeleted] = 0);
            """;

        /// <summary>Vadesiz eski veresiye satış borçlarına satış tarihi + 30 gün vade (karar günlüğü 2026-10-12).</summary>
        public const string BackfillSaleDueDatesSql = """
            UPDATE [LedgerEntries] SET [DueDate] = DATEADD(day, 30, [Date])
            WHERE [RefType] = 'Sale' AND [Debit] > 0 AND [DueDate] IS NULL;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DocumentNo",
                table: "CashTransactions",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ExpenseCategoryId",
                table: "CashTransactions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsReversal",
                table: "CashTransactions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "ReversalOfId",
                table: "CashTransactions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReversedAt",
                table: "CashTransactions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReversedBy",
                table: "CashTransactions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TransferId",
                table: "CashTransactions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IbanHolderName",
                table: "CashAccounts",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "CashAccounts",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "DefaultDueDays",
                table: "Businesses",
                type: "int",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<bool>(
                name: "DueDigestEnabled",
                table: "Businesses",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "LastDueDigestOn",
                table: "Businesses",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DueReminderLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContactId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    OldestDueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DueReminderLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DueReminderLogs_Contacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "Contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExpenseCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    SystemKey = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpenseCategories", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CashTransactions_BusinessId_Date_Type",
                table: "CashTransactions",
                columns: new[] { "BusinessId", "Date", "Type" })
                .Annotation("SqlServer:Include", new[] { "Amount", "IsReversal", "ExpenseCategoryId", "CashAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_CashTransactions_BusinessId_ReversalOfId",
                table: "CashTransactions",
                columns: new[] { "BusinessId", "ReversalOfId" },
                unique: true,
                filter: "[ReversalOfId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CashTransactions_BusinessId_TransferId",
                table: "CashTransactions",
                columns: new[] { "BusinessId", "TransferId" },
                filter: "[TransferId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CashTransactions_ExpenseCategoryId",
                table: "CashTransactions",
                column: "ExpenseCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_CashTransactions_ReversalOfId",
                table: "CashTransactions",
                column: "ReversalOfId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CashTransactions_Category",
                table: "CashTransactions",
                sql: "[Type] NOT IN ('Expense', 'Income') OR [ExpenseCategoryId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CashTransactions_Reversal",
                table: "CashTransactions",
                sql: "[IsReversal] = 0 OR [ReversalOfId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CashAccounts_BusinessId_Name",
                table: "CashAccounts",
                columns: new[] { "BusinessId", "Name" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_CashAccounts_BusinessId_Type_Default",
                table: "CashAccounts",
                columns: new[] { "BusinessId", "Type" },
                unique: true,
                filter: "[IsDefault] = 1 AND [IsDeleted] = 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Businesses_DefaultDueDays",
                table: "Businesses",
                sql: "[DefaultDueDays] BETWEEN 0 AND 365");

            migrationBuilder.CreateIndex(
                name: "IX_DueReminderLogs_BusinessId",
                table: "DueReminderLogs",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_DueReminderLogs_BusinessId_ContactId_SentAt",
                table: "DueReminderLogs",
                columns: new[] { "BusinessId", "ContactId", "SentAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DueReminderLogs_ContactId",
                table: "DueReminderLogs",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseCategories_BusinessId",
                table: "ExpenseCategories",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseCategories_BusinessId_Kind_Name",
                table: "ExpenseCategories",
                columns: new[] { "BusinessId", "Kind", "Name" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.AddForeignKey(
                name: "FK_CashTransactions_CashTransactions_ReversalOfId",
                table: "CashTransactions",
                column: "ReversalOfId",
                principalTable: "CashTransactions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CashTransactions_ExpenseCategories_ExpenseCategoryId",
                table: "CashTransactions",
                column: "ExpenseCategoryId",
                principalTable: "ExpenseCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(BackfillDefaultBankSql);
            migrationBuilder.Sql(BackfillExpenseCategoriesSql);
            migrationBuilder.Sql(BackfillSaleDueDatesSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CashTransactions_CashTransactions_ReversalOfId",
                table: "CashTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_CashTransactions_ExpenseCategories_ExpenseCategoryId",
                table: "CashTransactions");

            migrationBuilder.DropTable(
                name: "DueReminderLogs");

            migrationBuilder.DropTable(
                name: "ExpenseCategories");

            migrationBuilder.DropIndex(
                name: "IX_CashTransactions_BusinessId_Date_Type",
                table: "CashTransactions");

            migrationBuilder.DropIndex(
                name: "IX_CashTransactions_BusinessId_ReversalOfId",
                table: "CashTransactions");

            migrationBuilder.DropIndex(
                name: "IX_CashTransactions_BusinessId_TransferId",
                table: "CashTransactions");

            migrationBuilder.DropIndex(
                name: "IX_CashTransactions_ExpenseCategoryId",
                table: "CashTransactions");

            migrationBuilder.DropIndex(
                name: "IX_CashTransactions_ReversalOfId",
                table: "CashTransactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CashTransactions_Category",
                table: "CashTransactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CashTransactions_Reversal",
                table: "CashTransactions");

            migrationBuilder.DropIndex(
                name: "IX_CashAccounts_BusinessId_Name",
                table: "CashAccounts");

            migrationBuilder.DropIndex(
                name: "IX_CashAccounts_BusinessId_Type_Default",
                table: "CashAccounts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Businesses_DefaultDueDays",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "DocumentNo",
                table: "CashTransactions");

            migrationBuilder.DropColumn(
                name: "ExpenseCategoryId",
                table: "CashTransactions");

            migrationBuilder.DropColumn(
                name: "IsReversal",
                table: "CashTransactions");

            migrationBuilder.DropColumn(
                name: "ReversalOfId",
                table: "CashTransactions");

            migrationBuilder.DropColumn(
                name: "ReversedAt",
                table: "CashTransactions");

            migrationBuilder.DropColumn(
                name: "ReversedBy",
                table: "CashTransactions");

            migrationBuilder.DropColumn(
                name: "TransferId",
                table: "CashTransactions");

            migrationBuilder.DropColumn(
                name: "IbanHolderName",
                table: "CashAccounts");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "CashAccounts");

            migrationBuilder.DropColumn(
                name: "DefaultDueDays",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "DueDigestEnabled",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "LastDueDigestOn",
                table: "Businesses");
        }
    }
}
