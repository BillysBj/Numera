-- =============================================================================
-- Numera Row-Level Security policies (canonical reference)
-- =============================================================================
-- This is the human-readable master copy of the RLS statements that the initial
-- EF Core migration (InitialPlatform) applies via migrationBuilder.Sql. The
-- migration is the source of truth for a fresh database; this file documents the
-- intent and can be diffed against future policy changes.
--
-- Model:
--   * Every TENANT-SCOPED table (membership, accounts, journal_entries, postings)
--     is isolated on its `tenant_id` column against the `app.current_tenant` GUC.
--   * The `tenants` table IS the tenant, so it is SELF-scoped on `id`.
--   * RLS is both ENABLEd and FORCEd on every table so that even the table owner
--     (numera_migrator) is subject to the policy -- ownership never bypasses.
--   * The runtime role (numera_app) holds NO BYPASSRLS, so these policies are the
--     primary, unconditional isolation control.
--
-- The GUC is set per-connection by TenantConnectionInterceptor:
--     SELECT set_config('app.current_tenant', '<tenant-uuid>', false)
-- and RESET on connection close.
-- =============================================================================

-- --- Tenant-scoped tables ----------------------------------------------------
-- membership
ALTER TABLE membership ENABLE ROW LEVEL SECURITY;
ALTER TABLE membership FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON membership
    USING (tenant_id = current_setting('app.current_tenant')::uuid)
    WITH CHECK (tenant_id = current_setting('app.current_tenant')::uuid);

-- accounts
ALTER TABLE accounts ENABLE ROW LEVEL SECURITY;
ALTER TABLE accounts FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON accounts
    USING (tenant_id = current_setting('app.current_tenant')::uuid)
    WITH CHECK (tenant_id = current_setting('app.current_tenant')::uuid);

-- journal_entries
ALTER TABLE journal_entries ENABLE ROW LEVEL SECURITY;
ALTER TABLE journal_entries FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON journal_entries
    USING (tenant_id = current_setting('app.current_tenant')::uuid)
    WITH CHECK (tenant_id = current_setting('app.current_tenant')::uuid);

-- postings
ALTER TABLE postings ENABLE ROW LEVEL SECURITY;
ALTER TABLE postings FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON postings
    USING (tenant_id = current_setting('app.current_tenant')::uuid)
    WITH CHECK (tenant_id = current_setting('app.current_tenant')::uuid);

-- --- CRM tables (Phase 2, _Crm migration) ------------------------------------
-- partners, partner_contacts, partner_notes, partner_activities: standard
-- tenant_id isolation. Archived rows are deliberately NOT filtered by RLS
-- (archival is app-level only, RESEARCH.md Pattern 1).
-- partners
ALTER TABLE partners ENABLE ROW LEVEL SECURITY;
ALTER TABLE partners FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON partners
    USING (tenant_id = current_setting('app.current_tenant')::uuid)
    WITH CHECK (tenant_id = current_setting('app.current_tenant')::uuid);

-- partner_contacts
ALTER TABLE partner_contacts ENABLE ROW LEVEL SECURITY;
ALTER TABLE partner_contacts FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON partner_contacts
    USING (tenant_id = current_setting('app.current_tenant')::uuid)
    WITH CHECK (tenant_id = current_setting('app.current_tenant')::uuid);

-- partner_notes
ALTER TABLE partner_notes ENABLE ROW LEVEL SECURITY;
ALTER TABLE partner_notes FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON partner_notes
    USING (tenant_id = current_setting('app.current_tenant')::uuid)
    WITH CHECK (tenant_id = current_setting('app.current_tenant')::uuid);

-- partner_activities
ALTER TABLE partner_activities ENABLE ROW LEVEL SECURITY;
ALTER TABLE partner_activities FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON partner_activities
    USING (tenant_id = current_setting('app.current_tenant')::uuid)
    WITH CHECK (tenant_id = current_setting('app.current_tenant')::uuid);

-- Customer/supplier numbers: unique per tenant among active (non-archived) rows.
CREATE UNIQUE INDEX ux_partners_tenant_customer_number ON partners
    (tenant_id, customer_number) WHERE customer_number IS NOT NULL AND archived_at IS NULL;
CREATE UNIQUE INDEX ux_partners_tenant_supplier_number ON partners
    (tenant_id, supplier_number) WHERE supplier_number IS NOT NULL AND archived_at IS NULL;

-- --- Catalog table (Phase 2, _Catalog migration) -----------------------------
-- catalog_items: standard tenant_id isolation. Archived rows are deliberately NOT
-- filtered by RLS (archival is app-level only, RESEARCH.md Pattern 1).
-- catalog_items
ALTER TABLE catalog_items ENABLE ROW LEVEL SECURITY;
ALTER TABLE catalog_items FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON catalog_items
    USING (tenant_id = current_setting('app.current_tenant')::uuid)
    WITH CHECK (tenant_id = current_setting('app.current_tenant')::uuid);

-- Article number: unique per tenant among active (non-archived) rows, so an archived
-- item's number is reusable and a different tenant may reuse any number.
CREATE UNIQUE INDEX ux_catalog_items_tenant_item_number ON catalog_items
    (tenant_id, item_number) WHERE archived_at IS NULL;

-- --- Sales table (Phase 3, _CompanyProfile migration) ------------------------
-- company_profile: the §14 UStG issuer master data (legal name, address, VAT/tax
-- id, §19 flag). Standard tenant_id isolation. A tenant has exactly one profile,
-- enforced by a unique index on tenant_id (also the tenant-leading access index).
-- company_profile
ALTER TABLE company_profile ENABLE ROW LEVEL SECURITY;
ALTER TABLE company_profile FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON company_profile
    USING (tenant_id = current_setting('app.current_tenant')::uuid)
    WITH CHECK (tenant_id = current_setting('app.current_tenant')::uuid);

-- Exactly one company_profile per tenant (unique on tenant_id).
CREATE UNIQUE INDEX ix_company_profile_tenant_id ON company_profile (tenant_id);

-- --- Sales-document tables (Phase 3, _SalesDocuments migration) ---------------
-- The polymorphic sales-document schema: one sales_documents header (a document_type
-- discriminator), its sales_document_lines + sales_document_tax_breakdown (BG-23)
-- children, and the numbering substrate (number_sequences, document_number_formats)
-- plus open_items. Standard tenant_id isolation on all 6.
-- NOTE: GoBD immutability is ALSO DB-enforced by hand-written triggers that live in
-- the _SalesDocuments migration (NOT here): sales_document_immutable (status-guarded
-- BEFORE UPDATE/DELETE on sales_documents — drafts mutable, finalized frozen) and
-- sales_document_child_immutable (parent-status guard on lines + tax breakdown).
-- Number reuse is blocked by a partial UNIQUE (tenant_id, document_type,
-- document_number) WHERE document_number IS NOT NULL.
-- sales_documents
ALTER TABLE sales_documents ENABLE ROW LEVEL SECURITY;
ALTER TABLE sales_documents FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON sales_documents
    USING (tenant_id = current_setting('app.current_tenant')::uuid)
    WITH CHECK (tenant_id = current_setting('app.current_tenant')::uuid);

-- sales_document_lines
ALTER TABLE sales_document_lines ENABLE ROW LEVEL SECURITY;
ALTER TABLE sales_document_lines FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON sales_document_lines
    USING (tenant_id = current_setting('app.current_tenant')::uuid)
    WITH CHECK (tenant_id = current_setting('app.current_tenant')::uuid);

-- sales_document_tax_breakdown
ALTER TABLE sales_document_tax_breakdown ENABLE ROW LEVEL SECURITY;
ALTER TABLE sales_document_tax_breakdown FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON sales_document_tax_breakdown
    USING (tenant_id = current_setting('app.current_tenant')::uuid)
    WITH CHECK (tenant_id = current_setting('app.current_tenant')::uuid);

-- number_sequences
ALTER TABLE number_sequences ENABLE ROW LEVEL SECURITY;
ALTER TABLE number_sequences FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON number_sequences
    USING (tenant_id = current_setting('app.current_tenant')::uuid)
    WITH CHECK (tenant_id = current_setting('app.current_tenant')::uuid);

-- document_number_formats
ALTER TABLE document_number_formats ENABLE ROW LEVEL SECURITY;
ALTER TABLE document_number_formats FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON document_number_formats
    USING (tenant_id = current_setting('app.current_tenant')::uuid)
    WITH CHECK (tenant_id = current_setting('app.current_tenant')::uuid);

-- open_items
ALTER TABLE open_items ENABLE ROW LEVEL SECURITY;
ALTER TABLE open_items FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON open_items
    USING (tenant_id = current_setting('app.current_tenant')::uuid)
    WITH CHECK (tenant_id = current_setting('app.current_tenant')::uuid);

-- Document numbers unique per (tenant, doc_type) among numbered (non-draft) rows.
CREATE UNIQUE INDEX ux_sales_documents_tenant_type_number ON sales_documents
    (tenant_id, document_type, document_number) WHERE document_number IS NOT NULL;
CREATE UNIQUE INDEX ux_number_sequences_tenant_type_year ON number_sequences
    (tenant_id, doc_type, year);
CREATE UNIQUE INDEX ux_document_number_formats_tenant_type ON document_number_formats
    (tenant_id, doc_type);

-- --- Self-scoped tenant table ------------------------------------------------
-- tenants: the row IS the tenant, so isolate on id, not tenant_id.
ALTER TABLE tenants ENABLE ROW LEVEL SECURITY;
ALTER TABLE tenants FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON tenants
    USING (id = current_setting('app.current_tenant')::uuid)
    WITH CHECK (id = current_setting('app.current_tenant')::uuid);

-- sales_document_prepayment (Phase 7, frozen Schlussrechnung BT-113 snapshots)
ALTER TABLE sales_document_prepayment ENABLE ROW LEVEL SECURITY;
ALTER TABLE sales_document_prepayment FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON sales_document_prepayment
    USING (tenant_id = current_setting('app.current_tenant')::uuid)
    WITH CHECK (tenant_id = current_setting('app.current_tenant')::uuid);

-- recurring invoice templates (Phase 7, one tenant-scoped schedule per template)
ALTER TABLE recurring_invoice_templates ENABLE ROW LEVEL SECURITY;
ALTER TABLE recurring_invoice_templates FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON recurring_invoice_templates
    USING (tenant_id = current_setting('app.current_tenant')::uuid)
    WITH CHECK (tenant_id = current_setting('app.current_tenant')::uuid);

ALTER TABLE recurring_invoice_template_lines ENABLE ROW LEVEL SECURITY;
ALTER TABLE recurring_invoice_template_lines FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON recurring_invoice_template_lines
    USING (tenant_id = current_setting('app.current_tenant')::uuid)
    WITH CHECK (tenant_id = current_setting('app.current_tenant')::uuid);

-- partner tasks (Phase 8, editable due-date tracking; no append-only trigger)
ALTER TABLE partner_tasks ENABLE ROW LEVEL SECURITY;
ALTER TABLE partner_tasks FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON partner_tasks
    USING (tenant_id = current_setting('app.current_tenant')::uuid)
    WITH CHECK (tenant_id = current_setting('app.current_tenant')::uuid);
