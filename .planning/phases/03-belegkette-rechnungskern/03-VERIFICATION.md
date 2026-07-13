---
phase: 03-belegkette-rechnungskern
verified: 2026-07-13T12:19:45Z
status: passed
score: 5/5 must-haves verified
re_verification:
  previous_status: gaps_found
  previous_score: 1/5
  gaps_closed:
    - "Entwuerfe sind frei bearbeitbar; finalisierte Belege sind unveraenderbar - Korrekturen erzeugen Storno-/Korrekturbelege (GoBD, DB-seitig erzwungen)"
    - "Rechnungen enthalten alle Pflichtangaben nach Paragraph 14 UStG; Rechnungsnummern werden bei Finalisierung race-sicher, eindeutig und im konfigurierten Format vergeben"
    - "Nutzer kann finalisierte Rechnungen stornieren und Gutschriften erstellen; jede finalisierte Rechnung erzeugt einen offenen Posten mit Faelligkeit in der OP-Uebersicht"
  gaps_remaining: []
  regressions: []
human_verification:
  - test: "Finalize a real draft end-to-end through the running app (POST /api/documents/{id}/finalize or the UI Finalize button)."
    expected: "Finalize succeeds without a server error; the invoice becomes read-only in the UI; an RE-YYYY-##### number, a persisted BG-23 breakdown and an open item in the OP-Uebersicht are visible; Storno and Gutschrift then work end-to-end from the detail page action bar."
    why_human: "Requires the live API and UI stack (auth/session, real HTTP round-trip), not just the integration-test invocation of the internal core used for this verification."
  - test: "Open a finalized invoice detail page in the browser and visually confirm the issuer and recipient cards render the frozen legal name, address, VAT ID or tax number instead of the empty-state hint."
    expected: "Issuer and recipient cards show real, frozen data matching what was captured at finalize time, not live partner or company-profile data."
    why_human: "Visual confirmation of card rendering and defensive PascalCase/camelCase snapshot parsing is best done in a browser."
---

# Phase 3: Belegkette und Rechnungskern Verification Report

**Phase Goal:** Nutzer kann rechtskonforme, unveraenderbare Rechnungen mit korrekter USt-Behandlung erzeugen - das Herz von v1, inklusive der zweiten "jetzt oder nie"-Naht (Unveraenderbarkeit + Nummernvergabe).
**Verified:** 2026-07-13T12:19:45Z
**Status:** passed
**Re-verification:** Yes - after gap-closure plan 03-11

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | Nutzer kann Angebote erstellen und ueber AB/Lieferschein in Rechnungen ueberfuehren (Belegkette mit Statusverfolgung) | VERIFIED | Draft-level conversion (POST /{id}/convert, SalesDocumentEndpoints.cs 229-303) was already correct. The previously-blocking terminal step - finalizing the resulting Rechnung - now succeeds: FinalizeCoreAsync persists the breakdown via db.Add and no longer throws, so a document genuinely reaches Finalized status. The full chain (Angebot to AB to Lieferschein to finalized Rechnung) is now observable end-to-end, exercised by the integration suite. |
| 2 | Entwuerfe frei bearbeitbar; finalisierte Belege unveraenderbar (DB-erzwungen); Korrekturen erzeugen Storno/Korrekturbelege | VERIFIED | Draft PUT/DELETE 409-gate unchanged (lines 702-706). FinalizeCoreAsync (SalesDocumentEndpoints.cs ~722-791) now persists the BG-23 breakdown via explicit db.Add(new SalesDocumentTaxBreakdown{ TenantId, DocumentId = doc.Id, ... }) instead of the navigation-collection doc.TaxBreakdown.Add(...) that previously crashed with DbUpdateConcurrencyException. Independently re-ran dotnet test tests/Numera.IntegrationTests against real Postgres (Testcontainers postgres:18): 59/59 green, including Finalized_invoice_business_column_update_is_rejected_by_the_db and Finalized_invoice_delete_is_rejected_by_the_db (SalesFinalizeTests.cs 174-195), which now actually exercise the DB immutability triggers because documents can finally reach Finalized status. |
| 3 | Rechnungen enthalten alle Paragraph-14-Pflichtangaben; Rechnungsnummern race-sicher, eindeutig, im konfigurierten Format bei Finalisierung vergeben | VERIFIED | Finalize_creates_open_item_breakdown_snapshots_and_a_formatted_number (SalesFinalizeTests.cs 32-90) asserts doc.DocumentNumber == "RE-2026-00001", a persisted 2-row BG-23 breakdown, and non-null Issuer/RecipientSnapshot containing "Aussteller GmbH" / "Empfaenger AG" - all against the real FinalizeCoreAsync. SalesNumberingConcurrencyTests (parallel finalizations) still pass, now against the real core. On the read side, SalesDocumentDetail (SalesDocumentContracts.cs 98-99) now carries IssuerSnapshot/RecipientSnapshot, and ToDetail (SalesDocumentEndpoints.cs 651-657) projects d.IssuerSnapshot, d.RecipientSnapshot in the exact positional slot the record expects - confirmed by direct code read; build is 0-warning so positional arity is proven. Frontend DocumentDetailPage.tsx (already built, reads d.issuerSnapshot/d.recipientSnapshot via parseSnapshot) requires zero changes. |
| 4 | USt als EN-16931-Kategorie modelliert, deckt 19/7/0%, Paragraph 19, Paragraph 13b, innergemeinschaftliche Lieferung mit Pflichttexten ab | VERIFIED | VatCalculationService and Pflichttext (unit-tested in 03-03) are now reachable end-to-end: Finalize_per_category_rounding_sums_rounded_rows_not_the_grand_total and Kleinunternehmer_finalize_produces_a_zero_vat_exempt_breakdown_with_the_para19_note (SalesFinalizeTests.cs 94-246) confirm real per-category rounding and the Paragraph-19 Kleinunternehmer exemption text are actually persisted through the real finalize core, not just computed in isolation. |
| 5 | Nutzer kann finalisierte Rechnungen stornieren und Gutschriften erstellen; jede finalisierte Rechnung erzeugt einen offenen Posten mit Faelligkeit in der OP-Uebersicht | VERIFIED | Storno and Gutschrift-finalize both reuse the now-fixed FinalizeCoreAsync (fixed for free). Finalize_creates_open_item_breakdown_snapshots_and_a_formatted_number asserts a persisted open item (DueDate, OriginalAmount == OpenAmount == TotalGross, Status == Open, DocumentNumber). SalesStornoTests.cs is part of the 59/59 green run. GET /api/open-items and OpenItemsListPage.tsx (unaffected by the gap, already verified) will now show real data end-to-end. |

**Score:** 5/5 truths fully verified

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| src/Numera.Api/Endpoints/SalesDocumentEndpoints.cs (FinalizeCoreAsync) | Finalize transaction: snapshots, VAT breakdown, numbering, open item, status flip | VERIFIED | Line 746: db.Add(new SalesDocumentTaxBreakdown{...}) with DocumentId = doc.Id explicitly set. No doc.TaxBreakdown.Add( remains anywhere in the file (grepped, confirmed absent). Method signature changed private static -> internal static (line 722) to allow test invocation. |
| src/Numera.Api/Contracts/SalesDocumentContracts.cs (SalesDocumentDetail) | Paragraph-14-complete detail projection | VERIFIED | Lines 98-99: string? IssuerSnapshot, / string? RecipientSnapshot, added in positional order between CancelledByDocumentId and Lines. |
| src/Numera.Api/Endpoints/SalesDocumentEndpoints.cs (ToDetail) | Projects the frozen snapshots into the DTO | VERIFIED | Line 657: d.IssuerSnapshot, d.RecipientSnapshot, placed in the matching positional slot. Build is 0-warning, confirming positional arity is correct. |
| src/Numera.Api/Numera.Api.csproj | InternalsVisibleTo grants the test assembly access to the internal finalize core | VERIFIED | Lines 9-14: InternalsVisibleTo Include="Numera.IntegrationTests". |
| tests/Numera.IntegrationTests/Numera.IntegrationTests.csproj | ProjectReference to Numera.Api so tests bind to the real host finalize code | VERIFIED | Line 74: ProjectReference Include="..\..\src\Numera.Api\Numera.Api.csproj". |
| tests/Numera.IntegrationTests/SalesTestData.cs | Finalize harness delegates to the real production core; no parallel reimplementation | VERIFIED | ApplyFinalizeAsync (lines 332-343) now calls Numera.Api.Endpoints.SalesDocumentEndpoints.FinalizeCoreAsync(doc, profile, partner, db, numbering, audit, doc.TenantId, "FinalizeTest", ct) via a NoOpAuditWriter : IAuditWriter. Grepped the whole file: no db.Add(new SalesDocumentTaxBreakdown and no doc.TaxBreakdown.Add(new SalesDocumentTaxBreakdown reimplementation remains - only a benign doc-comment mention of the pattern name (line 32) describing the regression it now guards against. Class-level docstring (lines 16-38) rewritten to state the harness delegates to the real core; the old "faithful mirror ... lives as a private method, not referenced by this test project" claim is gone. |
| src/platform/Numera.Platform.Db/Migrations/20260712151258_SalesDocuments.cs | DB-enforced GoBD immutability triggers | VERIFIED, now reachable | Unchanged from initial verification (already correct); now actually exercised because documents reach Finalized status - confirmed by the passing Finalized_invoice_*_is_rejected_by_the_db tests. |
| web/src/features/documents/DocumentDetailPage.tsx | Paragraph-14 detail view plus lifecycle actions | VERIFIED (code-complete; visual confirmation still human) | Reads d.issuerSnapshot/d.recipientSnapshot via parseSnapshot/readSnapshot (lines 10, 119, 252-253); backend now populates these fields so the cards will render real data. No frontend change was required or made. |
| web/src/features/openItems/OpenItemsListPage.tsx | OP-Uebersicht | VERIFIED | Unaffected by the gaps; now backed by real data since open items are durably created. |

### Key Link Verification

| From | To | Via | Status | Details |
|------|-----|-----|--------|---------|
| POST /{id}/finalize | SalesDocumentTaxBreakdown persistence | explicit db.Add + SaveChangesAsync | WIRED | Confirmed by direct code read and by 59/59 green integration tests independently re-run against real Postgres, including tests that assert the breakdown rows and their values. |
| POST /{id}/storno, POST /{id}/credit-note/finalize | FinalizeCoreAsync | shared code path | WIRED | Both reuse the fixed core; SalesStornoTests.cs is part of the green 59/59 run. |
| DocumentDetailPage.tsx | GET /api/documents/{id} issuer/recipient snapshot | d.issuerSnapshot, d.recipientSnapshot | WIRED | Backend DTO now populates both fields (ToDetail line 657); frontend was already correct and reads them defensively. |
| FinalizeCoreAsync | NumberingService.AssignAsync | ambient transaction | WIRED | Confirmed by Finalize_creates_open_item_breakdown_snapshots_and_a_formatted_number asserting doc.DocumentNumber == "RE-2026-00001" and by the concurrency suite. |
| FinalizeCoreAsync | BuildOpenItem persistence | db.Add + same SaveChangesAsync | WIRED | Confirmed by the same test asserting open-item fields (DueDate, amounts, status, document number). |
| tests/Numera.IntegrationTests/SalesTestData.cs (ApplyFinalizeAsync) | src/Numera.Api/Endpoints/SalesDocumentEndpoints.cs (FinalizeCoreAsync) | direct invocation of the internal production method via InternalsVisibleTo + ProjectReference | WIRED | Confirmed by reading the delegation call and by the SUMMARY documented regression guard (reverting to the navigation-add pattern made 6/8 SalesFinalizeTests fail with DbUpdateConcurrencyException thrown from SalesDocumentEndpoints.FinalizeCoreAsync; restoring db.Add produced 59/59 green again). Not independently re-executed by this verifier (would require a temporary source mutation); accepted on the strength of (a) the delegation being directly visible in the current source, (b) the current 59/59 green run independently reproduced against the delegating code, and (c) the documented crash signature exactly matching the original empirically-reproduced GAP-1 defect (03-08-SUMMARY.md). |

### Requirements Coverage

| Requirement | Status | Blocking Issue |
|-------------|--------|-----------------|
| DOCS-01 (Belegkette Angebot to AB to Lieferschein to Rechnung) | SATISFIED | Draft-level conversion plus finalize now both work end-to-end. |
| DOCS-04 (Entwuerfe mutable, finalisierte Belege unveraenderbar via Storno) | SATISFIED | Finalize succeeds; DB immutability triggers are exercised and pass. |
| INV-01 (Paragraph-14 Pflichtangaben) | SATISFIED | Finalize succeeds; detail DTO now exposes IssuerSnapshot/RecipientSnapshot. |
| INV-02 (race-sichere Nummernvergabe) | SATISFIED | NumberingService durably commits within the now-succeeding finalize transaction; concurrency suite green. |
| INV-03 (Storno plus Gutschrift) | SATISFIED | Both reuse the fixed FinalizeCoreAsync; SalesStornoTests green. |
| INV-04 (USt-Kategorien plus Pflichttexte) | SATISFIED | VatCalculationService output now durably persisted end-to-end, confirmed by rounding and Kleinunternehmer tests. |
| OPDN-01 (offener Posten pro finalisierter Rechnung) | SATISFIED | Open item creation is inside the now-succeeding finalize transaction; asserted by the finalize test. |

Note: .planning/REQUIREMENTS.md itself still shows these as "Pending" checkboxes - that tracking file is maintained by the roadmap/orchestrator workflow, not by this verifier; the assessment above reflects the actual codebase state.

### Anti-Patterns Found

None found in the gap-closure files (SalesDocumentEndpoints.cs, SalesDocumentContracts.cs, SalesTestData.cs, Numera.Api.csproj, Numera.IntegrationTests.csproj) - no TODO/FIXME/XXX/HACK/PLACEHOLDER markers, no empty-implementation stubs, no orphaned dead code from the removed parallel reimplementation (SUMMARY documents the orphaned Json field and BuildOpenItem helper were removed to keep the 0-warning build).

### Human Verification Required

1. Finalize a real draft end-to-end through the running app (POST /api/documents/{id}/finalize or the UI Finalize button). Expected: success response with an RE-YYYY-##### number, persisted BG-23 breakdown, and an open item in the OP-Uebersicht; invoice becomes read-only; Storno and Gutschrift then work from the detail page action bar. Why human: needs the live API/UI stack (auth, session, real HTTP round-trip), not just the integration-test invocation of the internal core used here.
2. Visually confirm the Paragraph-14 cards render real data - open a finalized invoice detail page and confirm the issuer and recipient cards show the frozen legal name/address/VAT-ID or tax-number instead of the empty-state hint. Why human: visual/browser confirmation of card rendering and snapshot parsing.

These two items were already flagged as non-blocking in the initial 03-VERIFICATION.md and remain non-blocking here: all automated evidence (build, 59/59 integration tests against real Postgres exercising the real production FinalizeCoreAsync, direct code inspection of all three gap fixes) confirms the underlying functionality is now correct; only the live-UI/browser rendering confirmation requires a human and the running stack.

### Gaps Summary

All three gaps from the initial verification (score 1/5) are closed, independently confirmed against the actual codebase:

1. GAP 1 (finalize crash) - FinalizeCoreAsync (SalesDocumentEndpoints.cs line 746) now persists the BG-23 breakdown via explicit db.Add(new SalesDocumentTaxBreakdown{ DocumentId = doc.Id, ... }) instead of the navigation-collection doc.TaxBreakdown.Add(...) that previously caused DbUpdateConcurrencyException on every finalize/Storno/Gutschrift attempt. Confirmed absent anywhere in the codebase via grep.

2. GAP 2 (missing Paragraph-14 snapshot DTO fields) - SalesDocumentDetail (SalesDocumentContracts.cs lines 98-99) now carries IssuerSnapshot/RecipientSnapshot; ToDetail (SalesDocumentEndpoints.cs line 657) projects them in the correct positional slot. Frontend requires zero changes (already built defensively).

3. GAP 3 (tests guarded a parallel reimplementation, not production code) - SalesTestData.ApplyFinalizeAsync (lines 332-343) now delegates directly to the real, internal, production SalesDocumentEndpoints.FinalizeCoreAsync via InternalsVisibleTo + a Numera.Api ProjectReference. The old parallel reimplementation and its misleading "faithful mirror ... not referenced by this test project" docstring are gone.

Independent confirmation performed by this verifier beyond reading the SUMMARY: dotnet build (0 warnings, .NET 10.0.301 SDK) and dotnet test tests/Numera.IntegrationTests/Numera.IntegrationTests.csproj re-run from scratch against a freshly spun-up real Postgres 18 Testcontainer (Docker Desktop confirmed running) - 59/59 tests passed, including the specific assertions for breakdown persistence, number assignment, open-item creation, DB-enforced immutability (both a rejected business-column UPDATE and a rejected DELETE), and the Kleinunternehmer Paragraph-19 Pflichttext. The regression-guard revert/restore cycle itself (reverting to the navigation-add pattern to observe 6/8 failures, then restoring) was not independently re-executed by this verifier - it would require a temporary source mutation - but is accepted based on (a) the delegation to the real core being directly visible in the current source, (b) the fresh 59/59 green run independently reproduced against that exact delegating code, and (c) the documented failure signature matching the original, separately and empirically reproduced GAP-1 defect from 03-08-SUMMARY.md.

The phase goal - "Nutzer kann rechtskonforme, unveraenderbare Rechnungen mit korrekter USt-Behandlung erzeugen" - is now achieved and regression-guarded by tests that exercise the real shipped code path. The two remaining human-verification items (live-UI finalize, visual Paragraph-14 card rendering) are cosmetic/confirmatory, not functional blockers.

---

Verified: 2026-07-13T12:19:45Z
Verifier: Claude (gsd-verifier)
