using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Numera.Platform.Db.Migrations
{
    /// <inheritdoc />
    public partial class UgStatutoryReserve : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ug_ruecklagepflicht_aktiv",
                table: "ledger_settings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "ug_ruecklage_buchungen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    jahr = table.Column<int>(type: "integer", nullable: false),
                    betrag = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    verlustvortrag_vorjahr = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    journal_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ug_ruecklage_buchungen", x => x.id);
                    table.ForeignKey(
                        name: "fk_ug_ruecklage_buchungen_journal_entries_journal_entry_id",
                        column: x => x.journal_entry_id,
                        principalTable: "journal_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ug_ruecklage_buchungen_journal_entry_id",
                table: "ug_ruecklage_buchungen",
                column: "journal_entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_ug_ruecklage_buchungen_tenant_id_jahr",
                table: "ug_ruecklage_buchungen",
                columns: new[] { "tenant_id", "jahr" },
                unique: true);

            migrationBuilder.Sql("ALTER TABLE ug_ruecklage_buchungen ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE ug_ruecklage_buchungen FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON ug_ruecklage_buchungen " +
                "USING (tenant_id = current_setting('app.current_tenant')::uuid) " +
                "WITH CHECK (tenant_id = current_setting('app.current_tenant')::uuid);");

            migrationBuilder.Sql(
                "DO $$ BEGIN " +
                "IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'numera_app') THEN " +
                "GRANT SELECT, INSERT ON ug_ruecklage_buchungen TO numera_app; " +
                // Override the migrator's baseline default DML privileges for this append-only table.
                "REVOKE UPDATE, DELETE ON ug_ruecklage_buchungen FROM numera_app; " +
                "END IF; END $$;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ug_ruecklage_buchungen");

            migrationBuilder.DropColumn(
                name: "ug_ruecklagepflicht_aktiv",
                table: "ledger_settings");
        }
    }
}
