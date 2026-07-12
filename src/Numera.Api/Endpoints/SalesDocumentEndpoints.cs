using System.Text.Json;

using FluentValidation;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using Numera.Api.Contracts;
using Numera.Api.Validators;
using Numera.Modules.Crm;
using Numera.Modules.Sales;
using Numera.Modules.Sales.Events;
using Numera.Modules.Sales.Numbering;
using Numera.Modules.Sales.Vat;
using Numera.Platform.Audit;
using Numera.Platform.Db;
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
            DocumentStatus? status = null) =>
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

            return d is null ? Results.NotFound() : Results.Ok(ToDetail(d));
        });

        // POST /api/documents — create a Draft.
        g.MapPost("/", async (
            CreateSalesDocumentRequest req,
            IValidator<CreateSalesDocumentRequest> validator,
            NumeraDbContext db,
            IAuditWriter audit,
            ICurrentTenant tenant,
            CancellationToken ct) =>
        {
            var result = await validator.ValidateAsync(req, ct).ConfigureAwait(false);
            if (!result.IsValid)
            {
                return Results.ValidationProblem(result.ToDictionary());
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
                Currency = "EUR",
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

        // PUT /api/documents/{id} — full update of a Draft (409 if not Draft).
        g.MapPut("/{id:guid}", async (
            Guid id,
            UpdateSalesDocumentRequest req,
            IValidator<UpdateSalesDocumentRequest> validator,
            NumeraDbContext db,
            IAuditWriter audit,
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

            var before = Snapshot(doc);

            doc.DocumentType = req.DocumentType;
            doc.PartnerId = req.PartnerId;
            doc.DocumentDate = req.DocumentDate;
            doc.ServiceDate = req.ServiceDate;
            doc.Notes = req.Notes;
            doc.BuyerReference = req.BuyerReference;

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

            await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

            var before = Snapshot(doc);

            // 3. Freeze issuer + recipient onto the document as jsonb (GoBD — a later master-data
            //    edit must never mutate an issued invoice).
            doc.IssuerSnapshot = SerializeIssuer(profile!);
            doc.RecipientSnapshot = partner is null ? null : SerializeRecipient(partner);
            doc.IsKleinunternehmer = profile!.IsKleinunternehmer;

            // 4. VAT: bucket the lines into the BG-23 breakdown and persist one row per bucket.
            var vatInputs = doc.Lines
                .Select(l => new VatLineInput(l.TaxCategory, l.VatRatePercent, l.LineNetAmount));
            var rows = VatCalculationService.Calculate(vatInputs, profile.IsKleinunternehmer);
            foreach (var row in rows)
            {
                doc.TaxBreakdown.Add(new SalesDocumentTaxBreakdown
                {
                    TenantId = tenantId,
                    DocumentId = doc.Id,
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
            doc.AmountDue = doc.TotalGross;
            doc.ReverseCharge = doc.Lines.Any(l => l.TaxCategory == TaxCategory.AE);

            // 5. Number: atomic upsert-returning counter inside THIS transaction (Pattern 3).
            doc.DocumentNumber = await numbering
                .AssignAsync(doc.DocumentType, doc.DocumentDate.Year, ct)
                .ConfigureAwait(false);

            // 6. Due date + open item (OPDN-01). Only the Rechnung creates a receivable here;
            //    Storno/Gutschrift handling (cancel/negate) lands in plan 03-06.
            var netDays = partner?.PaymentTermsNetDays ?? profile.DefaultPaymentTermsNetDays ?? 14;
            doc.DueDate = doc.DocumentDate.AddDays(netDays);

            if (doc.DocumentType == DocumentType.Rechnung)
            {
                db.Add(BuildOpenItem(doc, partner, tenantId));
            }

            try
            {
                // 7. CRITICAL ordering (Pitfall 1): write the breakdown + all business columns
                //    while status is still Draft so the child trigger sees the parent Draft.
                await db.SaveChangesAsync(ct).ConfigureAwait(false);

                // Flip status LAST — the parent trigger allows this UPDATE because OLD.status = 0.
                doc.Status = DocumentStatus.Finalized;
                doc.FinalizedAt = DateTimeOffset.UtcNow;

                // 8. Audit before the final SaveChanges (atomic with the status flip).
                await audit.RecordAsync(
                    new SalesDocumentAuditEvent("sales_document.finalized", doc.Id, before, Snapshot(doc)), ct)
                    .ConfigureAwait(false);

                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                await tx.CommitAsync(ct).ConfigureAwait(false);
            }
            catch (DbUpdateException ex) when (IsDuplicateNumber(ex))
            {
                // The partial unique (tenant, doc_type, document_number) index rejected a
                // collision — should not happen given the atomic counter, but surface a clean
                // 409 rather than a 500 (mirrors CatalogEndpoints.IsDuplicateNumber).
                await tx.RollbackAsync(ct).ConfigureAwait(false);
                return Results.Problem(
                    title: "Document number collision",
                    detail: "The assigned document number collided; please retry finalization.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            // 10. AFTER commit: dispatch the domain event (side-effect seam — the open item +
            //     audit were done IN the transaction, not via the event).
            await publisher.PublishAsync(
                new InvoiceFinalized(
                    tenantId, doc.Id, doc.DocumentNumber!,
                    doc.TotalNet, doc.TotalTax, doc.TotalGross, doc.DocumentDate),
                ct).ConfigureAwait(false);

            return Results.Ok(ToDetail(doc));
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

    private static SalesDocumentDetail ToDetail(SalesDocument d) => new(
        d.Id, d.DocumentType, d.Status, d.DocumentNumber, d.PartnerId,
        d.DocumentDate, d.ServiceDate, d.ServicePeriodEnd, d.DueDate,
        d.Currency, d.TotalNet, d.TotalTax, d.TotalGross, d.AmountDue,
        d.IsKleinunternehmer, d.ReverseCharge, d.BuyerReference, d.Notes,
        d.SourceDocumentId, d.CorrectsDocumentId, d.CancelledByDocumentId,
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
            .ToList());

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

    // Builds the open item (OPDN-01, RESEARCH.md Pattern 5): OriginalAmount = OpenAmount =
    // gross, snapshotting the partner's Skonto terms as the Phase-6 payment seam.
    private static OpenItem BuildOpenItem(SalesDocument doc, BusinessPartner? partner, Guid tenantId)
    {
        var skontoDueDate = partner?.SkontoDays is int days
            ? doc.DocumentDate.AddDays(days)
            : (DateOnly?)null;

        var skontoAmount = partner?.SkontoPercent is decimal percent
            ? Math.Round(doc.TotalGross * percent / 100m, 2, MidpointRounding.AwayFromZero)
            : (decimal?)null;

        return new OpenItem
        {
            TenantId = tenantId,
            DocumentId = doc.Id,
            PartnerId = doc.PartnerId,
            DocumentNumber = doc.DocumentNumber!,
            Currency = doc.Currency,
            OriginalAmount = doc.TotalGross,
            OpenAmount = doc.TotalGross,
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
