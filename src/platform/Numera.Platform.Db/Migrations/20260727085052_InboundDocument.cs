using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Numera.Platform.Db.Migrations
{
    /// <inheritdoc />
    public partial class InboundDocument : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inbound_document",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_bytes = table.Column<byte[]>(type: "bytea", nullable: false),
                    original_file_name = table.Column<string>(type: "text", nullable: false),
                    original_content_type = table.Column<string>(type: "text", nullable: false),
                    byte_size = table.Column<long>(type: "bigint", nullable: false),
                    detected_format = table.Column<int>(type: "integer", nullable: false),
                    read_model = table.Column<string>(type: "jsonb", nullable: true),
                    validation_status = table.Column<int>(type: "integer", nullable: false),
                    validation_report = table.Column<string>(type: "jsonb", nullable: true),
                    matched_partner_id = table.Column<Guid>(type: "uuid", nullable: true),
                    seller_name = table.Column<string>(type: "text", nullable: true),
                    seller_vat_id = table.Column<string>(type: "text", nullable: true),
                    invoice_number = table.Column<string>(type: "text", nullable: true),
                    total_gross = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    currency = table.Column<string>(type: "text", nullable: true),
                    invoice_date = table.Column<DateOnly>(type: "date", nullable: true),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbound_document", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_inbound_document_tenant_id_uploaded_at",
                table: "inbound_document",
                columns: new[] { "tenant_id", "uploaded_at" });

            // =================================================================
            // Row-Level Security for the NEW inbound_document table. Identical
            // ENABLE + FORCE + tenant_isolation pattern to InitialPlatform /
            // AuditEvents / _Crm / _Catalog / _CompanyProfile / _SalesDocuments /
            // _DocumentDelivery / _DocumentEInvoice. Reflective ITenantEntity
            // discovery creates the table + tenant index + query filter but NEVER
            // the RLS policy (the #1 silent cross-tenant-leak trap), so it is
            // hand-written here. FORCE subjects even the migrator owner to the
            // policy; numera_app holds no BYPASSRLS, making RLS the primary,
            // unconditional isolation control. A leak here would cross-contaminate
            // one tenant's received supplier invoice (its immutable original bytes,
            // read-model and matched supplier) onto another's — a GoBD/privacy
            // breach on Eingangsbelege.
            // =================================================================
            foreach (var t in new[] { "inbound_document" })
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
            // Reverse RLS on the table before dropping it.
            foreach (var t in new[] { "inbound_document" })
            {
                migrationBuilder.Sql($"DROP POLICY IF EXISTS tenant_isolation ON {t};");
                migrationBuilder.Sql($"ALTER TABLE {t} NO FORCE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"ALTER TABLE {t} DISABLE ROW LEVEL SECURITY;");
            }

            migrationBuilder.DropTable(
                name: "inbound_document");
        }
    }
}
