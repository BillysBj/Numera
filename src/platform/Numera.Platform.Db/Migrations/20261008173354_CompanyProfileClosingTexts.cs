using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Numera.Platform.Db.Migrations
{
    /// <inheritdoc />
    public partial class CompanyProfileClosingTexts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "delivery_note_footer_text",
                table: "company_profile",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "invoice_footer_text",
                table: "company_profile",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "delivery_note_footer_text",
                table: "company_profile");

            migrationBuilder.DropColumn(
                name: "invoice_footer_text",
                table: "company_profile");
        }
    }
}
