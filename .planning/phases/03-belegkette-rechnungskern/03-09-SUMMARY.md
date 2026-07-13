---
phase: 03-belegkette-rechnungskern
plan: 09
subsystem: ui
tags: [react, react-query, react-i18next, react-router, shadcn, invoicing, en16931]

# Dependency graph
requires:
  - phase: 03-belegkette-rechnungskern
    provides: "03-07 documents BFF client + DE/EN namespace + /documents routes; 03-04 draft CRUD + convert; 03-05 finalize; 03-06 storno + credit-note endpoints"
provides:
  - "DocumentDetailPage — the read model for a single sales document (header, §14 issuer/recipient snapshots, lines, BG-23 VAT breakdown + Pflichttexte, chain links)"
  - "Lifecycle action bar: finalize (Draft → immutable numbered invoice), Storno + Gutschrift (finalized Rechnung), convert (forward chain), state-driven with confirms"
  - "documents.ts finalize/storno/credit-note client fns + enriched SalesDocumentDetail (finalizedAt/sentAt/snapshots) + SalesTaxBreakdownRow + parseSnapshot"
  - "/documents/:id detail route (the route 03-07 deliberately left open)"
affects: [phase-04-erechnung, phase-05-pdf, phase-06-zahlungen-buchhaltung]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Detail read model renders SERVER-computed truth (snapshots, numbering, VAT, immutability) — never recomputes"
    - "State-driven action bar keyed off document status/type; window.confirm on irreversible actions; 409/422 mapped to an inline banner"
    - "Defensive jsonb snapshot reading (parseSnapshot + casing-agnostic field pickers) — forward-compatible with the exact serialization"

key-files:
  created:
    - web/src/features/documents/DocumentDetailPage.tsx
  modified:
    - web/src/lib/api/documents.ts
    - web/src/i18n/locales/de/documents.json
    - web/src/i18n/locales/en/documents.json
    - web/src/App.tsx

key-decisions:
  - "Convert control offers only the forward chain (Angebot/AB/Lieferschein/Rechnung); Storno/Gutschrift are correction docs created via their own endpoints"
  - "Money is display-only via Intl.NumberFormat('de-DE', {currency: doc.currency}); the page never does arithmetic on wire decimals"
  - "Snapshot fields typed as unknown + parseSnapshot, read defensively (PascalCase + camelCase) so the page is forward-compatible with the current API detail projection"

patterns-established:
  - "Read-model detail page: shadcn Card/Badge/Table + react-query useQuery + per-action useMutation, mirroring PartnerDetailPage"
  - "actionErrorMessage: 422 ValidationProblem → joined field messages; 409/other → ProblemDetails detail/title, into an inline banner (no crash)"

# Metrics
duration: 9min
completed: 2026-07-13
---

# Phase 3 Plan 09: Document Detail + Lifecycle UI Summary

**DocumentDetailPage renders the §14 read model (frozen issuer/recipient snapshots, lines, BG-23 VAT breakdown with Pflichttexte, chain links) and drives finalize / Storno / Gutschrift / convert against the real 03-05/03-06 endpoints — read-only once finalized.**

## Performance

- **Duration:** 9 min
- **Started:** 2026-07-13T06:51:09Z
- **Completed:** 2026-07-13T06:59:54Z
- **Tasks:** 2
- **Files modified:** 5

## Accomplishments
- Extended `documents.ts` with `finalizeSalesDocument`/`stornoSalesDocument`/`createCreditNote`, enriched `SalesDocumentDetail` (finalizedAt/sentAt + issuer/recipient snapshots), a `SalesTaxBreakdownRow` interface and a `parseSnapshot` defensive jsonb reader.
- Built `DocumentDetailPage` — the visible core of criteria #1–#5: §14 issuer/recipient snapshot cards, a lines table, the persisted BG-23 VAT breakdown with mandatory Pflichttexte, Kleinunternehmer §19 / reverse-charge §13b notes, totals, and the source/corrects/cancelledBy chain links.
- State-driven action bar: Draft → editDraft + Finalize (irreversible, confirm, assigns + displays the number, flips read-only); finalized Rechnung → Storno (→ new Storno detail) + Gutschrift (→ new draft); any non-cancelled doc → Convert (forward chain → new draft). 409/422 surfaced in an inline banner, no crash.
- Appended a `detail` block + lifecycle `actions` keys to the DE/EN `documents` namespace (DE authoritative), and wired the `/documents/:id` route in `App.tsx`.

## Task Commits

Each task was committed atomically:

1. **Task 1: documents.ts finalize/storno/credit-note + detail i18n** - `33b6d6d` (feat)
2. **Task 2: DocumentDetailPage read model + lifecycle action bar + route** - `9a3d3fe` (feat)

## Files Created/Modified
- `web/src/features/documents/DocumentDetailPage.tsx` - The read-model detail view + lifecycle action bar (created).
- `web/src/lib/api/documents.ts` - finalize/storno/credit-note client fns; enriched SalesDocumentDetail; SalesTaxBreakdownRow; parseSnapshot.
- `web/src/i18n/locales/de/documents.json` / `en/documents.json` - detail section + lifecycle action strings.
- `web/src/App.tsx` - `/documents/:id` detail route + DocumentDetailPage import.

## Decisions Made
- Convert control lists only the forward chain (Angebot → AB → Lieferschein → Rechnung); Storno/Gutschrift are correction documents with dedicated endpoints, not convert targets.
- Money is presentation-only (`Intl.NumberFormat('de-DE',{currency: doc.currency})`); the page renders server-frozen totals and never recomputes VAT.
- Snapshot fields are typed `unknown` and read through `parseSnapshot` + casing-agnostic pickers (PascalCase and camelCase), so the issuer/recipient cards work regardless of the exact jsonb serialization.

## Deviations from Plan

None - plan executed exactly as written (all changes confined to the owned `web/*` files; no `src/*` or `tests/*` touched).

## Issues Encountered
- **API detail projection does not yet expose the frozen §14 snapshots.** `SalesDocumentEndpoints.ToDetail` / the `SalesDocumentDetail` C# contract (03-04) return the header/lines/breakdown/chain but NOT `IssuerSnapshot`/`RecipientSnapshot`, so at runtime `doc.issuerSnapshot`/`recipientSnapshot` are `undefined` and the issuer/recipient cards show the "wird bei Finalisierung eingefroren" hint (Draft) or "—" (finalized). This plan owns `web/*` only (parallel with 03-08 on `tests/*`), so the C# contract could not be extended here. The frontend is built forward-compatibly — `parseSnapshot` + defensive PascalCase/camelCase readers already match the finalize serialization (`LegalName`/`Address{Street,PostalCode,City,CountryCode}`/`VatId`/`TaxNumber`; recipient `Name`/`BillingAddress`), so the cards populate the instant a backend follow-up adds the two fields to `SalesDocumentDetail`. Recommended follow-up: a small `src/*` change adding `IssuerSnapshot`/`RecipientSnapshot` (raw jsonb string) to the detail DTO + `ToDetail`.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- The visible half of Phase 3 is complete: a user can view any sales document, finalize a draft into an immutable numbered §14 invoice, and Storno/Gutschrift/convert it from the UI. `npm run build` green; `tsc -b --noEmit` clean; vitest 39/39.
- Follow-up (non-blocking): extend the API detail DTO to return the frozen §14 snapshots so the issuer/recipient cards render real data (see Issues Encountered).
- Remaining Phase 3 work: 03-08 (finalize + correction integration tests) — running in parallel this wave.

---
*Phase: 03-belegkette-rechnungskern*
*Completed: 2026-07-13*

## Self-Check: PASSED

- FOUND: web/src/features/documents/DocumentDetailPage.tsx
- FOUND: web/src/lib/api/documents.ts (finalizeSalesDocument export)
- FOUND: .planning/phases/03-belegkette-rechnungskern/03-09-SUMMARY.md
- FOUND: web/src/App.tsx /documents/:id route
- FOUND commit 33b6d6d (Task 1)
- FOUND commit 9a3d3fe (Task 2)
