---
phase: 12-belege-ausgaben
plan: 03
subsystem: receipt-extraction-scanning
tags: [dotnet, azure-document-intelligence, clamav, receipts, dependency-injection]

requires:
  - phase: 12-belege-ausgaben
    plan: 02
    provides: Receipt aggregate with decimal extraction fields
provides:
  - "Provider-neutral receipt extraction port with per-field confidence and a zero-cloud stub default"
  - "Opt-in Azure AI Document Intelligence prebuilt-invoice adapter with decimal money boundary"
  - "Attachment scanning port with ClamAV INSTREAM adapter and EICAR-aware test double"
  - "Configuration-driven API dependency injection for both provider seams"
affects: [12-04-ingest, 12-05-confirm-book, 12-06-email-intake]

completed_tasks: [1, 2, 3]
pending_tasks: []
completed: 2026-08-04
---

# Phase 12-03: Receipt extraction and attachment scanning ports

Plan 12-03 is implemented. Receipt OCR and malware scanning now sit behind swappable module
ports. The shipping OCR default remains manual-entry/zero-cloud, while Azure Document
Intelligence and ClamAV activate only through explicit configuration.

## Implemented

- Added `IReceiptExtractor`, `ReceiptExtraction`, and `ExtractedField<T>` with per-field
  confidence and `decimal` for every money/rate field.
- Added `StubReceiptExtractor`, which performs no I/O and returns an entirely empty proposal for
  human entry and confirmation.
- Added `IAttachmentScanner`, `AttachmentScanResult`, and `ScanVerdict` plus an EICAR-aware
  `NoopAttachmentScanner` for local/integration-test execution without ClamAV.
- Added the Azure AI Document Intelligence 1.0.0 package reference. Restore was completed by the
  reviewer outside the sandbox after the sandbox could not read the user-level NuGet.Config.
- Added `AzureReceiptExtractor` using `prebuilt-invoice`. It maps vendor identity, invoice
  identity/date, subtotal, tax, total, confidence, and ISO currency. SDK numeric/currency values
  are immediately parsed into `decimal` at the adapter boundary.
- Added `ClamAvAttachmentScanner` using the standard null-terminated `zINSTREAM` command and
  big-endian length-prefixed chunks. It maps `stream: OK` to Clean, `... FOUND` to Infected with
  the returned signature, and connection/I/O/timeout failures to Error.
- Added empty opt-in `DocumentIntelligence` configuration and blank-host `ClamAv` configuration.
- Added one contiguous API DI block: Azure replaces the shipping stub only when Endpoint and
  ApiKey are both configured; ClamAV replaces the noop double only when Host is configured.
- Added compile-verified xUnit coverage for the empty stub result, benign Clean verdict, and exact
  EICAR Infected verdict. No live Azure or ClamAV dependency is used.

## Program.cs DI block

```csharp
// --- Receipt extraction + attachment scanning (plan 12-03, D1/D4) ---------
// The zero-cloud StubReceiptExtractor is the shipping default (D1). Azure is an
// explicit config opt-in and replaces it only when both endpoint and API key exist.
builder.Services.Configure<AzureDocumentIntelligenceOptions>(
    builder.Configuration.GetSection(AzureDocumentIntelligenceOptions.SectionName));
var documentIntelligence = builder.Configuration.GetSection(
    AzureDocumentIntelligenceOptions.SectionName);
if (!string.IsNullOrWhiteSpace(documentIntelligence[nameof(AzureDocumentIntelligenceOptions.Endpoint)])
    && !string.IsNullOrWhiteSpace(documentIntelligence[nameof(AzureDocumentIntelligenceOptions.ApiKey)]))
{
    builder.Services.AddScoped<IReceiptExtractor, AzureReceiptExtractor>();
}
else
{
    builder.Services.AddScoped<IReceiptExtractor, StubReceiptExtractor>();
}

builder.Services.Configure<ClamAvOptions>(
    builder.Configuration.GetSection(ClamAvOptions.SectionName));
var clamAv = builder.Configuration.GetSection(ClamAvOptions.SectionName);
if (!string.IsNullOrWhiteSpace(clamAv[nameof(ClamAvOptions.Host)]))
{
    builder.Services.AddScoped<IAttachmentScanner, ClamAvAttachmentScanner>();
}
else
{
    builder.Services.AddScoped<IAttachmentScanner, NoopAttachmentScanner>();
}
```

## Files created

- `src/modules/Numera.Modules.Sales/Belege/IReceiptExtractor.cs`
- `src/modules/Numera.Modules.Sales/Belege/StubReceiptExtractor.cs`
- `src/modules/Numera.Modules.Sales/Belege/IAttachmentScanner.cs`
- `src/Numera.Api/Services/AzureReceiptExtractor.cs`
- `src/Numera.Api/Services/ClamAvAttachmentScanner.cs`
- `tests/Numera.IntegrationTests/ReceiptExtractorScannerTests.cs`
- `.planning/phases/12-belege-ausgaben/12-03-SUMMARY.md`

## Files modified

- `src/Numera.Api/Numera.Api.csproj`
- `src/Numera.Api/Program.cs`
- `src/Numera.Api/appsettings.json`

## Verification

- Package add/restore: succeeded outside the sandbox using SDK 10.0.301; the package reference and
  restored Azure dependencies were present before implementation resumed.
- Sales Release build with `--no-restore`: passed with 0 warnings and 0 errors.
- API Release build with `--no-restore`: passed with 0 warnings and 0 errors.
- Integration-test project Release build with `--no-restore`: passed with 0 warnings and 0 errors.
- Repository whitespace check (`git diff --check`) for tracked plan files: passed.
- Integration tests were not run, per instruction.

## Deviations and uncertainties

- No scope or design deviations from plan 12-03.
- Live Azure and ClamAV calls were not exercised. Their provider-specific behavior remains subject
  to configured-service verification; default and EICAR paths are covered by the compile-verified
  no-external-service tests.
- Azure's SDK exposes normalized numeric/currency values as floating point. The adapter round-trips
  the normalized invariant representation directly into `decimal`, so no floating-point money is
  exposed by the port or persisted domain model.
- No files were staged or committed. The pre-existing untracked `.claude/` directory was left
  untouched.
