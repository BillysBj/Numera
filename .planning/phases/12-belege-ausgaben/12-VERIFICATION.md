---
phase: 12-belege-ausgaben
verified: 2026-08-04T20:42:57Z
status: passed
score: 7/7 must-haves verified (plans 12-01..12-07)
---

# Phase 12: Belege & Ausgaben Verification Report

**Phase Goal:** "Belege fotografieren, sie buchen sich selbst" — Eingangsbelege werden erfasst, per OCR ausgelesen, geprüft gebucht und GoBD-konform revisionssicher archiviert.

**Verified:** 2026-08-04T20:42:57Z
**Status:** passed
**Re-verification:** No — initial verification

## Goal Achievement

### Observable Truths (per plan `must_haves`)

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | 0%/steuerfrei expense books Aufwand+Kreditor only, no Vorsteuer leg | VERIFIED | ExpensePostingSource.BuildPostings skips the input-tax leg when breakdown.Tax > 0m is false; SkrMapping.ExpenseMapping returns null Vorsteuer account + Steuerschluessel.None for 0m |
| 2 | 19%/7% single-rate expense unchanged (golden tests) | VERIFIED | LedgerPostingEngineTests single-rate expense cases assert exact legs |
| 3 | Multi-rate expense produces N Aufwand+N Vorsteuer legs plus 1 Kreditor credit in ONE JournalEntry | VERIFIED | ExpensePostingSource.BuildPostings loops input.Breakdowns, one Kreditor credit = sum(Net+Tax); multi-rate test verifies 19%+7% produces 5 legs, golden totals, single journal-entry row |
| 4 | Receipt + archive are tenant-isolated (RLS); cross-tenant INSERT rejected | VERIFIED | Migration 20260804152146_Receipts.cs enables/forces RLS + tenant_isolation policy on both receipt and receipt_archive; proven live in ReceiptArchiveRlsTests |
| 5 | Archived original is append-only WORM (UPDATE/DELETE raise) | VERIFIED | Migration REVOKEs UPDATE/DELETE from numera_app + receipt_archive_immutable trigger raises exception; proven in ReceiptArchiveRlsTests |
| 6 | Duplicate detected by content-hash (exact) and business key | VERIFIED | ReceiptDeduplicator.CheckAsync checks exact hash first, then supplier+invoice+gross+date; proven in ReceiptArchiveRlsTests |
| 7 | Confirmed fields map to ONE ExpensePostingInput with N breakdown rows, single JournalEntryId | VERIFIED | ReceiptBookingProposal.Build maps confirmed/e-invoice rows to breakdown array inside one ExpensePostingInput |
| 8 | IReceiptExtractor default is a zero-cloud stub; Azure adapter opt-in only | VERIFIED | Program.cs registers AzureReceiptExtractor only when Document Intelligence is configured, else StubReceiptExtractor |
| 9 | IAttachmentScanner scans; EICAR reported infected; test double works without live ClamAV | VERIFIED | NoopAttachmentScanner detects the EICAR byte signature; ClamAvAttachmentScanner implements real INSTREAM protocol; registered by config |
| 10 | Upload is malware-scanned, WORM-archived, Captured, and OCR job enqueued (never sync) | VERIFIED | ReceiptIngestService.IngestAsync scans before archive, creates Receipt(Captured), enqueues ExtractReceiptJob only for Captured |
| 11 | Extraction job fills fields+confidence, flips Captured to Extracted, under SetTenant | VERIFIED | ExtractReceiptJob.RunAsync calls SetTenant before resolving DbContext, fills fields, serializes confidence, matches supplier, sets Status=Extracted |
| 12 | Infected attachment is Quarantined, never archived or booked | VERIFIED | ReceiptIngestService creates Quarantined receipt with no archive row on Infected/Error verdict; endpoints hard-reject Quarantined at proposal/review/confirm-book |
| 13 | XRechnung/ZUGFeRD (Tier A) creates Receipt linked to inbound_document, Extracted, zero OCR | VERIFIED | InboundEInvoiceService.IngestAsync builds Receipt with Source=EInvoice, Status=Extracted, InboundDocumentId set, no IReceiptExtractor call |
| 14 | List/detail endpoints return receipts + confidence; stream original, RLS-scoped | VERIFIED | ReceiptEndpoints GET list/detail parse confidence; GET original streams archive or inbound-document bytes |
| 15 | Receipt in Booked ALWAYS has ReviewedByUserId (never auto-booked) | VERIFIED | ConfirmBookAsync hard-rejects when Status is not Reviewed or ReviewedByUserId is null - D1 lock confirmed |
| 16 | Confirm-then-book is ONE ExpensePostingInput to ONE PostAsync in caller-owned tx, idempotent, SourceType=Expense | VERIFIED | ConfirmBookAsync idempotency guard via AnyAsync(SourceType==Expense) before booking, single PostAsync inside BeginTransactionAsync/CommitAsync |
| 17 | Multi-rate e-invoice books N legs + 1 Kreditor credit in ONE JournalEntry; totals reconciled | VERIFIED | LoadInboundBreakdownAsync extracts all rate rows; EnsureBookingTotals/AmountsMatch checks net+VAT+gross; ReceiptBookingTests (432 lines, 6 Facts) covers multi-rate |
| 18 | Booked expense feeds USt-VA Kz 66; 0% books Aufwand+Kreditor only | VERIFIED | Vorsteuer accounts 1576/1571/1406/1401 tagged ustvaKennziffer=66 in embedded chart JSON; UstVaCalculator.zahllast subtracts taxFigures[66]; RecognitionReader sums by booking date, Asset accounts, both Besteuerungsarten - matches the USER-APPROVED mid-phase extension exactly |
| 19 | Receipt links JournalEntryId + archived original; corrections are reversal-only | VERIFIED | receipt.JournalEntryId set only on first successful booking; no PATCH/DELETE exists for a Booked receipt |
| 20 | Each tenant has a unique unguessable Beleg email address; token resolves to exactly one tenant | VERIFIED | TenantBelegeMailbox.GenerateAddressToken is 256-bit random; unique index on AddressToken; resolve_belege_mailbox SQL function resolves token to tenant |
| 21 | Recurring IMAP poll fetches unseen mail, routes per-tenant, SetTenant, feeds ingest pipeline | VERIFIED | PollBelegMailboxJob.RunAsync searches NotSeen, resolves tenant per message, SetTenant before ReceiptIngestService.IngestAsync per attachment; recurring job registered in Numera.Worker/Program.cs |
| 22 | Idempotent: re-forwarded/re-polled mail never double-creates a Beleg | VERIFIED | Dedup by Message-Id or content-hash via ProcessedBelegeMail unique index + AnyAsync pre-check; proven in BelegeMailIntakeTests re-delivery test |
| 23 | No/ambiguous-tenant mail handled safely; attachment scanned before archive; mandantengetrennt | VERIFIED | ResolveTenantAsync returns null when tokens resolve to 0 or more than 1 tenants; attachments flow through same ReceiptIngestService; proven cross-tenant and EICAR tests in BelegeMailIntakeTests |
| 24 (frontend) | User captures via camera/upload, sees review queue | VERIFIED | BelegCapturePage.tsx (camera capture=environment input + drag/drop upload, 15 MiB/type validation) navigates to review queue on success |
| 25 (frontend) | Review page shows original alongside fields+confidence, confirm-then-book (never auto) | VERIFIED | BelegReviewPage.tsx OriginalViewer beside ExtractedField/ConfidenceBadge; confirm-book button gated on reviewed status, disabled while dirty; explicit never-automatic copy |
| 26 (frontend) | Multi-rate e-invoice shows its legs; tenant mailbox address surfaced | VERIFIED | ProposalPreview renders per-rate breakdown + posting-leg tables; MailboxAddressCard.tsx against GET /api/receipts/mailbox |

Score: 26/26 derived truths verified (all 7 plans must_haves.truths fully covered)

### Required Artifacts (representative sample; all plan artifacts checked)

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| ExpensePostingSource.cs | multi-rate/0% breakdown loop | VERIFIED | ExpensePostingBreakdown record + loop confirmed |
| SkrMapping.cs | 0% expense mapping | VERIFIED | SKR03 4980/SKR04 6300, null Vorsteuer, Steuerschluessel.None |
| Receipt.cs | converged aggregate, ITenantEntity | VERIFIED | 105 lines, full state machine + provenance links |
| ReceiptArchive.cs | WORM bytea + hash | VERIFIED | 54 lines, required byte[] OriginalBytes |
| *_Receipts.cs migration | RLS+WORM trigger | VERIFIED | REVOKE UPDATE/DELETE + receipt_archive_immutable trigger |
| ReceiptDeduplicator.cs | hash + business-key dedup | VERIFIED | Exact-then-business-key logic |
| ReceiptBookingProposal.cs | Receipt to ExpensePostingInput | VERIFIED | Maps confirmed/e-invoice rows to breakdowns |
| IReceiptExtractor.cs / StubReceiptExtractor.cs | OCR port + zero-cloud default | VERIFIED | ExtractedField<T> per-field confidence |
| AzureReceiptExtractor.cs | opt-in adapter | VERIFIED | Registered only when Document Intelligence configured |
| IAttachmentScanner.cs / ClamAvAttachmentScanner.cs | scan port + adapter + test double | VERIFIED | NoopAttachmentScanner EICAR detection + real INSTREAM adapter |
| ReceiptIngestService.cs | scan then archive then Captured then enqueue | VERIFIED | 205 lines, shared upload+email entrypoint |
| ExtractReceiptJob.cs | SetTenant then extract then Extracted | VERIFIED | 117 lines |
| ReceiptEndpoints.cs | upload/list/detail/original/proposal/review/confirm-book | VERIFIED | 848 lines; all 7 routes present and substantive |
| InboundEInvoiceService.cs | Tier-A receipt hook | VERIFIED | Creates linked Receipt in Extracted, no OCR |
| TenantBelegeMailbox.cs | per-tenant address + dedup entity | VERIFIED | 256-bit token, unique indexes |
| PollBelegMailboxJob.cs | IMAP poll then route then SetTenant then ingest | VERIFIED | 295 lines |
| MailboxEndpoints.cs | GET mailbox provisioning | VERIFIED | 86 lines |
| *_BelegeMailIntake.cs migration | RLS + resolve_belege_mailbox fn | VERIFIED | RLS on both tables, SECURITY-scoped function |
| belegeApi.ts / BelegCapturePage.tsx / BelegReviewQueuePage.tsx / BelegReviewPage.tsx / MailboxAddressCard.tsx | frontend surface | VERIFIED | Fully wired TanStack Query hooks, routed pages, i18n |

### Key Link Verification

| From | To | Via | Status | Details |
|------|-----|-----|--------|---------|
| ExpensePostingSource | AccountResolver | per-breakdown loop, skip Vorsteuer at Tax==0 | WIRED | Confirmed in code |
| Receipts migration | numera_app role | REVOKE UPDATE/DELETE + trigger | WIRED | Confirmed |
| ReceiptBookingProposal | ExpensePostingInput | breakdown mapping | WIRED | Confirmed |
| Program.cs | IReceiptExtractor/IAttachmentScanner | config-gated DI | WIRED | Stub/Noop default, Azure/ClamAV opt-in |
| ReceiptIngestService | IAttachmentScanner+ReceiptArchive+IBackgroundJobClient | scan-before-archive, then enqueue | WIRED | Confirmed |
| ExtractReceiptJob | ICurrentTenant.SetTenant+IReceiptExtractor | scope+SetTenant then ExtractAsync | WIRED | Confirmed |
| InboundEInvoiceService | Receipt.InboundDocumentId | Tier-A receipt creation | WIRED | Confirmed |
| ReceiptEndpoints.ConfirmBookAsync | PostingEngine.PostAsync+ExpensePostingSource | one PostAsync in tx, SourceType=Expense | WIRED | Confirmed |
| ReceiptEndpoints.ConfirmBookAsync | JournalEntry idempotency | AnyAsync guard on SourceType+SourceRef | WIRED | Confirmed |
| PollBelegMailboxJob | ICurrentTenant.SetTenant+ReceiptIngestService.IngestAsync | resolve token to tenant, SetTenant, ingest each attachment | WIRED | Confirmed |
| PollBelegMailboxJob | Message-Id dedup store | skip processed Message-Ids/content-hash | WIRED | Confirmed |
| belegeApi.ts | /api/receipts endpoints | lib/api fetch + TanStack Query | WIRED | Confirmed |
| App.tsx | belege pages | Route path=/belege/... | WIRED | Confirmed |
| Vorsteuer accounts (1576/1571/1406/1401) | USt-VA Kz 66 | ustvaKennziffer=66 seed + RecognitionReader/UstVaCalculator | WIRED | Confirmed end-to-end (booking date, Asset accounts, both Besteuerungsarten, Kz 83 deducts it) |

### Requirements Coverage

| Requirement | Status | Evidence |
|-------------|--------|----------|
| BELEG-01 (Kamera/Upload) | SATISFIED | BelegCapturePage.tsx + ReceiptEndpoints POST upload; camera recorded as Source=Upload (documented, intentional) |
| BELEG-02 (OCR + Konfidenz + Pruefung) | SATISFIED | IReceiptExtractor/ExtractedField confidence + ExtractReceiptJob; review gate before booking |
| BELEG-03 (E-Rechnung strukturiert, kein OCR) | SATISFIED | InboundEInvoiceService Tier-A hook, reuses Phase-5 parser, zero-OCR |
| BELEG-04 (GoBD WORM 10 Jahre) | SATISFIED | receipt_archive REVOKE+trigger; WORM, indexed, linked to booking confirmed (10-year retention is an operational/DB-retention policy, consistent with the GoBD pattern used elsewhere in the codebase) |
| BELEG-05 (Buchungsvorschlag then Bestaetigung then Buchung) | SATISFIED | ConfirmBookAsync hard-rejects non-Reviewed; single PostAsync; feeds Kz 66 |
| BELEG-06 (Lieferantenzuordnung + Dubletten) | SATISFIED | SupplierMatcher.MatchSellerAsync in ExtractReceiptJob/InboundEInvoiceService; ReceiptDeduplicator |
| BELEG-07 (Mandanten-E-Mail-Adresse) | SATISFIED | TenantBelegeMailbox + PollBelegMailboxJob + MailboxEndpoints, GreenMail-tested |

### Anti-Patterns Found

None. Grep for TODO/FIXME/XXX/HACK/placeholder/coming-soon/not-implemented across all phase-12 backend and frontend files returned zero matches. No stub returns (return null, placeholder divs, empty handlers) found in the reviewed surface.

### LOCKED Decision Compliance (12-CONTEXT.md)

| Decision | Status | Evidence |
|----------|--------|----------|
| D1: stub-default OCR port, Azure opt-in, never auto-book | HELD | Program.cs DI; ConfirmBookAsync hard-rejects ReviewedByUserId null |
| D2: email intake in scope, per-tenant address, IMAP + dedup + GreenMail | HELD | TenantBelegeMailbox, PollBelegMailboxJob, BelegeMailIntakeTests (GreenMail) |
| D3: 19%/7%/0% expense VAT, no reverse-charge, multi-rate to ONE JournalEntry | HELD | ExpensePostingSource, IsSupportedRate, e-invoice rows with TaxCategory AE/K explicitly rejected |
| D4: ClamAV scanner + test double, scan before archive, infected to Quarantined | HELD | ReceiptIngestService scans before any archive write; ClamAvAttachmentScanner + NoopAttachmentScanner |
| USER-APPROVED: Kz 66 Vorsteuer extension, Kz 83 deducts it, EUeR stays cash-correct | HELD | UstVaCalculator.zahllast subtracts taxFigures[66]; EUeR reads same 1576/1571/1406/1401 accounts for paid input VAT - booked-but-unpaid correctly does not appear there (deferred to Banking phase, as documented) |

### Human Verification Required

None outstanding. The 12-07 plan human-verification checkpoint (Task 3: capture, extraction, review/booking, e-invoice multi-rate, WORM persistence, duplicates, quarantine, email intake) was already run and approved by the user per the orchestrator context, so no further human sign-off is pending for this phase.

### Gaps Summary

No gaps found. All 26 derived observable truths across the 7 plans (12-01 through 12-07) are verified against actual code, not merely SUMMARY claims. Key findings:

- The multi-rate/0% expense posting engine correctly extends the Phase-10 PostingEngine without a new engine, exactly as scoped.
- The Receipt aggregate, WORM archive, RLS, and dedup are proven live against real Postgres (Testcontainers), not just unit-level.
- The never-auto-book invariant (D1) is enforced at the API boundary (ConfirmBookAsync), not just documented; a receipt without ReviewedByUserId is hard-rejected.
- Tier-A e-invoices bypass OCR entirely and reuse the Phase-5 parser via InboundEInvoiceService, landing directly in Extracted.
- The Kz 66 Vorsteuer wiring (USER-APPROVED mid-phase decision) is fully traced end-to-end: seed-JSON account tagging, RecognitionReader SQL, UstVaCalculator.zahllast subtraction - an easy place for a silent gap, but it is solid.
- Email intake is mandantengetrennt end-to-end: unguessable per-tenant token, ambiguous/no-match mail is safely skipped, malware is scanned before archive, and re-delivery is idempotent by Message-Id/content-hash - all proven with GreenMail integration tests.
- The frontend is not a stub UI: the review page implements the full original-alongside-fields workflow with confidence badges, a proposal preview with posting legs, and an explicit two-gate confirm-book action that cannot fire while the form is dirty.
- Full solution build (dotnet build Numera.sln --configuration Release) is clean with 0 warnings and 0 errors as of this verification.

Known, documented, and correctly-scoped-out items (not gaps): Azure OCR/ClamAV/IMAP are config-driven external dependencies (proven against test doubles); camera uploads record as Source=Upload (frontend-only distinction, backend field not yet extended); paragraph 13b reverse-charge and intra-community-acquisition expense VAT and their USt-VA Kennziffern (89/61/46/47) are deferred; the EUeR expense side flows only on supplier payment (deferred to the Banking phase) - this last point was an explicit user-approved mid-phase decision, not an implementation gap.

---

_Verified: 2026-08-04T20:42:57Z_
_Verifier: Claude (gsd-verifier)_
