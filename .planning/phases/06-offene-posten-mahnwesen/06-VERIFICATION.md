---
phase: 06-offene-posten-mahnwesen
status: passed
verified_by: claude (reviewer of the Codex-delegated implementation)
verified_on: 2026-07-28
method: independent build + full integration/vitest re-runs with the .NET 10 SDK; goal-backward mapping of success criteria to committed code + tests
---

# Phase 6 Verification — Offene Posten & Mahnwesen

**Goal:** Nutzer kann den Geld-Kreislauf schließen — von der offenen Forderung über die erfasste Zahlung bis zur mehrstufigen Mahnung.

**Result:** PASSED. All 3 success criteria are delivered and backed by tests on real Postgres 18 (+ Mailpit for the send path). Human-verify checkpoint (06-05 Task 4) approved by the user on 2026-07-28.

## Success criteria → evidence

1. **Zahlungen manuell erfassen + OP zuordnen; Teilzahlungen aktualisieren den Zahlungsstatus.**
   - `PaymentService.RecordAsync` reduces `OpenItem.OpenAmount`, flips Open→PartiallyPaid→Paid, sets the whitelisted `SalesDocument.AmountDue`/status, audited, in one tx; over-allocation rejected; append-only with reversal. `POST/GET /api/payments` (06-01). Frontend `RecordPaymentDialog` + OP-list action (06-02).
   - Tests: `PaymentTests` (7) — partial→PartiallyPaid, full→Paid, multi-OP split, over-allocation writes nothing, append-only trigger, reversal restores, RLS. vitest client gates (5).

2. **Zahlungserinnerungen + mehrstufige Mahnungen mit konfigurierbaren Stufen/Fristen/Gebühren erzeugen und versenden.**
   - `dunning_level_config` (per-tenant, seeded German ladder L0–L3) + `GET/PUT /api/dunning/config` with validation (06-03). `POST /api/dunning/run` records a `dunning_notice` per candidate (fee + §288 interest as Nebenforderungen, never touching OpenAmount), renders a culture-driven QuestPDF notice, and emails it via `SendDunningNoticeJob` (06-04). Config settings UI + Mahnlauf action (06-05).
   - Tests: `DunningConfigTests` (6) — seed/upsert/validation/RLS/ALTER/unique index; `DunningRunTests` (real Postgres + Mailpit e2e) — record→advance→same-day idempotency→level-2 escalation→§288 interest→RLS→frozen-recipient fallback→PDF emailed→Sent. vitest config gates + Mahnlauf (4).

3. **Fällige offene Posten für Mahnläufe erkannt + in der OP-Übersicht priorisiert sichtbar.**
   - `DunningService.SelectCandidates` selects due_date<today, Open/PartiallyPaid, next-level threshold reached, not dunned today (06-04). OP-Übersicht surfaces `currentDunningLevel` + days-overdue with escalating badges and a Mahnlauf action (06-05); OP-list API merges the dunning shadow columns via raw SQL.

## Verification runs (final committed state)
- Full solution build: **0 warnings / 0 errors** (.NET 10 SDK).
- Integration suite: **102/102** on Testcontainers postgres:18 (+ Mailpit e2e).
- Frontend: `tsc -b` clean, **vitest 56/56**.

## Notes / accepted tradeoffs (non-blocking)
- `open_items.current_dunning_level` + `last_dunned_on` are DB-only shadow columns (hand-added in the 06-03 migration, accessed via raw SQL in 06-04/06-05), not EF-mapped — internally consistent, no model/snapshot drift.
- `PaymentService.RecordAsync` takes no row lock on the open item; concurrent payments on the same OP could over-pay. Acceptable for manual, low-concurrency entry — optional future hardening.
- The 06-04 Mahnlauf e2e is one comprehensive test (not six `[Fact]`s) to avoid repeated Postgres+Mailpit container spin-up; coverage is complete.
