using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Numera.Platform.Db.Migrations
{
    /// <inheritdoc />
    public partial class AuditEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "text", nullable: false),
                    entity_type = table.Column<string>(type: "text", nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    before = table.Column<string>(type: "jsonb", nullable: true),
                    after = table.Column<string>(type: "jsonb", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    prev_hash = table.Column<byte[]>(type: "bytea", nullable: true),
                    row_hash = table.Column<byte[]>(type: "bytea", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_events", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_tenant_id_id",
                table: "audit_events",
                columns: new[] { "tenant_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_tenant_id_occurred_at",
                table: "audit_events",
                columns: new[] { "tenant_id", "occurred_at" });

            // -----------------------------------------------------------------
            // Row-Level Security: tenant isolation, identical pattern to every
            // other tenant table (ENABLE + FORCE + tenant_isolation policy). Even
            // the schema-owning migrator role is subject to it (FORCE); the runtime
            // role (numera_app) holds no BYPASSRLS.
            // -----------------------------------------------------------------
            migrationBuilder.Sql("ALTER TABLE audit_events ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE audit_events FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON audit_events " +
                "USING (tenant_id = current_setting('app.current_tenant')::uuid) " +
                "WITH CHECK (tenant_id = current_setting('app.current_tenant')::uuid);");

            // -----------------------------------------------------------------
            // Append-only enforcement #1 (privilege): the app role may only
            // INSERT/SELECT — UPDATE/DELETE are revoked. Guarded by role existence
            // so a CI database without numera_app (created by scripts/db-roles.sql)
            // still applies the migration; the trigger below is the unconditional
            // enforcement.
            // -----------------------------------------------------------------
            migrationBuilder.Sql(
                "DO $$ BEGIN " +
                "IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'numera_app') THEN " +
                "REVOKE UPDATE, DELETE ON audit_events FROM numera_app; " +
                "GRANT INSERT, SELECT ON audit_events TO numera_app; " +
                "END IF; END $$;");

            // -----------------------------------------------------------------
            // Append-only enforcement #2 (belt-and-braces trigger): blocks any
            // UPDATE/DELETE regardless of granted privileges (e.g. a mis-grant, a
            // superuser, or the owning migrator). This is the REAL, unconditional
            // guarantee that audit rows are immutable once written.
            // -----------------------------------------------------------------
            migrationBuilder.Sql(
                "CREATE FUNCTION audit_no_mutate() RETURNS trigger LANGUAGE plpgsql AS $$ " +
                "BEGIN RAISE EXCEPTION 'audit_events is append-only'; END; $$;");
            migrationBuilder.Sql(
                "CREATE TRIGGER audit_immutable BEFORE UPDATE OR DELETE ON audit_events " +
                "FOR EACH ROW EXECUTE FUNCTION audit_no_mutate();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse append-only enforcement.
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS audit_immutable ON audit_events;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS audit_no_mutate();");
            migrationBuilder.Sql(
                "DO $$ BEGIN " +
                "IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'numera_app') THEN " +
                "GRANT UPDATE, DELETE ON audit_events TO numera_app; " +
                "END IF; END $$;");

            // Reverse RLS.
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON audit_events;");
            migrationBuilder.Sql("ALTER TABLE audit_events NO FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE audit_events DISABLE ROW LEVEL SECURITY;");

            migrationBuilder.DropTable(
                name: "audit_events");
        }
    }
}
