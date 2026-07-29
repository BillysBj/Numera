# Phase 9 Context — v1-Feinschliff & Compliance (FINAL phase, closes v1)

Decisions locked before planning (engineering by Claude-as-architect, 2 pricing by the user). No `/gsd:discuss-phase` run; these resolve D1–D6 from `09-RESEARCH.md`.

## Decisions (LOCKED — plans must honor exactly)

1. **DSGVO export (PLAT-08)** — a SYNCHRONOUS streamed `ZipArchive` produced by iterating every RLS-scoped table for the current tenant: per-entity JSON (NO CSV in v1) + the real binary blobs (rendered PDFs, e-invoice XML, inbound originals, customer files, logo). New `/api/export` (GET, streams `application/zip`), **Owner-only** (`RequireOwner`), gated on `Capability.DataExport` (S+, all tiers already have it), audited (`data.exported`), NON-mutating, reusing the proven `SetTenant` tenant-scoping. The 08-01 TaxAdvisor read allow-list already default-DENIES `/api/export` — keep it denied (do NOT add it to the allow-list). Enumerate the tables/blobs from `09-RESEARCH.md`. No Hangfire job (sync is fine for v1). No new migration.

2. **PWA install + offline** — pragmatic v1: reliable INSTALL (valid manifest + icons + service worker — the vite-plugin-pwa config already precaches the app-shell and is `NetworkOnly` for `/api` with a navigate-fallback denylist) + offline app-shell + cached reads/offline-viewable drafts. **Finalisierung/Versand/E-Rechnung STAY ONLINE-BOUND** (they already require the server). NO IndexedDB, NO offline mutation/sync queue (out of scope). Use the existing `common.offline.banner` offline signal. Verify install + offline-shell behavior.

3. **Tarif-gate enforcement + UX (criterion 3)** — make gating "durchgängig … Upgrade-Hinweise statt Fehler":
   - **USER DECISION: enforce EInvoicing at L+.** EInvoicing is declared L+ in `PlanCapabilityMap` but not enforced today. Add a server-authoritative `Capability.EInvoicing` gate to the USER-FACING e-invoice surface — generate/download XRechnung + ZUGFeRD + send-einvoice endpoints return 403 upgrade for non-L tenants; the finalize→e-invoice AUTO-ENQUEUE (EnqueueEInvoiceOnFinalize) must NOT generate e-invoices for a tenant lacking EInvoicing. Core finalize + PDF + email stay available to ALL tiers (only the e-invoice artifacts are L+). Frontend shows the UpgradeHint on the e-invoice actions. (Risk acknowledged: German E-Rechnungspflicht — the user chose to gate it anyway.)
   - **USER DECISION: enforce Dunning at L+.** Add a `Capability.Dunning` server gate to `/api/dunning/config` + `/api/dunning/run` (403 upgrade for non-L); frontend UpgradeHint on the dunning/Mahnlauf UI. Open-item dunning columns stay.
   - Fix the latent `web/src/pages/Dashboard.tsx` `.features` vs API `.capabilities` field mismatch. Add a current-plan/tier indicator so the user knows their tier. Parameterize `UpgradeHint` by the required tier (e.g. "ab Tarif L") instead of a hardcoded string, and apply it consistently across ALL gated features (FX/recurring/down-payment/team already have it; add e-invoice + dunning).

4. **GoBD conformance pass (criterion 4)** — mostly a docs + audit artefact:
   - Author a Verfahrensdokumentation DRAFT at `docs/gobd-verfahrensdokumentation.md` (4-part, per `09-RESEARCH.md`): invoice lifecycle + immutability, race-safe numbering, append-only audit trail, e-invoice/GoBD-2025 (BMF 2025-07-14), backups/retention.
   - WORDING audit: "GoBD-konform" phrasing is OK; NO "zertifiziert"/"certified" claim anywhere (codebase already verified clean — keep it clean; add a check).
   - Cross-tenant security suite GREEN + COMPLETE: add ONE consolidating RLS-isolation test proving NOTHING leaks across tenants for the newer phase-6→8 tables (payment, payment_allocation, dunning_level_config, dunning_notice, recurring_invoice_templates+lines, sales_document_prepayment, partner_tasks, customer_files, document_einvoice, inbound_document), using the canonical `IgnoreQueryFilters()` RLS pattern (a second tenant sees 0 rows). This is the auditable "nachweislich rechtskonform" proof.

## Claude's Discretion (freedom areas)
- Exact export archive layout, endpoint/DTO shapes, plan/wave split, doc outline details, which files to touch for the gate UX.
- Suggested slicing (research): (1) DSGVO export, (2) tarif-gate enforcement + UX, (3) PWA polish, (4) GoBD docs + cross-tenant test. Parallelize where disjoint; a final human-verify checkpoint for install/offline/export/gates is appropriate (autonomous:false).

## Deferred Ideas (OUT of scope for Phase 9)
- Async/Hangfire export job; CSV export; selective/partial export.
- Offline mutation queue / IndexedDB / background sync.
- Actual GoBD certification (only a self-assessment draft + honest "GoBD-konform" wording, never "zertifiziert").
- Moving EInvoicing/Dunning to lower tiers (user chose to enforce L+).
