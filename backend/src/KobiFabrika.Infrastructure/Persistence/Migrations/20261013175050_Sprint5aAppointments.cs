using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KobiFabrika.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Sprint5aAppointments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AppointmentId",
                table: "Sales",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StaffMemberId",
                table: "SaleLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "StaffSeesOnlyOwnAppointments",
                table: "Businesses",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Holidays",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    BusinessId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Holidays", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StaffMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Phone = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    Color = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ShowInBooking = table.Column<bool>(type: "bit", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
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
                    table.PrimaryKey("PK_StaffMembers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Appointments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContactId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StaffMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SaleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConfirmedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NoShowAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CancelSource = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TimeSetAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Appointments", x => x.Id);
                    table.CheckConstraint("CK_Appointments_Range", "[EndAt] > [StartAt] AND DATEDIFF(MINUTE, [StartAt], [EndAt]) <= 720");
                    table.ForeignKey(
                        name: "FK_Appointments_Contacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "Contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Appointments_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Appointments_Sales_SaleId",
                        column: x => x.SaleId,
                        principalTable: "Sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Appointments_StaffMembers_StaffMemberId",
                        column: x => x.StaffMemberId,
                        principalTable: "StaffMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StaffServices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StaffMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffServices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StaffServices_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StaffServices_StaffMembers_StaffMemberId",
                        column: x => x.StaffMemberId,
                        principalTable: "StaffMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StaffTimeOffs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StaffMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BusinessId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffTimeOffs", x => x.Id);
                    table.CheckConstraint("CK_StaffTimeOffs_Range", "[StartAt] < [EndAt]");
                    table.ForeignKey(
                        name: "FK_StaffTimeOffs_StaffMembers_StaffMemberId",
                        column: x => x.StaffMemberId,
                        principalTable: "StaffMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StaffWorkingHours",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StaffMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DayOfWeek = table.Column<byte>(type: "tinyint", nullable: false),
                    StartsAt = table.Column<TimeOnly>(type: "time", nullable: false),
                    EndsAt = table.Column<TimeOnly>(type: "time", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffWorkingHours", x => x.Id);
                    table.CheckConstraint("CK_StaffWorkingHours_Range", "[StartsAt] < [EndsAt]");
                    table.ForeignKey(
                        name: "FK_StaffWorkingHours_StaffMembers_StaffMemberId",
                        column: x => x.StaffMemberId,
                        principalTable: "StaffMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sales_BusinessId_AppointmentId",
                table: "Sales",
                columns: new[] { "BusinessId", "AppointmentId" },
                unique: true,
                filter: "[AppointmentId] IS NOT NULL AND [Status] = N'Completed'");

            migrationBuilder.CreateIndex(
                name: "IX_SaleLines_StaffMemberId",
                table: "SaleLines",
                column: "StaffMemberId",
                filter: "[StaffMemberId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_BusinessId",
                table: "Appointments",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_BusinessId_ContactId_StartAt",
                table: "Appointments",
                columns: new[] { "BusinessId", "ContactId", "StartAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_BusinessId_StaffMemberId_StartAt",
                table: "Appointments",
                columns: new[] { "BusinessId", "StaffMemberId", "StartAt" })
                .Annotation("SqlServer:Include", new[] { "EndAt", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_BusinessId_StartAt",
                table: "Appointments",
                columns: new[] { "BusinessId", "StartAt" })
                .Annotation("SqlServer:Include", new[] { "Status", "StaffMemberId" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_ContactId",
                table: "Appointments",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_ProductId",
                table: "Appointments",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_SaleId",
                table: "Appointments",
                column: "SaleId",
                filter: "[SaleId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_StaffMemberId",
                table: "Appointments",
                column: "StaffMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_Holidays_BusinessId",
                table: "Holidays",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_Holidays_BusinessId_Date",
                table: "Holidays",
                columns: new[] { "BusinessId", "Date" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_StaffMembers_BusinessId",
                table: "StaffMembers",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffMembers_BusinessId_IsActive_SortOrder",
                table: "StaffMembers",
                columns: new[] { "BusinessId", "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffMembers_BusinessId_UserId",
                table: "StaffMembers",
                columns: new[] { "BusinessId", "UserId" },
                unique: true,
                filter: "[UserId] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_StaffServices_BusinessId",
                table: "StaffServices",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffServices_BusinessId_StaffMemberId_ProductId",
                table: "StaffServices",
                columns: new[] { "BusinessId", "StaffMemberId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StaffServices_ProductId",
                table: "StaffServices",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffServices_StaffMemberId",
                table: "StaffServices",
                column: "StaffMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffTimeOffs_BusinessId",
                table: "StaffTimeOffs",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffTimeOffs_BusinessId_StaffMemberId_StartAt",
                table: "StaffTimeOffs",
                columns: new[] { "BusinessId", "StaffMemberId", "StartAt" })
                .Annotation("SqlServer:Include", new[] { "EndAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffTimeOffs_StaffMemberId",
                table: "StaffTimeOffs",
                column: "StaffMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffWorkingHours_BusinessId",
                table: "StaffWorkingHours",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffWorkingHours_BusinessId_StaffMemberId_DayOfWeek",
                table: "StaffWorkingHours",
                columns: new[] { "BusinessId", "StaffMemberId", "DayOfWeek" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffWorkingHours_StaffMemberId",
                table: "StaffWorkingHours",
                column: "StaffMemberId");

            migrationBuilder.AddForeignKey(
                name: "FK_SaleLines_StaffMembers_StaffMemberId",
                table: "SaleLines",
                column: "StaffMemberId",
                principalTable: "StaffMembers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SaleLines_StaffMembers_StaffMemberId",
                table: "SaleLines");

            migrationBuilder.DropTable(
                name: "Appointments");

            migrationBuilder.DropTable(
                name: "Holidays");

            migrationBuilder.DropTable(
                name: "StaffServices");

            migrationBuilder.DropTable(
                name: "StaffTimeOffs");

            migrationBuilder.DropTable(
                name: "StaffWorkingHours");

            migrationBuilder.DropTable(
                name: "StaffMembers");

            migrationBuilder.DropIndex(
                name: "IX_Sales_BusinessId_AppointmentId",
                table: "Sales");

            migrationBuilder.DropIndex(
                name: "IX_SaleLines_StaffMemberId",
                table: "SaleLines");

            migrationBuilder.DropColumn(
                name: "AppointmentId",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "StaffMemberId",
                table: "SaleLines");

            migrationBuilder.DropColumn(
                name: "StaffSeesOnlyOwnAppointments",
                table: "Businesses");
        }
    }
}
