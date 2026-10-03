using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Numera.Platform.Db.Migrations
{
    /// <inheritdoc />
    public partial class VatPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "vat_payment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    value_date = table.Column<DateOnly>(type: "date", nullable: false),
                    reference = table.Column<string>(type: "text", nullable: true),
                    reverses_payment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vat_payment", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_vat_payment_tenant_id_reverses_payment_id",
                table: "vat_payment",
                columns: new[] { "tenant_id", "reverses_payment_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_vat_payment_tenant_id_value_date",
                table: "vat_payment",
                columns: new[] { "tenant_id", "value_date" });

            migrationBuilder.Sql("ALTER TABLE vat_payment ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE vat_payment FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON vat_payment " +
                "USING (tenant_id = current_setting('app.current_tenant')::uuid) " +
                "WITH CHECK (tenant_id = current_setting('app.current_tenant')::uuid);");
            migrationBuilder.Sql(
                "DO $$ BEGIN " +
                "IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'numera_app') THEN " +
                "REVOKE UPDATE, DELETE ON vat_payment FROM numera_app; " +
                "GRANT INSERT, SELECT ON vat_payment TO numera_app; " +
                "END IF; END $$;");
            migrationBuilder.Sql(
                "CREATE FUNCTION vat_payment_immutable() RETURNS trigger LANGUAGE plpgsql AS $$ " +
                "BEGIN RAISE EXCEPTION 'vat_payment rows are append-only (GoBD); reverse, do not edit/delete'; END; $$;");
            migrationBuilder.Sql(
                "CREATE TRIGGER vat_payment_immutable BEFORE UPDATE OR DELETE ON vat_payment " +
                "FOR EACH ROW EXECUTE FUNCTION vat_payment_immutable();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS vat_payment_immutable ON vat_payment;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS vat_payment_immutable();");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON vat_payment;");
            migrationBuilder.Sql("ALTER TABLE vat_payment NO FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE vat_payment DISABLE ROW LEVEL SECURITY;");
            migrationBuilder.DropTable(
                name: "vat_payment");
        }
    }
}
