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

-- --- Self-scoped tenant table ------------------------------------------------
-- tenants: the row IS the tenant, so isolate on id, not tenant_id.
ALTER TABLE tenants ENABLE ROW LEVEL SECURITY;
ALTER TABLE tenants FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON tenants
    USING (id = current_setting('app.current_tenant')::uuid)
    WITH CHECK (id = current_setting('app.current_tenant')::uuid);
