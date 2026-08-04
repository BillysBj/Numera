using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Numera.Platform.Db.Migrations
{
    /// <inheritdoc />
    public partial class BelegeMailIntake : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "processed_belege_mail",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_id = table.Column<string>(type: "text", nullable: false),
                    content_hash = table.Column<string>(type: "text", nullable: true),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_processed_belege_mail", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tenant_belege_mailbox",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    address_token = table.Column<string>(type: "text", nullable: false),
                    local_part = table.Column<string>(type: "text", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_belege_mailbox", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_processed_belege_mail_tenant_id_message_id",
                table: "processed_belege_mail",
                columns: new[] { "tenant_id", "message_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenant_belege_mailbox_address_token",
                table: "tenant_belege_mailbox",
                column: "address_token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenant_belege_mailbox_tenant_id",
                table: "tenant_belege_mailbox",
                column: "tenant_id",
                unique: true);

            migrationBuilder.Sql("ALTER TABLE processed_belege_mail ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE processed_belege_mail FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(
                """
                CREATE POLICY tenant_isolation ON processed_belege_mail
                USING (tenant_id = current_setting('app.current_tenant')::uuid)
                WITH CHECK (tenant_id = current_setting('app.current_tenant')::uuid);
                """);

            migrationBuilder.Sql("ALTER TABLE tenant_belege_mailbox ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE tenant_belege_mailbox FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(
                """
                CREATE POLICY tenant_isolation ON tenant_belege_mailbox
                USING (tenant_id = current_setting('app.current_tenant')::uuid)
                WITH CHECK (tenant_id = current_setting('app.current_tenant')::uuid);
                """);

            // Polling must resolve the unguessable token before a tenant GUC can be set. This
            // exact-token, active-row-only function is the sole cross-tenant routing aperture;
            // all attachment and dedup work after it remains subject to the tenant RLS policy.
            migrationBuilder.Sql(
                """
                CREATE FUNCTION public.resolve_belege_mailbox(p_address_token text)
                RETURNS TABLE (tenant_id uuid)
                LANGUAGE sql
                SECURITY DEFINER
                SET search_path = pg_catalog, public
                AS $function$
                  SELECT mailbox.tenant_id
                  FROM public.tenant_belege_mailbox AS mailbox
                  WHERE mailbox.address_token = p_address_token
                    AND mailbox.is_active
                $function$;
                """);
            migrationBuilder.Sql(
                """
                REVOKE ALL ON FUNCTION public.resolve_belege_mailbox(text) FROM PUBLIC;
                DO $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'numera_app') THEN
                    GRANT EXECUTE ON FUNCTION public.resolve_belege_mailbox(text) TO numera_app;
                  END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS public.resolve_belege_mailbox(text);");

            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON processed_belege_mail;");
            migrationBuilder.Sql("ALTER TABLE processed_belege_mail NO FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE processed_belege_mail DISABLE ROW LEVEL SECURITY;");

            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON tenant_belege_mailbox;");
            migrationBuilder.Sql("ALTER TABLE tenant_belege_mailbox NO FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE tenant_belege_mailbox DISABLE ROW LEVEL SECURITY;");

            migrationBuilder.DropTable(
                name: "processed_belege_mail");

            migrationBuilder.DropTable(
                name: "tenant_belege_mailbox");
        }
    }
}
