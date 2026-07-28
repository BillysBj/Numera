using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Numera.Platform.Db.Migrations
{
    /// <inheritdoc />
    public partial class RecurringInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "recurring_period_key",
                table: "sales_documents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "recurring_template_id",
                table: "sales_documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "recurring_invoice_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    partner_id = table.Column<Guid>(type: "uuid", nullable: true),
                    currency = table.Column<string>(type: "text", nullable: false),
                    exchange_rate = table.Column<decimal>(type: "numeric(19,6)", precision: 19, scale: 6, nullable: true),
                    exchange_rate_date = table.Column<DateOnly>(type: "date", nullable: true),
                    interval_unit = table.Column<int>(type: "integer", nullable: false),
                    interval_count = table.Column<int>(type: "integer", nullable: false),
                    start_on = table.Column<DateOnly>(type: "date", nullable: false),
                    end_mode = table.Column<int>(type: "integer", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    max_occurrences = table.Column<int>(type: "integer", nullable: true),
                    next_run_on = table.Column<DateOnly>(type: "date", nullable: false),
                    last_generated_period_end = table.Column<DateOnly>(type: "date", nullable: true),
                    generated_count = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    auto_finalize = table.Column<bool>(type: "boolean", nullable: false),
                    auto_send = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recurring_invoice_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "recurring_invoice_template_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_number = table.Column<int>(type: "integer", nullable: false),
                    catalog_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(19,6)", precision: 19, scale: 6, nullable: false),
                    unit_code = table.Column<string>(type: "text", nullable: false),
                    net_unit_price = table.Column<decimal>(type: "numeric(19,6)", precision: 19, scale: 6, nullable: false),
                    tax_category = table.Column<int>(type: "integer", nullable: false),
                    vat_rate_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recurring_invoice_template_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_recurring_invoice_template_lines_recurring_invoice_template~",
                        column: x => x.template_id,
                        principalTable: "recurring_invoice_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sales_documents_tenant_id_recurring_template_id_recurring_p~",
                table: "sales_documents",
                columns: new[] { "tenant_id", "recurring_template_id", "recurring_period_key" },
                unique: true,
                filter: "recurring_template_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_recurring_invoice_template_lines_template_id",
                table: "recurring_invoice_template_lines",
                column: "template_id");

            migrationBuilder.CreateIndex(
                name: "ix_recurring_invoice_template_lines_tenant_id_template_id",
                table: "recurring_invoice_template_lines",
                columns: new[] { "tenant_id", "template_id" });

            migrationBuilder.CreateIndex(
                name: "ix_recurring_invoice_templates_tenant_id_status",
                table: "recurring_invoice_templates",
                columns: new[] { "tenant_id", "status" });

            // New tenant tables require explicit database-enforced isolation; reflective
            // ITenantEntity discovery supplies only the application query filter.
            foreach (var t in new[] { "recurring_invoice_templates", "recurring_invoice_template_lines" })
            {
                migrationBuilder.Sql($"ALTER TABLE {t} ENABLE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"ALTER TABLE {t} FORCE ROW LEVEL SECURITY;");
                migrationBuilder.Sql(
                    $"CREATE POLICY tenant_isolation ON {t} " +
                    "USING (tenant_id = current_setting('app.current_tenant')::uuid) " +
                    "WITH CHECK (tenant_id = current_setting('app.current_tenant')::uuid);");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var t in new[] { "recurring_invoice_templates", "recurring_invoice_template_lines" })
            {
                migrationBuilder.Sql($"DROP POLICY IF EXISTS tenant_isolation ON {t};");
                migrationBuilder.Sql($"ALTER TABLE {t} NO FORCE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"ALTER TABLE {t} DISABLE ROW LEVEL SECURITY;");
            }

            migrationBuilder.DropTable(
                name: "recurring_invoice_template_lines");

            migrationBuilder.DropTable(
                name: "recurring_invoice_templates");

            migrationBuilder.DropIndex(
                name: "ix_sales_documents_tenant_id_recurring_template_id_recurring_p~",
                table: "sales_documents");

            migrationBuilder.DropColumn(
                name: "recurring_period_key",
                table: "sales_documents");

            migrationBuilder.DropColumn(
                name: "recurring_template_id",
                table: "sales_documents");
        }
    }
}
