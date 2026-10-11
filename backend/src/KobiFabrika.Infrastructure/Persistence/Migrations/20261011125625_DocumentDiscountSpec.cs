using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KobiFabrika.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DocumentDiscountSpec : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DiscountType",
                table: "Sales",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountValue",
                table: "Sales",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DiscountType",
                table: "SaleLines",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountValue",
                table: "SaleLines",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DiscountType",
                table: "Quotes",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountValue",
                table: "Quotes",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DiscountType",
                table: "QuoteLines",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountValue",
                table: "QuoteLines",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            // Eski belgelerde indirimin nasıl girildiği bilinmiyor: TL tutarı olarak işaretlenir.
            foreach (var (table, amount) in new[] { ("Sales", "DiscountTotal"), ("Quotes", "DiscountTotal"), ("SaleLines", "LineDiscount"), ("QuoteLines", "LineDiscount") })
            {
                migrationBuilder.Sql($"UPDATE [{table}] SET [DiscountType] = N'Amount', [DiscountValue] = [{amount}] WHERE [{amount}] > 0;");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DiscountType",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "DiscountValue",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "DiscountType",
                table: "SaleLines");

            migrationBuilder.DropColumn(
                name: "DiscountValue",
                table: "SaleLines");

            migrationBuilder.DropColumn(
                name: "DiscountType",
                table: "Quotes");

            migrationBuilder.DropColumn(
                name: "DiscountValue",
                table: "Quotes");

            migrationBuilder.DropColumn(
                name: "DiscountType",
                table: "QuoteLines");

            migrationBuilder.DropColumn(
                name: "DiscountValue",
                table: "QuoteLines");
        }
    }
}
