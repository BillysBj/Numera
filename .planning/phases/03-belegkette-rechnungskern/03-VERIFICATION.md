---
phase: 03-belegkette-rechnungskern
verified: 2026-07-13T07:54:27Z
status: gaps_found
score: 1/5 must-haves verified
gaps:
  - truth: "Entwuerfe sind frei bearbeitbar; finalisierte Belege sind unveraenderbar - Korrekturen erzeugen Storno-/Korrekturbelege (GoBD, DB-seitig erzwungen)"
    status: failed
    reason: "The single shared FinalizeCoreAsync (used by /finalize, /storno and the Gutschrift finalize) throws DbUpdateConcurrencyException at runtime the instant it persists the BG-23 tax-breakdown rows, so no document can ever actually reach Finalized status through the running application. Empirically reproduced against real Postgres (Testcontainers) using the exact production load pattern (tracked existing SalesDocument loaded via Include(Lines), then doc.TaxBreakdown.Add(...) then SaveChangesAsync) - confirmed DbUpdateConcurrencyException, 0 rows affected. The DB-side immutability triggers themselves (sales_document_immutable / sales_document_child_immutable, migration 20260712151258_SalesDocuments.cs) are correctly written, but are unreachable in practice because no document ever leaves Draft."
    artifacts:
      - path: "src/Numera.Api/Endpoints/SalesDocumentEndpoints.cs"
        issue: "Line 745: doc.TaxBreakdown.Add(new SalesDocumentTaxBreakdown row) adds a new child with a client-set UUIDv7 PK (ValueGeneratedOnAdd, Guid.CreateVersion7()) via a navigation-collection fixup on an already-tracked existing parent (doc loaded via db.Set of SalesDocument .Include(Lines).FirstOrDefaultAsync). EF Core relationship-fixup heuristic marks the child Modified instead of Added, emits an UPDATE matching 0 rows, and SaveChangesAsync throws. Every other Add site in the same file (lines 141, 299, 486, 611, 775) uses explicit db.Add, which is the only reliable way to force an Added state for a client-set-key entity added via navigation on a tracked parent."
    missing:
      - "One-line fix in FinalizeCoreAsync: replace the navigation .Add with db.Add(new SalesDocumentTaxBreakdown row with DocumentId set), matching the pattern already used for the open item (line 775) and already encoded correctly in tests/Numera.IntegrationTests/SalesTestData.cs line 352."
  - truth: "Rechnungen enthalten alle Pflichtangaben nach Paragraph 14 UStG; Rechnungsnummern werden bei Finalisierung race-sicher, eindeutig und im konfigurierten Format vergeben"
    status: failed
    reason: "Blocked by the same finalize crash: NumberingService.AssignAsync (proven race-safe and atomic in isolation via the real production class under SalesNumberingConcurrencyTests) runs inside the same ambient transaction as the breakdown persist; when the breakdown SaveChanges throws, the whole finalize transaction rolls back, so no number is ever durably assigned by the running application. Additionally, on the read side, the frozen Paragraph-14 issuer and recipient snapshots are never exposed by the API: SalesDocumentDetail (src/Numera.Api/Contracts/SalesDocumentContracts.cs lines 76-99) and SalesDocumentEndpoints.ToDetail (line 651) omit IssuerSnapshot and RecipientSnapshot entirely, even though the frontend DocumentDetailPage (web/src/features/documents/DocumentDetailPage.tsx lines 252-253) already reads d.issuerSnapshot and d.recipientSnapshot and is written defensively to handle their absence - meaning the issuer and recipient cards always render empty (a dash or the pre-finalize hint), never the real data, for every document that would otherwise finalize successfully."
    artifacts:
      - path: "src/Numera.Api/Contracts/SalesDocumentContracts.cs"
        issue: "SalesDocumentDetail record (lines 76-99) has no IssuerSnapshot or RecipientSnapshot fields"
      - path: "src/Numera.Api/Endpoints/SalesDocumentEndpoints.cs"
        issue: "ToDetail (line 651) does not project d.IssuerSnapshot or d.RecipientSnapshot into the DTO"
    missing:
      - "Fix the finalize crash (see gap 1) so a document number can actually be assigned"
      - "Add IssuerSnapshot and RecipientSnapshot (raw jsonb string) to SalesDocumentDetail and populate them in ToDetail so the already-built frontend Paragraph-14 cards render real data"
  - truth: "Nutzer kann finalisierte Rechnungen stornieren und Gutschriften erstellen; jede finalisierte Rechnung erzeugt einen offenen Posten mit Faelligkeit in der OP-Uebersicht"
    status: failed
    reason: "Storno (POST id storno) and the Gutschrift finalize both reuse the identical FinalizeCoreAsync and therefore hit the identical DbUpdateConcurrencyException. A Rechnung can never be finalized in the first place (gap 1), so there is nothing to Storno; even a hand-seeded finalized Rechnung would crash the same way when its Storno call finalizes the Storno mirror. No open item is ever created by the running app because BuildOpenItem is added in the same SaveChangesAsync call that throws - the whole transaction, including the number claim, rolls back."
    artifacts:
      - path: "src/Numera.Api/Endpoints/SalesDocumentEndpoints.cs"
        issue: "FinalizeCoreAsync (lines 721-790), shared by finalize, storno and the Gutschrift finalize, is the single point of failure"
    missing:
      - "Same one-line fix as gap 1 (FinalizeCoreAsync uses db.Add for the breakdown row); no Storno, Gutschrift or open-item-specific code changes are needed once the shared core is fixed"
human_verification:
  - test: "After applying the db.Add fix to FinalizeCoreAsync, manually finalize a draft Rechnung through the running app (POST api documents id finalize or the UI Finalize button) and confirm a success response with an assigned RE-YYYY-##### number, a persisted BG-23 breakdown, and an open item appearing in the OP-Uebersicht."
    expected: "Finalize succeeds without a server error; the invoice becomes read-only in the UI; Storno and Gutschrift then work end-to-end from the detail page action bar."
    why_human: "Requires running the live API and UI stack (not just the isolated EF Core repro used for this verification) to confirm no other issue surfaces once the known blocker is removed."
  - test: "After adding IssuerSnapshot and RecipientSnapshot to SalesDocumentDetail and ToDetail, open a finalized invoice detail page and visually confirm the issuer and recipient cards render the frozen legal name, address, VAT ID or tax number instead of the empty-state hint."
    expected: "Issuer and recipient cards show real, frozen data matching what was captured at finalize time, not live partner or company-profile data."
    why_human: "Visual confirmation of card rendering and defensive PascalCase and camelCase snapshot parsing is best done in a browser."
---

# Phase 3: Belegkette und Rechnungskern Verification Report

**Phase Goal:** Nutzer kann rechtskonforme, unveraenderbare Rechnungen mit korrekter USt-Behandlung erzeugen - das Herz von v1, inklusive der zweiten "jetzt oder nie"-Naht (Unveraenderbarkeit + Nummernvergabe).
**Verified:** 2026-07-13T07:54:27Z
**Status:** gaps_found
**Re-verification:** No - initial verification

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | Nutzer kann Angebote erstellen und ueber AB/Lieferschein in Rechnungen ueberfuehren (Belegkette mit Statusverfolgung) | VERIFIED, draft mechanics only | POST id convert (SalesDocumentEndpoints.cs lines 229-303) correctly copies header and lines forward, sets SourceDocumentId, any source status converts freely - the draft-level chain works. Caveat: the chain terminal step (finalizing the resulting Rechnung) is blocked by gap 1, so full status tracking through to Finalized is not actually observable end-to-end. |
| 2 | Entwuerfe frei bearbeitbar; finalisierte Belege unveraenderbar (DB-erzwungen); Korrekturen erzeugen Storno/Korrekturbelege | FAILED | Draft PUT/DELETE 409-gate confirmed working (lines 702-706). DB immutability triggers (sales_document_immutable, sales_document_child_immutable) are correctly implemented in migration 20260712151258_SalesDocuments.cs but are unreachable: FinalizeCoreAsync crashes with DbUpdateConcurrencyException before any document ever reaches Finalized status. Empirically reproduced. |
| 3 | Rechnungen enthalten alle Paragraph-14-Pflichtangaben; Rechnungsnummern race-sicher, eindeutig, im konfigurierten Format bei Finalisierung vergeben | FAILED | NumberingService (atomic ON CONFLICT RETURNING) is correct and race-safe in isolation, but is enlisted in the same transaction that FinalizeCoreAsync rolls back on crash, so no number is ever durably assigned in production. Additionally the detail-read DTO omits IssuerSnapshot and RecipientSnapshot entirely (Paragraph-14 data is frozen server-side but never surfaced to the UI). |
| 4 | USt als EN-16931-Kategorie modelliert, deckt 19/7/0%, Paragraph 19, Paragraph 13b, innergemeinschaftliche Lieferung mit Pflichttexten ab | VERIFIED, engine only, not reachable end-to-end | VatCalculationService and Pflichttext (src/modules/Numera.Modules.Sales/Vat/) correctly implement all required categories (S, AE, K, E, Z, G, O), per-category rounding, Kleinunternehmer Paragraph-19 override, and the mandated German Pflichttexte with VATEX codes - this is real production code, proven by TDD unit tests (03-03), not a reimplementation. However, VatCalculationService.Calculate result is only ever persisted inside FinalizeCoreAsync, which crashes before commit - so no invoice in the running app ever actually carries a persisted, correct VAT breakdown. |
| 5 | Nutzer kann finalisierte Rechnungen stornieren und Gutschriften erstellen; jede finalisierte Rechnung erzeugt einen offenen Posten mit Faelligkeit in der OP-Uebersicht | FAILED | Storno and Gutschrift finalize both reuse the crashing FinalizeCoreAsync. BuildOpenItem is correct in isolation but is included in the same SaveChangesAsync call that throws, so the whole transaction, open item included, rolls back. The separate, unaffected GET api open-items read endpoint (OpenItemEndpoints.cs) and OpenItemsListPage frontend are fine, but will only ever show items in this codebase if seeded directly (never via the app). |

**Score:** 1/5 truths fully verified (2 more partially verified at the isolated-component level but not end-to-end; 3 fully failed)

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| src/Numera.Api/Endpoints/SalesDocumentEndpoints.cs (FinalizeCoreAsync) | Finalize transaction: snapshots, VAT breakdown, numbering, open item, status flip | STUB-LIKE, present but crashes | Line 745 navigation Add on tracked parent leads to DbUpdateConcurrencyException, empirically reproduced against real Postgres. |
| src/Numera.Api/Contracts/SalesDocumentContracts.cs (SalesDocumentDetail) | Paragraph-14-complete detail projection | INCOMPLETE | Missing IssuerSnapshot and RecipientSnapshot fields (lines 76-99). |
| src/platform/Numera.Platform.Db/Migrations/20260712151258_SalesDocuments.cs | DB-enforced GoBD immutability triggers | VERIFIED | Correctly implemented (status-guarded parent trigger plus parent-status-lookup child trigger); confirmed present, but unreachable via the app due to the finalize crash. |
| src/modules/Numera.Modules.Sales/Vat/VatCalculationService.cs and Pflichttext.cs | EN-16931 BG-23 breakdown and Pflichttexte | VERIFIED | All required categories, correct rounding policy, Kleinunternehmer override, mandatory German notes and VATEX codes present. |
| src/modules/Numera.Modules.Sales/Numbering/NumberingService.cs | Race-safe atomic numbering | VERIFIED, isolated | Correct atomic INSERT ON CONFLICT DO UPDATE RETURNING; not reachable end-to-end due to finalize rollback. |
| web/src/features/documents/DocumentDetailPage.tsx | Paragraph-14 detail view plus lifecycle actions | PARTIAL | Correctly built, forward-compatible with the missing snapshot fields, but currently always renders empty issuer and recipient cards because the backend never sends the data. |
| web/src/features/openItems/OpenItemsListPage.tsx | OP-Uebersicht | VERIFIED | Correctly built read view over GET api open-items; will show real data once finalize is fixed. |

### Key Link Verification

| From | To | Via | Status | Details |
|------|-----|-----|--------|---------|
| POST id finalize | SalesDocumentTaxBreakdown persistence | navigation Add plus SaveChangesAsync | NOT WIRED, throws | Empirically confirmed DbUpdateConcurrencyException via a Postgres-backed repro using the exact production load and add pattern. |
| POST id storno, POST id credit-note finalize | FinalizeCoreAsync | shared code path | NOT WIRED, throws | Both reuse the broken core. |
| DocumentDetailPage.tsx | GET api documents id issuer and recipient snapshot | d.issuerSnapshot, d.recipientSnapshot | NOT WIRED | Backend DTO never populates these fields; frontend code is correct but has nothing to read. |
| FinalizeCoreAsync | NumberingService.AssignAsync | ambient transaction | WIRED, in isolation | Correct atomic claim; rolled back as a side effect of the breakdown crash. |
| FinalizeCoreAsync | BuildOpenItem persistence | db.Add plus same SaveChangesAsync | WIRED, in isolation | Correct pattern; rolled back as a side effect of the breakdown crash. |

### Requirements Coverage

| Requirement | Status | Blocking Issue |
|-------------|--------|-----------------|
| DOCS-01 (Belegkette Angebot to AB to Lieferschein to Rechnung) | PARTIAL | Draft-level conversion works; full chain to a Finalized Rechnung is blocked by the finalize crash. |
| DOCS-04 (Entwuerfe mutable, finalisierte Belege unveraenderbar via Storno) | BLOCKED | Finalize never succeeds, so immutability, though DB-enforced and correct, is never exercised by real users. |
| INV-01 (Paragraph-14 Pflichtangaben) | BLOCKED | Finalize crash plus missing detail-DTO snapshot fields. |
| INV-02 (race-sichere Nummernvergabe) | BLOCKED | NumberingService itself correct, but never durably committed due to the finalize rollback. |
| INV-03 (Storno plus Gutschrift) | BLOCKED | Both reuse the crashing FinalizeCoreAsync. |
| INV-04 (USt-Kategorien plus Pflichttexte) | PARTIAL | VatCalculationService correct in isolation; never persisted end-to-end. |
| OPDN-01 (offener Posten pro finalisierter Rechnung) | BLOCKED | Open item creation is inside the same crashing transaction. |

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| src/Numera.Api/Endpoints/SalesDocumentEndpoints.cs | 745 | Navigation-collection Add on a tracked existing parent for a client-set-PK child, inconsistent with every other Add site in the same file | Blocker | Crashes finalize, storno and credit-note-finalize at runtime - the phase central "Herz von v1" transaction is completely non-functional. |
| src/Numera.Api/Contracts/SalesDocumentContracts.cs | 76-99 | Detail DTO omits fields (IssuerSnapshot, RecipientSnapshot) that the frontend already reads | Warning | Paragraph-14 issuer and recipient legally-required data is frozen server-side but never visible to the user. |

Note: both anti-patterns above are honestly self-documented by the implementer in 03-08-SUMMARY.md (Issues Encountered, carried-forward blocker), 03-09-SUMMARY.md (Issues Encountered), and 03-10-SUMMARY.md (carried-forward blocker), and echoed in .planning/STATE.md. This verification independently confirmed both by direct code inspection and, for the finalize crash, by an empirical reproduction against real Postgres using the exact production code path.

### Human Verification Required

1. Finalize a real draft end-to-end after the fix - apply the one-line db.Add fix in FinalizeCoreAsync, then finalize a draft Rechnung via the running app or UI and confirm number assignment, breakdown persistence, and open-item creation. Why human: needs the live API and UI stack, not just the isolated EF Core repro used here.
2. Visually confirm the Paragraph-14 cards render real data - after adding IssuerSnapshot and RecipientSnapshot to the detail DTO, open a finalized invoice detail page and confirm the issuer and recipient cards show frozen legal data. Why human: visual and browser confirmation.

### Gaps Summary

Phase 3 isolated building blocks are largely well-built and match the plan: the DB-level GoBD immutability triggers are correct, the numbering service is genuinely race-safe (atomic ON CONFLICT RETURNING), the VAT calculation engine correctly implements all required EN-16931 categories with mandatory Pflichttexte, and the frontend (list, edit, detail, OP-Uebersicht, settings pages) is well-built and largely forward-compatible.

However, all of this is gated behind a single shared function, FinalizeCoreAsync, which throws DbUpdateConcurrencyException the instant it tries to persist the BG-23 VAT breakdown, because a new child entity with a client-set (Guid.CreateVersion7) store-generated PK is added via collection-navigation fixup on an already-tracked parent instead of via explicit db.Add. This was empirically reproduced against a real Postgres instance using the exact production load and add pattern. Since finalize, storno, and a Gutschrift own later finalize call all share this one function, no document can ever actually be finalized, numbered, receive an open item, or become immutable through the running application - the phase stated centerpiece (das Herz von v1, die zweite jetzt-oder-nie-Naht) is completely non-functional in its current state, despite all of its individual pieces being independently correct.

A second, independent and less severe gap: the read-side detail DTO (SalesDocumentDetail and ToDetail) never returns the frozen IssuerSnapshot and RecipientSnapshot jsonb, so even once finalize is fixed, the Paragraph-14 issuer and recipient data will not be visible on the frontend until this DTO gap is also closed. The frontend was deliberately built forward-compatible for this.

Both gaps are self-documented by the implementer as known, deliberately-deferred src-only follow-ups (03-08-SUMMARY.md, 03-09-SUMMARY.md, .planning/STATE.md), described as required before finalize ships end-to-end. This verification confirms both are still unresolved in the current codebase and that their impact is total, not cosmetic, for the finalize gap.

Both fixes are small and well-scoped (one line for the finalize crash; a two-field DTO extension for the snapshot gap), and the correct patterns are already proven and ready to copy: tests/Numera.IntegrationTests/SalesTestData.cs line 352 (the db.Add pattern for the breakdown row) and the frontend parseSnapshot and readSnapshot machinery (already built and waiting for the two additional DTO fields).

---

Verified: 2026-07-13T07:54:27Z
Verifier: Claude (gsd-verifier)
