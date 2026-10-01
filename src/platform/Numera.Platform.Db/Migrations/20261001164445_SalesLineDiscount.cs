using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Numera.Platform.Db.Migrations
{
    /// <inheritdoc />
    public partial class SalesLineDiscount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "discount_percent",
                table: "sales_document_lines",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "discount_percent",
                table: "sales_document_lines");
        }
    }
}
