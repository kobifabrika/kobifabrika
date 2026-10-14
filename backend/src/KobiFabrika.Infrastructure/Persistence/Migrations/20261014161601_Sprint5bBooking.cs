using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KobiFabrika.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Sprint5bBooking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ServicePackageId",
                table: "SaleLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "BookingAutoConfirm",
                table: "Businesses",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "BookingEnabled",
                table: "Businesses",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "BookingMaxDaysAhead",
                table: "Businesses",
                type: "int",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<int>(
                name: "BookingMinNoticeMinutes",
                table: "Businesses",
                type: "int",
                nullable: false,
                defaultValue: 120);

            migrationBuilder.AddColumn<int>(
                name: "BookingSlotMinutes",
                table: "Businesses",
                type: "int",
                nullable: false,
                defaultValue: 15);

            migrationBuilder.AddColumn<int>(
                name: "CustomerCancelHours",
                table: "Businesses",
                type: "int",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<int>(
                name: "ReminderHoursBefore",
                table: "Businesses",
                type: "int",
                nullable: false,
                defaultValue: 24);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomerPackageId",
                table: "Appointments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ManageToken",
                table: "Appointments",
                type: "varchar(1000)",
                unicode: false,
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "ManageTokenHash",
                table: "Appointments",
                type: "binary(32)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PackageSessionUsed",
                table: "Appointments",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReminderSentAt",
                table: "Appointments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MessageLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    To = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Provider = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    ProviderMessageId = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Error = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Segments = table.Column<int>(type: "int", nullable: false),
                    CreditCost = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Billable = table.Column<bool>(type: "bit", nullable: false),
                    ContactId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RefType = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: true),
                    RefId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    BusinessId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessageLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MessageLogs_Contacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "Contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PhoneVerifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Phone = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    CodeHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    VerifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConsumedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IpAddress = table.Column<string>(type: "varchar(45)", unicode: false, maxLength: 45, nullable: true),
                    AppointmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BusinessId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhoneVerifications", x => x.Id);
                    table.CheckConstraint("CK_PhoneVerifications_Attempts", "[Attempts] BETWEEN 0 AND 3");
                });

            migrationBuilder.CreateTable(
                name: "ServicePackages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionCount = table.Column<int>(type: "int", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ValidityDays = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServicePackages", x => x.Id);
                    table.CheckConstraint("CK_ServicePackages_Price", "[Price] >= 0");
                    table.CheckConstraint("CK_ServicePackages_Sessions", "[SessionCount] BETWEEN 1 AND 500");
                    table.CheckConstraint("CK_ServicePackages_Validity", "[ValidityDays] BETWEEN 1 AND 1095");
                    table.ForeignKey(
                        name: "FK_ServicePackages_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerPackages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContactId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServicePackageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SaleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SaleLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TotalSessions = table.Column<int>(type: "int", nullable: false),
                    UsedSessions = table.Column<int>(type: "int", nullable: false),
                    ExpiresOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    CancelledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CloseNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    BusinessId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerPackages", x => x.Id);
                    table.CheckConstraint("CK_CustomerPackages_Sessions", "[UsedSessions] >= 0 AND [UsedSessions] <= [TotalSessions]");
                    table.ForeignKey(
                        name: "FK_CustomerPackages_Contacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "Contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerPackages_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerPackages_SaleLines_SaleLineId",
                        column: x => x.SaleLineId,
                        principalTable: "SaleLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerPackages_Sales_SaleId",
                        column: x => x.SaleId,
                        principalTable: "Sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerPackages_ServicePackages_ServicePackageId",
                        column: x => x.ServicePackageId,
                        principalTable: "ServicePackages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SaleLines_ServicePackageId",
                table: "SaleLines",
                column: "ServicePackageId",
                filter: "[ServicePackageId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Businesses_BookingMaxDaysAhead",
                table: "Businesses",
                sql: "[BookingMaxDaysAhead] BETWEEN 1 AND 180");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Businesses_BookingMinNotice",
                table: "Businesses",
                sql: "[BookingMinNoticeMinutes] BETWEEN 0 AND 10080");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Businesses_BookingSlotMinutes",
                table: "Businesses",
                sql: "[BookingSlotMinutes] IN (5, 10, 15, 20, 30, 60)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Businesses_CustomerCancelHours",
                table: "Businesses",
                sql: "[CustomerCancelHours] BETWEEN 0 AND 72");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Businesses_ReminderHours",
                table: "Businesses",
                sql: "[ReminderHoursBefore] BETWEEN 0 AND 72");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_CustomerPackageId",
                table: "Appointments",
                column: "CustomerPackageId",
                filter: "[CustomerPackageId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_ManageTokenHash",
                table: "Appointments",
                column: "ManageTokenHash",
                unique: true,
                filter: "[ManageTokenHash] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_ReminderDue",
                table: "Appointments",
                column: "StartAt",
                filter: "[ReminderSentAt] IS NULL AND [Status] = 'Confirmed' AND [IsDeleted] = 0")
                .Annotation("SqlServer:Include", new[] { "BusinessId", "TimeSetAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPackages_BusinessId",
                table: "CustomerPackages",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPackages_BusinessId_ContactId_Status",
                table: "CustomerPackages",
                columns: new[] { "BusinessId", "ContactId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPackages_BusinessId_SaleId",
                table: "CustomerPackages",
                columns: new[] { "BusinessId", "SaleId" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPackages_ContactId",
                table: "CustomerPackages",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPackages_ProductId",
                table: "CustomerPackages",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPackages_SaleId",
                table: "CustomerPackages",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPackages_SaleLineId",
                table: "CustomerPackages",
                column: "SaleLineId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPackages_ServicePackageId",
                table: "CustomerPackages",
                column: "ServicePackageId");

            migrationBuilder.CreateIndex(
                name: "IX_MessageLogs_BusinessId",
                table: "MessageLogs",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_MessageLogs_BusinessId_ContactId_CreatedAt",
                table: "MessageLogs",
                columns: new[] { "BusinessId", "ContactId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MessageLogs_BusinessId_Purpose_CreatedAt",
                table: "MessageLogs",
                columns: new[] { "BusinessId", "Purpose", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MessageLogs_ContactId",
                table: "MessageLogs",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_PhoneVerifications_BusinessId",
                table: "PhoneVerifications",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_PhoneVerifications_BusinessId_Phone_CreatedAt",
                table: "PhoneVerifications",
                columns: new[] { "BusinessId", "Phone", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PhoneVerifications_CreatedAt",
                table: "PhoneVerifications",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PhoneVerifications_IpAddress_CreatedAt",
                table: "PhoneVerifications",
                columns: new[] { "IpAddress", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PhoneVerifications_Phone_CreatedAt",
                table: "PhoneVerifications",
                columns: new[] { "Phone", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ServicePackages_BusinessId",
                table: "ServicePackages",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_ServicePackages_BusinessId_IsActive",
                table: "ServicePackages",
                columns: new[] { "BusinessId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_ServicePackages_ProductId",
                table: "ServicePackages",
                column: "ProductId");

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_CustomerPackages_CustomerPackageId",
                table: "Appointments",
                column: "CustomerPackageId",
                principalTable: "CustomerPackages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SaleLines_ServicePackages_ServicePackageId",
                table: "SaleLines",
                column: "ServicePackageId",
                principalTable: "ServicePackages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_CustomerPackages_CustomerPackageId",
                table: "Appointments");

            migrationBuilder.DropForeignKey(
                name: "FK_SaleLines_ServicePackages_ServicePackageId",
                table: "SaleLines");

            migrationBuilder.DropTable(
                name: "CustomerPackages");

            migrationBuilder.DropTable(
                name: "MessageLogs");

            migrationBuilder.DropTable(
                name: "PhoneVerifications");

            migrationBuilder.DropTable(
                name: "ServicePackages");

            migrationBuilder.DropIndex(
                name: "IX_SaleLines_ServicePackageId",
                table: "SaleLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Businesses_BookingMaxDaysAhead",
                table: "Businesses");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Businesses_BookingMinNotice",
                table: "Businesses");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Businesses_BookingSlotMinutes",
                table: "Businesses");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Businesses_CustomerCancelHours",
                table: "Businesses");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Businesses_ReminderHours",
                table: "Businesses");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_CustomerPackageId",
                table: "Appointments");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_ManageTokenHash",
                table: "Appointments");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_ReminderDue",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "ServicePackageId",
                table: "SaleLines");

            migrationBuilder.DropColumn(
                name: "BookingAutoConfirm",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "BookingEnabled",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "BookingMaxDaysAhead",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "BookingMinNoticeMinutes",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "BookingSlotMinutes",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "CustomerCancelHours",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "ReminderHoursBefore",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "CustomerPackageId",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "ManageToken",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "ManageTokenHash",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "PackageSessionUsed",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "ReminderSentAt",
                table: "Appointments");
        }
    }
}
