using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Numera.Platform.Db.Migrations
{
    /// <inheritdoc />
    public partial class AddLegalAcceptance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "legal_accepted_at",
                table: "tenants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "legal_accepted_version",
                table: "tenants",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "legal_accepted_at",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "legal_accepted_version",
                table: "tenants");
        }
    }
}
