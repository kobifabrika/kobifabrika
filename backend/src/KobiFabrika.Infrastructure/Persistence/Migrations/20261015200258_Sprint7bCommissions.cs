using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KobiFabrika.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Sprint7bCommissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MessageLogId",
                table: "ContactConsents",
                type: "uniqueidentifier",
                nullable: true);

            // Mevcut kampanyalarda çıkış bağlantısı yok (raporda "—"); yeni kampanyalar uygulamadan true yazar.
            migrationBuilder.AddColumn<bool>(
                name: "OptOutsTracked",
                table: "Campaigns",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "CommissionRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StaffMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Target = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    CategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Type = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Value = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommissionRules", x => x.Id);
                    table.CheckConstraint("CK_CommissionRules_Target", "([Target] = 'Category' AND [CategoryId] IS NOT NULL AND [ProductId] IS NULL) OR ([Target] = 'Item' AND [ProductId] IS NOT NULL AND [CategoryId] IS NULL) OR ([Target] IN ('All', 'Service', 'Product') AND [CategoryId] IS NULL AND [ProductId] IS NULL)");
                    table.CheckConstraint("CK_CommissionRules_Value", "([Type] = 'Percent' AND [Value] >= 0 AND [Value] <= 100) OR ([Type] = 'Fixed' AND [Value] >= 0)");
                    table.ForeignKey(
                        name: "FK_CommissionRules_ProductCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "ProductCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommissionRules_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommissionRules_StaffMembers_StaffMemberId",
                        column: x => x.StaffMemberId,
                        principalTable: "StaffMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Commissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StaffMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SaleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SaleLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CommissionRuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RuleText = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Base = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ReversalOfId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OccurredOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Period = table.Column<string>(type: "char(7)", unicode: false, fixedLength: true, maxLength: 7, nullable: false),
                    BusinessId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Commissions", x => x.Id);
                    table.CheckConstraint("CK_Commissions_Sign", "([Kind] = 'Accrual' AND [Amount] > 0) OR ([Kind] = 'Reversal' AND [Amount] < 0 AND [ReversalOfId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_Commissions_CommissionRules_CommissionRuleId",
                        column: x => x.CommissionRuleId,
                        principalTable: "CommissionRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Commissions_Commissions_ReversalOfId",
                        column: x => x.ReversalOfId,
                        principalTable: "Commissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Commissions_SaleLines_SaleLineId",
                        column: x => x.SaleLineId,
                        principalTable: "SaleLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Commissions_Sales_SaleId",
                        column: x => x.SaleId,
                        principalTable: "Sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Commissions_StaffMembers_StaffMemberId",
                        column: x => x.StaffMemberId,
                        principalTable: "StaffMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContactConsents_MessageLogId",
                table: "ContactConsents",
                column: "MessageLogId",
                filter: "[MessageLogId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CommissionRules_BusinessId",
                table: "CommissionRules",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_CommissionRules_CategoryId",
                table: "CommissionRules",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_CommissionRules_ProductId",
                table: "CommissionRules",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_CommissionRules_StaffMemberId",
                table: "CommissionRules",
                column: "StaffMemberId");

            migrationBuilder.CreateIndex(
                name: "UX_CommissionRules_Active",
                table: "CommissionRules",
                columns: new[] { "BusinessId", "StaffMemberId", "Target", "CategoryId", "ProductId" },
                unique: true,
                filter: "[IsActive] = 1 AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Commissions_BusinessId",
                table: "Commissions",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_Commissions_BusinessId_OccurredOn",
                table: "Commissions",
                columns: new[] { "BusinessId", "OccurredOn" })
                .Annotation("SqlServer:Include", new[] { "StaffMemberId", "Amount" });

            migrationBuilder.CreateIndex(
                name: "IX_Commissions_BusinessId_Period_StaffMemberId",
                table: "Commissions",
                columns: new[] { "BusinessId", "Period", "StaffMemberId" })
                .Annotation("SqlServer:Include", new[] { "Amount" });

            migrationBuilder.CreateIndex(
                name: "IX_Commissions_BusinessId_SaleId",
                table: "Commissions",
                columns: new[] { "BusinessId", "SaleId" });

            migrationBuilder.CreateIndex(
                name: "IX_Commissions_CommissionRuleId",
                table: "Commissions",
                column: "CommissionRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_Commissions_ReversalOfId",
                table: "Commissions",
                column: "ReversalOfId");

            migrationBuilder.CreateIndex(
                name: "IX_Commissions_SaleId",
                table: "Commissions",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_Commissions_SaleLineId",
                table: "Commissions",
                column: "SaleLineId");

            migrationBuilder.CreateIndex(
                name: "IX_Commissions_StaffMemberId",
                table: "Commissions",
                column: "StaffMemberId");

            migrationBuilder.CreateIndex(
                name: "UX_Commissions_Line",
                table: "Commissions",
                columns: new[] { "BusinessId", "SaleLineId", "Kind" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ContactConsents_MessageLogs_MessageLogId",
                table: "ContactConsents",
                column: "MessageLogId",
                principalTable: "MessageLogs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ContactConsents_MessageLogs_MessageLogId",
                table: "ContactConsents");

            migrationBuilder.DropTable(
                name: "Commissions");

            migrationBuilder.DropTable(
                name: "CommissionRules");

            migrationBuilder.DropIndex(
                name: "IX_ContactConsents_MessageLogId",
                table: "ContactConsents");

            migrationBuilder.DropColumn(
                name: "MessageLogId",
                table: "ContactConsents");

            migrationBuilder.DropColumn(
                name: "OptOutsTracked",
                table: "Campaigns");
        }
    }
}
