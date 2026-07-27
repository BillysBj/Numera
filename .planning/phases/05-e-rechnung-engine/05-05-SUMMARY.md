---
phase: 05-e-rechnung-engine
plan: 05
subsystem: inbound
tags: [einvoice, inbound, xrechnung, zugferd, pdfpig, zugferd-csharp, kosit, supplier-match, rls, react, i18n, checkpoint]

# Dependency graph
requires:
  - phase: 05-e-rechnung-engine
    provides: "ZUGFeRD-csharp 18 InvoiceDescriptor.Load (05-01), IEInvoiceValidator + EInvoiceValidationStatus (05-02), EInvoiceService/endpoint patterns (05-03)"
  - phase: 02-stammdaten
    provides: "BusinessPartner.IsSupplier + VatId for the supplier match"
provides:
  - "inbound_document — per-tenant immutable store of received e-invoices (original bytes + human-readable read-model + validation status + matched supplier), RLS-isolated"
  - "InboundParser (format detect + PdfPig embedded-XML extract + InvoiceDescriptor.Load parse), SupplierMatcher (VatId → BusinessPartner.IsSupplier)"
  - "InboundEInvoiceService + upload/list/detail/original endpoints"
  - "Bilingual inbound frontend (list + human-readable detail) — EINV-04/05"
affects: []

# Tech tracking
tech-stack:
  added: [PdfPig (Sales module — extract embedded XML from ZUGFeRD PDF/A-3)]
  patterns:
    - "Reuse ONE ZUGFeRD-csharp library for both outbound generate and inbound InvoiceDescriptor.Load parse"
    - "Received bytes are IMMUTABLE — stored verbatim; the read-model is a separate projection"
    - "Supplier match by VAT id (null when no match — never a wrong match)"
    - "Hand-written per-table RLS on inbound_document (reflective discovery never emits policies — the Numera trap)"

key-files:
  created:
    - src/modules/Numera.Modules.Sales/EInvoice/Inbound/InboundDocument.cs
    - src/modules/Numera.Modules.Sales/EInvoice/Inbound/InboundParser.cs
    - src/modules/Numera.Modules.Sales/EInvoice/Inbound/SupplierMatcher.cs
    - src/Numera.Api/Services/InboundEInvoiceService.cs
    - src/Numera.Api/Endpoints/InboundDocumentEndpoints.cs
    - src/platform/Numera.Platform.Db/Migrations/*_InboundDocument.cs
    - tests/Numera.IntegrationTests/InboundEInvoiceTests.cs
    - web/src/lib/api/inbound.ts
    - web/src/features/inbound/InboundListPage.tsx
    - web/src/features/inbound/InboundDetailPage.tsx
    - web/src/i18n/locales/de/inbound.json
    - web/src/i18n/locales/en/inbound.json
  modified:
    - src/Numera.Api/Program.cs
    - src/modules/Numera.Modules.Sales/Numera.Modules.Sales.csproj
    - web/src/i18n/index.ts
    - web/src/App.tsx
---

# 05-05 Summary — Inbound e-invoices (EINV-04 / EINV-05)

## Outcome
Numera can now RECEIVE e-invoices (legally required since 2025-01-01). A user uploads a received XRechnung (UBL/CII XML) or a ZUGFeRD PDF; the system detects the format, extracts the embedded XML via PdfPig when it is a PDF, parses it via `InvoiceDescriptor.Load` (the SAME ZUGFeRD-csharp 18 library used outbound), validates it against KoSIT (05-02), matches the supplier by VAT id (`BusinessPartner.IsSupplier`), and stores the **immutable** original bytes + a human-readable read-model in a new RLS-scoped `inbound_document` table. A bilingual (DE authoritative) frontend shows the inbound list and a human-readable detail (seller/buyer, dates, totals, VAT breakdown, KoSIT findings, matched supplier, original download).

## Tasks
1. **inbound_document + RLS, InboundParser, SupplierMatcher** — commit `c17730f`.
2. **InboundEInvoiceService + upload/list/detail/original endpoints + DI** — commit `c3472b5`.
3a. **Inbound integration test (real Postgres) + embedded-XML match fix** — commit `c7207a8`.
3b. **Bilingual inbound frontend (list + detail) + routes/nav/i18n wiring** — commit `01088ec`.
4. **Human-verify checkpoint — APPROVED** (see below).

## Verification
- Full solution build: **0 warnings / 0 errors**.
- Inbound integration test: **4/4** on real postgres:18 — byte-immutable original; round-trip parse (seller/number/gross=333.00); supplier match by VatId + null-when-absent; RLS second-tenant-sees-0; non-e-invoice rejected with no row.
- Frontend: `tsc -b --noEmit` clean, `vite build` green, `vitest` **47/47**.
- **Human-verify checkpoint (gate=blocking): APPROVED** — the user approved on the strength of the automated coverage (inbound integration 4/4 incl. ZUGFeRD embedded-XML extraction + supplier match + RLS; the 05-03 live-KoSIT conformance; frontend 47/47), electing to eyeball the inbound UI later rather than walk the live round-trip now.

## Deviations
- **[Rule 1 — bug, committed c7207a8]** ZUGFeRD PDF ingest initially missed the embedded XML: PdfPig exposes the `/EmbeddedFiles` name-tree KEY as the stem `"factur-x"` (QuestPDF writes the stem, not `"factur-x.xml"`), so exact-name matching found nothing. Fixed to match by name-stem substring + a content-sniff fallback (any embedded file whose bytes start with `<`), so foreign-producer attachment names also parse. Proven end-to-end by the integration test.

## Follow-ups (non-blocking)
- The visual inbound UI round-trip (upload → detail → supplier-match display, DE/EN findings) remains available as an optional deeper human check; automated coverage already exercises the full parse/validate/match/RLS path.
