using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Numera.Platform.Db.Migrations
{
    /// <inheritdoc />
    public partial class SalesDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "document_number_formats",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    doc_type = table.Column<int>(type: "integer", nullable: false),
                    prefix = table.Column<string>(type: "text", nullable: false),
                    include_year = table.Column<bool>(type: "boolean", nullable: false),
                    padding = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_number_formats", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "number_sequences",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    doc_type = table.Column<int>(type: "integer", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    next_value = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_number_sequences", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "open_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    partner_id = table.Column<Guid>(type: "uuid", nullable: true),
                    document_number = table.Column<string>(type: "text", nullable: false),
                    currency = table.Column<string>(type: "text", nullable: false),
                    original_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    open_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    issued_on = table.Column<DateOnly>(type: "date", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    skonto_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    skonto_days = table.Column<int>(type: "integer", nullable: true),
                    skonto_due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    skonto_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_open_items", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sales_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_type = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    document_number = table.Column<string>(type: "text", nullable: true),
                    source_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    corrects_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cancelled_by_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    partner_id = table.Column<Guid>(type: "uuid", nullable: true),
                    recipient_snapshot = table.Column<string>(type: "jsonb", nullable: true),
                    issuer_snapshot = table.Column<string>(type: "jsonb", nullable: true),
                    document_date = table.Column<DateOnly>(type: "date", nullable: false),
                    service_date = table.Column<DateOnly>(type: "date", nullable: true),
                    service_period_end = table.Column<DateOnly>(type: "date", nullable: true),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    currency = table.Column<string>(type: "text", nullable: false),
                    total_net = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    total_tax = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    total_gross = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    amount_due = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    is_kleinunternehmer = table.Column<bool>(type: "boolean", nullable: false),
                    reverse_charge = table.Column<bool>(type: "boolean", nullable: false),
                    buyer_reference = table.Column<string>(type: "text", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    finalized_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_documents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sales_document_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_number = table.Column<int>(type: "integer", nullable: false),
                    catalog_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(19,6)", precision: 19, scale: 6, nullable: false),
                    unit_code = table.Column<string>(type: "text", nullable: false),
                    net_unit_price = table.Column<decimal>(type: "numeric(19,6)", precision: 19, scale: 6, nullable: false),
                    line_net_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    tax_category = table.Column<int>(type: "integer", nullable: false),
                    vat_rate_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_document_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_sales_document_lines_sales_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "sales_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sales_document_tax_breakdown",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tax_category = table.Column<int>(type: "integer", nullable: false),
                    vat_rate_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    taxable_base = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    tax_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    exemption_reason_code = table.Column<string>(type: "text", nullable: true),
                    exemption_reason_text = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_document_tax_breakdown", x => x.id);
                    table.ForeignKey(
                        name: "fk_sales_document_tax_breakdown_sales_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "sales_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_document_number_formats_tenant_id_id",
                table: "document_number_formats",
                columns: new[] { "tenant_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_number_sequences_tenant_id_id",
                table: "number_sequences",
                columns: new[] { "tenant_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_open_items_tenant_id_status_due_date",
                table: "open_items",
                columns: new[] { "tenant_id", "status", "due_date" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_document_lines_document_id",
                table: "sales_document_lines",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_document_lines_tenant_id_document_id",
                table: "sales_document_lines",
                columns: new[] { "tenant_id", "document_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_document_tax_breakdown_document_id",
                table: "sales_document_tax_breakdown",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_document_tax_breakdown_tenant_id_document_id",
                table: "sales_document_tax_breakdown",
                columns: new[] { "tenant_id", "document_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_documents_tenant_id_document_type_status",
                table: "sales_documents",
                columns: new[] { "tenant_id", "document_type", "status" });

            // =================================================================
            // Row-Level Security for ALL 6 new sales tables. Identical ENABLE +
            // FORCE + tenant_isolation pattern to InitialPlatform / AuditEvents /
            // _Crm / _Catalog. Reflective entity discovery creates the tables +
            // tenant index + query filter but NEVER the RLS policy (the #1 silent
            // cross-tenant-leak trap), so it is hand-written here. FORCE subjects
            // even the migrator owner to the policy; numera_app holds no BYPASSRLS,
            // making RLS the primary, unconditional isolation control.
            // =================================================================
            foreach (var t in new[]
                     {
                         "sales_documents",
                         "sales_document_lines",
                         "sales_document_tax_breakdown",
                         "number_sequences",
                         "document_number_formats",
                         "open_items",
                     })
            {
                migrationBuilder.Sql($"ALTER TABLE {t} ENABLE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"ALTER TABLE {t} FORCE ROW LEVEL SECURITY;");
                migrationBuilder.Sql(
                    $"CREATE POLICY tenant_isolation ON {t} " +
                    "USING (tenant_id = current_setting('app.current_tenant')::uuid) " +
                    "WITH CHECK (tenant_id = current_setting('app.current_tenant')::uuid);");
            }

            // -----------------------------------------------------------------
            // Numbering integrity constraints (RESEARCH.md Pattern 3 / Pitfall 6).
            //  * Document numbers are unique per (tenant, doc_type) among the rows
            //    that HAVE a number — a partial unique index (drafts carry NULL).
            //    This makes number REUSE impossible without any brittle gapless
            //    logic; German law needs einmalig+nachvollziehbar, NOT lückenlos.
            //  * One counter row per (tenant, doc_type, year) and one number-format
            //    config per (tenant, doc_type).
            // -----------------------------------------------------------------
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX ux_sales_documents_tenant_type_number ON sales_documents " +
                "(tenant_id, document_type, document_number) WHERE document_number IS NOT NULL;");
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX ux_number_sequences_tenant_type_year ON number_sequences " +
                "(tenant_id, doc_type, year);");
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX ux_document_number_formats_tenant_type ON document_number_formats " +
                "(tenant_id, doc_type);");

            // -----------------------------------------------------------------
            // GoBD immutability — the STATUS-GUARDED parent trigger (RESEARCH.md
            // Pattern 2). Unlike audit_events (a blanket REVOKE — purely append-
            // only), a sales_documents DRAFT (status = 0) MUST stay fully mutable.
            // So this BEFORE UPDATE/DELETE trigger:
            //   * blocks DELETE once status <> 0 (finalized docs are permanent);
            //   * returns NEW unchecked while status = 0 (drafts freely mutable);
            //   * once finalized, RAISEs if ANY frozen business column changed
            //     (IS DISTINCT FROM catches NULLs). The lifecycle whitelist
            //     (status, sent_at, finalized_at, cancelled_by_document_id,
            //     due_date, amount_due) may still change.
            // -----------------------------------------------------------------
            migrationBuilder.Sql(
                "CREATE FUNCTION sales_document_immutable() RETURNS trigger LANGUAGE plpgsql AS $$ " +
                "BEGIN " +
                "  IF (TG_OP = 'DELETE') THEN " +
                "    IF OLD.status <> 0 THEN " +
                "      RAISE EXCEPTION 'sales_documents: finalized document % is immutable (no delete)', OLD.id; " +
                "    END IF; " +
                "    RETURN OLD; " +
                "  END IF; " +
                "  IF OLD.status = 0 THEN " +
                "    RETURN NEW; " +
                "  END IF; " +
                "  IF NEW.document_type      IS DISTINCT FROM OLD.document_type " +
                "     OR NEW.document_number  IS DISTINCT FROM OLD.document_number " +
                "     OR NEW.tenant_id        IS DISTINCT FROM OLD.tenant_id " +
                "     OR NEW.partner_id       IS DISTINCT FROM OLD.partner_id " +
                "     OR NEW.recipient_snapshot IS DISTINCT FROM OLD.recipient_snapshot " +
                "     OR NEW.issuer_snapshot  IS DISTINCT FROM OLD.issuer_snapshot " +
                "     OR NEW.document_date    IS DISTINCT FROM OLD.document_date " +
                "     OR NEW.service_date     IS DISTINCT FROM OLD.service_date " +
                "     OR NEW.total_net        IS DISTINCT FROM OLD.total_net " +
                "     OR NEW.total_tax        IS DISTINCT FROM OLD.total_tax " +
                "     OR NEW.total_gross      IS DISTINCT FROM OLD.total_gross " +
                "     OR NEW.currency         IS DISTINCT FROM OLD.currency " +
                "  THEN " +
                "    RAISE EXCEPTION 'sales_documents: finalized document % is immutable (business fields frozen)', OLD.id; " +
                "  END IF; " +
                "  RETURN NEW; " +
                "END; $$;");
            migrationBuilder.Sql(
                "CREATE TRIGGER sales_document_immutable " +
                "BEFORE UPDATE OR DELETE ON sales_documents " +
                "FOR EACH ROW EXECUTE FUNCTION sales_document_immutable();");

            // -----------------------------------------------------------------
            // Child immutability — lines and the tax breakdown have no own status,
            // so the trigger looks up the parent's status and RAISEs on any INSERT
            // / UPDATE / DELETE once the parent is non-Draft. This also blocks the
            // classic attack of appending a line to a finalized invoice.
            // Finalize (plan 03-05) inserts breakdown/lines while the parent is
            // still Draft (status = 0) and flips status LAST, so those writes pass.
            // -----------------------------------------------------------------
            migrationBuilder.Sql(
                "CREATE FUNCTION sales_document_child_immutable() RETURNS trigger LANGUAGE plpgsql AS $$ " +
                "DECLARE parent_status int; " +
                "BEGIN " +
                "  SELECT status INTO parent_status FROM sales_documents " +
                "    WHERE id = COALESCE(NEW.document_id, OLD.document_id); " +
                "  IF parent_status IS DISTINCT FROM 0 THEN " +
                "    RAISE EXCEPTION 'sales_document child: parent document is finalized/immutable'; " +
                "  END IF; " +
                "  RETURN COALESCE(NEW, OLD); " +
                "END; $$;");
            migrationBuilder.Sql(
                "CREATE TRIGGER sales_document_line_immutable " +
                "BEFORE INSERT OR UPDATE OR DELETE ON sales_document_lines " +
                "FOR EACH ROW EXECUTE FUNCTION sales_document_child_immutable();");
            migrationBuilder.Sql(
                "CREATE TRIGGER sales_document_tax_breakdown_immutable " +
                "BEFORE INSERT OR UPDATE OR DELETE ON sales_document_tax_breakdown " +
                "FOR EACH ROW EXECUTE FUNCTION sales_document_child_immutable();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse the immutability triggers + functions.
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS sales_document_tax_breakdown_immutable ON sales_document_tax_breakdown;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS sales_document_line_immutable ON sales_document_lines;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS sales_document_immutable ON sales_documents;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS sales_document_child_immutable();");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS sales_document_immutable();");

            // Reverse the numbering-integrity unique indexes.
            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_document_number_formats_tenant_type;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_number_sequences_tenant_type_year;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_sales_documents_tenant_type_number;");

            // Reverse RLS on all 6 tables.
            foreach (var t in new[]
                     {
                         "sales_documents",
                         "sales_document_lines",
                         "sales_document_tax_breakdown",
                         "number_sequences",
                         "document_number_formats",
                         "open_items",
                     })
            {
                migrationBuilder.Sql($"DROP POLICY IF EXISTS tenant_isolation ON {t};");
                migrationBuilder.Sql($"ALTER TABLE {t} NO FORCE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"ALTER TABLE {t} DISABLE ROW LEVEL SECURITY;");
            }

            migrationBuilder.DropTable(
                name: "document_number_formats");

            migrationBuilder.DropTable(
                name: "number_sequences");

            migrationBuilder.DropTable(
                name: "open_items");

            migrationBuilder.DropTable(
                name: "sales_document_lines");

            migrationBuilder.DropTable(
                name: "sales_document_tax_breakdown");

            migrationBuilder.DropTable(
                name: "sales_documents");
        }
    }
}
