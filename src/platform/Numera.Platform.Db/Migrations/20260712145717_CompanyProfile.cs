using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Numera.Platform.Db.Migrations
{
    /// <inheritdoc />
    public partial class CompanyProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "company_profile",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_name = table.Column<string>(type: "text", nullable: false),
                    address_street = table.Column<string>(type: "text", nullable: false),
                    address_line2 = table.Column<string>(type: "text", nullable: true),
                    address_postal_code = table.Column<string>(type: "text", nullable: false),
                    address_city = table.Column<string>(type: "text", nullable: false),
                    address_country_code = table.Column<string>(type: "text", nullable: false),
                    address_po_box = table.Column<string>(type: "text", nullable: true),
                    vat_id = table.Column<string>(type: "text", nullable: true),
                    tax_number = table.Column<string>(type: "text", nullable: true),
                    is_kleinunternehmer = table.Column<bool>(type: "boolean", nullable: false),
                    default_payment_terms_net_days = table.Column<int>(type: "integer", nullable: true),
                    default_tax_category = table.Column<int>(type: "integer", nullable: true),
                    iban = table.Column<string>(type: "text", nullable: true),
                    bic = table.Column<string>(type: "text", nullable: true),
                    bank_name = table.Column<string>(type: "text", nullable: true),
                    register_court = table.Column<string>(type: "text", nullable: true),
                    register_number = table.Column<string>(type: "text", nullable: true),
                    managing_director = table.Column<string>(type: "text", nullable: true),
                    contact_email = table.Column<string>(type: "text", nullable: true),
                    contact_phone = table.Column<string>(type: "text", nullable: true),
                    logo_ref = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company_profile", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_company_profile_tenant_id",
                table: "company_profile",
                column: "tenant_id",
                unique: true);

            // -----------------------------------------------------------------
            // Row-Level Security: tenant isolation on company_profile, identical
            // pattern to InitialPlatform / AuditEvents / _Crm / _Catalog (ENABLE +
            // FORCE + a tenant_isolation policy). EF has no fluent RLS API, so this
            // is raw SQL. FORCE subjects even the table owner (numera_migrator) to
            // the policy; the runtime role (numera_app) holds no BYPASSRLS, making
            // RLS the primary, unconditional isolation control. Reflective entity
            // discovery does NOT create this policy — it MUST be hand-written.
            // company_profile is the §14 issuer master-data table plan 03-05
            // snapshots onto finalized invoices; a leak here would cross-contaminate
            // one tenant's legal identity onto another's documents.
            // -----------------------------------------------------------------
            migrationBuilder.Sql("ALTER TABLE company_profile ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE company_profile FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON company_profile " +
                "USING (tenant_id = current_setting('app.current_tenant')::uuid) " +
                "WITH CHECK (tenant_id = current_setting('app.current_tenant')::uuid);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse the RLS setup before dropping the table.
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON company_profile;");
            migrationBuilder.Sql("ALTER TABLE company_profile NO FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE company_profile DISABLE ROW LEVEL SECURITY;");

            migrationBuilder.DropTable(
                name: "company_profile");
        }
    }
}
