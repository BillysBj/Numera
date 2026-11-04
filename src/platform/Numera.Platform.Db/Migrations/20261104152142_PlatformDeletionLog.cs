using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Numera.Platform.Db.Migrations
{
    /// <summary>
    /// Creates the tenant-independent, append-only <c>platform_deletion_log</c> (Phase B1 Scheibe 2).
    /// It records the proof that a tenant was hard-purged (Art. 5(2) DSGVO accountability) and
    /// deliberately outlives the tenant's own rows, so it is NOT an <c>ITenantEntity</c>, carries NO
    /// RLS, and is NOT mapped in <c>NumeraDbContext</c> (so the tenant export never leaks it and the
    /// purge never deletes it). It holds only the proof-of-deletion, no personal data of the subjects.
    /// </summary>
    public partial class PlatformDeletionLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE TABLE platform_deletion_log (
                    id               uuid        NOT NULL PRIMARY KEY,
                    tenant_id        uuid        NOT NULL,
                    tenant_name      text        NOT NULL,
                    requested_by     uuid        NULL,
                    requested_at     timestamptz NULL,
                    purged_at        timestamptz NOT NULL,
                    rows_deleted     integer     NOT NULL,
                    row_counts       jsonb       NOT NULL,
                    keycloak_outcome text        NOT NULL,
                    finapi_outcome   text        NOT NULL,
                    stripe_outcome   text        NOT NULL
                );
                """);

            migrationBuilder.Sql(
                "CREATE INDEX ix_platform_deletion_log_purged_at ON platform_deletion_log (purged_at);");

            // Operator-only: the least-privilege runtime role (numera_app) must have NO access to
            // this cross-tenant table. db-roles.sql grants DML on ALL (incl. future) tables to
            // numera_app via ALTER DEFAULT PRIVILEGES, so this new table would inherit those grants
            // unless explicitly revoked. The purge writes it on the privileged maintenance
            // connection (superuser), which ignores grants.
            migrationBuilder.Sql(
                "DO $$ BEGIN " +
                "IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'numera_app') THEN " +
                "REVOKE ALL ON platform_deletion_log FROM numera_app; " +
                "END IF; END $$;");

            // Belt-and-braces append-only guarantee: the log is insert-once. Any UPDATE/DELETE is
            // blocked unconditionally (even for the owner), so a purge record can never be altered.
            migrationBuilder.Sql(
                "CREATE FUNCTION platform_deletion_log_no_mutate() RETURNS trigger LANGUAGE plpgsql AS $$ " +
                "BEGIN RAISE EXCEPTION 'platform_deletion_log is append-only'; END; $$;");
            migrationBuilder.Sql(
                "CREATE TRIGGER platform_deletion_log_append_only " +
                "BEFORE UPDATE OR DELETE ON platform_deletion_log " +
                "FOR EACH ROW EXECUTE FUNCTION platform_deletion_log_no_mutate();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS platform_deletion_log_append_only ON platform_deletion_log;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS platform_deletion_log_no_mutate();");
            migrationBuilder.Sql("DROP TABLE IF EXISTS platform_deletion_log;");
        }
    }
}
