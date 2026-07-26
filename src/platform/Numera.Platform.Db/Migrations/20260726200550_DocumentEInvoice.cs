using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Numera.Platform.Db.Migrations
{
    /// <inheritdoc />
    public partial class DocumentEInvoice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "document_einvoice",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    format = table.Column<int>(type: "integer", nullable: false),
                    xml = table.Column<byte[]>(type: "bytea", nullable: false),
                    document_number = table.Column<string>(type: "text", nullable: false),
                    validation_status = table.Column<int>(type: "integer", nullable: false),
                    validation_report = table.Column<string>(type: "jsonb", nullable: true),
                    byte_size = table.Column<long>(type: "bigint", nullable: false),
                    generated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    validated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_einvoice", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_document_einvoice_tenant_id_document_id",
                table: "document_einvoice",
                columns: new[] { "tenant_id", "document_id" });

            // =================================================================
            // Row-Level Security for the NEW e-invoice artifact table. Identical
            // ENABLE + FORCE + tenant_isolation pattern to InitialPlatform /
            // AuditEvents / _Crm / _Catalog / _CompanyProfile / _SalesDocuments /
            // _DocumentDelivery. Reflective ITenantEntity discovery creates the
            // table + tenant index + query filter but NEVER the RLS policy (the
            // #1 silent cross-tenant-leak trap), so it is hand-written here. FORCE
            // subjects even the migrator owner to the policy; numera_app holds no
            // BYPASSRLS, making RLS the primary, unconditional isolation control.
            // A leak here would cross-contaminate one tenant's generated XRechnung
            // (its legal invoice XML + validation verdict) onto another's.
            // =================================================================
            migrationBuilder.Sql("ALTER TABLE document_einvoice ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE document_einvoice FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON document_einvoice " +
                "USING (tenant_id = current_setting('app.current_tenant')::uuid) " +
                "WITH CHECK (tenant_id = current_setting('app.current_tenant')::uuid);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse RLS before dropping the table.
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON document_einvoice;");
            migrationBuilder.Sql("ALTER TABLE document_einvoice NO FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE document_einvoice DISABLE ROW LEVEL SECURITY;");

            migrationBuilder.DropTable(
                name: "document_einvoice");
        }
    }
}
