using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KobiFabrika.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Sprint6bAutomations : Migration
    {
        /// <summary>
        /// Ziyaret istatistiği (Sprint 6B): tamamlanan satış ve tamamlanan randevu olan farklı İstanbul günleri (UTC+3; aynı gün
        /// satış + randevu tek ziyaret). Ziyaret sayısı, ilk ziyaret günü ve ortalama aralık. Tekrar çalıştırılabilir (baştan hesaplar);
        /// ContactVisits.RecomputeAsync ile aynı kural (testli).
        /// </summary>
        public const string BackfillVisitStatsSql = """
            WITH v AS (
                SELECT s.[ContactId], CAST(DATEADD(hour, 3, s.[Date]) AS date) AS d
                FROM [Sales] s WHERE s.[ContactId] IS NOT NULL AND s.[Status] = 'Completed' AND s.[IsDeleted] = 0
                UNION
                SELECT a.[ContactId], CAST(DATEADD(hour, 3, a.[StartAt]) AS date)
                FROM [Appointments] a WHERE a.[Status] = 'Completed' AND a.[IsDeleted] = 0
            ), s AS (
                SELECT [ContactId], COUNT(*) AS c, MIN(d) AS f, MAX(d) AS l FROM v GROUP BY [ContactId]
            )
            UPDATE c SET
                [VisitCount] = ISNULL(s.c, 0),
                [FirstVisitOn] = s.f,
                [AvgVisitIntervalDays] = CASE WHEN s.c > 1 THEN ROUND(CAST(DATEDIFF(day, s.f, s.l) AS decimal(9, 2)) / (s.c - 1), 2) END
            FROM [Contacts] c LEFT JOIN s ON s.[ContactId] = c.[Id];
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AvgVisitIntervalDays",
                table: "Contacts",
                type: "decimal(7,2)",
                precision: 7,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "FirstVisitOn",
                table: "Contacts",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VisitCount",
                table: "Contacts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Automations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    SendAtLocal = table.Column<TimeOnly>(type: "time", nullable: false),
                    InactiveDays = table.Column<int>(type: "int", nullable: true),
                    RepeatDays = table.Column<int>(type: "int", nullable: true),
                    DelayMinutes = table.Column<int>(type: "int", nullable: true),
                    EnabledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastRunOn = table.Column<DateOnly>(type: "date", nullable: true),
                    BusinessId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Automations", x => x.Id);
                    table.CheckConstraint("CK_Automations_DelayMinutes", "[DelayMinutes] IS NULL OR [DelayMinutes] BETWEEN 0 AND 4320");
                    table.CheckConstraint("CK_Automations_InactiveDays", "[InactiveDays] IS NULL OR [InactiveDays] BETWEEN 7 AND 730");
                    table.CheckConstraint("CK_Automations_RepeatDays", "[RepeatDays] IS NULL OR [RepeatDays] BETWEEN 7 AND 730");
                });

            migrationBuilder.CreateTable(
                name: "AutomationSends",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ContactId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    MessageLogId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SkipReason = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationSends", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AutomationSends_Contacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "Contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AutomationSends_MessageLogs_MessageLogId",
                        column: x => x.MessageLogId,
                        principalTable: "MessageLogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Surveys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContactId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StaffMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CodeHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    ProtectedCode = table.Column<string>(type: "varchar(500)", unicode: false, maxLength: 500, nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Score = table.Column<int>(type: "int", nullable: true),
                    AnsweredAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CommentedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    GoogleClickedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    BusinessId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Surveys", x => x.Id);
                    table.CheckConstraint("CK_Surveys_Score", "[Score] IS NULL OR [Score] BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "FK_Surveys_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Surveys_Contacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "Contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Contacts_BusinessId_VisitCount_LastVisitAt",
                table: "Contacts",
                columns: new[] { "BusinessId", "VisitCount", "LastVisitAt" })
                .Annotation("SqlServer:Include", new[] { "AvgVisitIntervalDays" });

            migrationBuilder.CreateIndex(
                name: "IX_Automations_BusinessId",
                table: "Automations",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_Automations_BusinessId_Type",
                table: "Automations",
                columns: new[] { "BusinessId", "Type" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Automations_Enabled",
                table: "Automations",
                column: "Type",
                filter: "[IsEnabled] = 1 AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_AutomationSends_BusinessId",
                table: "AutomationSends",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_AutomationSends_BusinessId_Type_ContactId_Key",
                table: "AutomationSends",
                columns: new[] { "BusinessId", "Type", "ContactId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AutomationSends_BusinessId_Type_CreatedAt",
                table: "AutomationSends",
                columns: new[] { "BusinessId", "Type", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationSends_ContactId",
                table: "AutomationSends",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_AutomationSends_MessageLogId",
                table: "AutomationSends",
                column: "MessageLogId");

            migrationBuilder.CreateIndex(
                name: "IX_Surveys_AppointmentId",
                table: "Surveys",
                column: "AppointmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Surveys_BusinessId",
                table: "Surveys",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_Surveys_BusinessId_AnsweredAt",
                table: "Surveys",
                columns: new[] { "BusinessId", "AnsweredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Surveys_BusinessId_AppointmentId",
                table: "Surveys",
                columns: new[] { "BusinessId", "AppointmentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Surveys_CodeHash",
                table: "Surveys",
                column: "CodeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Surveys_ContactId",
                table: "Surveys",
                column: "ContactId");

            migrationBuilder.Sql(BackfillVisitStatsSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Automations");

            migrationBuilder.DropTable(
                name: "AutomationSends");

            migrationBuilder.DropTable(
                name: "Surveys");

            migrationBuilder.DropIndex(
                name: "IX_Contacts_BusinessId_VisitCount_LastVisitAt",
                table: "Contacts");

            migrationBuilder.DropColumn(
                name: "AvgVisitIntervalDays",
                table: "Contacts");

            migrationBuilder.DropColumn(
                name: "FirstVisitOn",
                table: "Contacts");

            migrationBuilder.DropColumn(
                name: "VisitCount",
                table: "Contacts");
        }
    }
}
