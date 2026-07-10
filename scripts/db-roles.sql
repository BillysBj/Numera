-- =============================================================================
-- Numera least-privilege database roles
-- =============================================================================
-- Two roles, strict separation of duties:
--
--   numera_migrator  Owns the schema and runs all DDL / EF Core migrations
--                    (`dotnet ef database update`). Because it OWNS the tables it
--                    would implicitly bypass Row-Level Security -- which is exactly
--                    why every tenant table is created with FORCE ROW LEVEL SECURITY
--                    in the migration. It is NOT granted BYPASSRLS and NOT a
--                    SUPERUSER, so ownership alone never leaks cross-tenant data.
--
--   numera_app       The runtime application role used by the API/Worker via
--                    appsettings connection strings. Granted only DML on data
--                    tables. Explicitly NO BYPASSRLS, NO SUPERUSER, NO CREATE -- it
--                    can never see across tenants because RLS is FORCEd and it holds
--                    no bypass privilege.
--
-- Usage:
--   * `dotnet ef database update`  -> connect as numera_migrator
--   * appsettings ConnectionStrings -> connect as numera_app
--
-- Passwords below are placeholders for local/dev; inject real secrets in prod.
-- Run this script once as a superuser (e.g. the postgres bootstrap user) against
-- the target database BEFORE applying migrations.
-- =============================================================================

-- --- Migrator role (owns schema, runs DDL) -----------------------------------
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'numera_migrator') THEN
        CREATE ROLE numera_migrator LOGIN PASSWORD 'dev_migrator'
            NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE;
    END IF;
END
$$;

-- Migrator may create objects in the public schema (DDL / migrations).
GRANT CONNECT ON DATABASE numera TO numera_migrator;
GRANT USAGE, CREATE ON SCHEMA public TO numera_migrator;

-- --- Application role (runtime, DML only, RLS-subject) ------------------------
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'numera_app') THEN
        -- Explicitly least-privilege: NO BYPASSRLS, NO SUPERUSER, NO CREATE*.
        CREATE ROLE numera_app LOGIN PASSWORD 'dev_app'
            NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE;
    END IF;
END
$$;

GRANT CONNECT ON DATABASE numera TO numera_app;
GRANT USAGE ON SCHEMA public TO numera_app;

-- Data-table DML for the app role. Applied to existing and FUTURE tables created
-- by the migrator, so new module tables are covered automatically.
-- NOTE: append-only audit-table exceptions (revoke UPDATE/DELETE) are handled in
-- plan 01-03; this script grants the baseline runtime privileges only.
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO numera_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO numera_app;

ALTER DEFAULT PRIVILEGES FOR ROLE numera_migrator IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO numera_app;
ALTER DEFAULT PRIVILEGES FOR ROLE numera_migrator IN SCHEMA public
    GRANT USAGE, SELECT ON SEQUENCES TO numera_app;

-- Sanity: numera_app must NOT be able to bypass RLS.
-- SELECT rolname, rolbypassrls, rolsuper FROM pg_roles
--   WHERE rolname IN ('numera_app', 'numera_migrator');
-- Expect rolbypassrls = false and rolsuper = false for BOTH.
