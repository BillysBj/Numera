# Phase 5: E-Rechnung-Engine - Research

**Researched:** 2026-07-26 (supersedes the 2026-07-14 draft — refreshed versions + resolved the two MEDIUM flags)
**Domain:** EN 16931 electronic invoicing (XRechnung UBL+CII, ZUGFeRD/Factur-X PDF/A-3), KoSIT validation, inbound e-invoice ingestion — in a modular .NET 10 monolith with per-tenant Postgres RLS
**Confidence:** HIGH (stack, codebase fit, legal timeline, and library packaging all verified against current sources; two remaining spikes are narrow and flagged)

> No CONTEXT.md exists (no `/gsd:discuss-phase` was run). This research is driven by the ROADMAP goal, EINV-01..05, and the concrete Phase-1..4 codebase (all file paths below were read directly on 2026-07-26). Nothing is user-locked; recommendations are mine to make.

## Summary

Phase 5 is the strategic compliance core: it turns a finalized `SalesDocument` into a legally-conformant e-invoice (XRechnung as UBL **and** CII, ZUGFeRD as PDF/A-3 with embedded CII), gates every outbound e-invoice through the official KoSIT validator, and ingests inbound e-invoices as supplier-matched Eingangsbelege. The single most important finding is unchanged and now re-confirmed: **the codebase was deliberately built to receive this phase.** The frozen render model `InvoicePdfModel` (`src/modules/Numera.Modules.Sales/Pdf/InvoicePdfModel.cs`) annotates **every** field with its EN 16931 BT/BG code; the VAT layer already emits UNCL5305 category codes (S/AE/K/E/Z/G/O — `SalesEnums.cs`) and VATEX exemption codes (`Vat/Pflichttext.cs`); `InvoiceDocument.cs:27-29` carries an explicit documented "wrap with `WithSettings(new DocumentSettings{ PdfA = true })`" seam; and there is a proven Hangfire-job + `IDomainEventHandler<InvoiceFinalized>` + hand-written-RLS-migration pattern to copy verbatim. This phase is overwhelmingly **mapping + integration, not greenfield modelling.**

The XML question is answered by **ZUGFeRD-csharp 18.0.0** (NuGet `ZUGFeRD-csharp`, Apache-2.0, published 2026-03-25). One `InvoiceDescriptor` built from the frozen model saves to all three targets we need — `Save(stream, ZUGFeRDVersion.Version23, Profile.XRechnung, ZUGFeRDFormats.UBL)`, `…CII`, and the CII string embedded in the ZUGFeRD PDF — and the same library **reads** inbound XML via `InvoiceDescriptor.Load(stream)` with auto version/format detection. ONE mapper feeds outbound UBL, outbound CII, the PDF/A-3 embed, and inbound parsing — the "single source of truth" the criteria demand. **New this refresh:** 18.0.0 is explicitly the **last major OSS version** ("For new features go to factoorsharp.de"). This does not block us — create+read of XRechnung/EN16931 is fully covered and stable — but pin 18.0.0 and treat the commercial **FactoorSharp** fork (and a future XRechnung-4.0 config) as a documented upgrade seam, not v1 work.

The PDF/A-3 embed reuses the **existing QuestPDF engine** (2026.7.1, already installed in the Api). **Resolved (was Open Q1):** the ZUGFeRD/PDF-A-3 embed is **built into core QuestPDF** — `DocumentSettings.PdfA` + `DocumentOperation.AddAttachment` / `ExtendMetadata`, no separate `QuestPDF.ZUGFeRD` package. Value-identity between the printed PDF and the embedded XML is guaranteed **structurally** by building both from the same `InvoicePdfModel`/`InvoiceDescriptor` in one code path — never by cross-checking two pipelines.

KoSIT validation runs as a **Docker sidecar** (validator JAR **1.6.0** + `validator-configuration-xrechnung` release **2026-01-31 → XRechnung 3.0.2**, in daemon `-D` HTTP mode on port 8081), called over HTTP from .NET — mirroring exactly how Mailpit was added to `docker-compose.yml` in Phase 4. **Legal-timeline update:** as of 2026-07, **XRechnung 3.0.2 is the production target**; XRechnung 4.0 (tracking EN 16931-1:2026, CEN-approved Feb 2026) will only appear as a *pre-release for early visibility* around mid-2026 and is **not** valid for production, with KoSIT's 6-month advance-notice rule. The 4.0 upgrade path is therefore a **pinned-config swap** (+ a later ZUGFeRD-csharp/FactoorSharp bump), confirmed as a non-blocking seam for this phase.

**Primary recommendation:** Build one `EInvoiceMapper` (frozen `SalesDocument`/`InvoicePdfModel` → `InvoiceDescriptor`) in `Numera.Modules.Sales/EInvoice/`; generate XRechnung (UBL+CII) and ZUGFeRD PDF/A-3 from it via ZUGFeRD-csharp 18 + the existing QuestPDF engine, inside Hangfire jobs mirroring `RenderDocumentPdfJob`; gate outbound send on a synchronous KoSIT-sidecar HTTP validation (two-stage: pre-finalize dry-run + post-finalize send-gate); store artifacts + validation status in new hand-written-RLS tables; and ingest inbound e-invoices (extract embedded XML from ZUGFeRD PDF/A-3, `InvoiceDescriptor.Load` to parse, KoSIT to validate, `BusinessPartner.VatId` to match the supplier) into an immutable `inbound_document` store. **Close four concrete snapshot gaps** the mapper needs (BT-10 Käuferreferenz, BT-34/BT-49 electronic addresses, BG-6 seller contact, BT-81 payment means) — detailed below.

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| **ZUGFeRD-csharp** | **18.0.0** (NuGet id `ZUGFeRD-csharp`; 2026-03-25; Apache-2.0) | Build EN 16931 `InvoiceDescriptor`; save as XRechnung UBL/CII + ZUGFeRD CII; read/parse inbound XML | The de-facto .NET EN 16931 library (Stephan Stapel / s2industries). One descriptor → UBL, CII, all ZUGFeRD 2.x profiles; `Load` auto-detects version+format for inbound. Avoids hand-rolling ~200 BT/BG fields × 2 syntaxes + schematron. **Last major OSS version** — pin it; FactoorSharp is the commercial successor seam. |
| **QuestPDF** | 2026.7.1 (ALREADY INSTALLED in `Numera.Api`) | PDF/A-3b generation + XML attach + XMP metadata for ZUGFeRD | Same engine that renders the Phase-4 §14 PDF → visual layer and ZUGFeRD carrier are one code path. `DocumentSettings.PdfA`, `DocumentOperation.AddAttachment`, `ExtendMetadata` are **built-in** (no separate package — resolved). No engine swap, no iText/AGPL. |
| **KoSIT validator (Java sidecar)** | validator JAR **1.6.0** + `validator-configuration-xrechnung` **2026-01-31 (XRechnung 3.0.2)** | Government-authoritative validation of every outbound (and inbound) e-invoice | THE legal definition of "conformant." Run as a Docker sidecar in daemon HTTP mode (port 8081); POST XML → XML report with `accept/reject`. Not a NuGet package — a Java service beside Postgres/Keycloak/Mailpit. |
| **Hangfire (+ PostgreSql)** | ALREADY INSTALLED | Durable jobs for CPU-heavy XML/PDF generation | Copy `RenderDocumentPdfJob` verbatim: fresh scope + `ICurrentTenant.SetTenant`, `[AutomaticRetry(3)]`, **Api default queue** (LOCKED Phase-4 decision — `Numera.Worker` lacks Sales/QuestPDF refs). |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| Embedded-file extraction for inbound ZUGFeRD | see Open Q3 | Pull `factur-x.xml` / `zugferd-invoice.xml` / `xrechnung.xml` from an inbound PDF/A-3 | **First check whether ZUGFeRD-csharp 18 exposes PDF loading directly** (`InvoiceDescriptor.Load` on a PDF, or a PDF helper) — if so, drop the extra dependency. Else use **PdfPig** (NuGet `PdfPig`, MIT, PDFBox port) which reads the PDF `/EmbeddedFiles` name tree. |
| `IHttpClientFactory` (`System.Net.Http`) | built-in | POST XML to the KoSIT sidecar, receive the XML report | The validator gate. Register a typed `IEInvoiceValidator` client (timeout + health-check). |
| `System.Xml.Linq` | built-in | Parse the **KoSIT report** into structured findings | Report parsing only. Do NOT parse the invoice XML by hand — that is ZUGFeRD-csharp's job. |
| **veraPDF** (CLI, CI-only) | latest | Independently assert the ZUGFeRD PDF is valid PDF/A-3b | CI check on ≥1 golden ZUGFeRD PDF (Pitfall 5). Optional locally. |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| ZUGFeRD-csharp 18 (OSS) | commercial **FactoorSharp** (`factoorsharp.de`) | The OSS 18.0.0 covers XRechnung/EN16931 create+read (all we need) and is the pinned choice. FactoorSharp adds ongoing features/validation + XRechnung-4.0 support later. Start OSS; note the seam. |
| Hand-built `XDocument` UBL/CII | ZUGFeRD-csharp | Rejected. ~200 BT/BG fields × 2 syntaxes + BR-DE-* schematron; hand-rolling guarantees KoSIT failures and endless maintenance. |
| KoSIT self-hosted sidecar | hosted validation SaaS | SaaS = sending customer invoice data to a third party (privacy/GoBD concern). Rejected for runtime. |
| KoSIT sidecar (daemon) | invoke the JAR as a child process per call | Subprocess ships a JRE + JAR inside the app image + cold-start per call. Sidecar daemon = warm, isolated, matches the Mailpit/Keycloak compose pattern. Sidecar wins. |
| Official JAR + config in **our own image** | community images (`apps4everything/kosit-docker` 1.6.0/XR-3.0.2, `easybill/kosit-validator-xrechnung_3.0.2`) | Community images are fine for **dev speed / Testcontainers**; for the compliance core, **pin the official JAR + config version in our own Dockerfile** for reproducibility + update control. Use a community image in tests, our image in prod (decide at plan time). |
| new `Numera.Modules.EInvoice` module | keep in `Numera.Modules.Sales/EInvoice/` | For v1 velocity, keep it in Sales (tight coupling to the frozen model + QuestPDF seam already there). A new module = new csproj + DI + host refs for little benefit now. Flag a future `Numera.Modules.Purchasing` extraction as a seam. |

**Installation:**
```bash
dotnet add src/modules/Numera.Modules.Sales/Numera.Modules.Sales.csproj package ZUGFeRD-csharp --version 18.0.0
# Inbound PDF extraction — ONLY if ZUGFeRD-csharp 18 cannot read the PDF directly (verify in 05-05):
# dotnet add src/modules/Numera.Modules.Sales/Numera.Modules.Sales.csproj package PdfPig
# QuestPDF + Hangfire already present in the Api; jobs run on the Api default queue (LOCKED).
# docker-compose.yml: add a kosit-validator service (daemon HTTP :8081), mirroring the Mailpit entry.
```

## Architecture Patterns

### Recommended Project Structure
```
src/modules/Numera.Modules.Sales/
├── EInvoice/
│   ├── EInvoiceMapper.cs          # InvoicePdfModel/SalesDocument (frozen) → InvoiceDescriptor. THE single source of truth.
│   ├── EInvoiceFormat.cs          # enum: XRechnungUbl, XRechnungCii, ZugferdPdfA3
│   ├── EInvoiceArtifact.cs        # NEW ITenantEntity (RLS): generated bytes + format + profile + validation status/report
│   └── Inbound/
│       ├── InboundDocument.cs     # NEW ITenantEntity (RLS): immutable original bytes + detected format + parsed read-model (jsonb) + matched PartnerId
│       ├── InboundParser.cs       # extract embedded XML → InvoiceDescriptor.Load → read model
│       └── SupplierMatcher.cs     # seller VatId/name → BusinessPartner (partners table, IsSupplier)
├── Pdf/InvoiceDocument.cs         # EXTEND at the documented seam: PdfA=true + embed CII via DocumentOperation
src/Numera.Api/
├── Jobs/
│   ├── GenerateEInvoiceJob.cs     # mirror RenderDocumentPdfJob: map → generate → validate → store
│   └── (optional) ValidateInboundJob.cs
├── Services/
│   ├── EInvoiceService.cs         # shared entrypoint (endpoint + job): generate + persist, mirrors DocumentPdfService
│   └── KoSitValidatorClient.cs    # IEInvoiceValidator: HttpClient → sidecar, parse report → findings
└── Endpoints/
    └── (extend SalesDocumentEndpoints.cs + new InboundDocumentEndpoints.cs)
src/platform/Numera.Platform.Db/Migrations/
└── XXXX_EInvoice.cs               # document_einvoice + inbound_document tables + HAND-WRITTEN RLS (the #1 trap)
```

### Pattern 1: One mapper, four outputs (single source of truth)
**What:** `EInvoiceMapper` builds ONE `InvoiceDescriptor` from the frozen model (`SnapshotReader.FromDocument` → `InvoicePdfModel`, already stateless and reusable). All outbound artifacts are `descriptor.Save(...)` calls; the PDF embed uses the CII string from the same descriptor.
**When:** Every outbound generation. This is how EINV-02's "PDF- und XML-Werte stimmen exakt überein" is guaranteed — by having one source, not by reconciling two.
**Example:**
```csharp
// Source: github.com/stephanstapel/ZUGFeRD-csharp (Save overloads, Profile/Format enums)
var model = SnapshotReader.FromDocument(doc);          // frozen snapshot → InvoicePdfModel (existing)
var desc  = EInvoiceMapper.ToDescriptor(model, doc);   // NEW: fill BT/BG from the model + document row
desc.BusinessProcess = "urn:fdc:peppol.eu:2017:poacc:billing:01:1.0"; // REQUIRED since XRechnung 3.0.1

using var ubl = new MemoryStream(); desc.Save(ubl, ZUGFeRDVersion.Version23, Profile.XRechnung, ZUGFeRDFormats.UBL);
using var cii = new MemoryStream(); desc.Save(cii, ZUGFeRDVersion.Version23, Profile.XRechnung, ZUGFeRDFormats.CII);
// ZUGFeRD PDF: save an EN16931/XRECHNUNG-profile CII, embed it into the QuestPDF PDF/A-3 (Pattern 2).
```

### Pattern 2: PDF/A-3 ZUGFeRD embed on the existing QuestPDF seam (built-in)
**What:** Turn on PDF/A, then attach the CII XML + ZUGFeRD XMP. Confirmed built into core QuestPDF — no `QuestPDF.ZUGFeRD` package.
**When:** EINV-02 generation. Extends `InvoiceDocument.cs` at its documented `Phase-5-ready seam` (lines 27-29) — keep the DE/EN §14 layout unchanged.
**Example:**
```csharp
// Source: questpdf.com/examples/zugferd.html (HIGH — official)
var pdf = new InvoiceDocument(model)                       // the EXISTING layout
    .WithSettings(new DocumentSettings { PdfA = true })      // PDF/A-3b
    .GeneratePdf();
var withXml = DocumentOperation.LoadFile(/* bytes */)
    .AddAttachment(new DocumentOperation.DocumentAttachment {
        Key = "factur-zugferd", AttachmentName = "factur-x.xml", // ZUGFeRD 2.1.1 canonical filename
        MimeType = "text/xml", Description = "Factur-X Invoice",
        Relationship = DocumentOperation.DocumentAttachmentRelationship.Source })
    .ExtendMetadata(zugferdXmp)   // ZUGFeRD conformance XMP (DocumentType/ConformanceLevel/Version)
    .Save(/* bytes */);
```

### Pattern 3: KoSIT validation as a synchronous gate
**What:** POST the generated XML to the sidecar, parse the report, block on reject; map findings to human-readable German.
**When:** Two-stage (Pitfall 1). The report is the KoSIT XML with a computed acceptance.
**Example:**
```csharp
var resp = await _http.PostAsync("http://kosit-validator:8081/",
    new StringContent(xml, Encoding.UTF8, "application/xml"), ct);
var result = KoSitReport.Parse(await resp.Content.ReadAsStringAsync(ct)); // → Accepted + List<Finding>
if (!result.Accepted) return Problem(result.ToHumanReadableGerman());     // block + explain (EINV-03)
```

### Anti-Patterns to Avoid
- **Two independent pipelines for PDF vs XML.** Any divergence fails EINV-02. One descriptor, always.
- **Generating e-invoice XML from live master data.** Same GoBD rule as Phase 4: read ONLY the frozen snapshot via `SnapshotReader` (`IssuerSnapshot`/`RecipientSnapshot` + persisted lines/breakdown/totals). Never re-read `CompanyProfile`/`BusinessPartner`.
- **Recomputing totals in the mapper.** Feed the descriptor the already-frozen `TotalNet/TotalTax/TotalGross/AmountDue` + persisted `TaxBreakdown` rows verbatim (Pitfall 3).
- **Validating only in CI.** The criterion gates the *runtime send*; CI golden-file validation is regression cover, not the gate.
- **Mutating inbound originals.** GoBD requires the received bytes be kept immutably. Store the raw upload untouched; derive a separate read-model.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| EN 16931 UBL/CII XML | Custom `XDocument` writer | ZUGFeRD-csharp `InvoiceDescriptor.Save` | ~200 BT/BG × 2 syntaxes + BR-DE-* schematron; you will never pass KoSIT by hand. |
| PDF/A-3b + XML embed | Manual PDF surgery / iText | QuestPDF `DocumentSettings.PdfA` + `DocumentOperation.AddAttachment` | Built-in; PDF/A conformance + AF relationship + XMP handled. |
| E-invoice validation | Bundled/in-proc schematron | KoSIT sidecar | The government tool *is* the definition of conformant; anything else drifts. |
| Inbound XML parse | Custom UBL/CII reader | `InvoiceDescriptor.Load` (auto-detects) | One library round-trips in+out. |
| Extract PDF-embedded XML | Byte scanning | ZUGFeRD-csharp PDF read (verify) or PdfPig | Correct `/EmbeddedFiles` name-tree handling. |
| Supplier matching key | Fuzzy everything | `BusinessPartner.VatId` (`VatId.Normalize`) exact-match first, then name | Deterministic, auditable; VatId is BT-31/BT-48. |

**Key insight:** This domain is a minefield of format rules; the value of Phase 5 is *correct mapping of our already-clean frozen model into battle-tested libraries*, not re-implementing the standards.

## Snapshot → BT/BG Mapping and the Gaps the Plans MUST Close

The frozen data covers most of EN 16931 already. Verified sources: `SerializeIssuer` (`SalesDocumentEndpoints.cs:909`), `SerializeRecipient` (`:933`), `SalesDocument.cs`, `InvoicePdfModel.cs`.

**Present and directly mappable:** BT-1 number, BT-2 date, BT-3 type (DocumentType→380/381/384), BT-5 currency, BT-9 due date, BT-72/BG-14 service date/period, BT-22 notes; issuer BT-27 name / BG-5 address / BT-31 VatId / BT-32 TaxNumber / BT-84 IBAN / BT-86 BIC; recipient BT-44 name / BG-8 address / BT-48 VatId / BT-49-candidate email; lines BT-126/153/154/129/130/146/131/151/152; breakdown BG-23 BT-116/117/118/119/120/121; totals BT-106/109/110/112/115.

**GAPS to close (a mapping/data task for 05-01, not a schema redesign):**

| # | Gap | EN/CIUS rule | Where | Fix |
|---|-----|--------------|-------|-----|
| **G1** | **BT-10 Buyer reference (Käuferreferenz) is XRechnung-MANDATORY** for *every* XRechnung (not only B2G) | **BR-DE-15** | `SalesDocument.BuyerReference` is nullable and usually unset | Require/default BT-10 when generating an e-invoice: use the Leitweg-ID for B2G; for B2B allow any buyer reference (fallback to a sensible constant/order-ref). The pre-finalize dry-run (Pitfall 1) surfaces a missing one before a number is burned. **HIGH.** |
| **G2** | **Seller (BT-34) + Buyer (BT-49) electronic address with EAS scheme** | XRechnung requires both, each with a scheme code | Snapshot has issuer `ContactEmail` + recipient `Email` but **no scheme** | Map `ContactEmail`→BT-34 and recipient `Email`→BT-49 with scheme **EM** (email). Both must be non-empty for a valid XRechnung — validate at the gate. **MED-HIGH.** |
| **G3** | **BG-6 Seller contact** (BT-41 name / BT-42 phone / BT-43 email) | **BR-DE-2** requires the group | Snapshot has `ContactEmail`, `ContactPhone`, `ManagingDirector` | Map `ManagingDirector` (or LegalName) → BT-41, `ContactPhone`→BT-42, `ContactEmail`→BT-43. Ensure phone/name present or the CIUS rejects. **MED.** |
| **G4** | **Payment means BT-81 type code** | required for non-cash | not modelled | Mapper supplies a constant: **58** (SEPA credit transfer) when IBAN present, else **30**. No snapshot change. **LOW.** |
| **G5** | §19 Kleinunternehmer category-E exemption code (`VATEX-EU-D`) is unverified | schematron may want a specific code/text | `Pflichttext.cs:39` (flagged MEDIUM in-code) | Let KoSIT decide: run the §19 golden file through the pinned config and pin whatever it accepts. **MED — a data fill.** |

None require a new migration to `sales_documents` — G1-G4 are populated by the mapper from existing frozen fields + constants, and the *validity* is proven by golden files through KoSIT (Pitfall 2). If G1/G2/G3 data is genuinely absent at finalize, the **pre-finalize dry-run blocks** so the user fixes it before the number is assigned.

## Common Pitfalls

### Pitfall 1: "vor Finalisierung" vs. number assignment (BIGGEST design decision)
**What goes wrong:** EINV-03 says validate *"vor Finalisierung"* and *"Fehler blockieren den Versand"*. But the legal number (BT-1) and the frozen snapshot only exist **after** finalize (`FinalizeCoreAsync` freezes snapshot + assigns the gapless number + creates the open item, then commits, then publishes `InvoiceFinalized`). Naively validating only post-finalize lets a user burn a number and *then* discover the e-invoice can't be sent — a stranded gapless number (GoBD-sensitive).
**How to avoid (recommended two-stage gate):**
  1. **Pre-finalize dry-run:** before committing finalize, build a *provisional* `InvoiceDescriptor` from the would-be-frozen data (a placeholder/no number is fine for structural + BR-DE checks) and run KoSIT synchronously; **block finalize on hard errors** so data is fixed *before* a number is burned. This literally satisfies "vor Finalisierung."
  2. **Post-finalize authoritative gate:** `GenerateEInvoiceJob` produces the real numbered XML, validates it, stores `ValidationStatus`; the **send** endpoint refuses unless the stored status is `Accepted`. This satisfies "Fehler blockieren den Versand."
**Warning signs:** a finalized invoice with `ValidationStatus = Rejected` and no way to send it — stage 1 was skipped.

### Pitfall 2: CIUS-mandatory fields our model treats as optional
**What goes wrong:** XRechnung (a CIUS of EN 16931) makes BR-DE-* fields mandatory that EN 16931 leaves optional — see G1-G3 above.
**How to avoid:** In 05-01, produce a golden-file test **per VAT scenario** (S standard, AE reverse-charge §13b, E Kleinunternehmer §19, K intra-community, G export, Z zero-rate) and run each through KoSIT. Let the validator tell you which fields are missing rather than guessing.
**Warning signs:** `BR-DE-*` failures on otherwise-valid EN 16931 XML.

### Pitfall 3: Decimal/rounding mismatch between XML and totals
**What goes wrong:** EN 16931 BR-CO-* rules are strict (line nets → BT-106; per-category BG-23 tax → BT-110; BT-112 = BT-109 + BT-110). If the mapper recomputes anything it can diverge from the persisted frozen totals and fail BR-CO-* or the PDF↔XML identity check.
**How to avoid:** Transcribe the **already-frozen** persisted values; never recompute. `Numera.Platform.Money.RoundingPolicy` (per-category round-away-from-zero then sum, EN 16931 BR-CO-14) already produced them at finalize.
**Warning signs:** BR-CO-10/13/15 failures; a cent of drift between PDF footer and XML `<TaxTotal>`.

### Pitfall 4: KoSIT config version drift
**What goes wrong:** XRechnung config is versioned (**current: 2026-01-31 → XRechnung 3.0.2**). A stale sidecar validates against the wrong ruleset; a too-new one may reject documents targeting an older profile.
**How to avoid:** Pin BOTH the validator JAR (**1.6.0**) and the configuration release in the sidecar image; surface the configured version in a health endpoint; add a CI check that our golden files pass the pinned config. Watch for the XRechnung 4.0 pre-release (mid-2026) but do NOT adopt it for production.
**Warning signs:** validation results change with no code change (image rebuilt from `:latest`).

### Pitfall 5: PDF/A-3 conformance is not automatic
**What goes wrong:** `PdfA = true` produces PDF/A-3b, but fonts must be embedded and metadata correct or the ZUGFeRD PDF fails PDF/A validation independently of the XML.
**How to avoid:** Use QuestPDF's embedded fonts (already the case); verify with **veraPDF** in CI on ≥1 golden ZUGFeRD PDF. The AF relationship (`Source`) + ZUGFeRD XMP are required for a conformant Factur-X. (Same Linux/SkiaSharp font caveat as Phase 4 — `libfontconfig1` + a font in the deploy image.)
**Warning signs:** veraPDF PDF/A-3b failures; ZUGFeRD-aware readers not detecting the embedded invoice.

### Pitfall 6: Sidecar availability couples send to an external service
**What goes wrong:** If the KoSIT sidecar is down, the synchronous gate blocks all sends.
**How to avoid:** Health-check + sane timeout + a distinct "validation service unavailable, try again" error (NOT "invoice rejected"). Validation status is persisted, so a transient outage loses no work.

### Pitfall 7: Storno negative amounts through the validator
**What goes wrong:** Our Storno is a negative-mirror `SalesDocument` (type 384, negative totals). KoSIT/EN 16931 have sign expectations; a negative invoice may trip BR-CO-* or profile rules.
**How to avoid:** Add a Storno golden file to the 05-01 suite and validate it through KoSIT; if negatives are rejected under type 384, represent the correction as a credit note (381) for the e-invoice output. Decide in 05-03 based on the validator's verdict.

## Code Examples

### Reading an inbound e-invoice (EINV-04/05)
```csharp
// 1. Detect + extract. PDF (ZUGFeRD): pull the embedded factur-x.xml/zugferd-invoice.xml/xrechnung.xml.
//    Raw XML (XRechnung UBL/CII): use bytes directly.
byte[] xml = isPdf ? PdfEmbeddedXml.Extract(pdfBytes) : uploadBytes;

// 2. Parse — auto-detects ZUGFeRD version + UBL/CII. Source: ZUGFeRD-csharp docs.
InvoiceDescriptor inv = InvoiceDescriptor.Load(new MemoryStream(xml));

// 3. Validate via the SAME KoSIT sidecar (human-readable findings).
// 4. Match supplier by normalized VatId then name → BusinessPartner (IsSupplier).
// 5. Store the ORIGINAL bytes IMMUTABLY + a jsonb read-model + PartnerId in inbound_document (RLS).
```

### New RLS table (hand-written policy — the #1 silent-leak trap)
```csharp
// Source: the six-times-repeated migration pattern; DocumentRender.cs is the template entity.
migrationBuilder.Sql("ALTER TABLE document_einvoice ENABLE ROW LEVEL SECURITY;");
migrationBuilder.Sql("ALTER TABLE document_einvoice FORCE ROW LEVEL SECURITY;");
migrationBuilder.Sql(
    "CREATE POLICY tenant_isolation ON document_einvoice " +
    "USING (tenant_id = current_setting('app.current_tenant')::uuid) " +
    "WITH CHECK (tenant_id = current_setting('app.current_tenant')::uuid);");
// Same for inbound_document. Reflective ITenantEntity discovery NEVER emits policies.
```

### Testcontainers: KoSIT sidecar beside Postgres
```csharp
// Source: PostgresFixture.cs (Testcontainers.PostgreSql) + Phase-4 Mailpit container pattern.
// Add a ContainerBuilder for the pinned KoSIT image exposing :8081; POST a generated golden
// XML and assert the report's acceptance == accept. Mirrors the Mailpit HTTP-assert e2e (04-04).
```

## Legal / Compliance Grounding (verified 2026-07-26)

**German E-Rechnungspflicht timeline (Wachstumschancengesetz / §14 UStG):**
- **Since 2025-01-01:** every domestic B2B business must be **able to receive and process** EN 16931 structured e-invoices (no recipient consent needed). → EINV-04/05 (inbound) is **legally required today**, not optional.
- **2025-2026:** paper / "other" e-invoices (plain PDF) may still be **sent** with recipient consent (transition).
- **From 2027-01-01:** businesses with **> €800,000** prior-year turnover **must send** B2B e-invoices.
- **From 2028-01-01:** **all** domestic B2B must send e-invoices (EDI extended transition to end-2027).

**Legally accepted formats:** **XRechnung** (German CIUS of EN 16931; UBL or CII syntax) and **ZUGFeRD ≥ 2.x in the EN16931 (Comfort) or XRECHNUNG profile** (hybrid PDF/A-3 + CII). Profiles below EN16931 (MINIMUM, BASIC-WL) are **not** valid e-invoices → always generate `Profile.XRechnung` / `EN16931`.

**XRechnung version status (the STATE "4.0 upgrade-path" concern — RESOLVED):**
- **Production target now = XRechnung 3.0.2** (validator config release 2026-01-31, JAR 1.6.0).
- **XRechnung 4.0** tracks **EN 16931-1:2026** (CEN-approved Feb 2026, expected published mid-2026). KoSIT will issue a **pre-release for early visibility only** around mid-2026 — **not valid for production** — and gives a **6-month advance notice** before a version becomes mandatory. So 4.0 is **out of scope for this phase**; target 3.0.2.
- **Upgrade path:** a **pinned-config swap** (drop in the new `validator-configuration-xrechnung`) + later a ZUGFeRD-csharp/FactoorSharp bump for 4.0 field changes (e.g. many-to-one billing). Keep `EInvoiceMapper` the single change-locus; keep the sidecar config version a pinned build arg.

**"verständlich erklärt" (EINV-03/04 error UX):** KoSIT emits rule IDs (`BR-DE-15`, `BR-CO-10`, …) + technical schematron messages. Maintain a DE (authoritative) + EN map of the common `BR-DE-*`/`BR-CO-*`/`BR-*` IDs → plain-language cause + fix (e.g. *"Die Käuferreferenz (BT-10) fehlt — für eine XRechnung ist sie Pflicht."*), fall back to the raw message for unmapped rules, group by severity (error blocks / warning informs), and surface through the existing react-i18next error UX.

## Architecture Fit & Plan Boundaries

**Module placement:** Keep **outbound** in `Numera.Modules.Sales/EInvoice/` (bound to the frozen model + `SnapshotReader` + the QuestPDF seam already there). Keep **inbound** as a distinct entity set (`EInvoice/Inbound/`) in the same module for v1; it references `BusinessPartner` (Crm) which Sales already can. Flag a future `Numera.Modules.Purchasing` extraction as a seam, not v1. Jobs live in `Numera.Api/Jobs`, run on the **Api default Hangfire queue** (LOCKED — `Numera.Worker` lacks the Sales/QuestPDF/ZUGFeRD refs; Phase-4 Pitfall 2).

**New RLS tables (hand-written policy in the migration; `DocumentRender.cs` is the template):**
- `document_einvoice` — generated artifact bytes + `EInvoiceFormat` + profile + `ValidationStatus` + report (jsonb). `ITenantEntity`, `DocumentId` provenance-only FK, insert via `db.Add`, idempotent replace per (document, format) — mirror `DocumentRender`.
- `inbound_document` — immutable original bytes + detected format + parsed read-model (jsonb) + `ValidationStatus`/report + matched `PartnerId` + upload metadata. `ITenantEntity` + RLS.

**Suggested plans & waves** (matches the existing 05-0x plan files on disk):

| Plan | Scope | Requirements | Depends on |
|------|-------|--------------|-----------|
| **05-01** | `EInvoiceMapper` frozen-model→`InvoiceDescriptor` (close G1-G5); emit XRechnung UBL+CII; golden-file test per VAT scenario | EINV-01 (core) | Phase 3/4 model (done) |
| **05-02** | KoSIT sidecar (pinned JAR 1.6.0 + config 2026-01-31) in docker-compose; `IEInvoiceValidator` HttpClient; report→human-readable DE/EN findings; pin report parser to a captured golden report | EINV-03 (engine) | none (parallel with 05-01) |
| **05-03** | XRechnung generate + **two-stage validation gate** (pre-finalize dry-run + send gate) + download + email; `document_einvoice` table | EINV-01, EINV-03 | 05-01, 05-02 |
| **05-04** | ZUGFeRD PDF/A-3: extend `InvoiceDocument` (PdfA + embed CII from the SAME descriptor); veraPDF check; PDF↔XML value-identity test | EINV-02 | 05-01, Phase-4 PDF (done) |
| **05-05** | Inbound: upload + format-detect + extract embedded XML + `InvoiceDescriptor.Load` + KoSIT validate + human view + supplier match (`IsSupplier` by VatId) + `inbound_document` table | EINV-04, EINV-05 | 05-01, 05-02 |

**Waves:**
- **Wave 1 (parallel):** 05-01 (mapper) ‖ 05-02 (validator sidecar) — independent.
- **Wave 2:** 05-03 (XRechnung + gate) — needs both wave-1 plans; establishes the `document_einvoice` table + gate pattern.
- **Wave 3 (parallel):** 05-04 (ZUGFeRD PDF) ‖ 05-05 (inbound). Sequencing 05-03 first is recommended so the validation-gate + `document_einvoice` pattern exist for 05-04/05-05 to reuse.

## Open Questions

1. **ZUGFeRD-csharp inbound PDF reading vs a second dependency.**
   - Known: `InvoiceDescriptor.Load` parses XML; PdfPig (MIT) reliably extracts embedded files.
   - Unclear: whether ZUGFeRD-csharp 18 exposes direct PDF loading (which would drop PdfPig).
   - Recommendation: 05-05 spikes ZUGFeRD-csharp's PDF facility first; add PdfPig only if needed. MEDIUM.

2. **KoSIT report schema parsing.**
   - Known: daemon HTTP mode (:8081), POST XML → XML report with findings + acceptance.
   - Unclear: exact XPath for findings/severity in the pinned 2026-01-31 config's report scenario.
   - Recommendation: 05-02 captures a real report from the pinned image and pins the parser to it (golden-file). MEDIUM.

3. **Own KoSIT image vs a community image in prod.**
   - Recommendation: community image (`apps4everything/kosit-docker` 1.6.0/XR-3.0.2) for dev + Testcontainers; build our own pinned image for prod reproducibility. Decide in 05-02. Non-blocking.

4. **Storno sign handling in the validator (Pitfall 7).**
   - Recommendation: 05-01 golden file decides — type 384 negative vs credit-note 381. Non-blocking for wave 1, must resolve before 05-03 send.

## Sources

### Primary (HIGH confidence)
- Codebase, read directly 2026-07-26: `Pdf/InvoicePdfModel.cs` (BT/BG annotations), `Pdf/SnapshotReader.cs` (stateless snapshot→model), `Pdf/InvoiceDocument.cs:27-29` (ZUGFeRD seam), `SalesDocument.cs` (BuyerReference/ServiceDate/ReverseCharge), `SalesEnums.cs` (TaxCategory + DocumentType→380/381/384), `Vat/Pflichttext.cs` (VATEX codes, §19 flagged), `Rendering/DocumentRender.cs` (RLS-table template), `Numera.Api/Endpoints/SalesDocumentEndpoints.cs` (SerializeIssuer:909 / SerializeRecipient:933 / finalize + send), `Jobs/RenderDocumentPdfJob.cs`, `Services/DocumentPdfService.cs`, `Crm/BusinessPartner.cs` + `Crm/VatId.cs` (supplier match), `tests/Numera.IntegrationTests/PostgresFixture.cs` (Testcontainers), `docker-compose.yml` (Mailpit sidecar pattern).
- `nuget.org/packages/ZUGFeRD-csharp` — **18.0.0 (2026-03-25), Apache-2.0, LAST major OSS version → factoorsharp.de successor.**
- `nuget.org/packages/QuestPDF` — 2026.7.1; `questpdf.com/examples/zugferd.html` + `/concepts/document-operations.html` — **PdfA + AddAttachment/ExtendMetadata built into core (no separate package).**
- `github.com/itplr-kosit/validator` (CHANGELOG, daemon HTTP mode) + `github.com/itplr-kosit/validator-configuration-xrechnung/releases` — **config 2026-01-31 → XRechnung 3.0.2**; `github.com/apps4everything/kosit-docker` — **KoSIT Validator 1.6.0 with XRechnung 3.0.2** (HTTP :8081).
- XRechnung 4.0 status: `xeinkauf.de` (KoSIT), `blog.cosinex.de`, `blog.adesso-bc.com`, `factora.software` — **3.0.2 currently valid; 4.0 = EN 16931-1:2026, pre-release mid-2026, not production; semi-annual releases, 6-month notice.**
- E-Rechnungspflicht timeline: BMF FAQ + IHK — 2025 receive / 2027 >€800k send / 2028 all.

### Secondary (MEDIUM confidence)
- `uglytoad.github.io/PdfPig` — MIT, reads PDF embedded files (inbound extraction fallback).
- `easybill/kosit-validator-xrechnung_3.0.2`, `flx235/xr-validator-service` — community daemon images (dev reference).
- veraPDF — independent PDF/A-3b conformance check (CI).

## Metadata

**Confidence breakdown:**
- Standard stack: **HIGH** — every version re-verified on NuGet/official repos on 2026-07-26; codebase fit read directly.
- Architecture / plan boundaries: **HIGH** — direct extensions of the existing Phase-4 job/RLS/seam patterns.
- Snapshot gaps (G1-G5): **HIGH** on *what* is missing (read `SerializeIssuer`/`SerializeRecipient`); the *exact* CIUS requirement per rule is pinned by KoSIT golden files (that's the point of 05-01/05-02).
- Validation-gate design (Pitfall 1): **HIGH** on the reconciliation logic; two-stage is a recommendation, not locked.
- KoSIT report parsing + inbound PDF extraction: **MEDIUM** — narrow spikes in Open Questions.
- Legal timeline + XRechnung 4.0: **HIGH** — multiple official/KoSIT/IHK sources agree.

**Research date:** 2026-07-26
**Valid until:** ~2026-09-26 (60 days for the stack; **but watch the XRechnung 4.0 pre-release around mid-2026** and any new `validator-configuration-xrechnung` release — re-check the pinned config before 05-02/05-03 execute).
