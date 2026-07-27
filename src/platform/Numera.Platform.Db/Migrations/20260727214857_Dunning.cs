using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Numera.Platform.Db.Migrations
{
    /// <inheritdoc />
    public partial class Dunning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "current_dunning_level",
                table: "open_items",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateOnly>(
                name: "last_dunned_on",
                table: "open_items",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "dunning_level_config",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    level = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    days_after_due = table.Column<int>(type: "integer", nullable: false),
                    fee = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    charge_interest = table.Column<bool>(type: "boolean", nullable: false),
                    interest_rate_percent = table.Column<decimal>(type: "numeric(6,3)", precision: 6, scale: 3, nullable: false),
                    template_text_de = table.Column<string>(type: "text", nullable: false),
                    template_text_en = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dunning_level_config", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "dunning_notice",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    open_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    level = table.Column<int>(type: "integer", nullable: false),
                    issued_on = table.Column<DateOnly>(type: "date", nullable: false),
                    new_due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    overdue_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    fee = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    interest = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    interest_rate_percent = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    total_to_pay = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    rendered_pdf = table.Column<byte[]>(type: "bytea", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dunning_notice", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_dunning_level_config_tenant_id_level",
                table: "dunning_level_config",
                columns: new[] { "tenant_id", "level" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_dunning_notice_tenant_id_open_item_id",
                table: "dunning_notice",
                columns: new[] { "tenant_id", "open_item_id" });

            migrationBuilder.CreateIndex(
                name: "ix_dunning_notice_tenant_id_open_item_id_level",
                table: "dunning_notice",
                columns: new[] { "tenant_id", "open_item_id", "level" },
                unique: true);

            // New tenant tables require explicit database-enforced isolation; reflective
            // ITenantEntity discovery supplies only the application query filter.
            foreach (var t in new[] { "dunning_level_config", "dunning_notice" })
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
            migrationBuilder.DropIndex(
                name: "ix_dunning_notice_tenant_id_open_item_id_level",
                table: "dunning_notice");

            foreach (var t in new[] { "dunning_level_config", "dunning_notice" })
            {
                migrationBuilder.Sql($"DROP POLICY IF EXISTS tenant_isolation ON {t};");
                migrationBuilder.Sql($"ALTER TABLE {t} NO FORCE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"ALTER TABLE {t} DISABLE ROW LEVEL SECURITY;");
            }

            migrationBuilder.DropColumn(
                name: "current_dunning_level",
                table: "open_items");

            migrationBuilder.DropColumn(
                name: "last_dunned_on",
                table: "open_items");

            migrationBuilder.DropTable(
                name: "dunning_level_config");

            migrationBuilder.DropTable(
                name: "dunning_notice");
        }
    }
}
