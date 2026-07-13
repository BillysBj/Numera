---
phase: 04-pdf-versand
plan: 01
subsystem: persistence
tags: [rls, migration, ef-core, bytea, document-render, document-email, logo, tenant-isolation]

# Dependency graph
requires:
  - phase: 03-belegkette-rechnungskern
    provides: "Finalized SalesDocument (SentAt whitelisted lifecycle column) + company_profile issuer master data + the hand-written per-table RLS migration convention"
provides:
  - "document_render — per-tenant PDF blob store (bytea) keyed by document, immutable-alongside-the-document, re-render allowed"
  - "document_email — per-tenant email send-status + retry ledger (EmailStatus Queued→Sent/Failed, attempts, last error, sent_at)"
  - "company_profile logo columns (logo bytea + content-type) for own-letterhead rendering"
  - "EmailStatus enum (SalesEnums.cs, root Numera.Modules.Sales namespace)"
affects: [04-03 (render job writes document_render + reads logo), 04-04 (send job writes document_email + flips SalesDocument.SentAt)]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Hand-written RLS policy per new table via migrationBuilder.Sql (ENABLE + FORCE + tenant_isolation) — reflective ITenantEntity discovery never emits policies (the standard Numera silent-leak trap)"
    - "PDF/logo bytes as Postgres bytea (no object storage in v1)"
    - "Delivery tables are tenant-scoped ITenantEntity with client-set UUIDv7 PKs"

key-files:
  created:
    - src/modules/Numera.Modules.Sales/Rendering/DocumentRender.cs
    - src/modules/Numera.Modules.Sales/Email/DocumentEmail.cs
    - src/platform/Numera.Platform.Db/Migrations/20260713130849_DocumentDelivery.cs
    - tests/Numera.IntegrationTests/DocumentDeliveryRlsTests.cs
  modified:
    - src/modules/Numera.Modules.Sales/CompanyProfile.cs
    - src/modules/Numera.Modules.Sales/SalesEnums.cs
    - src/platform/Numera.Platform.Db/Migrations/NumeraDbContextModelSnapshot.cs
---

# 04-01 Summary — Delivery persistence schema + RLS

## Outcome
The durable storage tier for Phase 4 exists and is tenant-isolated on real Postgres:
- **`document_render`** — the generated-PDF blob store (`bytea` + byte size + rendered-at + language), one row per rendered document, written by the async render job (04-03). Stored-immutable alongside the document; explicit re-render allowed.
- **`document_email`** — the email dispatch ledger: recipient, subject, `EmailStatus` (Queued → Sent / Failed), attempt count, last error, and sent-at. Written by the send job (04-04), which also flips the already-whitelisted `SalesDocument.SentAt` on success.
- **`company_profile`** gained `logo` (`bytea`) + logo content-type columns for own-letterhead rendering.

Both new tables carry hand-written per-table RLS (ENABLE + FORCE ROW LEVEL SECURITY + `tenant_isolation` policy) via `migrationBuilder.Sql` — the reflective `ITenantEntity` discovery never emits policies (the standard Numera trap), so policies are always hand-authored. One migration (`20260713130849_DocumentDelivery`) covers all three changes; it chains cleanly onto the Phase-3 snapshot as the sole migration of this wave.

## Tasks
1. **Entities + logo columns** — `DocumentRender`, `DocumentEmail` (+ `EmailStatus` enum), `company_profile` logo bytea/content-type. Commit `28a2f6a`.
2. **Migration + hand-written RLS** — `document_render` + `document_email` tables, `tenant_isolation` policies, logo `ALTER`. Commit `4cb5c02`.
3. **RLS isolation integration tests** — cross-tenant read-returns-zero + cross-tenant-insert-rejected for both tables on postgres:18 as `numera_app` (NO BYPASSRLS). Commit `d7071ce`.

## Verification
- `dotnet build Numera.sln` — 0 warnings / 0 errors.
- `DocumentDeliveryRlsTests` — **4/4 green** on real postgres:18 (Testcontainers, `numera_app`).
- Migration grep: `FORCE ROW LEVEL SECURITY` + `CREATE POLICY tenant_isolation` present for both tables; snapshot carries `document_render`, `document_email`, and the logo columns.

## Deviations
- **Orchestrator-applied finalization (session-limit recovery):** the executor was interrupted after committing Tasks 1–2 and writing (but not committing) the Task-3 test. The test referenced `EmailStatus` without importing the root `Numera.Modules.Sales` namespace (it lives in `SalesEnums.cs`, not the `.Email` sub-namespace), causing one build error `CS0103`. Fixed by adding `using Numera.Modules.Sales;` to the test file; then Task 3 was committed and the suite verified 4/4 green. No production-code change was needed.

## Notes for later waves
- `DocumentRender` bytea is the artifact `04-03` writes; `DocumentEmail` + `SentAt` are what `04-04` updates. Logo bytes are the sole *live* read allowed during render (presentation, not §14 legal content — RESEARCH.md Pattern 3 exception).
