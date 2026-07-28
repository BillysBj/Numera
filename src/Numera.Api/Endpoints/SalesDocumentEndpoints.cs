using System.Text.Json;

using FluentValidation;

using Hangfire;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using Numera.Api.Contracts;
using Numera.Api.Jobs;
using Numera.Api.Services;
using Numera.Api.Validators;
using Numera.Modules.Crm;
using Numera.Modules.Sales;
using Numera.Modules.Sales.EInvoice;
using Numera.Modules.Sales.Email;
using Numera.Modules.Sales.Events;
using Numera.Modules.Sales.Numbering;
using Numera.Modules.Sales.Vat;
using Numera.Platform.Audit;
using Numera.Platform.Db;
using Numera.Platform.Entitlements;
using Numera.Platform.Money;
using Numera.Platform.Tenancy;

namespace Numera.Api.Endpoints;

/// <summary>
/// The sales-document HTTP surface (plan 03-04): draft create/update/delete with a 409
/// app-guard when the document is no longer a Draft, the copy-forward <c>convert</c>
/// endpoint (the Angebot → Auftragsbestätigung → Lieferschein → Rechnung chain), and
/// list/detail reads. Delivers DOCS-01 for the draft lifecycle. Finalize, numbering and
/// Storno land in later plans; this plan owns drafts + reads.
/// </summary>
/// <remarks>
/// Every mutation follows RESEARCH.md Pattern 4: mutate → audit, committed by a single
/// <c>SaveChangesAsync</c> so the change and its audit row are atomic. RLS + the tenant
/// query filter scope all reads. The <c>PUT</c>/<c>DELETE</c> 409 guard mirrors the DB
/// <c>sales_document_immutable</c> trigger so a user editing a finalized document gets a
/// clean Conflict before the raw trigger exception (RESEARCH.md Pattern 2). Each line
/// SNAPSHOTS the catalog data supplied by the client — the catalog is never re-read once
/// a line exists (RESEARCH.md Pitfall 2).
/// </remarks>
public static class SalesDocumentEndpoints
{
    private static readonly JsonSerializerOptions AuditJson = new(JsonSerializerDefaults.Web);

    /// <summary>Maps <c>/api/documents</c> draft CRUD, convert and list/detail.</summary>
    public static IEndpointRouteBuilder MapSalesDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/documents").RequireAuthorization();

        // GET /api/documents — paged, filterable list (RLS-scoped, server-side).
        g.MapGet("/", async (
            NumeraDbContext db,
            CancellationToken ct,
            int page = 1,
            int pageSize = 25,
            string? q = null,
            DocumentType? type = null,
            DocumentStatus? status = null,
            Guid? partnerId = null) =>
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var query = db.Set<SalesDocument>().AsNoTracking();

            if (type is not null)
            {
                query = query.Where(d => d.DocumentType == type);
            }

            if (status is not null)
            {
                query = query.Where(d => d.Status == status);
            }

            if (partnerId is not null)
            {
                query = query.Where(d => d.PartnerId == partnerId);
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                query = query.Where(d => d.DocumentNumber != null && d.DocumentNumber.Contains(q));
            }

            var total = await query.CountAsync(ct).ConfigureAwait(false);

            var items = await query
                .OrderByDescending(d => d.DocumentDate).ThenByDescending(d => d.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(d => new SalesDocumentListItem(
                    d.Id, d.DocumentType, d.Status, d.DocumentNumber,
                    d.PartnerId, d.DocumentDate, d.TotalGross, d.Currency))
                .ToListAsync(ct)
                .ConfigureAwait(false);

            return Results.Ok(new { items, page, pageSize, total });
        });

        // GET /api/documents/{id} — full detail incl. lines, breakdown and chain links.
        g.MapGet("/{id:guid}", async (Guid id, NumeraDbContext db, CancellationToken ct) =>
        {
            var d = await db.Set<SalesDocument>()
                .AsNoTracking()
                .Include(x => x.Lines)
                .Include(x => x.TaxBreakdown)
                .FirstOrDefaultAsync(x => x.Id == id, ct)
                .ConfigureAwait(false);

            if (d is null)
            {
                return Results.NotFound();
            }

            var prepayments = await db.Set<SalesDocumentPrepayment>()
                .AsNoTracking()
                .Where(p => p.DocumentId == d.Id)
                .OrderBy(p => p.AbschlagDate)
                .ThenBy(p => p.AbschlagNumber)
                .Select(p => new SalesDocumentPrepaymentDto(
                    p.AbschlagDocumentId, p.AbschlagNumber, p.AbschlagDate,
                    p.NetAmount, p.VatAmount, p.GrossAmount))
                .ToListAsync(ct)
                .ConfigureAwait(false);

            return Results.Ok(ToDetail(d, prepayments));
        });

        // GET /api/documents/{id}/pdf — download the finalized document's §14 PDF (DOCS-02).
        // Returns the stored render if present, else renders-on-demand from the frozen snapshot
        // (the idempotent recovery path for documents whose async render has not landed yet, or
        // pre-existing ones). Optional ?lang=de|en selects the label set (default de). 404 when
        // the document is not found under RLS; 409 when it is still a Draft (no frozen snapshot).
        g.MapGet("/{id:guid}/pdf", async (
            Guid id,
            DocumentPdfService pdf,
            CancellationToken ct,
            string? lang = null) =>
        {
            var result = await pdf.GetOrRender(id, lang ?? "de", ct).ConfigureAwait(false);
            return result.Result switch
            {
                DocumentPdfService.Outcome.NotFound => Results.NotFound(),
                DocumentPdfService.Outcome.NotFinalized => Results.Problem(
                    title: "Document is not finalized",
                    detail: "Only a finalized document has a PDF; this document is still a Draft.",
                    statusCode: StatusCodes.Status409Conflict),
                _ => Results.File(result.PdfBytes!, "application/pdf", $"{result.DocumentNumber}.pdf"),
            };
        });

        // POST /api/documents/{id}/send — e-mail the finalized document's PDF to the customer
        // (DOCS-03). Records a Queued document_email row (RLS + audit) and ENQUEUES
        // SendDocumentEmailJob after the row commits — never sends inline on the request thread.
        // The job renders-if-absent, sends via MailKit, advances the row and flips SentAt. 404 when
        // the document is not found under RLS; 409 when it is still a Draft (nothing to send); 422
        // when no recipient e-mail is available (neither an override nor a frozen recipient e-mail).
        g.MapPost("/{id:guid}/send", async (
            Guid id,
            SendDocumentEmailRequest? req,
            NumeraDbContext db,
            IAuditWriter audit,
            ICurrentTenant tenant,
            IBackgroundJobClient jobs,
            CancellationToken ct) =>
        {
            var doc = await db.Set<SalesDocument>()
                .AsNoTracking()
                .Select(d => new { d.Id, d.Status, d.DocumentNumber, d.RecipientSnapshot })
                .FirstOrDefaultAsync(d => d.Id == id, ct)
                .ConfigureAwait(false);
            if (doc is null)
            {
                return Results.NotFound();
            }

            if (doc.Status == DocumentStatus.Draft)
            {
                return NonDraftConflict(doc.Status);
            }

            var toAddress = string.IsNullOrWhiteSpace(req?.ToAddress)
                ? ResolveRecipientEmail(doc.RecipientSnapshot)
                : req!.ToAddress!.Trim();
            if (string.IsNullOrWhiteSpace(toAddress))
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]>
                    {
                        ["toAddress"] = ["No recipient e-mail: supply toAddress or set the customer's e-mail."],
                    },
                    statusCode: StatusCodes.Status422UnprocessableEntity,
                    title: "No recipient e-mail address");
            }

            var language = string.Equals(req?.Language, "en", StringComparison.OrdinalIgnoreCase)
                ? "en"
                : "de";
            var subject = DocumentEmailTemplates
                .Build(language, doc.DocumentNumber ?? doc.Id.ToString())
                .Subject;

            var tenantId = tenant.TenantId!.Value;
            var email = new DocumentEmail
            {
                TenantId = tenantId,
                DocumentId = doc.Id,
                ToAddress = toAddress,
                Subject = subject,
                Status = EmailStatus.Queued,
                CreatedAt = DateTimeOffset.UtcNow,
            };

            db.Add(email);
            await audit.RecordAsync(
                new SalesDocumentAuditEvent(
                    "sales_document.email_queued", doc.Id, Before: null,
                    After: JsonSerializer.Serialize(new { email.Id, email.ToAddress, language }, AuditJson)),
                ct).ConfigureAwait(false);

            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            // Enqueue AFTER the row commits (enqueue-after-commit) so the job never races an
            // uncommitted document_email. The job runs on the Api default queue (Pitfall 2, LOCKED).
            jobs.Enqueue<SendDocumentEmailJob>(
                j => j.RunAsync(tenantId, email.Id, language, false, CancellationToken.None));

            return Results.Accepted(
                $"/api/documents/{doc.Id}/send", new { id = email.Id, status = email.Status });
        });

        // POST /api/documents — create a Draft.
        g.MapPost("/", async (
            CreateSalesDocumentRequest req,
            IValidator<CreateSalesDocumentRequest> validator,
            NumeraDbContext db,
            IAuditWriter audit,
            ICurrentTenant tenant,
            IEntitlementService entitlements,
            CancellationToken ct) =>
        {
            var result = await validator.ValidateAsync(req, ct).ConfigureAwait(false);
            if (!result.IsValid)
            {
                return Results.ValidationProblem(result.ToDictionary());
            }

            if (IsDownPaymentDocument(req.DocumentType)
                && !await entitlements.HasCapabilityAsync(Capability.DownPaymentInvoices, ct).ConfigureAwait(false))
            {
                return DownPaymentUpgradeRequired();
            }

            if (req.DocumentType == DocumentType.Schlussrechnung)
            {
                return InvalidPrepayments("Use /api/documents/final-invoice to create a Schlussrechnung.");
            }

            var currency = NormalizeCurrency(req.Currency);
            if (IsForeignCurrency(currency)
                && !await entitlements.HasCapabilityAsync(Capability.ForeignCurrencyInvoicing, ct).ConfigureAwait(false))
            {
                return ForeignCurrencyUpgradeRequired();
            }

            var tenantId = tenant.TenantId!.Value;
            var doc = new SalesDocument
            {
                TenantId = tenantId,
                DocumentType = req.DocumentType,
                Status = DocumentStatus.Draft,
                DocumentNumber = null,
                PartnerId = req.PartnerId,
                DocumentDate = req.DocumentDate,
                ServiceDate = req.ServiceDate,
                Notes = req.Notes,
                BuyerReference = req.BuyerReference,
                Currency = currency,
                ExchangeRate = IsForeignCurrency(currency) ? req.ExchangeRate : null,
                ExchangeRateDate = IsForeignCurrency(currency) ? req.ExchangeRateDate : null,
            };

            ReplaceLines(doc, req.Lines, tenantId);
            // Draft preview: persist line nets + provisional net total; tax/gross stay 0
            // until finalize computes the breakdown (plan 03-05).
            doc.TotalNet = doc.Lines.Sum(l => l.LineNetAmount);

            db.Add(doc);
            await audit.RecordAsync(
                new SalesDocumentAuditEvent("sales_document.created", doc.Id, Before: null, After: Snapshot(doc)), ct)
                .ConfigureAwait(false);

            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            return Results.Created($"/api/documents/{doc.Id}", new { doc.Id });
        });

        // POST /api/documents/abschlag — create an entitled Abschlagsrechnung Draft.
        g.MapPost("/abschlag", async (
            CreateSalesDocumentRequest req,
            IValidator<CreateSalesDocumentRequest> validator,
            NumeraDbContext db,
            IAuditWriter audit,
            ICurrentTenant tenant,
            IEntitlementService entitlements,
            CancellationToken ct) =>
        {
            if (!await entitlements.HasCapabilityAsync(Capability.DownPaymentInvoices, ct).ConfigureAwait(false))
            {
                return DownPaymentUpgradeRequired();
            }

            var forced = req with { DocumentType = DocumentType.Abschlagsrechnung };
            var result = await validator.ValidateAsync(forced, ct).ConfigureAwait(false);
            if (!result.IsValid)
            {
                return Results.ValidationProblem(result.ToDictionary());
            }

            var currency = NormalizeCurrency(forced.Currency);
            if (IsForeignCurrency(currency)
                && !await entitlements.HasCapabilityAsync(Capability.ForeignCurrencyInvoicing, ct).ConfigureAwait(false))
            {
                return ForeignCurrencyUpgradeRequired();
            }

            var tenantId = tenant.TenantId!.Value;
            var doc = BuildDraft(forced, DocumentType.Abschlagsrechnung, tenantId, currency);
            db.Add(doc);
            await audit.RecordAsync(
                new SalesDocumentAuditEvent("sales_document.created", doc.Id, Before: null, After: Snapshot(doc)), ct)
                .ConfigureAwait(false);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            return Results.Created($"/api/documents/{doc.Id}", new { doc.Id });
        });

        // POST /api/documents/final-invoice — create a Schlussrechnung Draft with frozen deductions.
        g.MapPost("/final-invoice", async (
            FinalInvoiceRequest req,
            IValidator<CreateSalesDocumentRequest> validator,
            NumeraDbContext db,
            IAuditWriter audit,
            ICurrentTenant tenant,
            IEntitlementService entitlements,
            CancellationToken ct) =>
        {
            if (!await entitlements.HasCapabilityAsync(Capability.DownPaymentInvoices, ct).ConfigureAwait(false))
            {
                return DownPaymentUpgradeRequired();
            }

            var create = new CreateSalesDocumentRequest(
                DocumentType.Schlussrechnung, req.PartnerId, req.DocumentDate, req.ServiceDate,
                req.Notes, req.BuyerReference, req.Lines, req.Currency,
                req.ExchangeRate, req.ExchangeRateDate);
            var result = await validator.ValidateAsync(create, ct).ConfigureAwait(false);
            if (!result.IsValid)
            {
                return Results.ValidationProblem(result.ToDictionary());
            }

            var ids = req.AbschlagDocumentIds.Distinct().ToArray();
            if (ids.Length != req.AbschlagDocumentIds.Count)
            {
                return InvalidPrepayments("Each Abschlagsrechnung may only be selected once.");
            }

            var abschlaege = await db.Set<SalesDocument>()
                .AsNoTracking()
                .Where(d => ids.Contains(d.Id))
                .ToListAsync(ct)
                .ConfigureAwait(false);
            if (abschlaege.Count != ids.Length
                || abschlaege.Any(d => d.DocumentType != DocumentType.Abschlagsrechnung
                    || d.Status != DocumentStatus.Finalized
                    || d.PartnerId != req.PartnerId
                    || string.IsNullOrWhiteSpace(d.DocumentNumber)))
            {
                return InvalidPrepayments(
                    "Every selected document must be a finalized Abschlagsrechnung for the same partner.");
            }

            var currency = NormalizeCurrency(create.Currency);
            if (IsForeignCurrency(currency)
                && !await entitlements.HasCapabilityAsync(Capability.ForeignCurrencyInvoicing, ct).ConfigureAwait(false))
            {
                return ForeignCurrencyUpgradeRequired();
            }

            var tenantId = tenant.TenantId!.Value;
            var doc = BuildDraft(create, DocumentType.Schlussrechnung, tenantId, currency);
            db.Add(doc);
            foreach (var abschlag in abschlaege)
            {
                db.Add(new SalesDocumentPrepayment
                {
                    TenantId = tenantId,
                    DocumentId = doc.Id,
                    AbschlagDocumentId = abschlag.Id,
                    AbschlagNumber = abschlag.DocumentNumber!,
                    AbschlagDate = abschlag.DocumentDate,
                    NetAmount = abschlag.TotalNet,
                    VatAmount = abschlag.TotalTax,
                    GrossAmount = abschlag.TotalGross,
                });
            }

            await audit.RecordAsync(
                new SalesDocumentAuditEvent("sales_document.final_invoice_created", doc.Id, Before: null, After: Snapshot(doc)), ct)
                .ConfigureAwait(false);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            return Results.Created($"/api/documents/{doc.Id}", new { doc.Id });
        });

        // PUT /api/documents/{id} — full update of a Draft (409 if not Draft).
        g.MapPut("/{id:guid}", async (
            Guid id,
            UpdateSalesDocumentRequest req,
            IValidator<UpdateSalesDocumentRequest> validator,
            NumeraDbContext db,
            IAuditWriter audit,
            IEntitlementService entitlements,
            CancellationToken ct) =>
        {
            var result = await validator.ValidateAsync(req, ct).ConfigureAwait(false);
            if (!result.IsValid)
            {
                return Results.ValidationProblem(result.ToDictionary());
            }

            var doc = await db.Set<SalesDocument>()
                .Include(x => x.Lines)
                .FirstOrDefaultAsync(x => x.Id == id, ct)
                .ConfigureAwait(false);
            if (doc is null)
            {
                return Results.NotFound();
            }

            // App-layer guard mirroring the DB immutability trigger: only drafts are mutable.
            if (doc.Status != DocumentStatus.Draft)
            {
                return NonDraftConflict(doc.Status);
            }

            if (IsDownPaymentDocument(req.DocumentType)
                && !await entitlements.HasCapabilityAsync(Capability.DownPaymentInvoices, ct).ConfigureAwait(false))
            {
                return DownPaymentUpgradeRequired();
            }

            var currency = NormalizeCurrency(req.Currency);
            if (IsForeignCurrency(currency)
                && !await entitlements.HasCapabilityAsync(Capability.ForeignCurrencyInvoicing, ct).ConfigureAwait(false))
            {
                return ForeignCurrencyUpgradeRequired();
            }

            var before = Snapshot(doc);

            doc.DocumentType = req.DocumentType;
            doc.PartnerId = req.PartnerId;
            doc.DocumentDate = req.DocumentDate;
            doc.ServiceDate = req.ServiceDate;
            doc.Notes = req.Notes;
            doc.BuyerReference = req.BuyerReference;
            doc.Currency = currency;
            doc.ExchangeRate = IsForeignCurrency(currency) ? req.ExchangeRate : null;
            doc.ExchangeRateDate = IsForeignCurrency(currency) ? req.ExchangeRateDate : null;
            doc.TotalTaxEur = null;

            // Delete-and-re-add lines; the parent is a Draft so the child trigger permits it.
            foreach (var old in doc.Lines.ToList())
            {
                db.Remove(old);
            }

            doc.Lines.Clear();
            ReplaceLines(doc, req.Lines, doc.TenantId);
            doc.TotalNet = doc.Lines.Sum(l => l.LineNetAmount);

            await audit.RecordAsync(
                new SalesDocumentAuditEvent("sales_document.updated", doc.Id, before, Snapshot(doc)), ct)
                .ConfigureAwait(false);

            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            return Results.NoContent();
        });

        // DELETE /api/documents/{id} — hard-delete a Draft (409 if not Draft).
        g.MapDelete("/{id:guid}", async (
            Guid id,
            NumeraDbContext db,
            IAuditWriter audit,
            CancellationToken ct) =>
        {
            var doc = await db.Set<SalesDocument>()
                .Include(x => x.Lines)
                .FirstOrDefaultAsync(x => x.Id == id, ct)
                .ConfigureAwait(false);
            if (doc is null)
            {
                return Results.NotFound();
            }

            if (doc.Status != DocumentStatus.Draft)
            {
                return NonDraftConflict(doc.Status);
            }

            await audit.RecordAsync(
                new SalesDocumentAuditEvent("sales_document.deleted", doc.Id, Snapshot(doc), After: null), ct)
                .ConfigureAwait(false);

            db.Remove(doc);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            return Results.NoContent();
        });

        // POST /api/documents/{id}/convert — copy-forward the chain (DOCS-01).
        // Creates a NEW Draft of the target type copying header + lines from the source
        // and setting source_document_id. Any source status converts freely (a finalized
        // Angebot can become a Rechnung draft); RESEARCH.md permits any target type in v1.
        g.MapPost("/{id:guid}/convert", async (
            Guid id,
            ConvertDocumentRequest req,
            NumeraDbContext db,
            IAuditWriter audit,
            ICurrentTenant tenant,
            IEntitlementService entitlements,
            CancellationToken ct) =>
        {
            var source = await db.Set<SalesDocument>()
                .AsNoTracking()
                .Include(x => x.Lines)
                .FirstOrDefaultAsync(x => x.Id == id, ct)
                .ConfigureAwait(false);
            if (source is null)
            {
                return Results.NotFound();
            }

            if (IsDownPaymentDocument(req.TargetType)
                && !await entitlements.HasCapabilityAsync(Capability.DownPaymentInvoices, ct).ConfigureAwait(false))
            {
                return DownPaymentUpgradeRequired();
            }

            if (req.TargetType == DocumentType.Schlussrechnung)
            {
                return InvalidPrepayments("Use /api/documents/final-invoice to create a Schlussrechnung.");
            }

            if (IsForeignCurrency(source.Currency)
                && !await entitlements.HasCapabilityAsync(Capability.ForeignCurrencyInvoicing, ct).ConfigureAwait(false))
            {
                return ForeignCurrencyUpgradeRequired();
            }

            var tenantId = tenant.TenantId!.Value;
            var doc = new SalesDocument
            {
                TenantId = tenantId,
                DocumentType = req.TargetType,
                Status = DocumentStatus.Draft,
                DocumentNumber = null,
                SourceDocumentId = source.Id,
                PartnerId = source.PartnerId,
                DocumentDate = DateOnly.FromDateTime(DateTime.UtcNow),
                Notes = source.Notes,
                BuyerReference = source.BuyerReference,
                Currency = source.Currency,
                ExchangeRate = source.ExchangeRate,
                ExchangeRateDate = source.ExchangeRateDate,
            };

            // Copy lines forward as fresh rows (new ids, same snapshot fields + order).
            foreach (var l in source.Lines.OrderBy(l => l.LineNumber))
            {
                doc.Lines.Add(new SalesDocumentLine
                {
                    TenantId = tenantId,
                    DocumentId = doc.Id,
                    LineNumber = l.LineNumber,
                    CatalogItemId = l.CatalogItemId,
                    Name = l.Name,
                    Description = l.Description,
                    Quantity = l.Quantity,
                    UnitCode = l.UnitCode,
                    NetUnitPrice = l.NetUnitPrice,
                    LineNetAmount = l.LineNetAmount,
                    TaxCategory = l.TaxCategory,
                    VatRatePercent = l.VatRatePercent,
                });
            }

            doc.TotalNet = doc.Lines.Sum(l => l.LineNetAmount);

            db.Add(doc);
            await audit.RecordAsync(
                new SalesDocumentAuditEvent("sales_document.converted", doc.Id, Before: null, After: Snapshot(doc)), ct)
                .ConfigureAwait(false);

            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            return Results.Created($"/api/documents/{doc.Id}", new { doc.Id });
        });

        // POST /api/documents/{id}/finalize — the single most important transaction in v1
        // (INV-01/INV-02/OPDN-01, RESEARCH.md Pattern 4). In ONE transaction it validates §14
        // completeness, snapshots issuer + recipient as jsonb, computes + persists the BG-23
        // VAT breakdown, assigns the race-safe number in the configured format, computes the
        // due date + creates the open item, then flips status to Finalized LAST; AFTER commit
        // it dispatches InvoiceFinalized. The breakdown/field writes happen while status is
        // still Draft (first SaveChanges) so the child immutability trigger permits them; the
        // status flip is a second SaveChanges (Pattern 4 step 7 / Pitfall 1).
        g.MapPost("/{id:guid}/finalize", async (
            Guid id,
            NumeraDbContext db,
            NumberingService numbering,
            IDomainEventPublisher publisher,
            IAuditWriter audit,
            ICurrentTenant tenant,
            EInvoiceService einvoice,
            IEntitlementService entitlements,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            // 1. Load the draft TRACKED with its lines; reject a non-draft before any DB work.
            var doc = await db.Set<SalesDocument>()
                .Include(x => x.Lines)
                .FirstOrDefaultAsync(x => x.Id == id, ct)
                .ConfigureAwait(false);
            if (doc is null)
            {
                return Results.NotFound();
            }

            if (doc.Status != DocumentStatus.Draft)
            {
                return NonDraftConflict(doc.Status);
            }

            if (IsForeignCurrency(doc.Currency))
            {
                if (!await entitlements.HasCapabilityAsync(
                        Capability.ForeignCurrencyInvoicing, ct).ConfigureAwait(false))
                {
                    return ForeignCurrencyUpgradeRequired();
                }

                if (doc.ExchangeRate is null or <= 0m || doc.ExchangeRateDate is null)
                {
                    return Results.ValidationProblem(
                        new Dictionary<string, string[]>
                        {
                            ["ExchangeRate"] = ["Ein positiver Wechselkurs und ein Wechselkursdatum sind erforderlich."],
                        },
                        statusCode: StatusCodes.Status422UnprocessableEntity,
                        title: "Document is not ready to finalize");
                }
            }

            // 2. Load issuer (exactly one per tenant) + recipient; run the §14 completeness gate.
            var profile = await db.Set<CompanyProfile>()
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
            var partner = doc.PartnerId is null
                ? null
                : await db.Set<BusinessPartner>()
                    .FirstOrDefaultAsync(p => p.Id == doc.PartnerId, ct)
                    .ConfigureAwait(false);

            var errors = FinalizeValidation.Check(doc, profile, partner);
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(
                    errors,
                    statusCode: StatusCodes.Status422UnprocessableEntity,
                    title: "Document is not ready to finalize");
            }

            // The gate guarantees a non-null issuer profile beyond this point.
            var tenantId = tenant.TenantId!.Value;

            // STAGE 1 (pre-finalize dry-run, EINV-03, RESEARCH Pitfall 1): for every receivable
            // invoice type, validate a
            // PROVISIONAL XRechnung against KoSIT BEFORE any gapless number is burned. A hard rejection
            // blocks finalize (422) so NO number is stranded (the transaction is not even started). A
            // validator OUTAGE does NOT block (infra must not strand the user — RESEARCH Pitfall 6);
            // the post-finalize job + the send gate still protect the actual Versand. Note the criterion
            // blocks on validation ERRORS, never on a down sidecar.
            if (IsReceivableInvoice(doc.DocumentType))
            {
                var dryRun = await einvoice.DryRunAsync(doc, profile!, partner, ct).ConfigureAwait(false);
                if (dryRun.Status == EInvoiceValidationStatus.Rejected)
                {
                    return Results.ValidationProblem(
                        EInvoiceGate.ToProblemDictionary(dryRun.Findings),
                        statusCode: StatusCodes.Status422UnprocessableEntity,
                        title: "E-Rechnung ist nicht konform");
                }

                if (dryRun.Status == EInvoiceValidationStatus.Unavailable)
                {
                    loggerFactory.CreateLogger("EInvoiceGate").LogWarning(
                        "Pre-finalize KoSIT dry-run unavailable for document {DocumentId}; finalizing anyway "
                        + "(a validator outage does not block finalize).", doc.Id);
                }
            }

            // 3-7. Everything below runs inside ONE transaction via the shared finalize core
            //      (extracted so Storno can reuse it, plan 03-06): freeze snapshots, persist the
            //      BG-23 breakdown + frozen totals, assign the race-safe number, create the open
            //      item (receivable invoice types only), then flip status LAST. The caller commits + publishes.
            await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            try
            {
                await FinalizeCoreAsync(
                    doc, profile!, partner, db, numbering, audit, tenantId,
                    "sales_document.finalized", ct).ConfigureAwait(false);
                await tx.CommitAsync(ct).ConfigureAwait(false);
            }
            catch (DbUpdateException ex) when (IsDuplicateNumber(ex))
            {
                // The partial unique (tenant, doc_type, document_number) index rejected a
                // collision — should not happen given the atomic counter, but surface a clean
                // 409 rather than a 500 (mirrors CatalogEndpoints.IsDuplicateNumber).
                await tx.RollbackAsync(ct).ConfigureAwait(false);
                return DuplicateNumberConflict();
            }

            // AFTER commit: dispatch the domain event (side-effect seam — the open item + audit
            // were done IN the transaction, not via the event).
            await publisher.PublishAsync(
                new InvoiceFinalized(
                    tenantId, doc.Id, doc.DocumentNumber!,
                    doc.TotalNet, doc.TotalTax, doc.TotalGross, doc.DocumentDate, doc.DocumentType),
                ct).ConfigureAwait(false);

            var prepayments = await db.Set<SalesDocumentPrepayment>()
                .AsNoTracking()
                .Where(p => p.DocumentId == doc.Id)
                .OrderBy(p => p.AbschlagDate)
                .ThenBy(p => p.AbschlagNumber)
                .Select(p => new SalesDocumentPrepaymentDto(
                    p.AbschlagDocumentId, p.AbschlagNumber, p.AbschlagDate,
                    p.NetAmount, p.VatAmount, p.GrossAmount))
                .ToListAsync(ct)
                .ConfigureAwait(false);
            return Results.Ok(ToDetail(doc, prepayments));
        });

        // POST /api/documents/{id}/storno — cancel a finalized invoice (INV-03 + DOCS-04).
        // Creates a Storno (EN 16931 type 384): a NEGATIVE MIRROR of the original, finalized
        // with its OWN number from the Storno series, referencing the original; the original
        // flips to Cancelled with a back-link and its open item is closed — all in ONE
        // transaction. The original stays DB-immutable: only its whitelisted lifecycle columns
        // (status, cancelled_by_document_id) change (RESEARCH.md Pattern 5/6).
        g.MapPost("/{id:guid}/storno", async (
            Guid id,
            NumeraDbContext db,
            NumberingService numbering,
            IDomainEventPublisher publisher,
            IAuditWriter audit,
            ICurrentTenant tenant,
            CancellationToken ct) =>
        {
            // 1. Load the original TRACKED with its lines. It MUST be a finalized Rechnung
            //    (Finalized or Sent); a draft, already-cancelled or non-invoice is 409'd.
            var original = await db.Set<SalesDocument>()
                .Include(x => x.Lines)
                .FirstOrDefaultAsync(x => x.Id == id, ct)
                .ConfigureAwait(false);
            if (original is null)
            {
                return Results.NotFound();
            }

            // V1 limitation: cancelling an Abschlagsrechnung already deducted by a finalized
            // Schlussrechnung does not recompute BT-113 or its residual. Users must cancel in
            // the correct order (Schlussrechnung first); there is no deduction-chain cascade.
            if (!IsReceivableInvoice(original.DocumentType)
                || original.Status is not (DocumentStatus.Finalized or DocumentStatus.Sent))
            {
                return Results.Problem(
                    title: "Document cannot be cancelled",
                    detail: $"Only a finalized Rechnung, Abschlagsrechnung, or Schlussrechnung can be cancelled by a Storno; this is a "
                          + $"{original.DocumentType} in status {original.Status}.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            // 2. Load issuer + recipient and re-run the §14 gate (a Storno is itself a legal
            //    document). The original passed once; this guards against since-deleted data.
            var profile = await db.Set<CompanyProfile>().FirstOrDefaultAsync(ct).ConfigureAwait(false);
            var partner = original.PartnerId is null
                ? null
                : await db.Set<BusinessPartner>()
                    .FirstOrDefaultAsync(p => p.Id == original.PartnerId, ct)
                    .ConfigureAwait(false);

            var tenantId = tenant.TenantId!.Value;

            // 3. Build the Storno as a Draft negative mirror (so the finalize machinery + child
            //    trigger work): copy the lines forward with NEGATED quantity + line net so it is
            //    a full negative mirror (same tax category/rate → a negated BG-23 breakdown).
            var storno = new SalesDocument
            {
                TenantId = tenantId,
                DocumentType = DocumentType.Storno,
                Status = DocumentStatus.Draft,
                CorrectsDocumentId = original.Id,
                PartnerId = original.PartnerId,
                DocumentDate = DateOnly.FromDateTime(DateTime.UtcNow),
                ServiceDate = original.ServiceDate,
                Currency = original.Currency,
                ExchangeRate = original.ExchangeRate,
                ExchangeRateDate = original.ExchangeRateDate,
                BuyerReference = original.BuyerReference,
                Notes = original.Notes,
            };
            foreach (var l in original.Lines.OrderBy(l => l.LineNumber))
            {
                storno.Lines.Add(new SalesDocumentLine
                {
                    TenantId = tenantId,
                    DocumentId = storno.Id,
                    LineNumber = l.LineNumber,
                    CatalogItemId = l.CatalogItemId,
                    Name = l.Name,
                    Description = l.Description,
                    Quantity = -l.Quantity,
                    UnitCode = l.UnitCode,
                    NetUnitPrice = l.NetUnitPrice,
                    LineNetAmount = -l.LineNetAmount,
                    TaxCategory = l.TaxCategory,
                    VatRatePercent = l.VatRatePercent,
                });
            }

            var errors = FinalizeValidation.Check(storno, profile, partner);
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(
                    errors,
                    statusCode: StatusCodes.Status422UnprocessableEntity,
                    title: "Invoice cannot be cancelled (issuer/recipient data incomplete)");
            }

            db.Add(storno);

            await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            try
            {
                // 4. Finalize the Storno via the SAME core (own Storno-series number, negative
                //    breakdown + totals, snapshots). Storno is not a Rechnung → NO open item.
                await FinalizeCoreAsync(
                    storno, profile!, partner, db, numbering, audit, tenantId,
                    "sales_document.storno", ct).ConfigureAwait(false);

                // 5. Mutate the ORIGINAL using ONLY whitelisted lifecycle columns (the DB
                //    immutability trigger permits status + cancelled_by_document_id).
                var origBefore = Snapshot(original);
                original.Status = DocumentStatus.Cancelled;
                original.CancelledByDocumentId = storno.Id;

                // Close the original's open item (RESEARCH.md Pattern 5): no positive receivable
                // survives a cancellation.
                var openItem = await db.Set<OpenItem>()
                    .FirstOrDefaultAsync(o => o.DocumentId == original.Id, ct)
                    .ConfigureAwait(false);
                if (openItem is not null)
                {
                    openItem.Status = OpenItemStatus.Cancelled;
                    openItem.OpenAmount = 0m;
                }

                await audit.RecordAsync(
                    new SalesDocumentAuditEvent("sales_document.cancelled", original.Id, origBefore, Snapshot(original)), ct)
                    .ConfigureAwait(false);

                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                await tx.CommitAsync(ct).ConfigureAwait(false);
            }
            catch (DbUpdateException ex) when (IsDuplicateNumber(ex))
            {
                await tx.RollbackAsync(ct).ConfigureAwait(false);
                return DuplicateNumberConflict();
            }

            // 6. AFTER commit: dispatch the cancellation event (post-commit side-effect seam).
            await publisher.PublishAsync(new InvoiceCancelled(tenantId, original.Id, storno.Id), ct)
                .ConfigureAwait(false);

            return Results.Created(
                $"/api/documents/{storno.Id}", new StornoResponse(storno.Id, storno.DocumentNumber!));
        });

        // POST /api/documents/{id}/credit-note — issue a kaufmännische Gutschrift (EN 16931
        // type 381) referencing the original invoice (INV-03). This is the COMMERCIAL credit
        // note that reduces what the customer owes — NOT the self-billed VAT Gutschrift (type
        // 389), which is out of scope for v1. It is created as a DRAFT with POSITIVE amounts
        // (RESEARCH.md Pattern 6 — a credit note is not a negative invoice; the "credit" sense
        // is carried by the document type, not a minus sign on the total). The user edits it
        // (a credit note is often partial), then finalizes it via the normal /finalize — which
        // assigns a Gutschrift-series number and, per the finalize open-item rule, creates NO
        // positive receivable (only a Rechnung does).
        g.MapPost("/{id:guid}/credit-note", async (
            Guid id,
            NumeraDbContext db,
            IAuditWriter audit,
            ICurrentTenant tenant,
            CancellationToken ct) =>
        {
            // The original must be an issued (non-draft) invoice; you correct an issued
            // Rechnung, not a draft (a draft is simply edited in place).
            var original = await db.Set<SalesDocument>()
                .AsNoTracking()
                .Include(x => x.Lines)
                .FirstOrDefaultAsync(x => x.Id == id, ct)
                .ConfigureAwait(false);
            if (original is null)
            {
                return Results.NotFound();
            }

            if (!IsReceivableInvoice(original.DocumentType) || original.Status == DocumentStatus.Draft)
            {
                return Results.Problem(
                    title: "Document cannot be credited",
                    detail: $"A credit note references an issued Rechnung, Abschlagsrechnung, or Schlussrechnung; this is a "
                          + $"{original.DocumentType} in status {original.Status}.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            var tenantId = tenant.TenantId!.Value;

            var creditNote = new SalesDocument
            {
                TenantId = tenantId,
                DocumentType = DocumentType.Gutschrift,
                Status = DocumentStatus.Draft,
                CorrectsDocumentId = original.Id,
                PartnerId = original.PartnerId,
                DocumentDate = DateOnly.FromDateTime(DateTime.UtcNow),
                ServiceDate = original.ServiceDate,
                Currency = original.Currency,
                ExchangeRate = original.ExchangeRate,
                ExchangeRateDate = original.ExchangeRateDate,
                BuyerReference = original.BuyerReference,
                Notes = original.Notes,
            };

            // Copy the lines forward with POSITIVE amounts (a commercial credit note carries
            // positive amounts; it is finalized/edited before it takes effect).
            foreach (var l in original.Lines.OrderBy(l => l.LineNumber))
            {
                creditNote.Lines.Add(new SalesDocumentLine
                {
                    TenantId = tenantId,
                    DocumentId = creditNote.Id,
                    LineNumber = l.LineNumber,
                    CatalogItemId = l.CatalogItemId,
                    Name = l.Name,
                    Description = l.Description,
                    Quantity = l.Quantity,
                    UnitCode = l.UnitCode,
                    NetUnitPrice = l.NetUnitPrice,
                    LineNetAmount = l.LineNetAmount,
                    TaxCategory = l.TaxCategory,
                    VatRatePercent = l.VatRatePercent,
                });
            }

            creditNote.TotalNet = creditNote.Lines.Sum(l => l.LineNetAmount);

            db.Add(creditNote);
            await audit.RecordAsync(
                new SalesDocumentAuditEvent("sales_document.credit_note_created", creditNote.Id, Before: null, After: Snapshot(creditNote)), ct)
                .ConfigureAwait(false);

            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            return Results.Created($"/api/documents/{creditNote.Id}", new { creditNote.Id });
        });

        return app;
    }

    // --- Line building + mapping helpers -------------------------------------

    // Snapshots each request line onto a fresh SalesDocumentLine (1-based LineNumber,
    // LineNetAmount = round(qty × price, 4) half-away-from-zero — kaufmännisch).
    private static void ReplaceLines(SalesDocument doc, IReadOnlyList<SalesLineRequest> lines, Guid tenantId)
    {
        var lineNumber = 1;
        foreach (var l in lines)
        {
            doc.Lines.Add(new SalesDocumentLine
            {
                TenantId = tenantId,
                DocumentId = doc.Id,
                LineNumber = lineNumber++,
                CatalogItemId = l.CatalogItemId,
                Name = l.Name,
                Description = l.Description,
                Quantity = l.Quantity,
                UnitCode = l.UnitCode,
                NetUnitPrice = l.NetUnitPrice,
                LineNetAmount = Math.Round(l.Quantity * l.NetUnitPrice, 4, MidpointRounding.AwayFromZero),
                TaxCategory = l.TaxCategory,
                VatRatePercent = l.VatRatePercent,
            });
        }
    }

    private static SalesDocument BuildDraft(
        CreateSalesDocumentRequest req,
        DocumentType documentType,
        Guid tenantId,
        string currency)
    {
        var doc = new SalesDocument
        {
            TenantId = tenantId,
            DocumentType = documentType,
            Status = DocumentStatus.Draft,
            PartnerId = req.PartnerId,
            DocumentDate = req.DocumentDate,
            ServiceDate = req.ServiceDate,
            Notes = req.Notes,
            BuyerReference = req.BuyerReference,
            Currency = currency,
            ExchangeRate = IsForeignCurrency(currency) ? req.ExchangeRate : null,
            ExchangeRateDate = IsForeignCurrency(currency) ? req.ExchangeRateDate : null,
        };
        ReplaceLines(doc, req.Lines, tenantId);
        doc.TotalNet = doc.Lines.Sum(l => l.LineNetAmount);
        return doc;
    }

    private static SalesDocumentDetail ToDetail(
        SalesDocument d,
        IReadOnlyList<SalesDocumentPrepaymentDto>? prepayments = null) => new(
        d.Id, d.DocumentType, d.Status, d.DocumentNumber, d.PartnerId,
        d.DocumentDate, d.ServiceDate, d.ServicePeriodEnd, d.DueDate,
        d.Currency, d.ExchangeRate, d.ExchangeRateDate, d.TotalTaxEur,
        d.TotalNet, d.TotalTax, d.TotalGross, d.AmountDue,
        d.IsKleinunternehmer, d.ReverseCharge, d.BuyerReference, d.Notes,
        d.SourceDocumentId, d.CorrectsDocumentId, d.CancelledByDocumentId,
        d.IssuerSnapshot, d.RecipientSnapshot,
        d.Lines
            .OrderBy(l => l.LineNumber)
            .Select(l => new SalesLineDto(
                l.Id, l.LineNumber, l.CatalogItemId, l.Name, l.Description,
                l.Quantity, l.UnitCode, l.NetUnitPrice, l.LineNetAmount,
                l.TaxCategory, l.VatRatePercent))
            .ToList(),
        d.TaxBreakdown
            .Select(b => new SalesTaxBreakdownDto(
                b.Id, b.TaxCategory, b.VatRatePercent, b.TaxableBase, b.TaxAmount,
                b.ExemptionReasonCode, b.ExemptionReasonText))
            .ToList(),
        prepayments ?? []);

    private static string Snapshot(SalesDocument d) => JsonSerializer.Serialize(new
    {
        DocumentType = d.DocumentType.ToString(),
        Status = d.Status.ToString(),
        d.DocumentNumber,
        d.PartnerId,
        d.SourceDocumentId,
        d.CorrectsDocumentId,
        d.DocumentDate,
        d.ServiceDate,
        d.Currency,
        d.TotalNet,
        d.TotalTax,
        d.TotalGross,
        d.BuyerReference,
        d.Notes,
        Lines = d.Lines
            .OrderBy(l => l.LineNumber)
            .Select(l => new
            {
                l.LineNumber,
                l.CatalogItemId,
                l.Name,
                l.Quantity,
                l.UnitCode,
                l.NetUnitPrice,
                l.LineNetAmount,
                TaxCategory = l.TaxCategory.ToString(),
                l.VatRatePercent,
            }),
    }, AuditJson);

    private static IResult NonDraftConflict(DocumentStatus status) =>
        Results.Problem(
            title: "Document is not a draft",
            detail: $"Only draft documents can be edited or deleted; this document is {status}.",
            statusCode: StatusCodes.Status409Conflict);

    // --- Finalize helpers ----------------------------------------------------

    // The shared finalize core (extracted from 03-05 so Storno reuses one code path, plan
    // 03-06). Runs INSIDE the caller's open transaction (NumberingService enlists the ambient
    // transaction); the caller owns BeginTransaction/Commit + the post-commit event. It freezes
    // the issuer + recipient snapshots, computes + persists the BG-23 VAT breakdown and the
    // frozen totals, assigns the race-safe number from the type's OWN series, computes the due
    // date, and creates the open item for a Rechnung ONLY — a Storno cancels and a Gutschrift
    // credits, so NEITHER creates a positive receivable (RESEARCH.md Pattern 5).
    // CRITICAL ordering (Pitfall 1): the breakdown rows + all frozen business columns are
    // written while status is still Draft (first SaveChanges) so the child immutability trigger
    // permits the child INSERTs; the status flip is a SECOND SaveChanges (OLD.status = 0 passes
    // the parent trigger). Audit is recorded before the final SaveChanges (atomic).
    internal static async Task FinalizeCoreAsync(
        SalesDocument doc,
        CompanyProfile profile,
        BusinessPartner? partner,
        NumeraDbContext db,
        NumberingService numbering,
        IAuditWriter audit,
        Guid tenantId,
        string auditAction,
        CancellationToken ct)
    {
        var before = Snapshot(doc);

        // Freeze issuer + recipient onto the document as jsonb (GoBD).
        doc.IssuerSnapshot = SerializeIssuer(profile);
        doc.RecipientSnapshot = partner is null ? null : SerializeRecipient(partner);
        doc.IsKleinunternehmer = profile.IsKleinunternehmer;

        // VAT: bucket the lines into the BG-23 breakdown (negative lines → a negated breakdown).
        var vatInputs = doc.Lines
            .Select(l => new VatLineInput(l.TaxCategory, l.VatRatePercent, l.LineNetAmount));
        var rows = VatCalculationService.Calculate(vatInputs, profile.IsKleinunternehmer);
        foreach (var row in rows)
        {
            db.Add(new SalesDocumentTaxBreakdown
            {
                TenantId = tenantId,
                DocumentId = doc.Id,      // FK already set — the navigation is not needed to force Added
                TaxCategory = row.Category,
                VatRatePercent = row.RatePercent,
                TaxableBase = row.TaxableBase,
                TaxAmount = row.TaxAmount,
                ExemptionReasonCode = row.ExemptionCode,
                ExemptionReasonText = row.ExemptionText,
            });
        }

        doc.TotalNet = doc.Lines.Sum(l => l.LineNetAmount);
        doc.TotalTax = VatCalculationService.DocumentVatTotal(rows);
        doc.TotalGross = doc.TotalNet + doc.TotalTax;
        if (IsForeignCurrency(doc.Currency))
        {
            if (doc.ExchangeRate is null or <= 0m || doc.ExchangeRateDate is null)
            {
                throw new InvalidOperationException(
                    "A positive exchange rate and an exchange-rate date are required for a foreign-currency document.");
            }

            doc.TotalTaxEur = RoundingPolicy.RoundAmount(doc.TotalTax / doc.ExchangeRate.Value);
        }
        else
        {
            doc.ExchangeRate = null;
            doc.ExchangeRateDate = null;
            doc.TotalTaxEur = null;
        }

        if (doc.DocumentType == DocumentType.Schlussrechnung)
        {
            var prepaidGross = await db.Set<SalesDocumentPrepayment>()
                .Where(p => p.DocumentId == doc.Id)
                .SumAsync(p => p.GrossAmount, ct)
                .ConfigureAwait(false);
            if (prepaidGross < 0m || prepaidGross > doc.TotalGross)
            {
                throw new InvalidOperationException(
                    $"Prepaid gross amount {prepaidGross} must be between zero and document gross {doc.TotalGross}.");
            }

            // BT-115 = BT-112 - BT-113. VAT totals and breakdown remain the full project values.
            doc.AmountDue = doc.TotalGross - prepaidGross;
        }
        else
        {
            doc.AmountDue = doc.TotalGross;
        }
        doc.ReverseCharge = doc.Lines.Any(l => l.TaxCategory == TaxCategory.AE);

        // Number: atomic upsert-returning counter from the type's own series (Pattern 3).
        doc.DocumentNumber = await numbering
            .AssignAsync(doc.DocumentType, doc.DocumentDate.Year, ct)
            .ConfigureAwait(false);

        var netDays = partner?.PaymentTermsNetDays ?? profile.DefaultPaymentTermsNetDays ?? 14;
        doc.DueDate = doc.DocumentDate.AddDays(netDays);

        // Open item (OPDN-01) for Rechnung, Abschlagsrechnung, and Schlussrechnung.
        if (IsReceivableInvoice(doc.DocumentType))
        {
            db.Add(BuildOpenItem(doc, partner, tenantId));
        }

        // Write breakdown + frozen columns while status is still Draft (child trigger permits).
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // Flip status LAST — the parent trigger allows this UPDATE because OLD.status = 0.
        doc.Status = DocumentStatus.Finalized;
        doc.FinalizedAt = DateTimeOffset.UtcNow;

        await audit.RecordAsync(
            new SalesDocumentAuditEvent(auditAction, doc.Id, before, Snapshot(doc)), ct)
            .ConfigureAwait(false);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    // A collision on the partial unique (tenant, doc_type, document_number) index → clean 409.
    private static IResult DuplicateNumberConflict() =>
        Results.Problem(
            title: "Document number collision",
            detail: "The assigned document number collided; please retry.",
            statusCode: StatusCodes.Status409Conflict);

    // Frozen issuer snapshot (BG-4/BG-5 + tax identity + bank/imprint) captured at finalize.
    private static string SerializeIssuer(CompanyProfile p) => JsonSerializer.Serialize(new
    {
        p.LegalName,
        Address = new
        {
            p.Address.Street,
            p.Address.Line2,
            p.Address.PostalCode,
            p.Address.City,
            p.Address.CountryCode,
            p.Address.PoBox,
        },
        p.VatId,
        p.TaxNumber,
        p.IsKleinunternehmer,
        Bank = new { p.Iban, p.Bic, p.BankName },
        p.RegisterCourt,
        p.RegisterNumber,
        p.ManagingDirector,
        p.ContactEmail,
        p.ContactPhone,
    }, AuditJson);

    // Frozen recipient snapshot (BG-7/BG-8 + tax identity) captured at finalize.
    private static string SerializeRecipient(BusinessPartner b) => JsonSerializer.Serialize(new
    {
        b.Name,
        b.LegalForm,
        BillingAddress = new
        {
            b.BillingAddress.Street,
            b.BillingAddress.Line2,
            b.BillingAddress.PostalCode,
            b.BillingAddress.City,
            b.BillingAddress.CountryCode,
            b.BillingAddress.PoBox,
        },
        b.VatId,
        b.TaxNumber,
        b.Email,
    }, AuditJson);

    // Reads the frozen recipient e-mail (BT-43) from the RecipientSnapshot jsonb (serialized with
    // Web camelCase at finalize; PascalCase tolerated). Returns null when absent so the send
    // endpoint can 422 rather than enqueue an undeliverable message.
    private static string? ResolveRecipientEmail(string? recipientSnapshot)
    {
        if (string.IsNullOrWhiteSpace(recipientSnapshot))
        {
            return null;
        }

        try
        {
            using var json = JsonDocument.Parse(recipientSnapshot);
            var root = json.RootElement;
            if ((root.TryGetProperty("email", out var e) || root.TryGetProperty("Email", out e))
                && e.ValueKind == JsonValueKind.String)
            {
                var value = e.GetString();
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }
        catch (JsonException)
        {
            // A malformed snapshot is treated as "no recipient e-mail" (endpoint 422s cleanly).
        }

        return null;
    }

    // Builds the open item (OPDN-01, RESEARCH.md Pattern 5): OriginalAmount = OpenAmount =
    // AmountDue, snapshotting the partner's Skonto terms as the Phase-6 payment seam.
    private static OpenItem BuildOpenItem(SalesDocument doc, BusinessPartner? partner, Guid tenantId)
    {
        var skontoDueDate = partner?.SkontoDays is int days
            ? doc.DocumentDate.AddDays(days)
            : (DateOnly?)null;

        var skontoAmount = partner?.SkontoPercent is decimal percent
            ? Math.Round(doc.AmountDue * percent / 100m, 2, MidpointRounding.AwayFromZero)
            : (decimal?)null;

        return new OpenItem
        {
            TenantId = tenantId,
            DocumentId = doc.Id,
            PartnerId = doc.PartnerId,
            DocumentNumber = doc.DocumentNumber!,
            Currency = doc.Currency,
            OriginalAmount = doc.AmountDue,
            OpenAmount = doc.AmountDue,
            Status = OpenItemStatus.Open,
            IssuedOn = doc.DocumentDate,
            DueDate = doc.DueDate!.Value,
            SkontoPercent = partner?.SkontoPercent,
            SkontoDays = partner?.SkontoDays,
            SkontoDueDate = skontoDueDate,
            SkontoAmount = skontoAmount,
        };
    }

    // A number collision violates the partial unique (tenant, doc_type, document_number)
    // index → Postgres 23505. Return a clean 409 instead of a 500.
    private static bool IsDuplicateNumber(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static bool IsReceivableInvoice(DocumentType documentType) =>
        documentType is DocumentType.Rechnung
            or DocumentType.Abschlagsrechnung
            or DocumentType.Schlussrechnung;

    private static bool IsDownPaymentDocument(DocumentType documentType) =>
        documentType is DocumentType.Abschlagsrechnung or DocumentType.Schlussrechnung;

    private static string NormalizeCurrency(string? currency)
        => string.IsNullOrWhiteSpace(currency) ? "EUR" : currency.Trim().ToUpperInvariant();

    private static bool IsForeignCurrency(string currency)
        => !string.Equals(currency, "EUR", StringComparison.OrdinalIgnoreCase);

    private static IResult ForeignCurrencyUpgradeRequired()
        => Results.Problem(
            title: "Upgrade required",
            detail: "Foreign-currency invoicing requires the ForeignCurrencyInvoicing capability (plan L or XL).",
            statusCode: StatusCodes.Status403Forbidden);

    private static IResult DownPaymentUpgradeRequired()
        => Results.Problem(
            title: "Upgrade required",
            detail: "Down-payment invoicing requires the DownPaymentInvoices capability (plan L or XL).",
            statusCode: StatusCodes.Status403Forbidden);

    private static IResult InvalidPrepayments(string detail)
        => Results.Problem(
            title: "Invalid Abschlagsrechnungen",
            detail: detail,
            statusCode: StatusCodes.Status422UnprocessableEntity);
}

/// <summary>
/// A minimal <see cref="IAuditEvent"/> for sales-document mutations. The writer stamps
/// tenant + actor from ambient context; the endpoint supplies only the change.
/// </summary>
internal sealed record SalesDocumentAuditEvent(string Action, Guid? EntityId, string? Before, string? After)
    : IAuditEvent
{
    public string EntityType => nameof(SalesDocument);
}
