using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Numera.Platform.Db.Migrations
{
    /// <inheritdoc />
    public partial class LedgerEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "steuerschluessel",
                table: "postings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "tax_category",
                table: "postings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "tax_rate_percent",
                table: "postings",
                type: "numeric(5,2)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "festgeschrieben_at",
                table: "journal_entries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "journal_number",
                table: "journal_entries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "period_id",
                table: "journal_entries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "posting_type",
                table: "journal_entries",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "reverses_entry_id",
                table: "journal_entries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "source_type",
                table: "journal_entries",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "chart_variant",
                table: "accounts",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                table: "accounts",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_automatikkonto",
                table: "accounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "parent_number",
                table: "accounts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "steuerschluessel",
                table: "accounts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ustva_kennziffer",
                table: "accounts",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "fiscal_periods",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    month = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    locked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fiscal_periods", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ledger_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chart_variant = table.Column<int>(type: "integer", nullable: false),
                    besteuerungsart = table.Column<int>(type: "integer", nullable: false),
                    gewinnermittlungsart = table.Column<int>(type: "integer", nullable: false),
                    fiscal_year_start_month = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ledger_settings", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_postings_account_id",
                table: "postings",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_period_id",
                table: "journal_entries",
                column: "period_id");

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_reverses_entry_id",
                table: "journal_entries",
                column: "reverses_entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_tenant_id_entry_date",
                table: "journal_entries",
                columns: new[] { "tenant_id", "entry_date" });

            migrationBuilder.CreateIndex(
                name: "ix_fiscal_periods_tenant_id_id",
                table: "fiscal_periods",
                columns: new[] { "tenant_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ux_fiscal_periods_tenant_id_year_month",
                table: "fiscal_periods",
                columns: new[] { "tenant_id", "year", "month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ledger_settings_tenant_id_id",
                table: "ledger_settings",
                columns: new[] { "tenant_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ux_ledger_settings_tenant_id",
                table: "ledger_settings",
                column: "tenant_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_journal_entries_fiscal_periods_period_id",
                table: "journal_entries",
                column: "period_id",
                principalTable: "fiscal_periods",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_journal_entries_journal_entries_reverses_entry_id",
                table: "journal_entries",
                column: "reverses_entry_id",
                principalTable: "journal_entries",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            foreach (var t in new[] { "ledger_settings", "fiscal_periods" })
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
                "REVOKE UPDATE, DELETE ON postings FROM numera_app; " +
                "GRANT INSERT, SELECT ON postings TO numera_app; " +
                "REVOKE DELETE ON journal_entries FROM numera_app; " +
                "GRANT INSERT, SELECT, UPDATE ON journal_entries TO numera_app; " +
                "END IF; END $$;");

            migrationBuilder.Sql(
                "CREATE FUNCTION postings_immutable() RETURNS trigger LANGUAGE plpgsql AS $$ " +
                "BEGIN RAISE EXCEPTION 'posting rows are append-only (GoBD); reverse via Stornobuchung'; END; $$;");
            migrationBuilder.Sql(
                "CREATE TRIGGER postings_immutable BEFORE UPDATE OR DELETE ON postings " +
                "FOR EACH ROW EXECUTE FUNCTION postings_immutable();");

            migrationBuilder.Sql(
                "CREATE FUNCTION journal_entries_immutable() RETURNS trigger LANGUAGE plpgsql AS $$ " +
                "BEGIN " +
                "  IF (TG_OP = 'DELETE') THEN " +
                "    RAISE EXCEPTION 'journal entries are append-only (GoBD); reverse via Stornobuchung'; " +
                "  END IF; " +
                "  -- Only the Festschreibung stamp may ever change, and only once (NULL -> value). " +
                "  IF OLD.journal_number IS NOT NULL OR OLD.festgeschrieben_at IS NOT NULL THEN " +
                "    RAISE EXCEPTION 'journal entry is festgeschrieben and immutable'; " +
                "  END IF; " +
                "  IF NEW.tenant_id      IS DISTINCT FROM OLD.tenant_id " +
                "     OR NEW.entry_date   IS DISTINCT FROM OLD.entry_date " +
                "     OR NEW.source_ref   IS DISTINCT FROM OLD.source_ref " +
                "     OR NEW.source_type  IS DISTINCT FROM OLD.source_type " +
                "     OR NEW.description  IS DISTINCT FROM OLD.description " +
                "     OR NEW.posting_type IS DISTINCT FROM OLD.posting_type " +
                "     OR NEW.reverses_entry_id IS DISTINCT FROM OLD.reverses_entry_id " +
                "  THEN RAISE EXCEPTION 'journal entry business fields are frozen; only Festschreibung may stamp journal_number'; " +
                "  END IF; " +
                "  RETURN NEW; " +
                "END; $$;");
            migrationBuilder.Sql(
                "CREATE TRIGGER journal_entries_immutable BEFORE UPDATE OR DELETE ON journal_entries " +
                "FOR EACH ROW EXECUTE FUNCTION journal_entries_immutable();");

            migrationBuilder.Sql(
                "CREATE FUNCTION postings_balanced() RETURNS trigger LANGUAGE plpgsql AS $$ " +
                "DECLARE d numeric(19,4); c numeric(19,4); " +
                "BEGIN " +
                "  SELECT COALESCE(SUM(amount) FILTER (WHERE direction = 1), 0), " +
                "         COALESCE(SUM(amount) FILTER (WHERE direction = 2), 0) " +
                "    INTO d, c FROM postings WHERE journal_entry_id = NEW.journal_entry_id; " +
                "  IF d <> c THEN " +
                "    RAISE EXCEPTION 'journal entry % is unbalanced: Soll % <> Haben %', NEW.journal_entry_id, d, c; " +
                "  END IF; " +
                "  RETURN NULL; " +
                "END; $$;");
            migrationBuilder.Sql(
                "CREATE CONSTRAINT TRIGGER postings_balanced AFTER INSERT ON postings " +
                "DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION postings_balanced();");

            migrationBuilder.Sql(
                "CREATE FUNCTION journal_entries_period_lock() RETURNS trigger LANGUAGE plpgsql AS $$ " +
                "DECLARE locked int; " +
                "BEGIN " +
                "  SELECT COUNT(*) INTO locked FROM fiscal_periods " +
                "    WHERE tenant_id = NEW.tenant_id " +
                "      AND year  = EXTRACT(YEAR  FROM NEW.entry_date)::int " +
                "      AND month = EXTRACT(MONTH FROM NEW.entry_date)::int " +
                "      AND status = 1; " +
                "  IF locked > 0 THEN " +
                "    RAISE EXCEPTION 'fiscal period %-% is festgeschrieben (locked); no new bookings', " +
                "      EXTRACT(YEAR FROM NEW.entry_date)::int, EXTRACT(MONTH FROM NEW.entry_date)::int; " +
                "  END IF; " +
                "  RETURN NEW; " +
                "END; $$;");
            migrationBuilder.Sql(
                "CREATE TRIGGER journal_entries_period_lock BEFORE INSERT ON journal_entries " +
                "FOR EACH ROW EXECUTE FUNCTION journal_entries_period_lock();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS journal_entries_period_lock ON journal_entries;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS journal_entries_period_lock();");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS postings_balanced ON postings;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS postings_balanced();");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS journal_entries_immutable ON journal_entries;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS journal_entries_immutable();");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS postings_immutable ON postings;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS postings_immutable();");

            migrationBuilder.Sql(
                "DO $$ BEGIN " +
                "IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'numera_app') THEN " +
                "GRANT UPDATE, DELETE ON journal_entries TO numera_app; " +
                "GRANT UPDATE, DELETE ON postings TO numera_app; " +
                "END IF; END $$;");

            foreach (var t in new[] { "fiscal_periods", "ledger_settings" })
            {
                migrationBuilder.Sql($"DROP POLICY IF EXISTS tenant_isolation ON {t};");
                migrationBuilder.Sql($"ALTER TABLE {t} NO FORCE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"ALTER TABLE {t} DISABLE ROW LEVEL SECURITY;");
            }

            migrationBuilder.DropForeignKey(
                name: "fk_journal_entries_fiscal_periods_period_id",
                table: "journal_entries");

            migrationBuilder.DropForeignKey(
                name: "fk_journal_entries_journal_entries_reverses_entry_id",
                table: "journal_entries");

            migrationBuilder.DropTable(
                name: "fiscal_periods");

            migrationBuilder.DropTable(
                name: "ledger_settings");

            migrationBuilder.DropIndex(
                name: "ix_postings_account_id",
                table: "postings");

            migrationBuilder.DropIndex(
                name: "ix_journal_entries_period_id",
                table: "journal_entries");

            migrationBuilder.DropIndex(
                name: "ix_journal_entries_reverses_entry_id",
                table: "journal_entries");

            migrationBuilder.DropIndex(
                name: "ix_journal_entries_tenant_id_entry_date",
                table: "journal_entries");

            migrationBuilder.DropColumn(
                name: "steuerschluessel",
                table: "postings");

            migrationBuilder.DropColumn(
                name: "tax_category",
                table: "postings");

            migrationBuilder.DropColumn(
                name: "tax_rate_percent",
                table: "postings");

            migrationBuilder.DropColumn(
                name: "festgeschrieben_at",
                table: "journal_entries");

            migrationBuilder.DropColumn(
                name: "journal_number",
                table: "journal_entries");

            migrationBuilder.DropColumn(
                name: "period_id",
                table: "journal_entries");

            migrationBuilder.DropColumn(
                name: "posting_type",
                table: "journal_entries");

            migrationBuilder.DropColumn(
                name: "reverses_entry_id",
                table: "journal_entries");

            migrationBuilder.DropColumn(
                name: "source_type",
                table: "journal_entries");

            migrationBuilder.DropColumn(
                name: "chart_variant",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "is_active",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "is_automatikkonto",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "parent_number",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "steuerschluessel",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "ustva_kennziffer",
                table: "accounts");
        }
    }
}
