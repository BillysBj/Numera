using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Numera.Platform.Db.Migrations
{
    /// <inheritdoc />
    public partial class Catalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "catalog_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_number = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    unit_code = table.Column<string>(type: "text", nullable: false),
                    net_price = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "text", nullable: false),
                    cost_price = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    tax_category = table.Column<int>(type: "integer", nullable: false),
                    vat_rate_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    revenue_account = table.Column<string>(type: "text", nullable: true),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_catalog_items", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_catalog_items_tenant_id_name",
                table: "catalog_items",
                columns: new[] { "tenant_id", "name" });

            // -----------------------------------------------------------------
            // Row-Level Security: tenant isolation on catalog_items, identical
            // pattern to InitialPlatform / AuditEvents / _Crm (ENABLE + FORCE + a
            // tenant_isolation policy). EF has no fluent RLS API, so this is raw
            // SQL. FORCE subjects even the table owner (numera_migrator) to the
            // policy; the runtime role (numera_app) holds no BYPASSRLS, making RLS
            // the primary, unconditional isolation control. Reflective entity
            // discovery does NOT create this policy — it MUST be hand-written.
            // -----------------------------------------------------------------
            migrationBuilder.Sql("ALTER TABLE catalog_items ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE catalog_items FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON catalog_items " +
                "USING (tenant_id = current_setting('app.current_tenant')::uuid) " +
                "WITH CHECK (tenant_id = current_setting('app.current_tenant')::uuid);");

            // -----------------------------------------------------------------
            // The article number is unique per tenant among the ACTIVE (non-archived)
            // items. A partial unique index models exactly that: an archived item never
            // blocks reuse of its number, and a different tenant may reuse any number.
            // -----------------------------------------------------------------
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX ux_catalog_items_tenant_item_number ON catalog_items " +
                "(tenant_id, item_number) WHERE archived_at IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse the partial-unique index and the RLS setup before dropping.
            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_catalog_items_tenant_item_number;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON catalog_items;");
            migrationBuilder.Sql("ALTER TABLE catalog_items NO FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE catalog_items DISABLE ROW LEVEL SECURITY;");

            migrationBuilder.DropTable(
                name: "catalog_items");
        }
    }
}
