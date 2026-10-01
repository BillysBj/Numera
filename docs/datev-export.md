# DATEV export and dunning downloads

`GET /api/ledger/datev?from=YYYY-MM-DD&to=YYYY-MM-DD` requires authentication,
the `RequireOwner` policy and the existing `DataExport` capability. All journal,
posting, account and document reads retain the tenant query filters and database RLS.
Dates are inclusive. Missing settings/chart return HTTP 409 with
`Kontenrahmen ist nicht eingerichtet`. Reversed ranges or ranges spanning fiscal
years return HTTP 422: a DATEV batch carries only one fiscal-year start.

The response is `text/csv; charset=windows-1252`, without a BOM, with CRLF records,
semicolon delimiters, double-quoted text and escaped embedded quotes. Decimal
amounts use two decimal places, midpoint rounding away from zero, comma decimal
separators and no grouping. Fields containing line breaks are flattened to one line.

The EXTF metadata has identifier `EXTF`, header version 700, category 21, format
`Buchungsstapel`, format version 7, EUR and Festschreibung 0. Both SKR03 and SKR04
use Sachkontennummernlänge 4. Wirtschaftsjahresbeginn is derived from the selected
range and `FiscalYearStartMonth`; Von/Bis use the supplied dates. Beraternummer and
Mandantennummer default to **0**, since Numera does not store these identifiers.
The receiving Steuerberater must assign the actual client during import.

## Posting mapping with automatic VAT

Each DATEV row represents a booking, not an individual ledger leg:

- Sales invoices: one row per revenue leg/rate. Konto is the debtor, Gegenkonto
  is the revenue account, Umsatz is net plus the matching recorded output VAT,
  direction is `S`, and BU-Schlüssel is the revenue leg's frozen tax key (3 for
  19%, 2 for 7%). Explicit output-VAT legs are not emitted. The rows sum to the
  invoice gross. For example, net 100 at 19% and net 200 at 7% become rows for
  119,00 and 214,00, totaling 333,00.
- Expenses/receipts: one row per expense leg/rate. Konto is the expense account,
  Gegenkonto the creditor, Umsatz is net plus matching recorded input VAT,
  direction is `S`, and BU-Schlüssel is the input-VAT key (9 for 19%, 8 for 7%).
  Explicit input-VAT legs are not emitted.
- Reversed invoice/expense shapes retain these account assignments and gross
  amounts, with direction `H` instead of `S`.
- Two-leg payments: one row from debit account to credit account, the payment
  amount, direction `S`, and no BU key. Payment reversals swap the accounts.
- Other shapes: one row per non-VAT debit leg, against the largest non-VAT credit
  leg (stable account-number/id tie breakers), using the debit amount and no BU
  key. This fallback does not allocate complex split credits. Pure VAT-account
  legs that cannot be folded through a supported BU key are skipped; arbitrary
  manual VAT adjustments therefore need separate handling by the Steuerberater.

Account types distinguish revenue/expense legs from balance-sheet legs. The
shared SKR03/04 mappings distinguish pure input/output VAT accounts from other
assets/liabilities; names are not used. VAT is matched by frozen rate, category,
direction and account type. Repeated net legs at one rate share that rate's
recorded tax proportionally, with the remainder assigned to the final leg, so
tax is included exactly once. No tax is invented for exempt, Kleinunternehmer
or reverse-charge-without-tax postings. Null/None tax keys stay empty.

Account numbers are padded to the charts' four-digit Sachkonto length; longer
debtor/creditor overrides and existing leading zeroes are preserved. Belegfeld 1
resolves sales document number, receipt invoice number or payment reference,
then falls back to journal number/source reference. Buchungstext uses the journal
description (Posting has no description property).

Confirm the final account/BU mapping with the Steuerberater. An actual DATEV
import has not been verified. Encoding, headers, Von/Bis and field order are unchanged.

## Exact data-row field order

All rows and the column header have 116 fields. Fields 4–6, 12–13 and 15–116 are
empty. Numeric account/tax fields are unquoted; textual values are double-quoted.

| Position | Field |
| --- | --- |
| 1 | Umsatz (ohne Soll/Haben-Kz) |
| 2 | Soll/Haben-Kennzeichen |
| 3 | WKZ Umsatz |
| 4 | Kurs |
| 5 | Basis-Umsatz |
| 6 | WKZ Basis-Umsatz |
| 7 | Konto |
| 8 | Gegenkonto (ohne BU-Schlüssel) |
| 9 | BU-Schlüssel |
| 10 | Belegdatum (DDMM) |
| 11 | Belegfeld 1 |
| 12 | Belegfeld 2 |
| 13 | Skonto |
| 14 | Buchungstext |
| 15 | Postensperre |
| 16 | Diverse Adressnummer |
| 17 | Geschäftspartnerbank |
| 18 | Sachverhalt |
| 19 | Zinssperre |
| 20 | Beleglink |
| 21–36 | Beleginfo - Art 1, Beleginfo - Inhalt 1, …, Beleginfo - Art 8, Beleginfo - Inhalt 8 (alternating pairs) |
| 37 | KOST1 - Kostenstelle |
| 38 | KOST2 - Kostenstelle |
| 39 | Kost-Menge |
| 40 | EU-Land u. UStID |
| 41 | EU-Steuersatz |
| 42 | Abw. Versteuerungsart |
| 43 | Sachverhalt L+L |
| 44 | Funktionsergänzung L+L |
| 45 | BU 49 Hauptfunktionstyp |
| 46 | BU 49 Hauptfunktionsnummer |
| 47 | BU 49 Funktionsergänzung |
| 48–87 | Zusatzinformation - Art 1, Zusatzinformation - Inhalt 1, …, Zusatzinformation - Art 20, Zusatzinformation - Inhalt 20 (alternating pairs) |
| 88 | Stück |
| 89 | Gewicht |
| 90 | Zahlweise |
| 91 | Forderungsart |
| 92 | Veranlagungsjahr |
| 93 | Zugeordnete Fälligkeit |
| 94 | Skontotyp |
| 95 | Auftragsnummer |
| 96 | Buchungstyp |
| 97 | USt-Schlüssel (Anzahlungen) |
| 98 | EU-Land (Anzahlungen) |
| 99 | Sachverhalt L+L (Anzahlungen) |
| 100 | EU-Steuersatz (Anzahlungen) |
| 101 | Erlöskonto (Anzahlungen) |
| 102 | Herkunft-Kz |
| 103 | Leerfeld |
| 104 | KOST-Datum |
| 105 | SEPA-Mandatsreferenz |
| 106 | Skontosperre |
| 107 | Gesellschaftername |
| 108 | Beteiligtennummer |
| 109 | Identifikationsnummer |
| 110 | Zeichnernummer |
| 111 | Postensperre bis |
| 112 | Bezeichnung SoBil-Sachverhalt |
| 113 | Kennzeichen SoBil-Buchung |
| 114 | Festschreibung |
| 115 | Leistungsdatum |
| 116 | Datum Zuord. Steuerperiode |

## Dunning artifacts

`GET /api/dunning/notices?page=1&pageSize=25` returns `{ items, total }`, newest
creation first with id as a stable tie breaker; page size is clamped to 1–100.
Each item contains `id`, `documentNumber`, frozen `recipient`, numeric `level`,
`issuedOn`, `fee`, `totalToPay`, `currency` and numeric dispatch `status`
(0 pending, 1 sent, 2 failed). The list does not fetch PDF blobs.

`GET /api/dunning/notices/{id}/pdf` shares `DunningNoticePdfService` with the email
job. It returns stored bytes unchanged, otherwise renders/stores the notice under
RLS. Concurrent renders use a conditional update and reload the winning artifact.
Both endpoints require authentication and the existing Dunning capability.

New notices freeze the PDF during issue, before enqueueing email, using invoice
snapshots and the notice's persisted claims. No schema migration is required.
Legacy notices with no PDF still use the then-current level template/logo, as the
old email renderer did: historic template text was never persisted, so it cannot
be recovered. Existing stored PDFs are always preserved. Render-if-absent storage,
concurrent PostgreSQL requests, actual RLS policies and email delivery require a
live integration environment to verify; no database migrations were run.

## Changed files

Backend:

- `src/Numera.Api/Contracts/DunningContracts.cs`
- `src/Numera.Api/Endpoints/DunningEndpoints.cs`
- `src/Numera.Api/Endpoints/LedgerEndpoints.cs`
- `src/Numera.Api/Jobs/SendDunningNoticeJob.cs`
- `src/Numera.Api/Program.cs`
- `src/Numera.Api/Services/DunningNoticePdfService.cs` (new)
- `src/Numera.Api/Services/DatevExportService.cs` (new)
- `src/modules/Numera.Modules.Ledger/DatevExport.cs` (new)

Frontend:

- `web/src/App.tsx`
- `web/src/features/dunning/DunningNoticesList.tsx` (new)
- `web/src/features/openItems/OpenItemsListPage.tsx`
- `web/src/features/reports/DatevExportPage.tsx` (new)
- `web/src/features/shared/UpgradeHint.tsx`
- `web/src/lib/api/dunning.ts`
- `web/src/lib/api/ledger.ts`
- `web/src/lib/saveBlob.ts` (new)

Tests and documentation:

- `tests/Numera.Platform.Tests/Ledger/DatevExportTests.cs` (new)
- `tests/Numera.Platform.Tests/Ledger/DatevBookingMappingTests.cs` (new; real posting-source fixtures for both charts)
- `tests/Numera.Platform.Tests/Sales/DunningNoticePdfTests.cs` (new)
- `tests/Numera.IntegrationTests/DunningRunTests.cs` (shared service registration)
- `tests/Numera.IntegrationTests/TeamManagementTests.cs` (existing compile error: compare `InviteResult.UserId`)
- `web/src/features/dunning/DunningConfigSettingsPage.test.tsx`
- `web/src/lib/api/downloads.test.ts` (new)
- `docs/datev-export.md` (new)

## Verification

- `C:/Users/Admin/AppData/Local/Microsoft/dotnet/dotnet.exe build Numera.sln -c Release --no-restore`: passed, no warnings/errors.
- `C:/Users/Admin/AppData/Local/Microsoft/dotnet/dotnet.exe test tests/Numera.Platform.Tests/Numera.Platform.Tests.csproj -c Release --no-restore`: 179 passed, 0 failed/skipped, including 20 new booking-mapping cases. This also builds all API project references in Release.
- In `web`, `npm run build` (Windows `npm.cmd` shim on the final run): passed; Vite warns about a bundle exceeding 500 kB.
- In `web`, `npm.cmd test`: 66 passed across 8 files.
- `git diff --check`: passed.
- Database integration tests were compiled but not executed because their fixtures apply migrations. No database migrations, commits or pushes were run. Live UI interaction and actual DATEV import were not exercised.
