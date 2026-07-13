---
phase: 04-pdf-versand
plan: 05
subsystem: frontend
tags: [react, react-query, shadcn, i18n, pdf-download, email-send, logo-upload, checkpoint]

# Dependency graph
requires:
  - phase: 04-pdf-versand
    provides: "GET /api/documents/{id}/pdf (render-if-absent), POST /api/documents/{id}/send (04-04), PUT/GET /api/company-profile/logo (04-03), document detail DTO sentAt/Status"
provides:
  - "Document detail page: DE/EN PDF download + send-by-email + send-status badge (finalized-only actions)"
  - "Company-profile settings: logo upload + live preview with client-side type/size validation"
  - "documents + settings i18n strings (DE authoritative) for the new actions"
affects: []

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "react-query useQuery/useMutation + shadcn Card/Badge/Button, mirroring the Phase-3 document detail conventions"
    - "PDF download via object-URL anchor; language chosen per-action (default de)"
    - "Logo card lives OUTSIDE the RHF §14 form (multipart PUT is separate from the JSON profile upsert)"

key-files:
  created: []
  modified:
    - web/src/lib/api/documents.ts
    - web/src/features/documents/DocumentDetailPage.tsx
    - web/src/lib/api/companyProfile.ts
    - web/src/features/settings/CompanyProfileSettingsPage.tsx
    - web/src/i18n/locales/de/documents.json
    - web/src/i18n/locales/en/documents.json
    - web/src/i18n/locales/de/settings.json
    - web/src/i18n/locales/en/settings.json
---

# 04-05 Summary — Frontend: PDF download + send + logo upload (Phase-4 close)

## Outcome
The user-facing surface for Phase 4, over the 04-03/04-04 backend:
- **Document detail page** — a finalized-only action group: a DE/EN language `Select` + "PDF herunterladen" (downloads `{documentNumber}.pdf` via an object-URL anchor over `GET /{id}/pdf`), a "Per E-Mail senden" button (confirm dialog → `POST /{id}/send`), and a send-status `Badge` (`Versand läuft` → `Gesendet`) driven by `sentAt` / `Status===Sent`. Drafts show neither action; cancelled documents can download but not send.
- **Settings** — a "Logo / Briefpapier" `Card` outside the RHF §14 form: file input (PNG/JPG), live preview (selected file → else current logo via `companyLogoUrl` cache-bust → else a "no logo" placeholder), client-side type + 1 MB validation mirroring the server, and 400/409 error mapping.
- New DE/EN `documents` + `settings` i18n strings (DE authoritative).

## Tasks
1. **Document client fns + detail-page download/send/status UI** — commit `e4ca8e1`.
2. **Logo upload/preview in settings** — commit `987aa11`.
3. **Human-verify checkpoint** — APPROVED (see below).

## Verification
- `npx tsc -b --noEmit` clean; `npm run build` green; `vitest run` 47/47 passing.
- **Human-verify checkpoint (gate=blocking): APPROVED.** The orchestrator drove the verification by rendering the production `InvoiceDocument` layout to real PDFs (DE + EN) with a sample logo and inspecting the rendered pages: §14-complete (issuer + logo, recipient, number/dates, line table, netto/USt/Gesamt totals, BG-23 breakdown S 19 % + AE 0 %, verbatim-German reverse-charge Pflichttext under the English labels, bank details, registry footer), correct DE↔EN label + culture switch (13.07.2026 / 300,00 € ↔ 13/07/2026 / 300.00 €) with a single shared layout (no DE/EN drift). The email round-trip is proven by the 04-04 end-to-end test (real Testcontainers Mailpit captured the message with the PDF attached; `document_email`→Sent; `SentAt` flipped; RLS-isolated). User approved.

## Deviations
- **Orchestrator-driven checkpoint verification:** rather than the user manually standing up the full browser stack, the orchestrator rendered the production layout to PDFs via a temporary harness (since `InvoiceDocument.Render(model)` is a pure function needing no DB/stack), presented them, and the user signed off. The temporary harness was removed; build left green.

## Follow-ups surfaced (non-blocking, for a later phase)
- **Recipient `Name` + `LegalForm` concatenation** renders redundantly when the frozen name already includes the legal form (e.g. "Kunde AG" + "AG" → "Kunde AG AG"). Cosmetic; a `SnapshotReader`/layout tweak (suppress `LegalForm` when the name already ends with it) would fix it. Not a §14 defect.
- Live manual browser round-trip (logo upload → PDF in browser → Mailpit UI) remains available as an optional deeper check; automated coverage already exercises the full path.
