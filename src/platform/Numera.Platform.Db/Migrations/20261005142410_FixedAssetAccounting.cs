using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Numera.Platform.Db.Migrations
{
    /// <inheritdoc />
    public partial class FixedAssetAccounting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "fixed_assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bezeichnung = table.Column<string>(type: "text", nullable: false),
                    lieferant = table.Column<string>(type: "text", nullable: true),
                    beleg_ref = table.Column<string>(type: "text", nullable: true),
                    rechnungsdatum = table.Column<DateOnly>(type: "date", nullable: true),
                    anschaffungs_datum = table.Column<DateOnly>(type: "date", nullable: false),
                    inbetriebnahme_datum = table.Column<DateOnly>(type: "date", nullable: false),
                    anschaffungskosten_netto = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    anschaffungsnebenkosten_netto = table.Column<decimal>(type: "numeric(19,4)", nullable: false, defaultValue: 0m),
                    anlagekonto_number = table.Column<string>(type: "text", nullable: false),
                    abschreibungskonto_number = table.Column<string>(type: "text", nullable: false),
                    nutzungsdauer_jahre = table.Column<int>(type: "integer", nullable: false),
                    methode = table.Column<int>(type: "integer", nullable: false),
                    abgangs_datum = table.Column<DateOnly>(type: "date", nullable: true),
                    abgangs_art = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fixed_assets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "afa_buchungen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fixed_asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    jahr = table.Column<int>(type: "integer", nullable: false),
                    betrag = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    journal_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_afa_buchungen", x => x.id);
                    table.ForeignKey(
                        name: "fk_afa_buchungen_fixed_assets_fixed_asset_id",
                        column: x => x.fixed_asset_id,
                        principalTable: "fixed_assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_afa_buchungen_journal_entries_journal_entry_id",
                        column: x => x.journal_entry_id,
                        principalTable: "journal_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_afa_buchungen_fixed_asset_id",
                table: "afa_buchungen",
                column: "fixed_asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_afa_buchungen_journal_entry_id",
                table: "afa_buchungen",
                column: "journal_entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_afa_buchungen_tenant_id_fixed_asset_id_jahr",
                table: "afa_buchungen",
                columns: new[] { "tenant_id", "fixed_asset_id", "jahr" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fixed_assets_tenant_id_id",
                table: "fixed_assets",
                columns: new[] { "tenant_id", "id" });

            foreach (var table in new[] { "fixed_assets", "afa_buchungen" })
            {
                migrationBuilder.Sql($"ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"ALTER TABLE {table} FORCE ROW LEVEL SECURITY;");
                migrationBuilder.Sql(
                    $"CREATE POLICY tenant_isolation ON {table} " +
                    "USING (tenant_id = current_setting('app.current_tenant')::uuid) " +
                    "WITH CHECK (tenant_id = current_setting('app.current_tenant')::uuid);");
            }

            migrationBuilder.Sql(
                "DO $$ BEGIN " +
                "IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'numera_app') THEN " +
                "GRANT SELECT, INSERT, UPDATE, DELETE ON fixed_assets TO numera_app; " +
                "GRANT SELECT, INSERT ON afa_buchungen TO numera_app; " +
                "END IF; END $$;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "afa_buchungen");

            migrationBuilder.DropTable(
                name: "fixed_assets");
        }
    }
}
