using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Numera.Platform.Db.Migrations
{
    /// <inheritdoc />
    public partial class Crm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "partner_activities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    partner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    summary = table.Column<string>(type: "text", nullable: false),
                    ref_type = table.Column<string>(type: "text", nullable: true),
                    ref_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_partner_activities", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "partner_contacts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    partner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    salutation = table.Column<string>(type: "text", nullable: true),
                    first_name = table.Column<string>(type: "text", nullable: true),
                    last_name = table.Column<string>(type: "text", nullable: false),
                    email = table.Column<string>(type: "text", nullable: true),
                    phone = table.Column<string>(type: "text", nullable: true),
                    position = table.Column<string>(type: "text", nullable: true),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_partner_contacts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "partner_notes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    partner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_partner_notes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "partners",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    legal_form = table.Column<string>(type: "text", nullable: true),
                    billing_address_street = table.Column<string>(type: "text", nullable: false),
                    billing_address_line2 = table.Column<string>(type: "text", nullable: true),
                    billing_address_postal_code = table.Column<string>(type: "text", nullable: false),
                    billing_address_city = table.Column<string>(type: "text", nullable: false),
                    billing_address_country_code = table.Column<string>(type: "text", nullable: false),
                    billing_address_po_box = table.Column<string>(type: "text", nullable: true),
                    shipping_address_street = table.Column<string>(type: "text", nullable: true),
                    shipping_address_line2 = table.Column<string>(type: "text", nullable: true),
                    shipping_address_postal_code = table.Column<string>(type: "text", nullable: true),
                    shipping_address_city = table.Column<string>(type: "text", nullable: true),
                    shipping_address_country_code = table.Column<string>(type: "text", nullable: true),
                    shipping_address_po_box = table.Column<string>(type: "text", nullable: true),
                    vat_id = table.Column<string>(type: "text", nullable: true),
                    tax_number = table.Column<string>(type: "text", nullable: true),
                    email = table.Column<string>(type: "text", nullable: true),
                    phone = table.Column<string>(type: "text", nullable: true),
                    website = table.Column<string>(type: "text", nullable: true),
                    payment_terms_net_days = table.Column<int>(type: "integer", nullable: true),
                    skonto_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    skonto_days = table.Column<int>(type: "integer", nullable: true),
                    default_currency = table.Column<string>(type: "text", nullable: false),
                    language = table.Column<int>(type: "integer", nullable: false),
                    default_tax_category = table.Column<int>(type: "integer", nullable: true),
                    is_customer = table.Column<bool>(type: "boolean", nullable: false),
                    is_supplier = table.Column<bool>(type: "boolean", nullable: false),
                    customer_number = table.Column<string>(type: "text", nullable: true),
                    supplier_number = table.Column<string>(type: "text", nullable: true),
                    iban = table.Column<string>(type: "text", nullable: true),
                    bic = table.Column<string>(type: "text", nullable: true),
                    leitweg_id = table.Column<string>(type: "text", nullable: true),
                    debtor_account = table.Column<string>(type: "text", nullable: true),
                    creditor_account = table.Column<string>(type: "text", nullable: true),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_partners", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_partner_activities_tenant_id_partner_id_occurred_at",
                table: "partner_activities",
                columns: new[] { "tenant_id", "partner_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_partner_contacts_tenant_id_partner_id",
                table: "partner_contacts",
                columns: new[] { "tenant_id", "partner_id" });

            migrationBuilder.CreateIndex(
                name: "ix_partner_notes_tenant_id_partner_id",
                table: "partner_notes",
                columns: new[] { "tenant_id", "partner_id" });

            migrationBuilder.CreateIndex(
                name: "ix_partners_tenant_id_name",
                table: "partners",
                columns: new[] { "tenant_id", "name" });

            // -----------------------------------------------------------------
            // Row-Level Security: tenant isolation on every CRM table, identical
            // pattern to InitialPlatform / AuditEvents (ENABLE + FORCE + a
            // tenant_isolation policy). EF has no fluent RLS API, so this is raw
            // SQL. FORCE subjects even the table owner (numera_migrator) to the
            // policy; the runtime role (numera_app) holds no BYPASSRLS, making RLS
            // the primary, unconditional isolation control. Reflective entity
            // discovery does NOT create these policies — they MUST be hand-written.
            // -----------------------------------------------------------------
            foreach (var t in new[] { "partners", "partner_contacts", "partner_notes", "partner_activities" })
            {
                migrationBuilder.Sql($"ALTER TABLE {t} ENABLE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"ALTER TABLE {t} FORCE ROW LEVEL SECURITY;");
                migrationBuilder.Sql(
                    $"CREATE POLICY tenant_isolation ON {t} " +
                    "USING (tenant_id = current_setting('app.current_tenant')::uuid) " +
                    "WITH CHECK (tenant_id = current_setting('app.current_tenant')::uuid);");
            }

            // -----------------------------------------------------------------
            // Customer/supplier numbers are unique per tenant among the ACTIVE
            // (non-archived) partners, and only when present. Partial unique indexes
            // model exactly that: an archived partner never blocks reuse of its
            // number, and a NULL number is unconstrained.
            // -----------------------------------------------------------------
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX ux_partners_tenant_customer_number ON partners " +
                "(tenant_id, customer_number) WHERE customer_number IS NOT NULL AND archived_at IS NULL;");
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX ux_partners_tenant_supplier_number ON partners " +
                "(tenant_id, supplier_number) WHERE supplier_number IS NOT NULL AND archived_at IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse the partial-unique indexes and the RLS setup before dropping.
            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_partners_tenant_customer_number;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_partners_tenant_supplier_number;");

            foreach (var t in new[] { "partners", "partner_contacts", "partner_notes", "partner_activities" })
            {
                migrationBuilder.Sql($"DROP POLICY IF EXISTS tenant_isolation ON {t};");
                migrationBuilder.Sql($"ALTER TABLE {t} NO FORCE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"ALTER TABLE {t} DISABLE ROW LEVEL SECURITY;");
            }

            migrationBuilder.DropTable(
                name: "partner_activities");

            migrationBuilder.DropTable(
                name: "partner_contacts");

            migrationBuilder.DropTable(
                name: "partner_notes");

            migrationBuilder.DropTable(
                name: "partners");
        }
    }
}
