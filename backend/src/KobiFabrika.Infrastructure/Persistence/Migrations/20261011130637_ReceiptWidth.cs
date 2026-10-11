using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KobiFabrika.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReceiptWidth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReceiptWidth",
                table: "Businesses",
                type: "int",
                nullable: false,
                defaultValue: 80);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Businesses_ReceiptWidth",
                table: "Businesses",
                sql: "[ReceiptWidth] IN (58, 80)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Businesses_ReceiptWidth",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "ReceiptWidth",
                table: "Businesses");
        }
    }
}
