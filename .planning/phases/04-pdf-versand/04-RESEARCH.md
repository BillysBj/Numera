# Phase 4: PDF & Versand - Research

**Researched:** 2026-07-13
**Domain:** Async document rendering (PDF), transactional email dispatch, worker-tier orchestration in a modular .NET monolith
**Confidence:** HIGH

> No CONTEXT.md exists for this phase (no `/gsd:discuss-phase` was run). This research is driven by the ROADMAP goal, DOCS-02/DOCS-03, and the actual Phase-1..3 codebase. Everything below is grounded in concrete file paths.

## Summary

Phase 4 turns finalized documents into professional PDFs on tenant letterhead and emails them to the customer — with rendering pushed off the finalize hot-path onto a durable worker tier. The single most important finding is that **the worker tier already exists and is wired**: the repo ships a Postgres-backed **Hangfire** setup with a dedicated `Numera.Worker` generic-host process (`src/Numera.Worker/Program.cs`, Hangfire server on the `"worker"` queue), the Api hosts a Hangfire server on the default queue (`src/Numera.Api/Program.cs:75-80`), and there is a working end-to-end template job (`src/Numera.Api/Jobs/WelcomeEmailJob.cs`) that demonstrates the exact production pattern: **enqueue-after-commit** (`RegistrationService.cs:126`) + **re-establish tenant context in a fresh DI scope** (`WelcomeEmailJob.SendAsync`) so RLS applies inside the job. Phase 4 should copy this pattern, not invent a new outbox/BackgroundService. Hangfire.PostgreSql *is* the durable queue that satisfies "blockiert die Finalisierung nicht" and survives process restarts.

The PDF engine question is now decisively answered in QuestPDF's favour. The earlier OPEN concern ("QuestPDF PDF/A-3b conformance — spike in Phase 4/5, fallback iText") is **resolved**: QuestPDF (current 2026.7.1, MIT-community-licensed under the $1M revenue threshold — Numera qualifies) ships **first-class PDF/A-3b generation and a dedicated `QuestPDF.ZUGFeRD` module** with `DocumentOperation.AddAttachment` / `ExtendMetadata` for embedding Factur-X/ZUGFeRD XML. This means the Phase-4 PDF foundation directly upgrades into the Phase-5 ZUGFeRD path with no engine swap and no iText fallback. Render from the **frozen document snapshot** (`IssuerSnapshot`/`RecipientSnapshot` jsonb + persisted `Lines` + `TaxBreakdown` + totals on `SalesDocument`), never live master data — the snapshot already carries §14-complete data (verified below, with two small gaps flagged).

Email uses **MailKit/MimeKit** (the standard; `System.Net.Mail.SmtpClient` is obsolete as of .NET 9). Local/testing uses **Mailpit** added to `docker-compose.yml` (SMTP 1025 + web UI 8025) so no real mail is sent. Send + render status live in **new RLS-scoped tables** (`document_render`/`document_email`) following the exact hand-written RLS migration pattern already used six times in `src/platform/Numera.Platform.Db/Migrations/`.

**Primary recommendation:** Render PDFs with QuestPDF 2026.x from the frozen snapshot inside a Hangfire job on the `worker` queue, enqueued by an `IDomainEventHandler<InvoiceFinalized>` (and by an explicit "generate PDF" endpoint for on-demand/older docs); store the rendered blob in a new RLS-scoped `document_render` table (bytea) keyed to the immutable document; email via MailKit against Mailpit locally, tracking status in a `document_email` table with Hangfire's built-in retry.

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Hangfire.AspNetCore + Hangfire.PostgreSql | 1.8.* / 1.20.* (ALREADY INSTALLED) | Durable background job queue on Postgres; the worker tier | Already wired in Api + `Numera.Worker`; jobs survive restarts (persisted in PG); built-in retry/backoff. NOT MediatR (commercial, banned per repo). |
| QuestPDF | 2026.7.1 (latest; 2025.7.4+ also fine) | Code-first PDF layout engine; PDF generation | Fluent C# layout, no HTML/Chromium dependency, bundles SkiaSharp; free community license under $1M revenue; **native PDF/A-3b + ZUGFeRD** (the Phase-5 upgrade path). |
| QuestPDF.ZUGFeRD | matches QuestPDF (Phase 5, seam now) | Embed Factur-X/ZUGFeRD XML into PDF/A-3 | Same engine → Phase-4 layout becomes Phase-5 e-invoice with no rewrite. Do NOT add in Phase 4; note the seam. |
| MailKit + MimeKit | MailKit 4.* (latest stable) | SMTP send with attachments + MIME building | `System.Net.Mail.SmtpClient` is obsolete/discouraged in .NET 9+; MailKit is the community + Microsoft-recommended replacement. Multipart (HTML+text) + attachments via `BodyBuilder`. |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| Mailpit (docker image `axllent/mailpit`) | latest | Local SMTP sink + web UI (ports 1025/8025) | Add to `docker-compose.yml` for dev; capture outbound mail without sending. Also usable in integration tests (has an HTTP API to assert received messages). |
| SkiaSharp.NativeAssets.Linux.NoDependencies | matches QuestPDF's transitive SkiaSharp | Native rendering libs for Linux containers | Only if the deploy base image lacks fontconfig; see Pitfall 3. Not needed for Windows dev. |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| QuestPDF | iText 8 | iText is AGPL (commercial license required for closed-source SaaS — expensive) OR paid. QuestPDF community is free under $1M and now matches iText on PDF/A-3/ZUGFeRD. **No reason to use iText.** |
| QuestPDF | Playwright/Chromium HTML→PDF | Heavy runtime (headless browser), harder PDF/A-3 conformance, worse for embedded-XML e-invoicing. Rejected. |
| Hangfire | Custom outbox table + `BackgroundService` | Would re-implement durability, retry, scheduling, dashboard that Hangfire already provides and that is already wired. Rejected — do not hand-roll. |
| Hangfire | Wolverine / MassTransit | Overkill; new dependency + broker; repo already standardized on Hangfire. Rejected. |
| MailKit | SendGrid/Postmark API | Provider lock-in + external account; v1 wants simple SMTP + testability. Keep an `IEmailSender` seam so a provider can slot in later. SMTP-first. |

**Installation:**
```bash
# Api (render + email + enqueue) and Worker (execute) — Hangfire already present.
dotnet add src/Numera.Api/Numera.Api.csproj package QuestPDF
dotnet add src/Numera.Api/Numera.Api.csproj package MailKit
# The render/email job code must be loadable by the Worker process too:
dotnet add src/Numera.Worker/Numera.Worker.csproj package QuestPDF
dotnet add src/Numera.Worker/Numera.Worker.csproj package MailKit
# docker-compose: add axllent/mailpit service (ports 1025:1025, 8025:8025)
```

> Placement decision the planner must make: the render/email job classes need types from `Numera.Modules.Sales`. Today `Numera.Worker` only references `Numera.Platform.Db`. Either (a) move the job + rendering code into a referenced module/assembly both hosts share, or (b) have the Api enqueue and the Api's own Hangfire server (default queue) execute. Simplest for v1: **run render/email jobs on the Api's default-queue Hangfire server** (the Api already has all module references), and keep `Numera.Worker` as the future scale-out seam. This still satisfies "runs on a worker tier, doesn't block finalize" because Hangfire executes jobs on background threads out-of-band from the HTTP request. If true process isolation is required, add the module references to `Numera.Worker` and route these jobs to the `"worker"` queue.

## Architecture Patterns

### Recommended Project Structure
```
src/modules/Numera.Modules.Sales/
├── Pdf/
│   ├── InvoiceDocument.cs         # QuestPDF IDocument — the §14 layout (DE/EN)
│   ├── InvoicePdfModel.cs         # flat render model built FROM the frozen snapshot
│   └── SnapshotReader.cs          # parse IssuerSnapshot/RecipientSnapshot jsonb → model
├── Rendering/
│   └── DocumentRender.cs          # NEW ITenantEntity: rendered PDF blob (RLS)
├── Email/
│   └── DocumentEmail.cs           # NEW ITenantEntity: send record + status (RLS)
src/Numera.Api/
├── Jobs/
│   ├── RenderDocumentPdfJob.cs    # Hangfire job: render + persist blob (tenant re-established)
│   └── SendDocumentEmailJob.cs    # Hangfire job: build MIME + SMTP send + status update
├── Events/
│   └── EnqueuePdfOnFinalize.cs    # IDomainEventHandler<InvoiceFinalized> → BackgroundJob.Enqueue
├── Services/
│   ├── IEmailSender.cs / MailKitEmailSender.cs   # SMTP seam
│   └── DocumentPdfService.cs      # shared render entrypoint (endpoint + job call it)
└── Endpoints/
    └── (extend SalesDocumentEndpoints.cs): GET /{id}/pdf, POST /{id}/send
src/platform/Numera.Platform.Db/Migrations/
└── XXXX_DocumentDelivery.cs       # document_render + document_email tables + hand-written RLS
```

### Pattern 1: Enqueue-after-commit via the existing domain-event seam
**What:** Finalize already publishes `InvoiceFinalized` AFTER commit through `IDomainEventPublisher` (`SalesDocumentEndpoints.cs:386`, no-op today with zero handlers). Register an `IDomainEventHandler<InvoiceFinalized>` in the Api that enqueues the render job. This is the intended hook — the seam was built for exactly this (`IDomainEventPublisher.cs` docstring names "Phase-4 PDF render").
**When to use:** Auto-render on finalize.
**Example:**
```csharp
// Source: mirrors RegistrationService.cs:126 enqueue-after-commit + IDomainEventHandler seam
public sealed class EnqueuePdfOnFinalize : IDomainEventHandler<InvoiceFinalized>
{
    private readonly IBackgroundJobClient _jobs;
    public EnqueuePdfOnFinalize(IBackgroundJobClient jobs) => _jobs = jobs;

    public Task HandleAsync(InvoiceFinalized e, CancellationToken ct)
    {
        // Enqueue only — never render inline (keeps finalize fast). Job re-establishes tenant.
        _jobs.Enqueue<RenderDocumentPdfJob>(j => j.RunAsync(e.TenantId, e.DocumentId, CancellationToken.None));
        return Task.CompletedTask;
    }
}
// Register in Program.cs: builder.Services.AddScoped<IDomainEventHandler<InvoiceFinalized>, EnqueuePdfOnFinalize>();
```
> Durability note: the publisher fires *after* `tx.CommitAsync`, so there is a millisecond window where a crash between commit and `Enqueue` loses the trigger. This matches the existing `WelcomeEmailJob` risk and is acceptable for v1 (the GET /{id}/pdf endpoint renders on demand as a fallback/idempotent recovery — a missing render is always recoverable because the document is immutable). A stricter transactional-outbox (enqueue inside the finalize tx via `Hangfire.PostgreSql` connection enlistment) is possible but NOT recommended for v1: it couples finalize to Hangfire's schema and the on-demand render path already closes the gap. Flag as an Open Question if the planner wants belt-and-suspenders.

### Pattern 2: Re-establish tenant context inside the job (RLS-safe background work)
**What:** A Hangfire job runs with no HttpContext. It must open its own DI scope and call `ICurrentTenant.SetTenant(tenantId)` BEFORE touching `NumeraDbContext`, so `TenantConnectionInterceptor` sets `app.current_tenant` and RLS applies exactly as in a request.
**When to use:** Every render/email job.
**Example:**
```csharp
// Source: WelcomeEmailJob.cs:39-52 (verbatim production pattern)
public async Task RunAsync(Guid tenantId, Guid documentId, CancellationToken ct)
{
    using var scope = _scopeFactory.CreateScope();
    scope.ServiceProvider.GetRequiredService<ICurrentTenant>().SetTenant(tenantId);
    var db = scope.ServiceProvider.GetRequiredService<NumeraDbContext>();
    var doc = await db.Set<SalesDocument>().AsNoTracking()
        .Include(x => x.Lines).Include(x => x.TaxBreakdown)
        .FirstOrDefaultAsync(x => x.Id == documentId, ct);   // RLS-scoped read
    // ... render from doc.IssuerSnapshot / doc.RecipientSnapshot / doc.Lines / doc.TaxBreakdown ...
}
```

### Pattern 3: Render from the FROZEN snapshot, never live master data
**What:** The PDF must reflect the invoice exactly as finalized (GoBD). All required data is already frozen on the row: `IssuerSnapshot` (jsonb: LegalName, Address, VatId/TaxNumber, IsKleinunternehmer, Bank {Iban,Bic,BankName}, RegisterCourt/Number, ManagingDirector, ContactEmail/Phone — see `SerializeIssuer`, `SalesDocumentEndpoints.cs:801`), `RecipientSnapshot` (Name, LegalForm, BillingAddress, VatId/TaxNumber, Email — `SerializeRecipient`:825), persisted `Lines`, `TaxBreakdown` (BG-23 with `ExemptionReasonCode`/`Text` Pflichttexte), `TotalNet/TotalTax/TotalGross/AmountDue`, `DocumentNumber`, `DocumentDate`, `DueDate`, `Currency`, `IsKleinunternehmer`, `ReverseCharge`.
**When to use:** Always. Never join back to `CompanyProfile`/`BusinessPartner` for rendering.
**Anti-Pattern:** Reading `CompanyProfile.LogoRef` live at render time is acceptable ONLY for the logo asset (logo is presentation, not legal content, and not snapshotted) — but treat a later logo change as affecting only re-renders; document this choice.

### Pattern 4: New RLS-scoped tables via hand-written migration SQL
**What:** `document_render` and `document_email` are `ITenantEntity` but reflective discovery does NOT emit RLS policies — they MUST be hand-written in the migration (the #1 silent-leak trap, per Phase-2 decision log).
**Example:**
```csharp
// Source: 20260712145717_CompanyProfile.cs:65-70 (the six-times-repeated pattern)
migrationBuilder.Sql("ALTER TABLE document_render ENABLE ROW LEVEL SECURITY;");
migrationBuilder.Sql("ALTER TABLE document_render FORCE ROW LEVEL SECURITY;");
migrationBuilder.Sql(
    "CREATE POLICY tenant_isolation ON document_render " +
    "USING (tenant_id = current_setting('app.current_tenant')::uuid) " +
    "WITH CHECK (tenant_id = current_setting('app.current_tenant')::uuid);");
```

### Anti-Patterns to Avoid
- **Rendering inline in the finalize request:** violates success criterion 3; QuestPDF+Skia is CPU-heavy. Always enqueue.
- **Building a custom job/outbox/`BackgroundService`:** Hangfire is already the standard here. Re-inventing it is wasted work and less durable.
- **Storing the logo as `LogoRef` = a filesystem path:** the app is multi-tenant SaaS with no shared FS guarantee. Store the logo bytes in Postgres (bytea, RLS-scoped) — see Open Question 1.
- **Re-reading `IValidator`/live partner for the PDF:** breaks GoBD immutability; use the snapshot.
- **Calling `QuestPDF.Settings.License = ...` incorrectly / not setting it:** QuestPDF requires `QuestPDF.Settings.License = LicenseType.Community;` at startup or it throws. Set once in Program.cs (both hosts).

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Durable async job execution | Custom outbox table + polling `BackgroundService` + retry loop | Hangfire (already wired) | Persistence, retry/backoff, scheduling, dashboard, tenant-scope pattern already solved in-repo. |
| PDF layout/rendering | Manual PDF byte streams / HTML+headless Chromium | QuestPDF | Fluent layout, pagination, PDF/A-3, ZUGFeRD — all native. |
| MIME construction + SMTP | `System.Net.Mail` / raw sockets | MailKit + MimeKit `BodyBuilder` | `SmtpClient` obsolete in .NET 9; MailKit handles multipart, attachments, TLS, encodings. |
| Local mail testing | Fake SMTP stub | Mailpit container | Real SMTP + inspectable web UI + HTTP API for test assertions. |
| PDF/A-3 + embedded XML (Phase 5) | Manual XMP/AFRelationship plumbing | QuestPDF.ZUGFeRD `AddAttachment`/`ExtendMetadata` | Mustang-validated ZUGFeRD conformance out of the box. |
| Retry on transient SMTP failure | Custom retry queue | Hangfire `[AutomaticRetry]` on the send job | Built-in exponential backoff + poison-job handling. |

**Key insight:** Phase 4 is almost entirely *integration* work — the durable-queue, tenant-scoping, and immutable-snapshot substrates already exist. The net-new code is one QuestPDF layout, two job classes, one email seam, one migration, and frontend buttons. Resist building infrastructure.

## Common Pitfalls

### Pitfall 1: QuestPDF license not configured → runtime throw
**What goes wrong:** QuestPDF throws on first `GeneratePdf` if no license type is set.
**Why it happens:** Community license must be explicitly acknowledged.
**How to avoid:** `QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;` in BOTH `Program.cs` files (Api and, if used, Worker) at startup. Numera qualifies (revenue < $1M).
**Warning signs:** `Exception: QuestPDF.Drawing.Exceptions...license` on first render.

### Pitfall 2: Job assembly can't load render/email types in the Worker process
**What goes wrong:** Enqueueing `RenderDocumentPdfJob` (which lives in/references `Numera.Modules.Sales`) but routing it to the `"worker"` queue, whose host (`Numera.Worker`) only references `Numera.Platform.Db` → Hangfire can't deserialize/activate the job.
**Why it happens:** `Numera.Worker.csproj` lacks the Sales/QuestPDF references.
**How to avoid:** For v1, execute these jobs on the **Api's default-queue** Hangfire server (Api already references every module). Only route to `"worker"` after adding the module + QuestPDF/MailKit references to `Numera.Worker`.
**Warning signs:** Jobs stuck in "Enqueued"/"Failed - could not load type" in the Hangfire dashboard.

### Pitfall 3: SkiaSharp native/font failure in Linux containers
**What goes wrong:** `DllNotFoundException: libSkiaSharp` or `CompatibilityChecker`/fontmanager failure, or blank/□ glyphs, when deployed to a slim Linux image.
**Why it happens:** QuestPDF's SkiaSharp needs `libfontconfig1`/`libfreetype6` and at least one installed font; slim images ship none.
**How to avoid:** In the deploy Dockerfile: `RUN apt-get update && apt-get install -y libfontconfig1 fontconfig` and install/register a font (e.g. bundle a TTF and `FontManager.RegisterFont`). Consider `SkiaSharp.NativeAssets.Linux.NoDependencies`. Dev on Windows is unaffected. (Note: local docker-compose only runs postgres/keycloak/mailpit today; the Api runs on the host — so this bites only at real deployment. Flag but don't block.)
**Warning signs:** Works on dev Windows, fails/blank in container.

### Pitfall 4: German number/date/currency formatting in the PDF
**What goes wrong:** PDF shows `1,234.56` / `2026-07-13` instead of `1.234,56 €` / `13.07.2026` in the German layout.
**Why it happens:** Rendering under invariant/`en-US` culture; jobs have no request culture.
**How to avoid:** Format money/dates with an explicit `CultureInfo` chosen by the document's target language (DE default), not ambient culture. Mirror the frontend's `Intl` de-DE conventions already used in `web/src/features`. The document language is a render input (see Open Question 2).
**Warning signs:** Locale-wrong output in the worker even though the UI looks right.

### Pitfall 5: Emailing a not-yet-rendered document
**What goes wrong:** `POST /{id}/send` runs before the async render finished → no attachment.
**Why it happens:** Render and send are separate async steps.
**How to avoid:** Make the send job depend on the render: either `BackgroundJob.ContinueJobWith` (Hangfire continuation) or have the send job render-if-absent (idempotent). The `document_render` row's presence is the readiness signal. The UI should reflect "PDF wird erstellt…" state.
**Warning signs:** Emails sent with a missing/empty attachment.

### Pitfall 6: Bilingual layout drift / missing §14 text
**What goes wrong:** English layout omits mandatory German legal texts, or Kleinunternehmer §19 / Reverse-Charge §13b Pflichttext is dropped.
**Why it happens:** Two layouts maintained separately; Pflichttext lives in `TaxBreakdown.ExemptionReasonText` (already persisted) but the layout must actually print it.
**How to avoid:** One `InvoiceDocument` with a resource-driven label set (DE/EN); ALWAYS render `TaxBreakdown[].ExemptionReasonText` verbatim (it is the legally-frozen note). A §14-complete invoice for a German issuer keeps German legal notes even in an English layout — confirm with the planner whether "English layout" means English *labels* over German legal content (recommended) vs full translation.

## Code Examples

### QuestPDF: plain PDF vs PDF/A-3b (the Phase-5 seam)
```csharp
// Source: https://www.questpdf.com/examples/zugferd.html (HIGH — official docs)
// Phase 4: a normal, high-quality PDF
Document.Create(doc => { /* page/header/lines/totals/§14 blocks */ })
    .WithMetadata(new DocumentMetadata { Title = number, Author = issuerLegalName })
    .GeneratePdf(); // returns byte[] overload available → store in document_render.pdf_bytes

// Phase 5 (seam, DO NOT build now): PDF/A-3b + embedded ZUGFeRD XML
Document.Create(doc => { /* same layout */ })
    .WithSettings(new DocumentSettings { PdfA = true })
    .GeneratePdf("invoice.pdf");
DocumentOperation.LoadFile("invoice.pdf")
    .AddAttachment(new DocumentOperation.DocumentAttachment {
        Key = "factur-x", FilePath = "factur-x.xml", MimeType = "text/xml" })
    .ExtendMetadata(zugferdXmpMetadata)
    .Save("zugferd-invoice.pdf");
```

### MailKit: send finalized PDF as attachment
```csharp
// Source: https://github.com/jstedfast/MailKit + conradakunga MailKit series (MEDIUM, cross-verified)
var msg = new MimeMessage();
msg.From.Add(MailboxAddress.Parse(issuerContactEmail));
msg.To.Add(MailboxAddress.Parse(recipientEmail));
msg.Subject = subject; // bilingual, from resources by document language
var body = new BodyBuilder { HtmlBody = htmlBody, TextBody = textBody };
body.Attachments.Add($"{documentNumber}.pdf", pdfBytes, new ContentType("application", "pdf"));
msg.Body = body.ToMessageBody();

using var smtp = new MailKit.Net.Smtp.SmtpClient();
await smtp.ConnectAsync(host, port, SecureSocketOptions.Auto, ct); // Mailpit: localhost:1025, None
// if (auth) await smtp.AuthenticateAsync(user, pass, ct);
await smtp.SendAsync(msg, ct);
await smtp.DisconnectAsync(true, ct);
```

### docker-compose Mailpit service
```yaml
# Source: https://hub.docker.com/r/axllent/mailpit (HIGH)
  mailpit:
    image: axllent/mailpit:latest
    container_name: numera-mailpit
    ports:
      - "1025:1025"   # SMTP
      - "8025:8025"   # web UI + HTTP API
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| iText for German e-invoice PDFs | QuestPDF community + QuestPDF.ZUGFeRD | 2024–2025 (ZUGFeRD module matured) | Free (under $1M), single engine Phase 4→5. Resolves the "iText fallback" open concern. |
| `System.Net.Mail.SmtpClient` | MailKit/MimeKit | .NET 6+ obsolete guidance; hard-flagged .NET 9 | Use MailKit. |
| Roll-your-own outbox | Postgres-backed Hangfire | already adopted in this repo (Phase 1) | Reuse; don't rebuild. |

**Deprecated/outdated:**
- `System.Net.Mail.SmtpClient`: Microsoft recommends against it; use MailKit.
- The "spike QuestPDF PDF/A-3b, fallback iText" plan: superseded — QuestPDF now supports it natively (verify the exact API against the installed version during planning, but the capability is confirmed).

## Open Questions

1. **Logo/Briefpapier storage.**
   - What we know: `CompanyProfile.LogoRef` (nullable string) exists as a seam (`CompanyProfile.cs:101`) but nothing writes/reads it; no asset table exists; the app is multi-tenant SaaS with RLS and no guaranteed shared filesystem.
   - What's unclear: bytea column on `company_profile` vs a dedicated `company_asset` table vs object storage.
   - Recommendation: **Postgres for v1** — either a `logo_bytes bytea` + `logo_content_type` on `company_profile` (simplest; one logo per tenant, already RLS-scoped and unique per tenant) or a small `company_asset` table if multiple assets are foreseen. Object storage (S3/MinIO) is over-engineering for v1 and adds infra. Add an upload endpoint (`PUT /api/company-profile/logo`, size/type-validated PNG/JPG) + a settings UI field. The logo is presentation, so reading it live at render time is fine (not part of the frozen legal snapshot).

2. **Meaning of "deutsches und englisches Layout" (success criterion 1).**
   - What we know: The app is DE-default bilingual; documents carry legally-frozen German Pflichttexte in `TaxBreakdown`.
   - What's unclear: Is the document language chosen per-send, per-customer (BusinessPartner has a language?), or per-document? Does "English layout" translate labels only, or also legal notes?
   - Recommendation: Add a `language` (de/en) input to the render (default de; optionally default from the partner). Translate *labels* via a resource set; render frozen legal Pflichttexte verbatim (they remain legally German for a German issuer). Confirm with the user during planning. Check whether `BusinessPartner`/`RecipientSnapshot` should carry a preferred language (currently it does not — possible small addition).

3. **Snapshot completeness for a §14-perfect PDF — two small gaps.**
   - What we know: The issuer/recipient snapshots + lines + breakdown + totals cover the §14 mandatory set. Verified against `SerializeIssuer`/`SerializeRecipient`.
   - Gaps flagged: (a) **Line-level snapshot does not persist a per-line VAT amount or line description formatting beyond `Description`** — fine, breakdown carries tax; confirm the layout computes line gross from net+category if needed. (b) **`ServiceDate`/`ServicePeriodEnd`/`BuyerReference`/`Notes`** live on the document (not the jsonb snapshot) but ARE persisted and immutable post-finalize — render them from the document row. (c) The recipient snapshot omits a **contact person/`ShippingAddress`** — not §14-mandatory, ignore for v1. No blocking gap.
   - Recommendation: No new snapshot fields required for a compliant invoice PDF. Confirm the layout pulls service date + payment terms (bank block from `IssuerSnapshot.Bank`) — all present.

4. **PDF lifecycle: store the blob vs regenerate on demand.**
   - What we know: The document is immutable post-finalize, so a rendered PDF is deterministic and cacheable.
   - Recommendation: **Store the rendered bytes** in `document_render` (bytea) keyed to the document, rendered once on finalize (and on-demand for pre-existing docs). This gives a stable byte-identical artifact for GoBD/audit and for the Phase-5 ZUGFeRD attachment, and avoids re-rendering on every download/email. Treat it as immutable alongside the document (a logo change does not retro-alter an issued PDF unless explicitly re-rendered — decide policy; recommend: keep the original, allow explicit re-render that supersedes).

5. **Send status + retry model.**
   - What we know: Hangfire provides retry; we need a tenant-visible send record.
   - Recommendation: `document_email` row (tenant, document_id, to_address, status enum Queued/Sent/Failed, error, sent_at, attempt_count). The send job updates it; `[AutomaticRetry(Attempts = n)]` handles transient SMTP faults. Surface status on the document detail page. Flip `SalesDocument.SentAt` (already a whitelisted lifecycle column, `SalesDocument.cs:137`) on first successful send — the immutability trigger already permits it.

6. **Where do render/email jobs execute (Api default queue vs Numera.Worker)?**
   - Recommendation: v1 = Api default-queue server (has all references); keep `Numera.Worker` as the documented scale-out path. Covered in Pitfall 2 + the Installation note. Non-blocking; planner picks.

## Sources

### Primary (HIGH confidence)
- Repo source (read directly): `src/Numera.Worker/Program.cs`, `src/Numera.Api/Program.cs`, `src/Numera.Api/Jobs/WelcomeEmailJob.cs`, `src/Numera.Api/Services/RegistrationService.cs`, `src/Numera.Api/Endpoints/SalesDocumentEndpoints.cs`, `src/modules/Numera.Modules.Sales/{CompanyProfile,SalesDocument}.cs`, `src/modules/Numera.Modules.Sales/Events/*`, `tests/Numera.IntegrationTests/PostgresFixture.cs`, `src/platform/Numera.Platform.Db/Migrations/20260712145717_CompanyProfile.cs`, `docker-compose.yml`, both `.csproj` files.
- https://www.questpdf.com/examples/zugferd.html — PDF/A-3b + ZUGFeRD API (`WithSettings{PdfA=true}`, `DocumentOperation.AddAttachment/ExtendMetadata`).
- https://www.nuget.org/packages/QuestPDF — version 2026.7.1 (2026-07-11), targets net10.0, SkiaSharp-based.
- https://www.questpdf.com/license/community.html and /pricing.html — free under $1M gross revenue; Pro $999 / Ent $2,999.
- https://github.com/axllent/mailpit + https://hub.docker.com/r/axllent/mailpit — Mailpit SMTP 1025 / UI 8025.

### Secondary (MEDIUM confidence)
- https://github.com/QuestPDF/QuestPDF/tree/main/Source/QuestPDF.ZUGFeRD — dedicated ZUGFeRD module exists.
- MailKit as the `SmtpClient` replacement (obsolete in .NET 9): dev.to/adrianbailador MailKit .NET 9 guide; conradakunga MailKit series (attachments via `BodyBuilder`).
- QuestPDF Linux/Docker font pitfalls: GitHub issues #676, #700, #1406, #266 (libfontconfig/SkiaSharp).

### Tertiary (LOW confidence)
- Medium/IronSoftware licensing summary (cross-checked against official QuestPDF pages — treat official pages as authoritative).

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — worker tier + Hangfire verified in-repo; QuestPDF/MailKit verified against official docs/NuGet.
- Architecture: HIGH — patterns are direct extensions of existing, read production code (domain-event seam, tenant re-establishment, RLS migration).
- Pitfalls: HIGH — license/font/queue-loading issues are documented and cross-verified; formatting/bilingual are reasoned from the codebase.
- Open questions: MEDIUM — logo storage and document-language semantics are genuine product decisions for the planner/user, not technical unknowns.

**Research date:** 2026-07-13
**Valid until:** 2026-08-13 (30 days; QuestPDF releases monthly — re-check the exact PDF/A API against the pinned version at plan time, but the capability is stable).
