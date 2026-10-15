using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KobiFabrika.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Sprint7aStaffTime : Migration
    {
        /// <summary>
        /// İzin türü olmayan işletmelere varsayılanları ekler (LeaveType.DefaultList ile aynı; test edilir).
        /// Tekrar çalıştırılabilir: aynı addaki tür varsa eklemez.
        /// </summary>
        public const string BackfillLeaveTypesSql = """
            INSERT INTO [LeaveTypes] ([Id], [BusinessId], [Name], [IsPaid], [SystemKey], [IsActive], [SortOrder], [IsDeleted], [CreatedAt])
            SELECT NEWID(), b.[Id], t.[Name], t.[IsPaid], t.[SystemKey], 1, t.[SortOrder], 0, SYSUTCDATETIME()
            FROM [Businesses] b
            CROSS JOIN (VALUES
                (N'Yıllık izin', 1, 'annual', 0),
                (N'Mazeret izni', 1, NULL, 1),
                (N'Ücretsiz izin', 0, NULL, 2),
                (N'Hastalık/rapor', 0, NULL, 3)) t([Name], [IsPaid], [SystemKey], [SortOrder])
            WHERE NOT EXISTS (SELECT 1 FROM [LeaveTypes] l
                WHERE l.[BusinessId] = b.[Id] AND l.[IsDeleted] = 0 AND (l.[Name] = t.[Name] OR l.[SystemKey] = t.[SystemKey]));
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LeaveRequestId",
                table: "StaffTimeOffs",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "BirthDate",
                table: "StaffMembers",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "LeaveAccrualFrom",
                table: "StaffMembers",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "StaffMembers",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Salary",
                table: "StaffMembers",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SalaryBasis",
                table: "StaffMembers",
                type: "nvarchar(5)",
                maxLength: 5,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "StartDate",
                table: "StaffMembers",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StaffMemberId",
                table: "CashTransactions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OvertimeWeeklyHours",
                table: "Businesses",
                type: "decimal(4,1)",
                precision: 4,
                scale: 1,
                nullable: false,
                defaultValue: 45m);

            migrationBuilder.CreateTable(
                name: "LeaveTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IsPaid = table.Column<bool>(type: "bit", nullable: false),
                    SystemKey = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
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
                    table.PrimaryKey("PK_LeaveTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TimeEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StaffMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClockInAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ClockOutAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    InSource = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    OutSource = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    ForgottenAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BusinessId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimeEntries", x => x.Id);
                    table.CheckConstraint("CK_TimeEntries_Range", "[ClockOutAt] IS NULL OR ([ClockOutAt] > [ClockInAt] AND DATEDIFF(MINUTE, [ClockInAt], [ClockOutAt]) <= 1440)");
                    table.ForeignKey(
                        name: "FK_TimeEntries_StaffMembers_StaffMemberId",
                        column: x => x.StaffMemberId,
                        principalTable: "StaffMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LeaveRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StaffMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeaveTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    HalfDay = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Days = table.Column<decimal>(type: "decimal(4,1)", precision: 4, scale: 1, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    RequestedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DecidedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CancelledBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    BusinessId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveRequests", x => x.Id);
                    table.CheckConstraint("CK_LeaveRequests_Days", "[Days] >= 0.5");
                    table.CheckConstraint("CK_LeaveRequests_HalfDay", "[HalfDay] = 'None' OR [EndDate] = [StartDate]");
                    table.CheckConstraint("CK_LeaveRequests_Range", "[EndDate] >= [StartDate]");
                    table.ForeignKey(
                        name: "FK_LeaveRequests_LeaveTypes_LeaveTypeId",
                        column: x => x.LeaveTypeId,
                        principalTable: "LeaveTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaveRequests_StaffMembers_StaffMemberId",
                        column: x => x.StaffMemberId,
                        principalTable: "StaffMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LeaveBalanceEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StaffMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Days = table.Column<decimal>(type: "decimal(5,1)", precision: 5, scale: 1, nullable: false),
                    ServiceYear = table.Column<int>(type: "int", nullable: true),
                    LeaveRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BusinessId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveBalanceEntries", x => x.Id);
                    table.CheckConstraint("CK_LeaveBalanceEntries_Days", "[Days] <> 0");
                    table.ForeignKey(
                        name: "FK_LeaveBalanceEntries_LeaveRequests_LeaveRequestId",
                        column: x => x.LeaveRequestId,
                        principalTable: "LeaveRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaveBalanceEntries_StaffMembers_StaffMemberId",
                        column: x => x.StaffMemberId,
                        principalTable: "StaffMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StaffTimeOffs_LeaveRequestId",
                table: "StaffTimeOffs",
                column: "LeaveRequestId",
                filter: "[LeaveRequestId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StaffMembers_Salary",
                table: "StaffMembers",
                sql: "[Salary] IS NULL OR [Salary] >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_CashTransactions_BusinessId_StaffMemberId_Date",
                table: "CashTransactions",
                columns: new[] { "BusinessId", "StaffMemberId", "Date" },
                filter: "[StaffMemberId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CashTransactions_StaffMemberId",
                table: "CashTransactions",
                column: "StaffMemberId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Businesses_OvertimeWeeklyHours",
                table: "Businesses",
                sql: "[OvertimeWeeklyHours] BETWEEN 1 AND 80");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveBalanceEntries_BusinessId",
                table: "LeaveBalanceEntries",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveBalanceEntries_BusinessId_StaffMemberId_CreatedAt",
                table: "LeaveBalanceEntries",
                columns: new[] { "BusinessId", "StaffMemberId", "CreatedAt" })
                .Annotation("SqlServer:Include", new[] { "Days" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveBalanceEntries_LeaveRequestId",
                table: "LeaveBalanceEntries",
                column: "LeaveRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveBalanceEntries_StaffMemberId",
                table: "LeaveBalanceEntries",
                column: "StaffMemberId");

            migrationBuilder.CreateIndex(
                name: "UX_LeaveBalanceEntries_Accrual",
                table: "LeaveBalanceEntries",
                columns: new[] { "BusinessId", "StaffMemberId", "ServiceYear" },
                unique: true,
                filter: "[Kind] = 'Accrual'");

            migrationBuilder.CreateIndex(
                name: "UX_LeaveBalanceEntries_Request",
                table: "LeaveBalanceEntries",
                columns: new[] { "BusinessId", "LeaveRequestId", "Kind" },
                unique: true,
                filter: "[LeaveRequestId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequests_BusinessId",
                table: "LeaveRequests",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequests_BusinessId_StaffMemberId_StartDate",
                table: "LeaveRequests",
                columns: new[] { "BusinessId", "StaffMemberId", "StartDate" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequests_BusinessId_Status_StartDate",
                table: "LeaveRequests",
                columns: new[] { "BusinessId", "Status", "StartDate" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequests_LeaveTypeId",
                table: "LeaveRequests",
                column: "LeaveTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequests_StaffMemberId",
                table: "LeaveRequests",
                column: "StaffMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveTypes_BusinessId",
                table: "LeaveTypes",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveTypes_BusinessId_Name",
                table: "LeaveTypes",
                columns: new[] { "BusinessId", "Name" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveTypes_BusinessId_SystemKey",
                table: "LeaveTypes",
                columns: new[] { "BusinessId", "SystemKey" },
                unique: true,
                filter: "[SystemKey] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_TimeEntries_BusinessId",
                table: "TimeEntries",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_TimeEntries_BusinessId_ClockInAt",
                table: "TimeEntries",
                columns: new[] { "BusinessId", "ClockInAt" })
                .Annotation("SqlServer:Include", new[] { "StaffMemberId", "ClockOutAt", "ForgottenAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TimeEntries_BusinessId_StaffMemberId_ClockInAt",
                table: "TimeEntries",
                columns: new[] { "BusinessId", "StaffMemberId", "ClockInAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TimeEntries_StaffMemberId",
                table: "TimeEntries",
                column: "StaffMemberId");

            migrationBuilder.CreateIndex(
                name: "UX_TimeEntries_Open",
                table: "TimeEntries",
                columns: new[] { "BusinessId", "StaffMemberId" },
                unique: true,
                filter: "[ClockOutAt] IS NULL AND [ForgottenAt] IS NULL AND [IsDeleted] = 0");

            migrationBuilder.AddForeignKey(
                name: "FK_CashTransactions_StaffMembers_StaffMemberId",
                table: "CashTransactions",
                column: "StaffMemberId",
                principalTable: "StaffMembers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StaffTimeOffs_LeaveRequests_LeaveRequestId",
                table: "StaffTimeOffs",
                column: "LeaveRequestId",
                principalTable: "LeaveRequests",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(BackfillLeaveTypesSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CashTransactions_StaffMembers_StaffMemberId",
                table: "CashTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_StaffTimeOffs_LeaveRequests_LeaveRequestId",
                table: "StaffTimeOffs");

            migrationBuilder.DropTable(
                name: "LeaveBalanceEntries");

            migrationBuilder.DropTable(
                name: "TimeEntries");

            migrationBuilder.DropTable(
                name: "LeaveRequests");

            migrationBuilder.DropTable(
                name: "LeaveTypes");

            migrationBuilder.DropIndex(
                name: "IX_StaffTimeOffs_LeaveRequestId",
                table: "StaffTimeOffs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StaffMembers_Salary",
                table: "StaffMembers");

            migrationBuilder.DropIndex(
                name: "IX_CashTransactions_BusinessId_StaffMemberId_Date",
                table: "CashTransactions");

            migrationBuilder.DropIndex(
                name: "IX_CashTransactions_StaffMemberId",
                table: "CashTransactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Businesses_OvertimeWeeklyHours",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "LeaveRequestId",
                table: "StaffTimeOffs");

            migrationBuilder.DropColumn(
                name: "BirthDate",
                table: "StaffMembers");

            migrationBuilder.DropColumn(
                name: "LeaveAccrualFrom",
                table: "StaffMembers");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "StaffMembers");

            migrationBuilder.DropColumn(
                name: "Salary",
                table: "StaffMembers");

            migrationBuilder.DropColumn(
                name: "SalaryBasis",
                table: "StaffMembers");

            migrationBuilder.DropColumn(
                name: "StartDate",
                table: "StaffMembers");

            migrationBuilder.DropColumn(
                name: "StaffMemberId",
                table: "CashTransactions");

            migrationBuilder.DropColumn(
                name: "OvertimeWeeklyHours",
                table: "Businesses");
        }
    }
}
