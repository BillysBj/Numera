using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Numera.Platform.Db.Migrations
{
    /// <inheritdoc />
    public partial class Payments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    value_date = table.Column<DateOnly>(type: "date", nullable: false),
                    method = table.Column<int>(type: "integer", nullable: false),
                    reference = table.Column<string>(type: "text", nullable: true),
                    reverses_payment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payment_allocation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    open_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    allocated_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_allocation", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payment_tenant_id_value_date",
                table: "payment",
                columns: new[] { "tenant_id", "value_date" });

            migrationBuilder.CreateIndex(
                name: "ix_payment_allocation_tenant_id_open_item_id",
                table: "payment_allocation",
                columns: new[] { "tenant_id", "open_item_id" });

            foreach (var t in new[] { "payment", "payment_allocation" })
            {
                migrationBuilder.Sql($"ALTER TABLE {t} ENABLE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"ALTER TABLE {t} FORCE ROW LEVEL SECURITY;");
                migrationBuilder.Sql(
                    $"CREATE POLICY tenant_isolation ON {t} " +
                    "USING (tenant_id = current_setting('app.current_tenant')::uuid) " +
                    "WITH CHECK (tenant_id = current_setting('app.current_tenant')::uuid);");
            }

            migrationBuilder.Sql(
                "DO $$ BEGIN " +
                "IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'numera_app') THEN " +
                "REVOKE UPDATE, DELETE ON payment FROM numera_app; " +
                "GRANT INSERT, SELECT ON payment TO numera_app; " +
                "REVOKE UPDATE, DELETE ON payment_allocation FROM numera_app; " +
                "GRANT INSERT, SELECT ON payment_allocation TO numera_app; " +
                "END IF; END $$;");

            migrationBuilder.Sql(
                "CREATE FUNCTION payment_immutable() RETURNS trigger LANGUAGE plpgsql AS $$ " +
                "BEGIN RAISE EXCEPTION 'payment rows are append-only (GoBD); reverse, do not edit/delete'; END; $$;");
            migrationBuilder.Sql(
                "CREATE TRIGGER payment_immutable BEFORE UPDATE OR DELETE ON payment " +
                "FOR EACH ROW EXECUTE FUNCTION payment_immutable();");
            migrationBuilder.Sql(
                "CREATE FUNCTION payment_allocation_immutable() RETURNS trigger LANGUAGE plpgsql AS $$ " +
                "BEGIN RAISE EXCEPTION 'payment_allocation rows are append-only (GoBD); reverse, do not edit/delete'; END; $$;");
            migrationBuilder.Sql(
                "CREATE TRIGGER payment_allocation_immutable BEFORE UPDATE OR DELETE ON payment_allocation " +
                "FOR EACH ROW EXECUTE FUNCTION payment_allocation_immutable();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS payment_allocation_immutable ON payment_allocation;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS payment_allocation_immutable();");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS payment_immutable ON payment;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS payment_immutable();");
            migrationBuilder.Sql(
                "DO $$ BEGIN " +
                "IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'numera_app') THEN " +
                "GRANT UPDATE, DELETE ON payment TO numera_app; " +
                "GRANT UPDATE, DELETE ON payment_allocation TO numera_app; " +
                "END IF; END $$;");

            foreach (var t in new[] { "payment_allocation", "payment" })
            {
                migrationBuilder.Sql($"DROP POLICY IF EXISTS tenant_isolation ON {t};");
                migrationBuilder.Sql($"ALTER TABLE {t} NO FORCE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"ALTER TABLE {t} DISABLE ROW LEVEL SECURITY;");
            }

            migrationBuilder.DropTable(
                name: "payment");

            migrationBuilder.DropTable(
                name: "payment_allocation");
        }
    }
}
