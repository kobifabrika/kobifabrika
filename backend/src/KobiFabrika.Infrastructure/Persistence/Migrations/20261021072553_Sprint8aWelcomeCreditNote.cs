using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KobiFabrika.Infrastructure.Persistence.Migrations
{
    /// <summary>Panelde hitap "siz" (karar 2026-10-21): eski hoş geldin kredisi notları yeni metne.</summary>
    public partial class Sprint8aWelcomeCreditNote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE [SmsCreditTransactions] SET [Note] = N'Hoş geldiniz kredisi' WHERE [Kind] = 'Welcome' AND [Note] = N'Hoş geldin kredisi';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE [SmsCreditTransactions] SET [Note] = N'Hoş geldin kredisi' WHERE [Kind] = 'Welcome' AND [Note] = N'Hoş geldiniz kredisi';");
        }
    }
}
