# Phase 5: E-Rechnung-Engine - Research

**Researched:** 2026-07-14
**Domain:** EN 16931 electronic invoicing (XRechnung UBL+CII, ZUGFeRD/Factur-X PDF/A-3), KoSIT validation, inbound e-invoice ingestion — in a modular .NET monolith with per-tenant RLS
**Confidence:** HIGH (stack + codebase fit + legal timeline verified; MEDIUM on exact QuestPDF ZUGFeRD packaging and KoSIT report-schema parsing details — flagged below)

> No CONTEXT.md exists (no `/gsd:discuss-phase` was run). This research is driven by the ROADMAP goal, EINV-01..05, and the concrete Phase-1..4 codebase. Nothing is user-locked; recommendations are mine to make.

## Summary

Phase 5 is the strategic compliance core: it turns a finalized `SalesDocument` into a legally-conformant e-invoice (XRechnung as UBL and CII, ZUGFeRD as PDF/A-3 with embedded CII), gates every outbound e-invoice through the official KoSIT validator, and ingests inbound e-invoices as supplier-matched Eingangsbelege. The single most important finding is that **the codebase was deliberately built to receive this phase**: the frozen render model (`InvoicePdfModel`) already annotates every field with its EN 16931 BT/BG code, the VAT layer already emits the correct **UNCL5305 category codes (S/AE/K/E/Z/G)** and **VATEX exemption reason codes** (`Pflichttext.For`), the QuestPDF layer has an explicit documented "PdfA/ZUGFeRD" seam, and there is a proven Hangfire job + `IDomainEventHandler<InvoiceFinalized>` + RLS-migration pattern to copy. This phase is overwhelmingly **mapping and integration work, not greenfield modelling.**

The XML question is decisively answered by **ZUGFeRD-csharp 18.0.0** (`ZUGFeRD-csharp` on NuGet, Apache-2.0). One `InvoiceDescriptor` built from the frozen model saves to **all three targets** we need — `Save(stream, ZUGFeRDVersion.Version23, Profile.XRechnung, ZUGFeRDFormats.UBL)`, `…CII`, and the ZUGFeRD `Profile.EN16931/XRechnung` CII string that gets embedded in the PDF — and the same library **reads** inbound XML via `InvoiceDescriptor.Load(stream)` with auto version/format detection. This means ONE mapping layer feeds outbound UBL, outbound CII, the PDF/A-3 embed, and inbound parsing — the "single source of truth" the criteria demand. Do **not** hand-roll EN 16931 XML.

The PDF/A-3 embed reuses the **existing QuestPDF engine** (2026.7.1, already installed): `.WithSettings(new DocumentSettings { PdfA = true })` for PDF/A-3b, then `DocumentOperation.AddAttachment(...)` with `Relationship = Source` + `ExtendMetadata(zugferdXmp)` to attach the CII XML. Value-identity between the printed PDF and the embedded XML is **guaranteed structurally** by building both from the same `InvoicePdfModel` in one code path.

KoSIT validation runs as a **Docker sidecar** (the official `itplr-kosit/validator` JAR + `validator-configuration-xrechnung`, in daemon `-D` HTTP mode), called over HTTP from .NET — exactly mirroring how Mailpit was added to `docker-compose.yml` in Phase 4. The validator returns an XML report with a computed `accept/reject` status; we parse it into structured, human-readable German errors.

**Primary recommendation:** Build one `EInvoiceMapper` (frozen `SalesDocument`/`InvoicePdfModel` → `InvoiceDescriptor`) in `Numera.Modules.Sales`; generate XRechnung (UBL+CII) and ZUGFeRD PDF/A-3 from it via ZUGFeRD-csharp + the existing QuestPDF engine, inside Hangfire jobs that mirror `RenderDocumentPdfJob`; gate outbound send on a synchronous KoSIT-sidecar HTTP validation; store artifacts + validation status in new RLS-scoped tables; and ingest inbound e-invoices (PdfPig to pull embedded XML, `InvoiceDescriptor.Load` to parse, KoSIT to validate, `BusinessPartner.VatId` to match the supplier) into an immutable `inbound_document` store.

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| **ZUGFeRD-csharp** | 18.0.0 (NuGet id `ZUGFeRD-csharp`; released 2026-03) | Build EN 16931 `InvoiceDescriptor`; save as XRechnung UBL/CII + ZUGFeRD CII; read/parse inbound XML | The de-facto .NET EN 16931 library (s2industries / Stephan Stapel). Apache-2.0. One descriptor → UBL, CII, all ZUGFeRD 2.x profiles. `InvoiceDescriptor.Load` auto-detects version+format for inbound. Avoids hand-rolling ~200 BT/BG fields + schematron edge cases. |
| **QuestPDF** | 2026.7.1 (ALREADY INSTALLED) | PDF/A-3b generation + XML attach + XMP metadata for ZUGFeRD | Same engine that renders the Phase-4 §14 PDF → the visual layer and the ZUGFeRD carrier are one code path. `DocumentSettings.PdfA`, `DocumentOperation.AddAttachment`, `ExtendMetadata` are documented (official `questpdf.com/examples/zugferd.html`). No engine swap, no iText. |
| **KoSIT validator (sidecar)** | validator JAR 1.5.0/1.6.0 + `validator-configuration-xrechnung` release **2025-03-21** (XRechnung 3.0.x) | Official government validation of every outbound (and inbound) e-invoice | THE legally-authoritative validator (referenced by the standard). Run as a Docker sidecar in daemon HTTP mode; call over HTTP from .NET. Not a NuGet package — a Java service. |
| **PdfPig** | latest (NuGet `PdfPig`, MIT) | Extract embedded CII/UBL XML from an inbound ZUGFeRD PDF/A-3 | MIT, pure-managed C# port of PDFBox; reads PDF embedded files. Lightweight; used only for inbound attachment extraction. |
| **Hangfire (+ PostgreSql)** | ALREADY INSTALLED | Durable jobs for CPU-heavy XML/PDF generation | Copy `RenderDocumentPdfJob` verbatim: fresh scope + `ICurrentTenant.SetTenant`, `[AutomaticRetry(3)]`, Api default queue. |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| `System.Net.Http` (`IHttpClientFactory`) | built-in | POST XML to the KoSIT sidecar, receive the XML report | The validator gate. Register a typed `IEInvoiceValidator` client. |
| `System.Xml.Linq` / `XmlSerializer` | built-in | Parse the KoSIT XML report into structured findings | Report parsing only; do NOT parse invoice XML by hand — that's ZUGFeRD-csharp's job. |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| ZUGFeRD-csharp | Hand-built `XDocument` UBL/CII | Rejected. EN 16931 has ~200 BT/BG fields across two syntaxes plus schematron rules; hand-rolling guarantees KoSIT failures and endless maintenance. |
| ZUGFeRD-csharp (OSS) | commercial fork `FactoorSharp.FacturX` | The OSS package covers XRechnung/EN16931 create+read (what we need). The commercial fork adds bundled validation we're getting from KoSIT anyway. Start OSS; note the seam. |
| KoSIT sidecar (self-hosted) | hosted validation SaaS / community image `apps4everything/kosit-validator-xrechnung` | A community image is fine for dev speed, but for a compliance core **pin the official JAR + config in our own image** for reproducibility and update control. SaaS = sending customer invoice data to a third party (privacy/GoBD concern). Rejected for runtime. |
| KoSIT via Docker sidecar | invoke the JAR as a child process from .NET | Subprocess means shipping a JRE + JAR inside the app image and cold-start per call. Sidecar daemon = warm, isolated, horizontally scalable, matches the existing docker-compose service pattern (Mailpit/Keycloak). Sidecar wins. |
| PdfPig | iText7 / PDFsharp for extraction | iText7 is AGPL/commercial; PDFsharp's PDF/A support is weaker. PdfPig (MIT) reads embedded files and is dependency-light. QuestPDF's own `DocumentOperation` may also extract — verify at plan time. |
| new `Numera.Modules.EInvoice` module | keep in `Numera.Modules.Sales` | For v1 velocity, keep outbound in `Sales/EInvoice/` (tight coupling to the frozen model + QuestPDF seam already there). A new module adds a csproj + DI wiring + host references for little benefit now. See Architecture. |

**Installation:**
```bash
dotnet add src/modules/Numera.Modules.Sales/Numera.Modules.Sales.csproj package ZUGFeRD-csharp
dotnet add src/modules/Numera.Modules.Sales/Numera.Modules.Sales.csproj package PdfPig   # inbound extraction
# QuestPDF + Hangfire already present in the Api. If gen/validation jobs must run on the
# "worker" queue, that host needs the Sales/QuestPDF/ZUGFeRD refs too — but the LOCKED
# Phase-4 decision runs these on the Api default queue (worker host stays lean).
# docker-compose.yml: add a kosit-validator service (daemon HTTP), mirror the Mailpit entry.
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
│       ├── InboundParser.cs       # PdfPig extract → InvoiceDescriptor.Load → read model
│       └── SupplierMatcher.cs     # seller VatId/name → BusinessPartner (partners table)
├── Pdf/InvoiceDocument.cs         # EXTEND: PdfA=true + embed CII via DocumentOperation
src/Numera.Api/
├── Jobs/
│   ├── GenerateEInvoiceJob.cs     # mirror RenderDocumentPdfJob: map → generate → validate → store
│   └── ValidateInboundJob.cs      # (optional async) parse + validate inbound uploads
├── Services/
│   ├── EInvoiceService.cs         # shared entrypoint (endpoint + job): generate + persist, mirrors DocumentPdfService
│   └── KoSitValidatorClient.cs    # IEInvoiceValidator: HttpClient → sidecar, parse report → findings
└── Endpoints/
    └── (extend SalesDocumentEndpoints.cs + new InboundDocumentEndpoints.cs)
```

### Pattern 1: One mapper, four outputs (single source of truth)
**What:** `EInvoiceMapper` builds ONE `InvoiceDescriptor` from the frozen model. All outbound artifacts are `descriptor.Save(...)` calls; the PDF embed uses the CII string from the same descriptor.
**When:** Every outbound generation. This is how EINV-02's "PDF- und XML-Werte stimmen exakt überein" is guaranteed — not by cross-checking two pipelines, but by having one.
**Example:**
```csharp
// Source: ZUGFeRD-csharp docs (github.com/stephanstapel/ZUGFeRD-csharp)
var desc = EInvoiceMapper.ToDescriptor(model);        // frozen InvoicePdfModel → InvoiceDescriptor
desc.BusinessProcess = "urn:fdc:peppol.eu:2017:poacc:billing:01:1.0"; // REQUIRED since XRechnung 3.0.1

using var ubl = new MemoryStream(); desc.Save(ubl, ZUGFeRDVersion.Version23, Profile.XRechnung, ZUGFeRDFormats.UBL);
using var cii = new MemoryStream(); desc.Save(cii, ZUGFeRDVersion.Version23, Profile.XRechnung, ZUGFeRDFormats.CII);
// ZUGFeRD PDF: save an EN16931/XRECHNUNG-profile CII, embed into the QuestPDF PDF/A-3 (below).
```

### Pattern 2: PDF/A-3 ZUGFeRD embed on the existing QuestPDF seam
**What:** Turn on PDF/A, then attach the CII XML + ZUGFeRD XMP.
**When:** EINV-02 generation.
**Example:**
```csharp
// Source: questpdf.com/examples/zugferd.html
var pdf = document.WithSettings(new DocumentSettings { PdfA = true }).GeneratePdf();  // PDF/A-3b
var withXml = DocumentOperation.LoadFile(/* or bytes */)
    .AddAttachment(new DocumentOperation.DocumentAttachment {
        Key = "factur-zugferd", AttachmentName = "factur-x.xml", // ZUGFeRD 2.1.1 filename
        MimeType = "text/xml", Description = "Factur-X Invoice",
        Relationship = DocumentOperation.DocumentAttachmentRelationship.Source,
        CreationDate = DateTime.UtcNow, ModificationDate = DateTime.UtcNow })
    .ExtendMetadata(zugferdXmp)   // ZUGFeRD conformance metadata (DocumentType/ConformanceLevel/Version)
    .Save(/* bytes */);
```
> The Phase-4 seam docstring literally names this: *"a single `Document.Create` that the ZUGFeRD path can later wrap with `WithSettings(new DocumentSettings{ PdfA = true })`"* (`InvoiceDocument.cs:27-29`). Build on it; keep the DE/EN §14 layout unchanged.

### Pattern 3: KoSIT validation as a synchronous gate
**What:** POST the generated XML to the sidecar, parse the report, block on reject.
**When:** Before an e-invoice may be sent; optionally as a pre-finalize dry-run (see Pitfall 1).
**Example:**
```csharp
// POST XML → sidecar; response is the KoSIT XML report with an accept/reject assessment.
var report = await _http.PostAsync("http://kosit-validator:8081/", new StringContent(xml, ..."application/xml"));
var result = KoSitReport.Parse(await report.Content.ReadAsStringAsync()); // → Accepted + List<Finding>
if (!result.Accepted) return Problem(result.ToHumanReadableGerman());     // block, explain
```

### Anti-Patterns to Avoid
- **Two independent pipelines for PDF vs XML.** Any divergence fails EINV-02. One descriptor, always.
- **Generating e-invoice XML from live master data.** Same GoBD rule as Phase 4: read ONLY the frozen snapshot (`IssuerSnapshot`/`RecipientSnapshot` + persisted lines/breakdown/totals). Reuse `SnapshotReader`.
- **Validating only in CI.** The criterion gates the *runtime send*. CI validation of golden files is good regression cover but is NOT the gate.
- **Mutating inbound originals.** GoBD requires the received bytes be kept immutably. Store the raw upload untouched; derive a separate read-model.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| EN 16931 UBL/CII XML | Custom `XDocument` writer | ZUGFeRD-csharp `InvoiceDescriptor.Save` | ~200 BT/BG fields × 2 syntaxes + schematron; you will never pass KoSIT by hand. |
| PDF/A-3b + embed | Manual PDF surgery / iText | QuestPDF `DocumentSettings.PdfA` + `DocumentOperation.AddAttachment` | Already the engine; PDF/A conformance + AF relationship + XMP are handled. |
| E-invoice validation | Bundled schematron in-proc | KoSIT sidecar | The government tool is the definition of "conformant"; anything else drifts. |
| Inbound XML parse | Custom UBL/CII reader | `InvoiceDescriptor.Load` (auto-detects) | Same library round-trips; one model for in+out. |
| Extract PDF-embedded XML | Byte scanning | PdfPig embedded-files API | Correct handling of the PDF /EmbeddedFiles name tree. |
| Supplier matching key | Fuzzy everything | `BusinessPartner.VatId` (there's a `VatId` value object) exact-match first, then name | Deterministic, auditable; VatId is BT-31/BT-48. |

**Key insight:** This domain is a minefield of format rules; the value of Phase 5 is *correct mapping of our already-clean frozen model into battle-tested libraries*, not re-implementing the standards.

## Common Pitfalls

### Pitfall 1: "vor Finalisierung" vs. number assignment (BIGGEST design decision)
**What goes wrong:** EINV-03 says validate *"vor Finalisierung"* and *"Fehler blockieren den Versand"*. But the legal number (BT-1) and the frozen snapshot only exist **after** finalize — so the real, sendable XML can't be built before finalize. Naively validating only post-finalize means a user can finalize (burning a gapless legal number via `NumberingService`) and then discover the invoice can't be sent as an e-invoice — a stranded number.
**Why it happens:** Current flow (`SalesDocumentEndpoints` finalize): freeze snapshot + assign number + create open item → commit → publish `InvoiceFinalized` → enqueue render. No XML in that path yet.
**How to avoid (recommended):** Two-stage validation.
  1. **Pre-finalize dry-run:** before committing finalize, build a *provisional* `InvoiceDescriptor` from the would-be-frozen data (placeholder/no number is fine for structural checks) and run KoSIT synchronously; block finalize on hard errors so the user fixes data *before* a number is burned. This literally satisfies "vor Finalisierung."
  2. **Post-finalize authoritative gate:** `GenerateEInvoiceJob` produces the real numbered XML, validates it, and stores `ValidationStatus`. The **send** endpoint refuses unless the stored status is `Accepted`. This satisfies "Fehler blockieren den Versand."
**Warning signs:** A finalized invoice with `ValidationStatus = Rejected` and no way to send it — means stage 1 was skipped.

### Pitfall 2: Frozen-model gaps for CIUS-mandatory fields
**What goes wrong:** XRechnung (a CIUS of EN 16931) makes some fields mandatory that our model treats as nullable — notably **BT-10 Buyer reference / Leitweg-ID** (mandatory for B2G), a **buyer electronic address / seller electronic address** (BG-electronic address, e.g. email endpoint), and **payment means / IBAN** for non-cash. Our model has `BuyerReference` (BT-10, nullable), `ContactEmail`, `Iban/Bic` — good — but B2B (not B2G) XRechnung uses `BuyerReference` as a free buyer reference, and the electronic-address fields may need population from the recipient email.
**How to avoid:** During 05-01 mapping, produce a golden-file test per scenario (S standard-rate, AE reverse-charge §13b, E Kleinunternehmer §19, K intra-community, G export, Z zero) and run each through KoSIT. Let the validator tell you which fields are missing rather than guessing. Map `RecipientBlock.Email` → buyer electronic address; require BT-10 only when B2G is signalled.
**Warning signs:** KoSIT BR-DE-* rule failures (the German CIUS rules) on otherwise-valid EN 16931 XML.

### Pitfall 3: Decimal/rounding mismatch between XML and totals
**What goes wrong:** EN 16931 has strict rules — line nets sum to BT-106, per-category BG-23 tax sums to BT-110, BT-112 = BT-109 + BT-110, etc. If the mapper recomputes anything, it can diverge from the persisted frozen totals and fail BR-CO-* rules or the PDF-vs-XML identity check.
**How to avoid:** Feed the descriptor the **already-frozen** persisted values (`TotalNet/TotalTax/TotalGross/AmountDue` and the persisted `TaxBreakdown` rows), never recomputed ones. The `Numera.Platform.Money` `RoundingPolicy` (per-category round-away-from-zero then sum) already produced these; the mapper just transcribes.
**Warning signs:** BR-CO-10/BR-CO-13/BR-CO-15 failures; a cent of drift between PDF footer and XML `<TaxTotal>`.

### Pitfall 4: KoSIT config version drift
**What goes wrong:** XRechnung config is versioned (current `validator-configuration-xrechnung` 2025-03-21 → XRechnung 3.0.x). A stale sidecar image validates against the wrong ruleset; a too-new one may reject documents targeting an older profile.
**How to avoid:** Pin both the validator JAR and the configuration version in the sidecar image; surface the configured version in a health endpoint; add a CI check that our generated golden files pass the pinned config.
**Warning signs:** Validation results change with no code change (image was rebuilt from `:latest`).

### Pitfall 5: PDF/A-3 conformance is not automatic
**What goes wrong:** Turning on `PdfA = true` produces PDF/A-3b, but fonts must be embedded and colours/metadata correct or the ZUGFeRD PDF fails PDF/A validation independently of the XML.
**How to avoid:** Use QuestPDF's embedded fonts (already the case), verify the output with a PDF/A validator (veraPDF) in CI on at least one golden ZUGFeRD PDF. The AF relationship (`Source`) and the ZUGFeRD XMP extension are required for a conformant Factur-X.
**Warning signs:** veraPDF PDF/A-3b failures; ZUGFeRD-aware readers not detecting the embedded invoice.

### Pitfall 6: Sidecar availability couples send to an external service
**What goes wrong:** If the KoSIT sidecar is down, the synchronous gate blocks all sends.
**How to avoid:** Health-check the sidecar; give the validator client a sane timeout + a clear "validation service unavailable, try again" error (distinct from "invoice rejected"). Validation status is persisted, so a transient outage doesn't lose work.

## Legal / Compliance Grounding

**German E-Rechnungspflicht timeline (verified, Wachstumschancengesetz / §14 UStG):**
- **Since 2025-01-01:** every domestic B2B business must be **able to receive and process** EN 16931 structured e-invoices (no recipient consent needed for a compliant e-invoice). → makes EINV-04/05 (inbound) *legally required now*, not optional.
- **2025-2026:** paper and "other" e-invoices (e.g. plain PDF) may still be **sent** with recipient consent (transition).
- **From 2027-01-01:** businesses with **>€800,000** prior-year turnover **must send** B2B e-invoices; smaller businesses have the transition to end-2027.
- **From 2028-01-01:** **all** domestic B2B must send e-invoices. EDI gets extended transition to end-2027.

**Legally accepted formats:** **XRechnung** (the German CIUS of EN 16931; UBL or CII syntax) and **ZUGFeRD ≥ 2.x in the EN16931 (Comfort) or XRECHNUNG profile** (hybrid PDF/A-3 + CII). ZUGFeRD profiles *below* EN16931 (MINIMUM, BASIC-WL) are **not** valid e-invoices. → generate `Profile.XRechnung` for pure XML and `Profile.XRechnung`/`EN16931` CII for the ZUGFeRD embed.

**"verständlich erklärt" (EINV-03/04 error UX):** KoSIT emits rule IDs (e.g. `BR-DE-15`, `BR-CO-10`) and technical schematron messages. The criterion demands human-readable explanations. Recommendation: maintain a DE (authoritative) + EN map of the common BR-DE-*/BR-CO-*/BR-* rule IDs → plain-language cause + fix ("Die Leitweg-ID (BT-10) fehlt — für Rechnungen an öffentliche Auftraggeber ist sie Pflicht."), fall back to the raw message for unmapped rules, and group findings by severity (error blocks, warning informs). Surface in the existing react-i18next error UX.

## Code Examples

### Reading an inbound e-invoice (EINV-04/05)
```csharp
// 1. Detect + extract. If PDF (ZUGFeRD): pull the embedded factur-x.xml/zugferd-invoice.xml/xrechnung.xml
//    via PdfPig; if raw XML (XRechnung UBL/CII): use the bytes directly.
byte[] xml = isPdf ? PdfEmbeddedXml.Extract(pdfBytes) : uploadBytes;

// 2. Parse — auto-detects ZUGFeRD version + UBL/CII.  Source: ZUGFeRD-csharp docs.
InvoiceDescriptor inv = InvoiceDescriptor.Load(new MemoryStream(xml));

// 3. Validate (reuse the KoSIT sidecar). 4. Match supplier by VatId then name.
var partner = await _matcher.MatchSeller(inv.Seller.SpecifiedTaxRegistration /* VatId */, inv.Seller.Name);
// 5. Store the ORIGINAL bytes immutably + a jsonb read-model + PartnerId in inbound_document (RLS).
```

## State of the Art
| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| PDF-only invoices with recipient consent | Structured EN 16931 mandatory (receive 2025, send phased to 2028) | §14 UStG reform | Inbound (EINV-04/05) is a legal must *today*. |
| iText/paid PDF libs for PDF/A-3 | QuestPDF native PDF/A-3b + ZUGFeRD (2024.12+) | already in repo | No new PDF engine, no AGPL. |
| ZUGFeRD-csharp fully-OSS | Open-core: create+read stays OSS (Apache-2.0), new features commercial | v18 (2026-03) | Our needs (XRechnung create + read) are OSS; note the seam. |
| XRechnung 2.x, config pre-2024 | XRechnung 3.0.x, config 2025-03-21, BusinessProcess required (3.0.1+) | 2024-2025 | Set `BusinessProcess`; pin config 2025-03-21. |

## Architecture Fit & Plan Boundaries

**Module placement (recommendation):** Keep **outbound** e-invoice code in `Numera.Modules.Sales` (`EInvoice/`) — it is tightly bound to the frozen sales model, `SnapshotReader`, and the QuestPDF seam that already live there. Keep **inbound** as a distinct entity set (`EInvoice/Inbound/`) in the same module for v1 to avoid a new csproj/DI/host-wiring cost; it references `BusinessPartner` (Crm) which Sales can reference. Flag a future `Numera.Modules.Purchasing` extraction as a seam, not v1 work. Jobs live in `Numera.Api/Jobs` and run on the **Api default Hangfire queue** (the LOCKED Phase-4 decision — the `Numera.Worker` host lacks Sales/QuestPDF refs).

**New RLS tables (hand-written policy in the migration, per `Sql/rls_policies.sql` pattern):**
- `document_einvoice` — generated artifact bytes + `EInvoiceFormat` + profile + `ValidationStatus` + report (jsonb). `ITenantEntity`, `DocumentId` provenance-only FK, insert via `db.Add`, idempotent replace per (document, format) — mirror `DocumentRender`.
- `inbound_document` — immutable original bytes + detected format + parsed read-model (jsonb) + `ValidationStatus`/report + matched `PartnerId` + upload metadata. `ITenantEntity` + RLS.

**Suggested plans & waves:**

| Plan | Scope | Requirements | Depends on |
|------|-------|--------------|-----------|
| **05-01** | `EInvoiceMapper` frozen-model→`InvoiceDescriptor`; emit XRechnung UBL+CII; golden-file tests per VAT scenario | EINV-01 (core) | Phase 3/4 model (done) |
| **05-02** | KoSIT sidecar in docker-compose; `IEInvoiceValidator` HttpClient; report→human-readable DE/EN findings | EINV-03 (engine) | none (parallel with 05-01) |
| **05-03** | XRechnung generate + **validation gate** (pre-finalize dry-run + send gate) + download + email; `document_einvoice` table | EINV-01, EINV-03 | 05-01, 05-02 |
| **05-04** | ZUGFeRD PDF/A-3: extend `InvoiceDocument` (PdfA + embed CII from the SAME descriptor); veraPDF check; value-identity test | EINV-02 | 05-01, Phase-4 PDF (done) |
| **05-05** | Inbound: upload + format-detect + PdfPig extract + `InvoiceDescriptor.Load` + KoSIT validate + human view + supplier match + `inbound_document` table | EINV-04, EINV-05 | 05-01 (parse reuse), 05-02 |

**Waves:**
- **Wave 1 (parallel):** 05-01 (mapper) ‖ 05-02 (validator sidecar). Independent.
- **Wave 2:** 05-03 (XRechnung + gate) — needs both wave-1 plans.
- **Wave 3 (parallel):** 05-04 (ZUGFeRD PDF) ‖ 05-05 (inbound). 05-04 needs 05-01; 05-05 needs 05-01+05-02. Both can start once wave 1 lands; sequencing 05-03 first is recommended so the validation-gate + `document_einvoice` pattern exists for 05-04 to reuse.

## Open Questions

1. **Exact QuestPDF ZUGFeRD packaging.** The embed API (`DocumentSettings.PdfA`, `DocumentOperation.AddAttachment/ExtendMetadata`) is documented in **core** QuestPDF (2024.12+, official example). Phase-4 research referenced a separate "QuestPDF.ZUGFeRD" module.
   - Known: the API works via core `QuestPDF.Fluent`/`DocumentOperation`; QuestPDF 2026.7.1 installed.
   - Unclear: whether a companion package is needed or the ZUGFeRD XMP helper is bundled.
   - Recommendation: 05-04 spikes the core API first (no new package); add a companion only if a helper is required. MEDIUM confidence.

2. **KoSIT report schema parsing.** The validator returns an XML report with an `accept/reject` assessment; the exact element paths depend on the report scenario config.
   - Known: daemon `-D` HTTP mode; POST XML → XML report with rule findings + acceptance status.
   - Unclear: precise XPath for findings/severity in the pinned 2025-03-21 config.
   - Recommendation: 05-02 captures a real report from the pinned image and pins the parser to it (golden-file). MEDIUM confidence.

3. **Inbound PDF extraction library choice.** PdfPig (MIT) verified to read embedded files; ZUGFeRD-csharp may also expose PDF reading.
   - Recommendation: default to PdfPig; check ZUGFeRD-csharp's PDF facility during 05-05 to possibly drop a dependency.

## Sources

### Primary (HIGH confidence)
- `questpdf.com/examples/zugferd.html` — PdfA setting, `DocumentOperation.AddAttachment`, `Relationship.Source`, `ExtendMetadata`.
- `github.com/stephanstapel/ZUGFeRD-csharp` (+ `/docs`) — v18.0.0, Apache-2.0, `InvoiceDescriptor.Save(version, profile, format)`, `Load` auto-detect, `Profile.XRechnung`, `ZUGFeRDFormats.UBL/CII`, `BusinessProcess` requirement.
- `nuget.org/packages/ZUGFeRD-csharp` (18.0.0), `nuget.org/packages/QuestPDF` (2026.7.1).
- `github.com/itplr-kosit/validator` + `itplr-kosit/validator-configuration-xrechnung` (config 2025-03-21, XRechnung 3.0.x) — daemon `-D` HTTP mode, XML report + acceptance.
- `bundesfinanzministerium.de` FAQ + IHK Frankfurt/Darmstadt — E-Rechnungspflicht timeline (2025 receive; 2027 >€800k send; 2028 all).
- Codebase: `InvoicePdfModel.cs` (BT/BG annotations), `Pflichttext.cs` (VATEX codes), `TaxCategory.cs` (S/AE/K/E/Z/G), `InvoiceDocument.cs:27-29` (ZUGFeRD seam), `RenderDocumentPdfJob.cs`, `DocumentPdfService.cs`, `DocumentRender.cs`, `EnqueuePdfOnFinalize.cs`, `Sql/rls_policies.sql`, `BusinessPartner.cs`+`VatId.cs`, `docker-compose.yml` (Mailpit pattern).

### Secondary (MEDIUM confidence)
- `uglytoad.github.io/PdfPig` — MIT, reads PDF embedded files (inbound extraction).
- `apps4everything/kosit-docker`, `flexness/xrechnung-validator-docker`, DockerHub `apps4everything/kosit-validator-xrechnung` — community daemon images (dev reference; pin official for prod).
- `textcontrol.com` (2021) — extracting ZUGFeRD attachments from PDF/A-3b (approach confirmation).

## Metadata

**Confidence breakdown:**
- Standard stack: **HIGH** — libraries verified on NuGet/official repos; codebase fit inspected directly.
- Architecture / plan boundaries: **HIGH** — grounded in existing Phase-4 job/RLS/seam patterns.
- Validation gate design (Pitfall 1): **HIGH** on the reconciliation logic; the two-stage approach is a recommendation, not a locked decision.
- QuestPDF ZUGFeRD packaging + KoSIT report parsing: **MEDIUM** — flagged as spikes in Open Questions.
- Legal timeline: **HIGH** — multiple official/IHK/BMF sources agree.

**Research date:** 2026-07-14
**Valid until:** ~2026-08-14 (30 days; watch XRechnung config releases + ZUGFeRD-csharp minor bumps — treat those as fast-moving, re-check before 05-02/05-03 start).
