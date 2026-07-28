using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Numera.Platform.Db.Migrations
{
    /// <inheritdoc />
    public partial class DownPaymentInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sales_document_prepayment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    abschlag_document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    abschlag_number = table.Column<string>(type: "text", nullable: false),
                    abschlag_date = table.Column<DateOnly>(type: "date", nullable: false),
                    net_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    vat_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    gross_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_document_prepayment", x => x.id);
                    table.ForeignKey(
                        name: "fk_sales_document_prepayment_sales_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "sales_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sales_document_prepayment_document_id",
                table: "sales_document_prepayment",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_document_prepayment_tenant_id_document_id",
                table: "sales_document_prepayment",
                columns: new[] { "tenant_id", "document_id" });

            migrationBuilder.Sql("ALTER TABLE sales_document_prepayment ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE sales_document_prepayment FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON sales_document_prepayment " +
                "USING (tenant_id = current_setting('app.current_tenant')::uuid) " +
                "WITH CHECK (tenant_id = current_setting('app.current_tenant')::uuid);");

            // Prepayment snapshots share the existing child immutability function:
            // inserts/updates/deletes are permitted only while the parent is Draft.
            migrationBuilder.Sql(
                "CREATE TRIGGER sales_document_prepayment_immutable " +
                "BEFORE INSERT OR UPDATE OR DELETE ON sales_document_prepayment " +
                "FOR EACH ROW EXECUTE FUNCTION sales_document_child_immutable();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS sales_document_prepayment_immutable ON sales_document_prepayment;");
            migrationBuilder.Sql(
                "DROP POLICY IF EXISTS tenant_isolation ON sales_document_prepayment;");
            migrationBuilder.Sql(
                "ALTER TABLE sales_document_prepayment NO FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(
                "ALTER TABLE sales_document_prepayment DISABLE ROW LEVEL SECURITY;");

            migrationBuilder.DropTable(
                name: "sales_document_prepayment");
        }
    }
}
