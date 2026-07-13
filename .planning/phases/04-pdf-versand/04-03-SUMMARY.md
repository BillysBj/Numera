---
phase: 04-pdf-versand
plan: 03
subsystem: rendering-pipeline
tags: [hangfire, background-job, domain-event, questpdf, rls, tenant-context, pdf-download, logo-upload, idempotent]

# Dependency graph
requires:
  - phase: 04-pdf-versand
    plan: 01
    provides: "document_render PDF bytea store (RLS) + company_profile logo bytea/content-type columns"
  - phase: 04-pdf-versand
    plan: 02
    provides: "SnapshotReader.FromDocument + InvoiceDocument.Render(model) → byte[] (§14 layout, DE/EN)"
  - phase: 03-belegkette-rechnungskern
    provides: "FinalizeCoreAsync + InvoiceFinalized domain event (fired after commit) + InProcessDomainEventPublisher + Hangfire on the Api default queue"
provides:
  - "DocumentPdfService — shared render entrypoint: load finalized doc under RLS, read live logo, render from frozen snapshot, store/replace document_render idempotently (RenderAndStore + GetOrRender)"
  - "RenderDocumentPdfJob — Hangfire job (Api default queue) that re-establishes tenant context and renders+stores off the finalize hot-path"
  - "EnqueuePdfOnFinalize — IDomainEventHandler<InvoiceFinalized> that ONLY enqueues the render job (never renders inline)"
  - "GET /api/documents/{id}/pdf — stored/render-on-demand PDF download (404 not found, 409 draft)"
  - "PUT/GET /api/company-profile/logo — validated logo upload (PNG/JPG, <=1MB) + serve, RLS-scoped"
affects: [04-04 (send job attaches the stored render + reuses GetOrRender's readiness signal), 04-05 (frontend PDF download + logo upload UI)]

# Tech tracking
tech-stack:
  added: [QuestPDF 2026.7.1 (Api host; Community license set at startup)]
  patterns:
    - "Enqueue-after-commit via the existing InvoiceFinalized domain-event seam (register an IDomainEventHandler, no finalize edit)"
    - "Tenant re-establishment in a fresh DI scope inside the job (mirrors WelcomeEmailJob) so RLS applies to background work"
    - "Idempotent render store: delete-then-add per (document, language) so retries/re-renders never duplicate"
    - "Render legal content ONLY from the frozen snapshot; the tenant logo is the sole live read (presentation, not §14)"

key-files:
  created:
    - src/Numera.Api/Services/DocumentPdfService.cs
    - src/Numera.Api/Jobs/RenderDocumentPdfJob.cs
    - src/Numera.Api/Events/EnqueuePdfOnFinalize.cs
    - tests/Numera.IntegrationTests/DocumentRenderJobTests.cs
  modified:
    - src/Numera.Api/Numera.Api.csproj
    - src/Numera.Api/Program.cs
    - src/Numera.Api/Endpoints/SalesDocumentEndpoints.cs
    - src/Numera.Api/Endpoints/CompanyProfileEndpoints.cs

key-decisions:
  - "Render jobs run on the Api's default-queue Hangfire server (the Api has the Sales/QuestPDF refs; Numera.Worker does not — Pitfall 2, LOCKED)"
  - "The enqueue-on-finalize hook is registered as an IDomainEventHandler ONLY — the existing publisher already fires InvoiceFinalized after commit, so FinalizeCoreAsync was NOT touched"
  - "Logo upload attaches to an EXISTING company_profile (409 if none) — a profile requires a legal name + address a logo upload cannot supply; not a true upsert"
  - "GET /{id}/pdf renders-if-absent (idempotent recovery) so a missing async render is always downloadable; drafts 409, unknown 404"

patterns-established:
  - "Drive the REAL Hangfire job in-test via a minimal DI container (scoped ICurrentTenant + AddDbContext + service) instead of a live Hangfire server — deterministic and faithful to production tenant re-establishment"
  - "Prove enqueue-not-inline at the seam: a fake IBackgroundJobClient records exactly one Create and asserts NO document_render row is written by the handler"

# Metrics
duration: 49min
completed: 2026-07-13
---

# Phase 4 Plan 03: Async Render Pipeline Summary

**Finalizing an invoice enqueues a Hangfire job (Api default queue) that re-establishes tenant context, renders the §14 PDF from the frozen snapshot with the tenant's live logo, and stores the byte-identical blob in `document_render` — entirely off the finalize hot-path — while `GET /{id}/pdf` downloads it (render-if-absent) and `PUT/GET /company-profile/logo` manages the letterhead logo.**

## Performance

- **Duration:** ~49 min
- **Started:** 2026-07-13T20:01:09Z
- **Completed:** 2026-07-13T20:50:03Z
- **Tasks:** 3
- **Files:** 8 (4 created, 4 modified)

## Accomplishments
- `DocumentPdfService` is the single render entrypoint the endpoint and the job share: it loads the finalized `SalesDocument` under RLS (`AsNoTracking().Include(Lines).Include(TaxBreakdown)`), reads the tenant `CompanyProfile.LogoBytes`/`LogoContentType` live (the sole permitted live read), maps via `SnapshotReader.FromDocument` → `InvoiceDocument.Render` → `byte[]`, and stores it idempotently (delete-then-`db.Add` per document+language). `GetOrRender` returns the stored blob if present else renders-on-demand, distinguishing NotFound / NotFinalized / Ok.
- `RenderDocumentPdfJob` mirrors `WelcomeEmailJob` verbatim — a fresh DI scope + `ICurrentTenant.SetTenant` so the tenant interceptor pushes `app.current_tenant` and RLS applies to the read + store — with `[AutomaticRetry(Attempts = 3)]` (idempotent, so retries never duplicate). It runs on the Api's default-queue Hangfire server (Pitfall 2, LOCKED).
- `EnqueuePdfOnFinalize : IDomainEventHandler<InvoiceFinalized>` enqueues the job and returns immediately — never renders inline. Registered in DI, it hooks the EXISTING post-commit `InvoiceFinalized` dispatch with ZERO change to `FinalizeCoreAsync` (the "blockiert die Finalisierung nicht" guarantee).
- `GET /api/documents/{id}/pdf` downloads the stored/rendered PDF (`?lang=de|en`, default de) — 404 unknown, 409 draft, else `Results.File(bytes, "application/pdf", "{number}.pdf")`. `PUT /api/company-profile/logo` validates content-type (PNG/JPG) + size (<=1MB) and upserts the logo columns with an audit row; `GET .../logo` serves the bytes with their content-type (404 if none).
- QuestPDF 2026.7.1 added to the Api; `QuestPDF.Settings.License = Community` set once at startup (Pitfall 1).
- 3 integration tests on real postgres:18 (`numera_app`, NO BYPASSRLS): finalize-through-the-real-core → render-via-the-real-job stores exactly one §14 PDF (`%PDF`, >1KB); re-render replaces (idempotency); Kleinunternehmer §19 snapshot renders valid; and the finalize hook enqueues once and writes NO render row (enqueue-not-inline). Full integration suite **66/66 green** (63 prior + 3 new); solution build 0 warnings.

## Task Commits

1. **Task 1: QuestPDF license + DocumentPdfService + GET /{id}/pdf** — `f252d93` (feat)
2. **Task 2: Render job + enqueue-on-finalize handler + logo endpoints + DI** — `fe26d4f` (feat)
3. **Task 3: Integration test — finalize→render, idempotent, enqueue-not-inline** — `8b67dd5` (test)

## Decisions Made
- **Default-queue execution.** The render job runs on the Api's existing Hangfire server (default queue), not the `worker` queue whose host lacks the Sales/QuestPDF references (RESEARCH.md Pitfall 2, LOCKED). Still off the HTTP hot-path (background thread), so criterion 3 holds.
- **Handler-only hook.** The `InProcessDomainEventPublisher` already dispatches `InvoiceFinalized` after the finalize commit; registering `EnqueuePdfOnFinalize` in DI is the entire seam. `FinalizeCoreAsync` was not touched.
- **Logo requires an existing profile.** A `CompanyProfile` needs a legal name + address (required), which a logo upload cannot supply, so the logo attaches to an existing profile (409 if none) rather than a true upsert.
- **Render-if-absent download.** Because the document is immutable, a missing async render is always recoverable; `GET /{id}/pdf` renders on demand so a download never fails on a not-yet-rendered document.

## Deviations from Plan

### Auto-fixed / clarified

**1. [Rule 3 - Clarification] Logo "upsert" is an update-on-existing-profile, not a create**
- **Found during:** Task 2
- **Issue:** The plan said "upsert `CompanyProfile.LogoBytes`". A `CompanyProfile` has required `LegalName` + `Address`; a logo-only request cannot create a valid profile.
- **Resolution:** `PUT /logo` updates the logo columns on the existing profile and returns 409 when no profile exists yet (the §14 settings must be created first). No new behaviour beyond the plan's intent; the logo still round-trips PUT→GET.
- **Files:** src/Numera.Api/Endpoints/CompanyProfileEndpoints.cs
- **Committed in:** `fe26d4f`

**2. [Rule 2 - Correctness] DisableAntiforgery on the multipart logo PUT**
- **Found during:** Task 2
- **Issue:** Minimal-API `IFormFile` binding triggers the framework's implicit antiforgery check; the BFF host does not register antiforgery services, so the endpoint would throw at runtime.
- **Fix:** `.DisableAntiforgery()` on the logo PUT — the BFF's same-origin HttpOnly-cookie model is the CSRF story (consistent with the rest of the API).
- **Files:** src/Numera.Api/Endpoints/CompanyProfileEndpoints.cs
- **Committed in:** `fe26d4f`

**3. [Improvement] DocumentPdfService DI registered in Task 1, not Task 2**
- The plan grouped all DI into Task 2; `DocumentPdfService` (the service the Task-1 endpoint depends on) was registered in Task 1 so each commit is self-consistent. Job + handler DI stayed in Task 2. No functional difference.

**Total deviations:** 2 auto-applied + 1 sequencing improvement. No architectural change; no scope creep.

## Issues Encountered
- The machine's default `dotnet` shim resolves no compatible SDK; used the user-local SDK 10 (`C:\Users\Admin\.dotnet10`) with `DOTNET_ROOT` + `DOTNET_MULTILEVEL_LOOKUP=0` per STATE.md.

## User Setup Required
None — QuestPDF Community license is set in-code; no account/key needed under the $1M threshold.

## Next Phase Readiness
- **04-04 (email):** the send job can attach the stored render (reuse `DocumentPdfService.GetOrRender` — its render-if-absent path is the readiness signal per RESEARCH.md Pitfall 5) and write `document_email` + flip `SalesDocument.SentAt`.
- **04-05 (frontend):** wire a download button to `GET /{id}/pdf` and a logo upload field to `PUT /company-profile/logo` (multipart); the human-verify checkpoint can inspect the rendered PDF on the tenant letterhead.
- **Deploy note (RESEARCH.md Pitfall 3):** the render runs on the Api; a slim Linux deploy image will need `libfontconfig1` + a font for SkiaSharp. Dev on Windows is unaffected. Flagged, not blocking.

## Self-Check: PASSED

All 4 created source/test files exist on disk; all 3 task commits (f252d93, fe26d4f, 8b67dd5) are present in git history.
