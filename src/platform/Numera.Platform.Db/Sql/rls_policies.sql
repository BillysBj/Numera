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

-- --- Self-scoped tenant table ------------------------------------------------
-- tenants: the row IS the tenant, so isolate on id, not tenant_id.
ALTER TABLE tenants ENABLE ROW LEVEL SECURITY;
ALTER TABLE tenants FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON tenants
    USING (id = current_setting('app.current_tenant')::uuid)
    WITH CHECK (id = current_setting('app.current_tenant')::uuid);
