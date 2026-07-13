using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Numera.Platform.Db.Migrations
{
    /// <inheritdoc />
    public partial class DocumentDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "logo_bytes",
                table: "company_profile",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "logo_content_type",
                table: "company_profile",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "document_email",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_address = table.Column<string>(type: "text", nullable: false),
                    subject = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_email", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "document_render",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pdf_bytes = table.Column<byte[]>(type: "bytea", nullable: false),
                    document_number = table.Column<string>(type: "text", nullable: false),
                    language = table.Column<string>(type: "text", nullable: false),
                    byte_size = table.Column<long>(type: "bigint", nullable: false),
                    rendered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_render", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_document_email_tenant_id_document_id",
                table: "document_email",
                columns: new[] { "tenant_id", "document_id" });

            migrationBuilder.CreateIndex(
                name: "ix_document_render_tenant_id_document_id",
                table: "document_render",
                columns: new[] { "tenant_id", "document_id" });

            // =================================================================
            // Row-Level Security for the two NEW delivery tables. Identical
            // ENABLE + FORCE + tenant_isolation pattern to InitialPlatform /
            // AuditEvents / _Crm / _Catalog / _CompanyProfile / _SalesDocuments.
            // Reflective ITenantEntity discovery creates the tables + tenant
            // index + query filter but NEVER the RLS policy (the #1 silent
            // cross-tenant-leak trap), so it is hand-written here. FORCE subjects
            // even the migrator owner to the policy; numera_app holds no
            // BYPASSRLS, making RLS the primary, unconditional isolation control.
            // A leak here would cross-contaminate one tenant's rendered invoice
            // PDF / send record onto another's.
            //
            // company_profile is NOT re-policied: it already carries its
            // tenant_isolation policy from the _CompanyProfile migration, and the
            // two new logo columns inherit that existing table policy.
            // =================================================================
            foreach (var t in new[] { "document_render", "document_email" })
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
            // Reverse RLS on both new tables before dropping them.
            foreach (var t in new[] { "document_render", "document_email" })
            {
                migrationBuilder.Sql($"DROP POLICY IF EXISTS tenant_isolation ON {t};");
                migrationBuilder.Sql($"ALTER TABLE {t} NO FORCE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"ALTER TABLE {t} DISABLE ROW LEVEL SECURITY;");
            }

            migrationBuilder.DropTable(
                name: "document_email");

            migrationBuilder.DropTable(
                name: "document_render");

            migrationBuilder.DropColumn(
                name: "logo_bytes",
                table: "company_profile");

            migrationBuilder.DropColumn(
                name: "logo_content_type",
                table: "company_profile");
        }
    }
}
