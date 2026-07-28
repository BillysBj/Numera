using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Numera.Platform.Db.Migrations
{
    /// <inheritdoc />
    public partial class ForeignCurrencyColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "exchange_rate",
                table: "sales_documents",
                type: "numeric(19,6)",
                precision: 19,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "exchange_rate_date",
                table: "sales_documents",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "total_tax_eur",
                table: "sales_documents",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "exchange_rate",
                table: "sales_documents");

            migrationBuilder.DropColumn(
                name: "exchange_rate_date",
                table: "sales_documents");

            migrationBuilder.DropColumn(
                name: "total_tax_eur",
                table: "sales_documents");
        }
    }
}
