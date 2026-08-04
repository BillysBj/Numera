# Phase 11: Berichte & USt-Voranmeldung - Research

**Researched:** 2026-08-03
**Domain:** German tax reporting from a double-entry ledger — EÜR (Anlage EÜR, §4(3) EStG cash-basis) + USt-Voranmeldung (Kennziffern calc, Prüfansicht, ELSTER-XML + print export for manual upload)
**Confidence:** HIGH on codebase seams + posting data model (read from source this session); HIGH on Kennziffer meanings + EÜR structure (BMF/DATEV/sevdesk verified); MEDIUM on the exact ELSTER `ustva/v2026` XSD element list (pull the ERiC release doc during planning); the Ist-Versteuerung derivation gap is the single biggest design decision.

## Summary

Phase 11 is a **read/aggregation + export** phase on top of the Phase-10 ledger — no new posting engine, no new booking rules. Everything renders from the immutable postings written in Phase 10. Two reports must be produced: the **USt-Voranmeldung** (Kennziffern 81/86/35/66/83/41/89/46/47/61 computed from postings, a Prüfansicht drill-down, and an ELSTER-conformant XML + QuestPDF print for *manual* upload — no ERiC), and the **EÜR** in the structure of the Anlage EÜR (Betriebseinnahmen/-ausgaben, Zufluss/Abfluss) as on-screen + QuestPDF print.

The **crux discovered by reading the actual Phase-10 code**: the posting model books revenue and output-VAT at **invoice date** (`8400`→`1776`, entry_date = `DocumentDate`) and expenses+input-VAT at expense date, while **payment postings carry NO tax metadata at all** — `PaymentPostingSource` writes only `Bank↔Forderung` legs with no `Steuerschluessel`/`TaxCategory`/`TaxRatePercent`. This means:
- **Soll-Versteuerung USt-VA** is a clean, posting-driven aggregation: sum the invoice-posting VAT legs (which carry `TaxCategory` + `Steuerschluessel` + `TaxRatePercent`) by `entry_date` period. The locked decision "VAT driven off account + Steuerschlüssel on the posting, never re-derived" holds directly. ✅
- **Ist-Versteuerung USt-VA** and the **EÜR cash-basis income** (Zufluss/Abfluss) **cannot** be produced by summing accounts by period, because the economic fact (VAT liability / income realization) happens at *payment* but the tax-bearing posting sits at *invoice*. This is a genuine gap that Phase 11 must resolve — it is the #1 planning decision (see Open Questions and the Ist/EÜR Derivation section).

**Primary recommendation:** Build report aggregation as RLS-scoped SQL reads mirroring `LedgerEndpoints` (raw `SqlQuery` over `postings`/`journal_entries`). Compute USt-VA Kennziffern from a **per-fiscal-year mapping table** keyed off `Account.UstvaKennziffer` + `Posting.Steuerschluessel`/`TaxCategory`. For the Ist-Versteuerung + EÜR cash-basis cases, **derive recognition at report time from `PaymentAllocation → OpenItem → SalesDocument → SalesDocumentTaxBreakdown`** (keeps Phase-10 postings untouched) rather than re-opening the posting invariants. Hand-build the ELSTER `Anmeldungssteuern` Nutzdaten XML (ISO-8859-15, namespace `.../ustva/v2026`) — mirror the Phase-5 XRechnung hand-XML approach; there is no maintained .NET library. Render both reports with the existing QuestPDF pattern (`InvoiceDocument`/`DocumentPdfService`). EÜR is **print + on-screen only** (no ELSTER XML this phase — Anlage EÜR is an annual ESt attachment, out of scope for a monthly report tool).

## Standard Stack

### Core (all already in the solution — nothing new to install)

| Component | Where | Purpose | Why standard |
|-----------|-------|---------|--------------|
| `Numera.Modules.Ledger` | `src/modules/Numera.Modules.Ledger` | Source of truth: `Account` (`UstvaKennziffer`, `Steuerschluessel`, `Type`, `ChartVariant`), `JournalEntry` (`EntryDate`, `SourceType`, `PeriodId`, `FestgeschriebenAt`), `Posting` (`AccountId`, `Amount`, `Direction`, `Steuerschluessel`, `TaxRatePercent`, `TaxCategory`), `LedgerSettings` (`Besteuerungsart`, `Gewinnermittlungsart`, `FiscalYearStartMonth`), `FiscalPeriod` | Read these; do not re-derive |
| `NumeraDbContext` + raw `SqlQuery` | `src/platform/Numera.Platform.Db` | RLS-scoped aggregation reads | `LedgerEndpoints` already establishes the exact pattern (raw SQL, keyset, RLS-scoped) |
| `TenantConnectionInterceptor` / `ICurrentTenant` | `src/platform/Numera.Platform.Tenancy` | RLS GUC per connection | All report reads are automatically tenant-scoped |
| `RoundingPolicy` + `TaxCategory` + `Money` (NodaMoney) | `src/platform/Numera.Platform.Money` | Decimal money + EN-16931 per-category rounding | Report money must match booking money to the cent |
| QuestPDF (`InvoiceDocument`, `DocumentPdfService`) | `src/modules/Numera.Modules.Sales/Pdf` + `src/Numera.Api/Services/DocumentPdfService.cs` | Report print export | Locked decision: no new charting/report lib; mirror the `IDocument` + render-and-store pattern |
| Hangfire (Api default queue) | `src/Numera.Api/Jobs` | Only if XML/PDF generation is deferred | Reports are cheap; synchronous is fine for MVP |

### Supporting assets (net-new for this phase)

| Asset | Purpose | Notes |
|-------|---------|-------|
| **Per-fiscal-year USt-VA Kennziffer map** | Maps `(Account.UstvaKennziffer, Steuerschluessel/TaxCategory, base-vs-tax)` → a UStVA line | Versioned by year (`ustva/v2026`); the form changes annually. Seed as checked-in data, not hardcoded in one method. |
| **Hand-built `Anmeldungssteuern` XML writer** | ELSTER UStVA Nutzdaten XML for manual Mein-ELSTER upload | `System.Xml`/`XmlWriter`, ISO-8859-15. Mirror the Phase-5 XRechnung post-processing approach. No .NET library exists. |
| **`ust_va_filing` persistence (recommended)** | Store a generated UStVA (period, computed Kz values snapshot, status, XML bytes) | Analogous to `document_render`; new table → needs RLS + (optionally) immutability once submitted. See Period/Festschreibung section. |

### Alternatives Considered

| Recommended | Alternative | Tradeoff |
|-------------|-------------|----------|
| Hand-build `Anmeldungssteuern` Nutzdaten XML | Full `Elster` envelope with `TransferHeader`/`DatenTeil` | The **manual upload** path only needs the `Anmeldungssteuern` Nutzdaten payload (verified on elster.de + forum). The full envelope + `TransferHeader` is only for ERiC direct send — explicitly out of scope. Build the smaller file. |
| Ist/EÜR: report-time payment→invoice attribution | Posting-model change: "USt nicht fällig" accounts (SKR03 1766/1767, SKR04 3816/3817) reclassified at payment | The posting-driven approach is the "proper" DATEV Ist handling but re-opens Phase-10 (new seed accounts, extend `PaymentPostingSource` with tax-reclassification legs, migration). Report-time attribution keeps Phase-10 frozen and uses `PaymentAllocation`+frozen `SalesDocumentTaxBreakdown`. Recommend attribution for this phase; document the debt. |
| EÜR = on-screen + QuestPDF print | EÜR as ELSTER Anlage-EÜR XML | Anlage EÜR is an **annual** ESt attachment needing the full ESt/ERiC context. Success criterion 1 only asks for "in der Struktur der Anlage EÜR" — print/on-screen satisfies it. Defer XML. |

**Installation:** none. New entities → `dotnet ef migrations add ...` built with the user-local .NET 10 SDK (MEMORY: dotnet-10-sdk-path — not the PATH default .NET 8).

## USt-VA Kennziffer Mapping (the crux)

### What each required Kennziffer means (verified BMF/DATEV/sevdesk, 2026 form)

Base = **Bemessungsgrundlage** (net, whole euros, no decimals). Tax = **Steuer** (with cents). "auto" = ELSTER computes the tax from the base × rate; you still compute+display it in the Prüfansicht.

| Kz | Meaning | Base or Tax | Source in postings (Soll-basis) |
|----|---------|-------------|----------------------------------|
| **81** | Steuerpflichtige Umsätze zu 19 % — Bemessungsgrundlage | Base (auto tax) | Σ credit legs on revenue acct with `TaxCategory=S, TaxRatePercent=19` (acct `8400`/`4400`, Kz seam `"81"`) |
| **86** | Steuerpflichtige Umsätze zu 7 % — Bemessungsgrundlage | Base (auto tax) | Σ credit legs on revenue acct `S`/`7%` (acct `8300`/`4300`, Kz `"86"`) |
| **35** | Umsätze zu **anderen** Steuersätzen — Bemessungsgrundlage | Base | Only if non-19/7 rates ever booked; **currently none** — see Coverage gap |
| **36** | Steuer auf Kz 35 (andere Sätze) | Tax (manual) | pairs with Kz 35; none today |
| **41** | Innergemeinschaftliche **Lieferungen** (§4 Nr. 1b) — steuerfrei | Base | Σ credit legs on i.g. revenue acct (`8125`/`4125`, `TaxCategory.K`) — **acct not seeded yet** |
| **89** | Innergemeinschaftliche **Erwerbe** zu 19 % — Bemessungsgrundlage | Base (auto tax → owed USt) | reverse-charge acquisition; **no posting path today** (`Steuerschluessel.InnergemeinschaftlicherErwerb=42` exists but no acct/expense rule) |
| **46** | Leistungen §13b (Leistungsempfänger schuldet USt) — Bemessungsgrundlage | Base | reverse-charge purchase; `ReverseChargeUst19/7 (23/22)`, `ReverseChargeNoTax (20)` exist as keys, but no §13b expense posting rule today |
| **47** | Steuer auf Kz 46 (§13b) | Tax | pairs with Kz 46 |
| **61** | Vorsteuer aus **innergemeinschaftlichen Erwerben** | Tax | deductible counterpart of Kz 89 |
| **66** | **Vorsteuer** aus Rechnungen anderer Unternehmer | Tax | Σ debit legs on Vorsteuer accts (`1576`/`1571`/`1406`/`1401`, Kz seam `"66"`) |
| **83** | **Verbleibende USt-Vorauszahlung / Überschuss** (Zahllast) | Tax (computed) | = (Σ output USt: 81·19% + 86·7% + 36 + i.g.-Erwerb-USt(89·19%) + §13b-USt(47)) − (Σ Vorsteuer: 66 + 61) |

**Kz 83 formula (Zahllast):** `Umsatzsteuer_total − abziehbare_Vorsteuer`, where `Umsatzsteuer_total = Kz81·19% + Kz86·7% + Kz36 + Kz89·19% + Kz47(+other §13b)` and `Vorsteuer_total = Kz66 + Kz61`. Positive = Zahllast, negative = Erstattung/Überschuss.

**2026 form note:** the flat-rate Kz **23 is replaced by the differentiated Kz 500** from the 2026 assessment period (not in the required set, but confirms the form changes yearly — the Kennziffer map MUST be versioned by fiscal year, and the XML namespace is `.../ustva/v2026` with `version="2026"`).

### Coverage: what the Phase-10 posting data can already produce vs what needs work

| Kennziffer | Producible today? | What's missing |
|------------|-------------------|----------------|
| 81, 86 (Umsätze 19/7 base) | ✅ Directly (Soll) | — seeded `UstvaKennziffer` `"81"`/`"86"` already on `8400`/`8300`/`4400`/`4300` |
| 66 (Vorsteuer) | ✅ Directly (Soll) | — seeded `"66"` on `1576`/`1571`/`1406`/`1401` (+`3400`/`4980`/`6300` expense accts) |
| 83 (Zahllast) | ✅ Derived from the above | — pure computation |
| 41 (i.g. Lieferung) | ⚠ Partial | `TaxCategory.K`→acct `8125`/`4125` exists in `SkrMapping` **but those accounts are NOT in the seed JSON** and carry no `UstvaKennziffer="41"`. Must add seed rows + Kennziffer. |
| 35/36 (andere Sätze) | ❌ | No non-19/7 rate is ever booked. Realistically leave at 0 for MVP (note it, don't fabricate). |
| 89 + 61 (i.g. Erwerb + its Vorsteuer) | ❌ | No incoming-i.g.-acquisition posting rule/accounts. `Steuerschluessel=42` enum value exists but nothing books it. |
| 46/47 (§13b Leistungsempfänger) | ❌ | Enum keys `20/22/23/28/29` exist; no §13b **expense/purchase** posting rule or accounts seeded. Outgoing §13b (`SalesDocument.ReverseCharge`) is booked as steuerfrei revenue (no output USt) — that's the *supplier* side, which does NOT populate 46/47 (those are the *recipient's* self-assessed tax). |

**Recommendation for the planner:** Ship **81/86/66/83 fully** (the real-world 95% case for a small-business regelbesteuerter Freiberufler). Ship **41** by extending the seed (add `8125`/`4125` with `UstvaKennziffer="41"`). For **35/36/89/61/46/47**: build the Prüfansicht + XML to *carry* these fields (so the form is complete and valid), compute them from postings where a path exists, and **leave them at 0 with a documented gap** where no posting path exists yet — do NOT fabricate a posting rule for i.g.-Erwerb/§13b-purchases in a *reports* phase (that belongs to the Belege/expense phase). Flag §13b/i.g.-Erwerb incoming as deferred so the numbers are honest rather than silently missing.

### How to compute (Soll, the clean path)

Aggregate over `postings p JOIN journal_entries je JOIN accounts a`, filtered by `je.entry_date` in the Voranmeldungszeitraum, grouped by `a.UstvaKennziffer` + `p.TaxCategory` + `p.TaxRatePercent`:
- **Base amounts (81/86/35/41/89/46):** Σ `p.amount` of the **revenue/base** legs for that Kennziffer, rounded down to whole euros.
- **Tax amounts (66/61/47/36):** Σ `p.amount` of the **VAT** legs (output-USt accts for owed; Vorsteuer accts for 66/61).
- Storno entries (`PostingType.Storno`) net out automatically because a reversal posts the opposite direction on the same accounts within its own `entry_date` period.

Drive it off `Account.UstvaKennziffer` (the seeded seam) as the primary key, cross-checked with `Posting.Steuerschluessel`/`TaxCategory` (defence-in-depth against an account whose Kennziffer was mis-seeded). This satisfies the locked "driven off account + Steuerschlüssel, never re-derived" decision.

## Ist-Versteuerung & EÜR Cash-Basis Derivation (the real design decision)

**The problem, precisely:** Phase-10 books accrual/Soll — output-VAT and revenue land on the invoice's `DocumentDate`; `PaymentPostingSource` books only `Bank↔Forderung` with no tax legs (verified in `src/Numera.Api/Services/PaymentService.cs:300-324` and `Posting/PaymentPostingSource.cs`). So for:
- **Ist-Versteuerung** (VAT due on Zahlungseingang, §20 UStG): summing the output-USt account by `entry_date` gives the **invoice** period, which is wrong.
- **EÜR** (§4(3) EStG Zufluss/Abfluss): Betriebseinnahmen must be recognized when **paid**, not invoiced.

`Gewinnermittlungsart.Euer` implies cash-basis P&L; most EÜR freelancers also elect Ist-Versteuerung — so this is the *common* case, not an edge case. (`Kleinunternehmer §19` files **no USt-VA at all** — gate that off via `CompanyProfile.IsKleinunternehmer` / `SalesDocument.IsKleinunternehmer`; but they still file an EÜR with brutto Betriebsausgaben.)

**Recommended approach — report-time payment attribution (keeps Phase-10 frozen):**

The data to do this cleanly already exists:
- `Payment` → `PaymentAllocation` (`AllocatedAmount` per `OpenItemId`) → `OpenItem` → `SalesDocument` → `SalesDocumentTaxBreakdown` (`TaxCategory`, `VatRatePercent`, `TaxableBase`, `TaxAmount`).
- The payment's recognition date is `Payment.ValueDate` (already the `entry_date` of the payment booking).

For an **Ist** tenant's USt-VA period *P*: for each payment with `ValueDate ∈ P`, split the allocated amount across the invoice's frozen tax breakdown pro-rata (allocation ÷ invoice gross) and recognize the corresponding net→Kz81/86 base and VAT→owed-USt. Vorsteuer (Kz66) similarly follows *paid* supplier invoices. Reversals (`Payment.Amount<0` / `ReversesPaymentId`) net out.

For an **EÜR** report: Betriebseinnahmen = payments received (by `ValueDate`), split net vs vereinnahmte USt (Anlage EÜR Zeile 17) via the same allocation×breakdown attribution; Betriebsausgaben = supplier payments made (Abfluss), net + gezahlte Vorsteuer.

**Alternative (heavier, defer):** introduce "Umsatzsteuer nicht fällig" accounts and reclassify at payment inside `PaymentPostingSource` — proper posting-driven Ist, but a Phase-10 change (seed + posting + migration + new `LedgerSettings`-driven branch). Only choose this if the planner wants USt-VA to remain 100% account-sum-driven for Ist.

**Scope-reduction option:** if the timeline is tight, the planner may scope USt-VA v2.0 to **Soll-Versteuerung only** (the ELSTER default under §16 UStG) and defer Ist — but Pitfall 5 (milestone PITFALLS.md) explicitly flags Ist as a must, so prefer the attribution approach. **Whichever is chosen must be an explicit, documented decision** — this is Open Question #1.

## EÜR (Anlage EÜR) Structure

Cash-basis (Zufluss/Abfluss). Verified against BMF Anlage EÜR 2025 form.

**Betriebseinnahmen (Zeilen ~11–22):**
- Umsatzsteuerpflichtige Betriebseinnahmen (netto) — from paid revenue (Soll tenant: may use accrual revenue; EÜR tenant: paid).
- Umsatzsteuerfreie / nicht steuerbare Betriebseinnahmen.
- **Zeile 17: Vereinnahmte Umsatzsteuer** (+ USt auf unentgeltliche Wertabgaben) — the VAT collected, recognized on Zufluss.
- **Zeile 18: Vom Finanzamt erstattete USt.**
- Summe Betriebseinnahmen (Zeile 22).

**Betriebsausgaben (Zeilen ~23–75):**
- Waren/Roh-/Hilfsstoffe; bezogene Leistungen; Personalkosten; **AfA** (Abschreibungen — note: EÜR is cash-basis *except* durable assets are depreciated, not expensed on payment — out of scope for MVP, flag it); Raumkosten; sonstige unbeschränkt abziehbare Betriebsausgaben.
- **Gezahlte Vorsteuer** (Abfluss).
- **An das Finanzamt gezahlte Umsatzsteuer.**
- Summe Betriebsausgaben.

**Gewinnermittlung:** Betriebseinnahmen (Zeile 71) − Betriebsausgaben (Zeile 72) = Gewinn/Verlust.

**Key EÜR nuances for the planner:**
- **Kleinunternehmer** book Betriebsausgaben **brutto** (no Vorsteuer line); **Regelunternehmer** book **netto** + separate USt lines. Gate on `CompanyProfile.IsKleinunternehmer`.
- The chart of accounts is not itself an EÜR line map — you need an **account→EÜR-line mapping** (like `UstvaKennziffer` but for EÜR). Phase-10 seeded no EÜR mapping field. Recommend adding a lightweight per-account EÜR-line seam (or a checked-in `account→EÜR-Zeile` map keyed by SKR variant) similar to the Kennziffer map. This is net-new mapping data for this phase.
- **Scope:** on-screen + QuestPDF print, in the Anlage-EÜR grouping. No ELSTER XML (annual ESt attachment, out of scope).

## ELSTER USt-VA XML (manual upload, no ERiC)

**Verified facts (elster.de help + ELSTER Anwenderforum):**
- **Manual upload** ("Mein ELSTER → Formular importieren") needs only the **Nutzdaten payload** — the `Anmeldungssteuern` element — **not** the full `Elster`/`TransferHeader`/`DatenTeil` envelope (that envelope is only for ERiC direct send, which is out of scope).
- **Encoding: ISO-8859-15** (mandatory). Declare `<?xml version="1.0" encoding="ISO-8859-15" standalone="no"?>`.
- **Namespace/version:** `xmlns="http://finkonsens.de/elster/elsteranmeldung/ustva/v2026"` with `version="2026"` (namespace + version bump every fiscal year — key it off the report period's year).

**Element shape (from the forum; confirm exact required/optional set against the ERiC v2026 Datensatzbeschreibung):**
```xml
<?xml version="1.0" encoding="ISO-8859-15" standalone="no"?>
<Anmeldungssteuern xmlns="http://finkonsens.de/elster/elsteranmeldung/ustva/v2026" version="2026">
  <Erstellungsdatum>20260410</Erstellungsdatum>
  <DatenLieferant>...</DatenLieferant>          <!-- may be trimmed for upload; portal pre-fills -->
  <Steuerfall>
    <Unternehmer>...</Unternehmer>               <!-- name/address omitted on upload; portal fills -->
    <Umsatzsteuervoranmeldung>
      <Jahr>2026</Jahr>
      <Zeitraum>03</Zeitraum>                    <!-- month 01-12; quarter uses 41-44 -->
      <Steuernummer>...</Steuernummer>           <!-- ELSTER 13-digit Bundesland format -->
      <Kz81>10000</Kz81>                          <!-- base, whole euros -->
      <Kz86>500</Kz86>
      <Kz66>380.80</Kz66>                         <!-- tax, 2 decimals -->
      <Kz83>1550.20</Kz83>
      <!-- ...only the Kz present in this filing... -->
    </Umsatzsteuervoranmeldung>
  </Steuerfall>
</Anmeldungssteuern>
```
- **Kz value formatting:** base amounts = integer euros (truncated, no decimals); tax amounts = decimal with 2 places, `.` decimal separator. Only emit Kz that are non-empty (empty Kz must be omitted, not zero-filled — verify against XSD).
- **Steuernummer gotcha:** `CompanyProfile.TaxNumber` (BT-32) is stored in the local/print format (e.g. `151/815/08154`). ELSTER requires the **13-digit vereinheitlichtes Steuernummer** per Bundesland. A conversion (Bundesland prefix + reformatting) is required — flag as an implementation task and a test case. `VatId` (USt-IdNr) is NOT the Steuernummer.

**Approach:** hand-build with `XmlWriter`/`XDocument`, exactly like the Phase-5 XRechnung/ZUGFeRD XML was hand-produced (see `src/modules/Numera.Modules.Sales/EInvoice/ZugferdGenerator.cs`). No maintained .NET ELSTER-XML library exists; do not add one. **Download the ERiC release `ERiC-*-Dokumentation.zip` → `Dokumentation/Schnittstellenbeschreibungen`** for the authoritative v2026 XSD to validate against (doc only; do not ship the ERiC binary).

**Print export:** mirror `DocumentPdfService` + an `IDocument` (like `InvoiceDocument`) — a UStVA form-style QuestPDF page listing each Kz with its value. Store bytes like `document_render` if persisting.

## Prüfansicht (review view)

The review screen (success criterion 2) must let the user sanity-check before manual ELSTER upload:
- One row per relevant Kennziffer: `Kz | Bezeichnung | Bemessungsgrundlage | Steuer`, plus the computed **Kz 83 Zahllast/Erstattung**.
- **Drill-down** from each Kz to the contributing `journal_entries`/`postings` (reuse the `LedgerEndpoints` journal read filtered by the accounts/period that feed that Kz) — so the user can trace a number back to the bookings.
- Show the tenant's `Besteuerungsart` (Ist/Soll) and the Voranmeldungszeitraum, and a warning if the period is **not yet festgeschrieben** (see below).
- Frontend: new `web/src/features/` feature (e.g. `reports` or `ustva`) + a page under `web/src/pages`; data via TanStack Query against new `/api/reports/...` endpoints. Follow the existing feature-folder + `lib/api` conventions.

## Period / Festschreibung Interaction

- USt-VA is per **Voranmeldungszeitraum** — monthly by default, quarterly if turnover is low; annual/Dauerfristverlängerung + Sondervorauszahlung (Kz 38/39) exist (model the filing calendar per tenant; MVP can assume monthly). `Zeitraum` codes: months `01`–`12`, quarters `41`–`44`.
- Phase-10 `FiscalPeriod` locking is **per calendar month** (`FestschreibungService.LockPeriodAsync(year, month)`), which sets `Status=Locked`, stamps gapless `JournalNumber`, `FestgeschriebenAt`, and `PeriodId`. A period lock hard-blocks later bookings into that month (the Phase-10 period-lock trigger).
- **Recommendation:** generating/finalizing a USt-VA for a period should **encourage (and optionally require) Festschreibung** of that period's months first — this is the GoBD-correct sequence (report the numbers, then freeze so they can't drift; a late booking into a filed period would otherwise silently invalidate the declaration → Pitfall 3). Offer "Zeitraum festschreiben & USt-VA erzeugen" as one flow. A quarterly filing must lock all three months.
- The `AccountStatementAsync` opening-balance logic already only counts **locked** periods (`period.Status == Locked`) — mirror that "only-locked-is-final" stance in reports, or clearly badge a report as "vorläufig (Zeitraum noch nicht festgeschrieben)".
- If persisting a `ust_va_filing` record: once a filing is marked submitted, treat it append-only (RLS + immutability trigger, copy the Phase-10 `journal_entries_immutable` template); a correction is a **berichtigte Anmeldung** (new filing referencing the prior), never an edit.

## Data Source & Correctness

- All report reads are **RLS-scoped aggregations** — reuse `NumeraDbContext` raw `SqlQuery` (see `LedgerEndpoints.GetJournalAsync`/`GetAccountStatementAsync`); RLS auto-applies via `TenantConnectionInterceptor`. Any Hangfire-deferred generation must `SetTenant`.
- **Which date drives the period:** Soll → `JournalEntry.EntryDate` (= invoice `DocumentDate`); Ist → `Payment.ValueDate`. Read `LedgerSettings.Besteuerungsart` and branch. (`FiscalYearStartMonth` matters for EÜR annual boundaries.)
- **Kleinunternehmer (§19):** `IsKleinunternehmer` ⇒ **no USt-VA** (return zero / hide the report) but **still an EÜR** (brutto Betriebsausgaben). Verify from `CompanyProfile.IsKleinunternehmer` (tenant-level) and `SalesDocument.IsKleinunternehmer` (frozen per-doc).
- **Money:** decimal end-to-end; base amounts truncated to whole euros only at the *display/XML* boundary, never mid-aggregation. Use `RoundingPolicy` consistent with the booking to avoid cent drift between the ledger and the report.
- **Reverse-charge output** (`SalesDocument.ReverseCharge=true`): booked as steuerfrei revenue with no output-USt leg — belongs in the steuerfreie-Umsätze section (and, for the *supplier*, the §18b/other lines), NOT in Kz 46/47 (those are the *recipient's* self-assessed tax).

## Architecture Patterns

### Recommended structure (net-new)
```
src/modules/Numera.Modules.Ledger/Reporting/     (or a new Numera.Modules.Reporting)
├── UstVaCalculator.cs           # postings/payments → Kennziffer values (Soll + Ist branches)
├── UstVaKennzifferMap.cs        # per-fiscal-year Kz map (versioned data)
├── EuerCalculator.cs            # cash-basis Betriebseinnahmen/-ausgaben
├── EuerLineMap.cs               # account → Anlage-EÜR line (per SKR variant)
├── Elster/UstVaXmlWriter.cs     # ISO-8859-15 Anmeldungssteuern Nutzdaten
└── (optional) UstVaFiling.cs    # persisted filing (RLS + immutable-on-submit)
src/Numera.Api/
├── Endpoints/ReportEndpoints.cs # /api/reports/ustva, /api/reports/euer (+ Prüfansicht drill-down)
├── Pdf/UstVaDocument.cs, EuerDocument.cs   # QuestPDF IDocument, mirror InvoiceDocument
web/src/features/ustva/ (or reports/) + web/src/pages/...   # Prüfansicht + EÜR view
```

### Pattern: RLS-scoped aggregation read (copy from LedgerEndpoints)
```csharp
// Source: src/Numera.Api/Endpoints/LedgerEndpoints.cs — raw SqlQuery over postings/journal_entries,
// FILTER (WHERE p.direction = 1/2) for Soll/Haben sums, RLS auto-applied. Mirror for Kennziffer sums:
//   SELECT a.ustva_kennziffer, p.tax_category, p.tax_rate_percent,
//          SUM(p.amount) FILTER (WHERE p.direction = <base/tax side>) ...
//   FROM postings p JOIN journal_entries je ... JOIN accounts a ...
//   WHERE je.entry_date BETWEEN <from> AND <to> GROUP BY 1,2,3
```

### Anti-Patterns to Avoid
- **Re-deriving VAT rates at report time.** Use the stored `Posting.Steuerschluessel`/`TaxRatePercent`/`TaxCategory` + `Account.UstvaKennziffer`. (Report-time *period attribution* for Ist/EÜR still uses the frozen `SalesDocumentTaxBreakdown` — that is not re-derivation, it's re-timing.)
- **Summing accounts by `entry_date` for an Ist tenant / EÜR income.** Wrong period. Use payment attribution (or the nicht-fällig accounts if you took that route).
- **Hardcoding one year's Kennziffer set / namespace.** Version by fiscal year (`ustva/v2026`, Kz 500 vs 23).
- **Building the full `Elster`/`TransferHeader` envelope.** Manual upload needs only `Anmeldungssteuern`.
- **Emitting a USt-VA for a Kleinunternehmer.** Gate it off.
- **Treating a submitted filing as mutable.** Berichtigung = new filing.

## Don't Hand-Roll

| Problem | Don't build | Use instead | Why |
|---------|-------------|-------------|-----|
| RLS/tenant scoping on report reads | Manual `SET`/filters | `TenantConnectionInterceptor` + raw `SqlQuery` (LedgerEndpoints pattern) | Already pool-safe, proven |
| Money rounding for Kennziffer/EÜR sums | Custom rounding | `RoundingPolicy` (same as booking) | Cent-exact tie-out to the ledger |
| PDF report layout | New report/charting lib | QuestPDF `IDocument` + `DocumentPdfService` render-and-store | Locked decision; identical code path to invoices |
| VAT breakdown per invoice | Recompute from lines | Frozen `SalesDocumentTaxBreakdown` | GoBD-frozen source of truth |
| ELSTER envelope/transport | ERiC / full envelope | Nutzdaten-only `Anmeldungssteuern` XML for manual upload | ERiC is out of scope; upload path needs only the payload |

## Common Pitfalls

### Pitfall 1: Ist/EÜR period timing (the big one)
**What goes wrong:** Ist-Versteuerung USt-VA and EÜR income computed by summing accounts by booking date → every period is wrong because VAT/income is booked at invoice but realized at payment.
**How to avoid:** branch on `LedgerSettings.Besteuerungsart`/`Gewinnermittlungsart`; for Ist/EÜR, attribute via `PaymentAllocation → SalesDocument → SalesDocumentTaxBreakdown` at `Payment.ValueDate`. Test an Ist tenant whose USt-VA must move with payment, not invoice.
**Warning signs:** an Ist tenant's Kz81 changes when an invoice is issued rather than paid.

### Pitfall 2: Kennziffer coverage gaps presented as zeros silently
**What goes wrong:** 35/36/89/61/46/47 have no posting path today; a report that silently shows 0 hides missing data.
**How to avoid:** compute what's producible (81/86/66/83, +41 after seed), and explicitly mark unproducible Kz as "kein Buchungspfad in v2.0" rather than a confident 0. Defer i.g.-Erwerb/§13b-purchase posting to the Belege/expense phase.

### Pitfall 3: Late booking into a filed period (GoBD)
**What goes wrong:** a booking dropped into an already-filed month silently invalidates the declaration.
**How to avoid:** tie USt-VA generation to Festschreibung of the period; badge un-festgeschriebene reports "vorläufig"; corrections = berichtigte Anmeldung.

### Pitfall 4: Steuernummer format for ELSTER
**What goes wrong:** `CompanyProfile.TaxNumber` local format rejected by ELSTER (needs 13-digit vereinheitlicht per Bundesland); or `VatId` used by mistake.
**How to avoid:** convert to the ELSTER format; test per Bundesland; validate against the XSD.

### Pitfall 5: Kennziffer map / namespace not versioned by year
**What goes wrong:** 2026 form changes (Kz 23→500) break next year.
**How to avoid:** version the Kz map and `ustva/vYYYY` namespace by the period's fiscal year.

### Pitfall 6: New report tables without RLS
**What goes wrong:** a `ust_va_filing` (or EÜR snapshot) table leaks cross-tenant.
**How to avoid:** RLS ENABLE+FORCE+`tenant_isolation` policy in the migration (copy Phase-10/Payments template) + add to the cross-tenant test suite; immutability trigger once submitted.

## Code Examples

### Kennziffer aggregation (Soll) — mirror LedgerEndpoints raw SQL
```sql
-- Source pattern: src/Numera.Api/Endpoints/LedgerEndpoints.cs (GetJournalAsync)
SELECT a.ustva_kennziffer                                   AS "Kennziffer",
       p.tax_category                                       AS "TaxCategory",
       p.tax_rate_percent                                   AS "Rate",
       COALESCE(SUM(p.amount), 0)                           AS "Amount"
  FROM postings p
  JOIN journal_entries je ON je.id = p.journal_entry_id
  JOIN accounts a         ON a.id = p.account_id
 WHERE je.entry_date >= {from} AND je.entry_date <= {to}
   AND a.ustva_kennziffer IS NOT NULL
 GROUP BY a.ustva_kennziffer, p.tax_category, p.tax_rate_percent
-- RLS auto-scopes to the tenant via TenantConnectionInterceptor.
```

### Ist / EÜR attribution source (frozen, re-timed to payment)
```csharp
// PaymentAllocation.AllocatedAmount (per OpenItemId) → OpenItem.DocumentId → SalesDocument
//   → SalesDocumentTaxBreakdown (TaxCategory, VatRatePercent, TaxableBase, TaxAmount).
// Recognize in the period of Payment.ValueDate, pro-rata = AllocatedAmount / SalesDocument.TotalGross.
// Reversals (Payment.Amount < 0 / ReversesPaymentId) net out in their own ValueDate period.
```

## State of the Art

| Old | Current (2026) | Impact |
|-----|----------------|--------|
| UStVA Kz 23 (pauschal) | **Kz 500** (differenziert) from 2026 | Confirms yearly form changes → version the map |
| ERiC direct send | Out of scope | Manual `Anmeldungssteuern` XML upload only |
| Full `Elster` envelope | Nutzdaten-only upload | Smaller, simpler XML to build |

## Open Questions

1. **Ist-Versteuerung derivation approach (BLOCKING DECISION).** Report-time payment attribution (recommended, keeps Phase-10 frozen) vs "USt nicht fällig" posting-model change vs Soll-only scope for v2.0. Pitfalls.md says Ist is a must → recommend attribution. Planner must decide explicitly.
2. **EÜR account→line mapping.** No EÜR-line seam was seeded in Phase-10 (only `UstvaKennziffer`). Add a per-account EÜR-line field or a checked-in `account→Zeile` map per SKR variant. Also decide AfA handling (depreciation of durable assets breaks pure cash-basis) — recommend flag/defer AfA for MVP.
3. **Persist filings?** Recommend a `ust_va_filing` table (period, Kz snapshot JSON, XML bytes, status, `SubmittedAt`) with RLS + immutable-on-submit, so re-generation is idempotent and berichtigte Anmeldungen are traceable. Confirm scope.
4. **Exact v2026 XSD element set.** Pull `ERiC-*-Dokumentation.zip` → Schnittstellenbeschreibungen to confirm required/optional Kz, `DatenLieferant` trimming for upload, and empty-Kz omission rules. MEDIUM confidence until verified.
5. **Steuernummer→ELSTER 13-digit conversion.** Where to store/convert (per Bundesland). New field on `CompanyProfile` or a converter + validation.
6. **Coverage of 35/36/89/61/46/47.** Confirm it's acceptable to ship these as structurally-present-but-zero with a documented gap (no posting path in v2.0), deferring incoming i.g.-Erwerb/§13b to the Belege/expense phase.
7. **Kleinunternehmer EÜR-only** and **quarterly/Dauerfristverlängerung** filing calendar: confirm MVP assumes monthly + full-Regelbesteuerung, quarterly as a fast-follow.

## Sources

### Primary (HIGH — read from the repo this session)
- `src/modules/Numera.Modules.Ledger/{Account,JournalEntry,Posting,LedgerSettings,FiscalPeriod,Steuerschluessel}.cs` — exact report data model
- `src/modules/Numera.Modules.Ledger/Posting/{PostingEngine,InvoicePostingSource,PaymentPostingSource,ExpensePostingSource,AccountResolver}.cs` — **payment legs carry no tax metadata (the Ist/EÜR gap)**
- `src/modules/Numera.Modules.Ledger/Seed/{SkrMapping.cs,ChartSeeder.cs,skr03.accounts.json}` — seeded `UstvaKennziffer` `81/86/66`; `8125`/`4125` (Kz 41) NOT seeded
- `src/Numera.Api/Endpoints/LedgerEndpoints.cs` — RLS-scoped raw-SQL aggregation + keyset read pattern to mirror
- `src/Numera.Api/Services/{FestschreibungService,PaymentService}.cs`, `src/Numera.Api/Endpoints/SalesDocumentEndpoints.cs` (PostInvoice/Storno) — period lock + posting hooks + entry-date sources
- `src/Numera.Api/Services/DocumentPdfService.cs`, `src/modules/Numera.Modules.Sales/Pdf/InvoiceDocument.cs` — QuestPDF render-and-store pattern to mirror
- `src/modules/Numera.Modules.Sales/CompanyProfile.cs` — `TaxNumber` (BT-32) / `VatId` (BT-31) / `IsKleinunternehmer`
- `.planning/research/{SUMMARY,STACK,PITFALLS}.md` + `.planning/phases/10-buchhaltungs-fundament/10-RESEARCH.md` — locked decisions + foundation

### Secondary (HIGH/MEDIUM — verified this session)
- [BMF: Vordruckmuster USt-Voranmeldung 2026](https://www.bundesfinanzministerium.de/Content/DE/Downloads/BMF_Schreiben/Steuerarten/Umsatzsteuer/2025-12-29-vordruckmuster-USt-voranmeldung-2026.pdf) — authoritative 2026 form
- [sevdesk: Kennzahlen der Umsatzsteuervoranmeldung](https://hilfe.sevdesk.de/de/articles/9886922-kennzahlen-der-umsatzsteuervoranmeldung) — Kz 81/86/35/36/41/46/47/66/61/89 meanings + base-vs-tax
- [ELSTER Hilfe: UStVA XML-Upload](https://www.elster.de/eportal/helpGlobal?themaGlobal=ustva_upload) — Nutzdaten-only, ISO-8859-15, `Anmeldungssteuern` path
- [ELSTER Anwenderforum: aktuelles UStVA XML-Format](https://forum.elster.de/anwenderforum/forum/elster-webanwendungen/mein-elster/425731-umsatzsteuervoranmeldung-aktuelles-xml-format) — element hierarchy (`Anmeldungssteuern/Steuerfall/Umsatzsteuervoranmeldung/Kz*`, namespace/version)
- [BMF: Anlage EÜR 2025](https://www.bundesfinanzministerium.de/Content/DE/Downloads/BMF_Schreiben/Steuerarten/Einkommensteuer/2025-08-29-anlage-EUER-2025.pdf) + [sevdesk Anlage EÜR](https://sevdesk.de/ratgeber/buchhaltung-finanzen/euer/anlage-euer/) — Betriebseinnahmen (Z.11-22, vereinnahmte USt Z.17), Betriebsausgaben, Kleinunternehmer brutto
- [ebnerstolz / int-acc: UStVA 2026 Änderungen (Kz 23→500)](https://www.ebnerstolz.de/de/unser-angebot/leistungen/steuerberatung/umsatzsteuer/vordruckmuster-umsatzsteuervoranmeldung-2026-100714.html) — yearly form change confirmation

### Tertiary (MEDIUM/LOW — confirm during planning)
- Exact `ustva/v2026` XSD element set + empty-Kz rules → pull ERiC `ERiC-*-Dokumentation.zip` / Schnittstellenbeschreibungen (not fetched this session)
- Steuernummer→ELSTER 13-digit conversion per Bundesland (known requirement; exact rules to verify)

## Metadata

**Confidence breakdown:**
- Codebase data model / posting seams / the Ist-EÜR gap: HIGH — read from source
- USt-VA Kennziffer meanings + EÜR structure: HIGH — BMF/DATEV/sevdesk verified
- ELSTER XML shape (namespace, encoding, Nutzdaten-only upload): MEDIUM-HIGH — elster.de + forum; exact v2026 element list MEDIUM until ERiC doc pulled
- Recommended Ist/EÜR derivation approach: HIGH on the *problem*, MEDIUM on the *chosen solution* (a real planner decision)

**Research date:** 2026-08-03
**Valid until:** ~2026-09-03 for codebase facts (until Ledger changes); UStVA form/XSD facts are fiscal-year-versioned — re-verify for any year ≠ 2026.
