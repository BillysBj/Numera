using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Numera.Platform.Db.Migrations
{
    /// <inheritdoc />
    public partial class PartnerTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "partner_tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    partner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    assigned_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_partner_tasks", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_partner_tasks_tenant_id_partner_id_due_date",
                table: "partner_tasks",
                columns: new[] { "tenant_id", "partner_id", "due_date" });

            migrationBuilder.Sql("ALTER TABLE partner_tasks ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE partner_tasks FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON partner_tasks " +
                "USING (tenant_id = current_setting('app.current_tenant')::uuid) " +
                "WITH CHECK (tenant_id = current_setting('app.current_tenant')::uuid);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON partner_tasks;");
            migrationBuilder.Sql("ALTER TABLE partner_tasks NO FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE partner_tasks DISABLE ROW LEVEL SECURITY;");

            migrationBuilder.DropTable(
                name: "partner_tasks");
        }
    }
}
