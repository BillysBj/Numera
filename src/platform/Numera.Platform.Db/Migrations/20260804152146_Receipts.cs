using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Numera.Platform.Db.Migrations
{
    /// <inheritdoc />
    public partial class Receipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "receipt",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    inbound_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    archive_id = table.Column<Guid>(type: "uuid", nullable: true),
                    content_hash = table.Column<string>(type: "text", nullable: false),
                    supplier_name = table.Column<string>(type: "text", nullable: true),
                    supplier_vat_id = table.Column<string>(type: "text", nullable: true),
                    invoice_number = table.Column<string>(type: "text", nullable: true),
                    invoice_date = table.Column<DateOnly>(type: "date", nullable: true),
                    expense_date = table.Column<DateOnly>(type: "date", nullable: true),
                    net_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    vat_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    gross_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    vat_rate_percent = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    currency = table.Column<string>(type: "text", nullable: true),
                    field_confidence = table.Column<string>(type: "jsonb", nullable: true),
                    matched_partner_id = table.Column<Guid>(type: "uuid", nullable: true),
                    expense_account_override = table.Column<string>(type: "text", nullable: true),
                    journal_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_receipt", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "receipt_archive",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receipt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_bytes = table.Column<byte[]>(type: "bytea", nullable: false),
                    original_file_name = table.Column<string>(type: "text", nullable: false),
                    content_type = table.Column<string>(type: "text", nullable: false),
                    byte_size = table.Column<long>(type: "bigint", nullable: false),
                    content_hash = table.Column<string>(type: "text", nullable: false),
                    source = table.Column<int>(type: "integer", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    uploaded_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_receipt_archive", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_receipt_tenant_id_content_hash",
                table: "receipt",
                columns: new[] { "tenant_id", "content_hash" });

            migrationBuilder.CreateIndex(
                name: "ix_receipt_tenant_id_status",
                table: "receipt",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_receipt_archive_tenant_id_content_hash",
                table: "receipt_archive",
                columns: new[] { "tenant_id", "content_hash" });

            migrationBuilder.Sql("ALTER TABLE receipt ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE receipt FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(
                """
                CREATE POLICY tenant_isolation ON receipt
                USING (tenant_id = current_setting('app.current_tenant')::uuid)
                WITH CHECK (tenant_id = current_setting('app.current_tenant')::uuid);
                """);

            migrationBuilder.Sql("ALTER TABLE receipt_archive ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE receipt_archive FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(
                """
                CREATE POLICY tenant_isolation ON receipt_archive
                USING (tenant_id = current_setting('app.current_tenant')::uuid)
                WITH CHECK (tenant_id = current_setting('app.current_tenant')::uuid);
                """);

            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'numera_app') THEN
                    REVOKE UPDATE, DELETE ON receipt_archive FROM numera_app;
                    GRANT INSERT, SELECT ON receipt_archive TO numera_app;
                  END IF;
                END
                $$;
                """);

            migrationBuilder.Sql(
                """
                CREATE FUNCTION receipt_archive_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  RAISE EXCEPTION 'receipt_archive rows are append-only (GoBD); archive a new original, do not edit/delete';
                END;
                $$;
                """);
            migrationBuilder.Sql(
                """
                CREATE TRIGGER receipt_archive_immutable
                BEFORE UPDATE OR DELETE ON receipt_archive
                FOR EACH ROW EXECUTE FUNCTION receipt_archive_immutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS receipt_archive_immutable ON receipt_archive;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS receipt_archive_immutable();");
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'numera_app') THEN
                    GRANT UPDATE, DELETE ON receipt_archive TO numera_app;
                  END IF;
                END
                $$;
                """);
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON receipt_archive;");
            migrationBuilder.Sql("ALTER TABLE receipt_archive NO FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE receipt_archive DISABLE ROW LEVEL SECURITY;");

            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON receipt;");
            migrationBuilder.Sql("ALTER TABLE receipt NO FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE receipt DISABLE ROW LEVEL SECURITY;");

            migrationBuilder.DropTable(
                name: "receipt");

            migrationBuilder.DropTable(
                name: "receipt_archive");
        }
    }
}
