---
phase: 11-berichte-ust-voranmeldung
plan: 04
subsystem: reporting
tags: [elster, ustva, xml, steuernummer, ef-core]

requires:
  - phase: 11-02
    provides: computed USt-VA report lines and fiscal-year Kennziffer map
provides:
  - "Strict Landes-format Steuernummer to 13-digit ELSTER conversion for all 16 Länder"
  - "Nullable CompanyProfile Bundesland source with additive EF migration"
  - "ISO-8859-15 bare Anmeldungssteuern USt-VA Nutzdaten writer"
  - "Pure XML shape, omission, formatting, and conversion tests"
affects: [ustva-export, reporting-endpoints, company-profile]

tech-stack:
  added: []
  patterns:
    - "Pure XmlWriter bytes-out export without DB or ERiC transport"
    - "Fiscal-year-keyed ELSTER namespace and version"
    - "Additive Kennziffer serialization from report lines only"

key-files:
  created:
    - src/Numera.Api/Reporting/Elster/SteuernummerConverter.cs
    - src/Numera.Api/Reporting/Elster/UstVaXmlWriter.cs
    - src/platform/Numera.Platform.Db/Migrations/20260804085356_CompanyProfileBundesland.cs
    - src/platform/Numera.Platform.Db/Migrations/20260804085356_CompanyProfileBundesland.Designer.cs
    - tests/Numera.IntegrationTests/UstVaXmlWriterTests.cs
    - .planning/phases/11-berichte-ust-voranmeldung/11-04-SUMMARY.md
  modified:
    - src/modules/Numera.Modules.Sales/CompanyProfile.cs
    - src/platform/Numera.Platform.Db/Migrations/NumeraDbContextModelSnapshot.cs

key-decisions:
  - "Persist Bundesland as a nullable enum/integer so existing profiles migrate additively"
  - "Reject null/empty Steuernummern, VAT-ID-shaped values, unsupported characters/shapes, and missing/invalid Bundesland values"
  - "Reject XML generation for Kleinunternehmer instead of emitting an empty VAT payload"
  - "Omit a Kz element when its report line has neither a base nor tax value; never emit an empty element"
  - "Truncate bases to integer euros and format tax values with exactly two invariant decimal places"

completed: 2026-08-04
---

# Phase 11-04: ELSTER USt-VA XML export

**Numera can now serialize a computed USt-VA into a bare ISO-8859-15 `Anmeldungssteuern` payload and normalize a profile's local Steuernummer to ELSTER's 13-digit format.**

## Accomplishments

- Added a `Bundesland` enum covering all 16 German Länder and a nullable `CompanyProfile.Bundesland` property.
- Added strict per-Land Steuernummer normalization using the Bundesfinanzamtsnummer prefix, including Hessen's commonly printed leading office zero and Nordrhein-Westfalen's distinct `FFF/BBBB/UUUP` layout.
- Explicitly reject missing tax numbers, country-prefixed/USt-IdNr-like values such as `DE...`, invalid characters, wrong state-specific digit counts, and missing/invalid state values.
- Added the additive `CompanyProfileBundesland` migration. Its `Up` contains only `AddColumn<int>("bundesland", "company_profile", nullable: true)`; inherited RLS is unchanged and no Phase-10 ledger table is touched.
- Added `UstVaXmlWriter.Write(UstVaReport, CompanyProfile) : byte[]` using `XmlWriter` and ISO-8859-15.
- Emit the bare fiscal-year-keyed `Anmeldungssteuern/Steuerfall/Umsatzsteuervoranmeldung` Nutzdaten hierarchy without an `Elster`/`TransferHeader` envelope.
- Emit only supplied report lines with values. Base values are integer euros; tax values (including Kz83) are invariant two-decimal values.
- Throw for Kleinunternehmer because they do not file a USt-VA; the later endpoint gate remains defence in depth.
- Added pure tests for root/namespace/version/declaration, additive Kz omission, numeric formatting, known Bayern conversion, common state-layout conversions, and VAT-ID rejection.

## Verification

- Pinned SDK check: `& "C:\Users\Admin\AppData\Local\Microsoft\dotnet\dotnet.exe" --version` returned `10.0.301`.
- Exact final build command: `& "C:\Users\Admin\AppData\Local\Microsoft\dotnet\dotnet.exe" build --configuration Release --no-restore`.
- Result: successful full-solution Release build, including `Numera.Api` and `Numera.IntegrationTests`; **0 warnings, 0 errors**.
- Migration generated successfully with `& "C:\Users\Admin\AppData\Local\Microsoft\dotnet\dotnet.exe" ef migrations add CompanyProfileBundesland --project src/platform/Numera.Platform.Db --startup-project src/Numera.Api --configuration Release --no-build` after the prescribed no-flag invocation attempted an unmandated Debug build and failed without diagnostics.
- EF emitted a tooling-version warning (`tools 10.0.3`, runtime `10.0.10`); migration generation still completed successfully.
- Integration tests were compiled but not run, per explicit instruction.
- No `Pdf/*.cs` file was touched. No file was staged or committed.

## Deviations and uncertainties

- **MEDIUM confidence / official ERiC v2026 Datensatzbeschreibung not available locally:** the payload deliberately implements the plan's minimal element set and omits `DatenLieferant` and `Unternehmer`. Phase research says Mein-ELSTER can supply those for manual upload, but this was not validated against the authoritative `ustva/v2026` XSD in this environment. If that XSD marks either element required, they must be added before claiming schema validation.
- **MEDIUM confidence / Kz element set:** the writer serializes exactly the Kz lines present in `UstVaReport`; it does not hardcode or fabricate the full official 2026 Kennziffer set. This matches locked D3 and is additive, but the result was not XSD-validated against the official ERiC package.
- The implemented 16-Länder conversion table follows the established Bundesfinanzamtsnummer/local-layout rules and validates digit shapes, but does not validate the Steuernummer's mathematical check digit. Check-digit validation was not required by plan 11-04.
- No behavioral deviation from the requested encoding, namespace/version, period, Steuernummer, Kz omission, or number-format rules.

---
*Phase: 11-berichte-ust-voranmeldung*
*Completed: 2026-08-04*
