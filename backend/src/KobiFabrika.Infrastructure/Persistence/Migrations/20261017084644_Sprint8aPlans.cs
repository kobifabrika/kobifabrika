using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KobiFabrika.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Sprint8aPlans : Migration
    {
        /// <summary>
        /// Paketler (karar 2026-10-17). Özellik bitleri: 1 kampanya, 2 otomasyon, 4 online randevu, 8 prim, 16 anket, 32 risk listesi.
        /// Tekrar çalıştırılabilir: olan paket değişmez (süper admin fiyat/limit değiştirmiş olabilir).
        /// </summary>
        public const string SeedPlansSql = """
            INSERT INTO [Plans] ([Id], [Code], [Name], [MonthlyPrice], [YearlyPrice], [MaxUsers], [MaxContacts], [MaxAppointmentsPerMonth], [Features], [SortOrder])
            SELECT v.[Id], v.[Code], v.[Name], v.[MonthlyPrice], v.[YearlyPrice], v.[MaxUsers], v.[MaxContacts], v.[MaxAppointmentsPerMonth], v.[Features], v.[SortOrder]
            FROM (VALUES
                (CAST('0192f000-0000-7000-8000-000000000001' AS uniqueidentifier), 'cirak', N'Çırak', 0, 0, 1, 100, 50, 0, 1),
                (CAST('0192f000-0000-7000-8000-000000000002' AS uniqueidentifier), 'kalfa', N'Kalfa', 699, 6990, 3, 1000, 500, 7, 2),
                (CAST('0192f000-0000-7000-8000-000000000003' AS uniqueidentifier), 'usta', N'Usta', 1299, 12990, 10, NULL, NULL, 63, 3)
            ) AS v([Id], [Code], [Name], [MonthlyPrice], [YearlyPrice], [MaxUsers], [MaxContacts], [MaxAppointmentsPerMonth], [Features], [SortOrder])
            WHERE NOT EXISTS (SELECT 1 FROM [Plans] p WHERE p.[Code] = v.[Code]);
            """;

        /// <summary>
        /// Bu migration'dan önce açılmış işletmeler (geliştirmede Kaya Restaurant) Pilot başlar: süresiz, limitsiz (karar 2026-10-17).
        /// Yeni işletmeler kurulumda 14 günlük denemeyle başlar.
        /// </summary>
        public const string BackfillPilotSql = """
            UPDATE [Businesses] SET [IsPilot] = 1 WHERE [IsPilot] = 0 AND [TrialEndsAt] IS NULL;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "StoppedByPlan",
                table: "Campaigns",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsPilot",
                table: "Businesses",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsReadOnly",
                table: "Businesses",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PlanCode",
                table: "Businesses",
                type: "varchar(10)",
                unicode: false,
                maxLength: 10,
                nullable: false,
                defaultValue: "cirak");

            migrationBuilder.AddColumn<string>(
                name: "ReadOnlyReason",
                table: "Businesses",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestedPeriod",
                table: "Businesses",
                type: "varchar(10)",
                unicode: false,
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestedPlanCode",
                table: "Businesses",
                type: "varchar(10)",
                unicode: false,
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TrialEndsAt",
                table: "Businesses",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastLoginAt",
                table: "AspNetUsers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestedPeriod",
                table: "AspNetUsers",
                type: "varchar(10)",
                unicode: false,
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestedPlanCode",
                table: "AspNetUsers",
                type: "varchar(10)",
                unicode: false,
                maxLength: 10,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Plans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    MonthlyPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    YearlyPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    MaxUsers = table.Column<int>(type: "int", nullable: true),
                    MaxContacts = table.Column<int>(type: "int", nullable: true),
                    MaxAppointmentsPerMonth = table.Column<int>(type: "int", nullable: true),
                    Features = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Plans", x => x.Id);
                    table.UniqueConstraint("AK_Plans_Code", x => x.Code);
                    table.CheckConstraint("CK_Plans_Limits", "([MaxUsers] IS NULL OR [MaxUsers] >= 1) AND ([MaxContacts] IS NULL OR [MaxContacts] >= 0) AND ([MaxAppointmentsPerMonth] IS NULL OR [MaxAppointmentsPerMonth] >= 0)");
                    table.CheckConstraint("CK_Plans_Prices", "[MonthlyPrice] >= 0 AND [YearlyPrice] >= 0");
                });

            migrationBuilder.Sql(SeedPlansSql);
            migrationBuilder.Sql(BackfillPilotSql);

            migrationBuilder.CreateIndex(
                name: "IX_Businesses_PlanCode",
                table: "Businesses",
                column: "PlanCode");

            migrationBuilder.AddForeignKey(
                name: "FK_Businesses_Plans_PlanCode",
                table: "Businesses",
                column: "PlanCode",
                principalTable: "Plans",
                principalColumn: "Code",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Businesses_Plans_PlanCode",
                table: "Businesses");

            migrationBuilder.DropTable(
                name: "Plans");

            migrationBuilder.DropIndex(
                name: "IX_Businesses_PlanCode",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "StoppedByPlan",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "IsPilot",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "IsReadOnly",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "PlanCode",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "ReadOnlyReason",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "RequestedPeriod",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "RequestedPlanCode",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "TrialEndsAt",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "LastLoginAt",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "RequestedPeriod",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "RequestedPlanCode",
                table: "AspNetUsers");
        }
    }
}
