using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Numera.Platform.Db.Migrations
{
    /// <inheritdoc />
    public partial class CustomerFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "customer_files",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    partner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bytes = table.Column<byte[]>(type: "bytea", nullable: false),
                    file_name = table.Column<string>(type: "text", nullable: false),
                    content_type = table.Column<string>(type: "text", nullable: false),
                    byte_size = table.Column<long>(type: "bigint", nullable: false),
                    uploaded_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_files", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_customer_files_tenant_id_partner_id_uploaded_at",
                table: "customer_files",
                columns: new[] { "tenant_id", "partner_id", "uploaded_at" });

            migrationBuilder.Sql("ALTER TABLE customer_files ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE customer_files FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON customer_files " +
                "USING (tenant_id = current_setting('app.current_tenant')::uuid) " +
                "WITH CHECK (tenant_id = current_setting('app.current_tenant')::uuid);");

            migrationBuilder.Sql(
                "DO $$ BEGIN " +
                "IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'numera_app') THEN " +
                "REVOKE UPDATE, DELETE ON customer_files FROM numera_app; " +
                "GRANT INSERT, SELECT ON customer_files TO numera_app; " +
                "END IF; END $$;");

            migrationBuilder.Sql(
                "CREATE FUNCTION customer_file_immutable() RETURNS trigger LANGUAGE plpgsql AS $$ " +
                "BEGIN RAISE EXCEPTION 'customer_files rows are append-only (Kundenakte); upload a new file, do not edit/delete'; END; $$;");
            migrationBuilder.Sql(
                "CREATE TRIGGER customer_file_immutable BEFORE UPDATE OR DELETE ON customer_files " +
                "FOR EACH ROW EXECUTE FUNCTION customer_file_immutable();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS customer_file_immutable ON customer_files;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS customer_file_immutable();");
            migrationBuilder.Sql(
                "DO $$ BEGIN " +
                "IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'numera_app') THEN " +
                "GRANT UPDATE, DELETE ON customer_files TO numera_app; " +
                "END IF; END $$;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON customer_files;");
            migrationBuilder.Sql("ALTER TABLE customer_files NO FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE customer_files DISABLE ROW LEVEL SECURITY;");

            migrationBuilder.DropTable(
                name: "customer_files");
        }
    }
}
