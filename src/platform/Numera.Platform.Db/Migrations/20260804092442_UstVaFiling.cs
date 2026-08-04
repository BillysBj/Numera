using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Numera.Platform.Db.Migrations
{
    /// <inheritdoc />
    public partial class UstVaFiling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ust_va_filing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    jahr = table.Column<int>(type: "integer", nullable: false),
                    zeitraum = table.Column<string>(type: "text", nullable: false),
                    besteuerungsart = table.Column<int>(type: "integer", nullable: false),
                    kz_snapshot_json = table.Column<string>(type: "jsonb", nullable: false),
                    zahllast = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    xml_bytes = table.Column<byte[]>(type: "bytea", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    berichtigt_von_filing_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ust_va_filing", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ust_va_filing_tenant_id_jahr_zeitraum",
                table: "ust_va_filing",
                columns: new[] { "tenant_id", "jahr", "zeitraum" });

            migrationBuilder.Sql("ALTER TABLE ust_va_filing ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE ust_va_filing FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(
                """
                CREATE POLICY tenant_isolation ON ust_va_filing
                USING (tenant_id = current_setting('app.current_tenant')::uuid)
                WITH CHECK (tenant_id = current_setting('app.current_tenant')::uuid);
                """);

            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'numera_app') THEN
                    REVOKE UPDATE, DELETE ON ust_va_filing FROM numera_app;
                    GRANT INSERT, SELECT ON ust_va_filing TO numera_app;
                  END IF;
                END
                $$;
                """);

            migrationBuilder.Sql(
                """
                CREATE FUNCTION ust_va_filing_immutable_on_submit() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF OLD.status = 1 THEN
                    RAISE EXCEPTION 'submitted ust_va_filing rows are append-only; create a correction filing';
                  END IF;
                  IF TG_OP = 'DELETE' THEN
                    RETURN OLD;
                  END IF;
                  RETURN NEW;
                END;
                $$;
                """);
            migrationBuilder.Sql(
                """
                CREATE TRIGGER ust_va_filing_immutable_on_submit
                BEFORE UPDATE OR DELETE ON ust_va_filing
                FOR EACH ROW EXECUTE FUNCTION ust_va_filing_immutable_on_submit();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS ust_va_filing_immutable_on_submit ON ust_va_filing;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS ust_va_filing_immutable_on_submit();");
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'numera_app') THEN
                    GRANT UPDATE, DELETE ON ust_va_filing TO numera_app;
                  END IF;
                END
                $$;
                """);
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON ust_va_filing;");
            migrationBuilder.Sql("ALTER TABLE ust_va_filing NO FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE ust_va_filing DISABLE ROW LEVEL SECURITY;");

            migrationBuilder.DropTable(
                name: "ust_va_filing");
        }
    }
}
