using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Numera.Api.Contracts;
using Numera.Api.Services;
using Numera.Modules.Crm;
using Numera.Modules.Ledger;
using Numera.Modules.Ledger.Seed;
using Numera.Modules.Sales.Belege;
using Numera.Modules.Sales.EInvoice.Inbound;
using Numera.Platform.Audit;
using Numera.Platform.Db;
using Numera.Platform.Tenancy;

namespace Numera.Api.Endpoints;

/// <summary>Upload and RLS-scoped review/read access for the unified receipt aggregate.</summary>
public static class ReceiptEndpoints
{
    private const int MaxPageSize = 100;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Maps the authenticated <c>/api/receipts</c> surface.</summary>
    public static IEndpointRouteBuilder MapReceiptEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/receipts").RequireAuthorization();

        g.MapPost("/", async (
            IFormFile? file,
            ReceiptIngestService ingest,
            ICurrentUser currentUser,
            CancellationToken ct) =>
        {
            if (file is null || file.Length <= 0 || file.Length > ReceiptIngestService.MaxUploadBytes)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["file"] =
                    [
                        $"Die Datei muss zwischen 1 Byte und {ReceiptIngestService.MaxUploadBytes} Bytes gro\u00df sein.",
                    ],
                });
            }

            var fileName = string.IsNullOrWhiteSpace(file.FileName) ? "upload" : file.FileName.Trim();
            var contentType = file.ContentType?.Trim() ?? string.Empty;
            if (ReceiptIngestService.ResolveContentType(contentType, fileName) is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["file"] = ["Es werden nur PDF-, JPEG-, PNG-, TIFF- oder HEIF-Dateien akzeptiert."],
                });
            }

            using var stream = new MemoryStream();
            await file.CopyToAsync(stream, ct).ConfigureAwait(false);

            ReceiptIngestService.IngestResult result;
            try
            {
                result = await ingest.IngestAsync(
                    stream.ToArray(),
                    fileName,
                    contentType,
                    ReceiptSource.Upload,
                    currentUser.UserId,
                    ct).ConfigureAwait(false);
            }
            catch (ArgumentException ex)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["file"] = [ex.Message],
                });
            }

            if (result.Quarantined)
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]> { ["file"] = [result.RejectReason!] },
                    statusCode: StatusCodes.Status422UnprocessableEntity,
                    title: "Datei quarant\u00e4nisiert");
            }

            return Results.Created(
                $"/api/receipts/{result.ReceiptId}",
                new { id = result.ReceiptId, status = (int)result.Status });
        }).DisableAntiforgery();

        g.MapGet("/", async (
            NumeraDbContext db,
            CancellationToken ct,
            ReceiptStatus? status = null,
            int page = 1,
            int pageSize = 25) =>
        {
            var take = Math.Clamp(pageSize, 1, MaxPageSize);
            var normalizedPage = Math.Max(page, 1);
            var query = db.Set<Receipt>().AsNoTracking();
            if (status is { } requestedStatus)
            {
                query = query.Where(receipt => receipt.Status == requestedStatus);
            }

            var total = await query.CountAsync(ct).ConfigureAwait(false);
            var receipts = await query
                .OrderByDescending(receipt => receipt.CreatedAt)
                .Skip((normalizedPage - 1) * take)
                .Take(take)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            var archives = await LoadArchivesAsync(db, receipts, ct).ConfigureAwait(false);
            var inbound = await LoadInboundAsync(db, receipts, ct).ConfigureAwait(false);
            var items = receipts.Select(receipt =>
            {
                var originalFileName = receipt.ArchiveId is { } archiveId
                    && archives.TryGetValue(archiveId, out var archive)
                        ? archive.FileName
                        : receipt.InboundDocumentId is { } inboundId
                            && inbound.TryGetValue(inboundId, out var inboundOriginal)
                                ? inboundOriginal.FileName
                                : null;
                return new ReceiptContracts.ReceiptListItem(
                    receipt.Id,
                    (int)receipt.Source,
                    (int)receipt.Status,
                    receipt.SupplierName,
                    receipt.SupplierVatId,
                    receipt.InvoiceNumber,
                    receipt.InvoiceDate,
                    receipt.NetAmount,
                    receipt.VatAmount,
                    receipt.GrossAmount,
                    receipt.VatRatePercent,
                    receipt.Currency,
                    ParseConfidence(receipt.FieldConfidence),
                    receipt.MatchedPartnerId,
                    originalFileName,
                    receipt.CreatedAt);
            }).ToList();

            return Results.Ok(new ReceiptContracts.ReceiptListResponse(
                items, normalizedPage, take, total));
        });

        g.MapGet("/{id:guid}", async (Guid id, NumeraDbContext db, CancellationToken ct) =>
        {
            var receipt = await db.Set<Receipt>()
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == id, ct)
                .ConfigureAwait(false);
            if (receipt is null)
            {
                return Results.NotFound();
            }

            OriginalMetadata? original = null;
            if (receipt.ArchiveId is { } archiveId)
            {
                original = await db.Set<ReceiptArchive>()
                    .AsNoTracking()
                    .Where(a => a.Id == archiveId)
                    .Select(a => new OriginalMetadata(
                        a.OriginalFileName,
                        a.ContentType,
                        a.ByteSize,
                        a.ReceivedAt,
                        a.UploadedByUserId))
                    .FirstOrDefaultAsync(ct)
                    .ConfigureAwait(false);
            }
            else if (receipt.InboundDocumentId is { } inboundDocumentId)
            {
                original = await db.Set<InboundDocument>()
                    .AsNoTracking()
                    .Where(d => d.Id == inboundDocumentId)
                    .Select(d => new OriginalMetadata(
                        d.OriginalFileName,
                        d.OriginalContentType,
                        d.ByteSize,
                        d.UploadedAt,
                        null))
                    .FirstOrDefaultAsync(ct)
                    .ConfigureAwait(false);
            }

            return Results.Ok(new ReceiptContracts.ReceiptDetail(
                receipt.Id,
                (int)receipt.Source,
                (int)receipt.Status,
                receipt.InboundDocumentId,
                receipt.ArchiveId,
                receipt.ContentHash,
                receipt.SupplierName,
                receipt.SupplierVatId,
                receipt.InvoiceNumber,
                receipt.InvoiceDate,
                receipt.ExpenseDate,
                receipt.NetAmount,
                receipt.VatAmount,
                receipt.GrossAmount,
                receipt.VatRatePercent,
                receipt.Currency,
                ParseConfidence(receipt.FieldConfidence),
                receipt.MatchedPartnerId,
                receipt.ExpenseAccountOverride,
                receipt.JournalEntryId,
                original?.FileName,
                original?.ContentType,
                original?.ByteSize,
                original?.ReceivedAt,
                original?.UploadedByUserId,
                receipt.CreatedAt));
        });

        g.MapGet("/{id:guid}/original", async (Guid id, NumeraDbContext db, CancellationToken ct) =>
        {
            var receipt = await db.Set<Receipt>()
                .AsNoTracking()
                .Where(r => r.Id == id)
                .Select(r => new { r.ArchiveId, r.InboundDocumentId })
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
            if (receipt is null)
            {
                return Results.NotFound();
            }

            if (receipt.ArchiveId is { } archiveId)
            {
                var archive = await db.Set<ReceiptArchive>()
                    .AsNoTracking()
                    .Where(a => a.Id == archiveId)
                    .Select(a => new { a.OriginalBytes, a.ContentType, a.OriginalFileName })
                    .FirstOrDefaultAsync(ct)
                    .ConfigureAwait(false);
                return archive is null
                    ? Results.NotFound()
                    : Results.File(archive.OriginalBytes, archive.ContentType, archive.OriginalFileName);
            }

            if (receipt.InboundDocumentId is { } inboundId)
            {
                var document = await db.Set<InboundDocument>()
                    .AsNoTracking()
                    .Where(d => d.Id == inboundId)
                    .Select(d => new { d.OriginalBytes, d.OriginalContentType, d.OriginalFileName })
                    .FirstOrDefaultAsync(ct)
                    .ConfigureAwait(false);
                return document is null
                    ? Results.NotFound()
                    : Results.File(document.OriginalBytes, document.OriginalContentType, document.OriginalFileName);
            }

            return Results.NotFound();
        });

        g.MapGet("/{id:guid}/proposal", async (
            Guid id,
            NumeraDbContext db,
            AccountResolver accountResolver,
            ICurrentTenant tenant,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
            await GetProposalAsync(
                id, db, accountResolver, tenant, loggerFactory, ct).ConfigureAwait(false));

        g.MapPatch("/{id:guid}/review", async (
            Guid id,
            ReceiptContracts.ReviewReceiptRequest request,
            NumeraDbContext db,
            IAuditWriter audit,
            ICurrentUser currentUser,
            CancellationToken ct) =>
            await ReviewAsync(id, request, db, audit, currentUser, ct).ConfigureAwait(false));

        g.MapPost("/{id:guid}/confirm-book", async (
            Guid id,
            NumeraDbContext db,
            PostingEngine postingEngine,
            AccountResolver accountResolver,
            IAuditWriter audit,
            ICurrentTenant tenant,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
            await ConfirmBookAsync(
                id,
                db,
                postingEngine,
                accountResolver,
                audit,
                tenant,
                loggerFactory,
                ct).ConfigureAwait(false));

        return app;
    }

    internal static async Task<IResult> GetProposalAsync(
        Guid id,
        NumeraDbContext db,
        AccountResolver accountResolver,
        ICurrentTenant tenant,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var receipt = await db.Set<Receipt>()
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == id, ct)
            .ConfigureAwait(false);
        if (receipt is null)
        {
            return Results.NotFound();
        }

        if (receipt.Status == ReceiptStatus.Quarantined)
        {
            return Unprocessable("Ein quarant\u00e4nisierter Beleg kann keine Buchung vorschlagen.");
        }

        if (receipt.Status is not (ReceiptStatus.Extracted or ReceiptStatus.Reviewed))
        {
            return Results.Problem(
                title: "Beleg kann nicht vorgeschlagen werden",
                detail: $"Nur extrahierte oder gepr\u00fcfte Belege haben einen Buchungsvorschlag; Status ist {receipt.Status}.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var tenantId = tenant.TenantId
            ?? throw new InvalidOperationException("No tenant is active for receipt proposal.");
        var chartVariant = await TryResolveChartVariant(
            db, loggerFactory, tenantId, receipt.Id, ct).ConfigureAwait(false);
        if (chartVariant is null)
        {
            return Unprocessable("F\u00fcr den Mandanten ist kein Kontenrahmen eingerichtet.");
        }

        try
        {
            var supplier = await LoadSupplierAsync(db, receipt.MatchedPartnerId, ct).ConfigureAwait(false);
            var input = await BuildPostingInputAsync(receipt, supplier?.CreditorAccount, db, ct)
                .ConfigureAwait(false);
            EnsureBookingTotals(receipt, input);

            var postings = new ExpensePostingSource(
                    tenantId, chartVariant.Value, input, accountResolver)
                .BuildPostings();
            var accountIds = postings.Select(posting => posting.AccountId).Distinct().ToArray();
            var accountNumbers = await db.Set<Account>()
                .AsNoTracking()
                .Where(account => accountIds.Contains(account.Id))
                .ToDictionaryAsync(account => account.Id, account => account.Number, ct)
                .ConfigureAwait(false);
            var defaultExpenseAccount = SkrMapping
                .ExpenseMapping(chartVariant.Value, input.Breakdowns[0].RatePercent)
                .ExpenseAccount;
            var expenseAccount = string.IsNullOrWhiteSpace(input.ExpenseAccount)
                ? defaultExpenseAccount
                : input.ExpenseAccount.Trim();
            var creditorAccount = string.IsNullOrWhiteSpace(input.CreditorAccount)
                ? SkrMapping.StandardAccount(chartVariant.Value, StandardAccountKind.Creditor)
                : input.CreditorAccount.Trim();

            return Results.Ok(new ReceiptContracts.ReceiptBookingProposalResponse(
                receipt.Id,
                new ReceiptContracts.ReceiptProposalSupplier(
                    supplier?.Id,
                    supplier?.Name ?? receipt.SupplierName,
                    creditorAccount),
                expenseAccount,
                input.EntryDate,
                input.Breakdowns
                    .Select(row => new ReceiptContracts.ReceiptProposalBreakdown(
                        row.RatePercent, row.Net, row.Tax))
                    .ToList(),
                postings.Select(posting => new ReceiptContracts.ReceiptProposalPostingLeg(
                        accountNumbers[posting.AccountId],
                        (int)posting.Direction,
                        posting.Amount,
                        posting.TaxRatePercent,
                        posting.Steuerschluessel is null ? null : (int)posting.Steuerschluessel.Value))
                    .ToList(),
                input.Breakdowns.Sum(row => row.Net),
                input.Breakdowns.Sum(row => row.Tax),
                input.Breakdowns.Sum(row => row.Net + row.Tax)));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or JsonException)
        {
            return Unprocessable(ex.Message);
        }
    }

    internal static async Task<IResult> ReviewAsync(
        Guid id,
        ReceiptContracts.ReviewReceiptRequest request,
        NumeraDbContext db,
        IAuditWriter audit,
        ICurrentUser currentUser,
        CancellationToken ct)
    {
        var receipt = await db.Set<Receipt>()
            .FirstOrDefaultAsync(candidate => candidate.Id == id, ct)
            .ConfigureAwait(false);
        if (receipt is null)
        {
            return Results.NotFound();
        }

        if (receipt.Status == ReceiptStatus.Quarantined)
        {
            return Unprocessable("Ein quarant\u00e4nisierter Beleg darf nicht gepr\u00fcft oder gebucht werden.");
        }

        if (receipt.Status is not (ReceiptStatus.Extracted or ReceiptStatus.Reviewed))
        {
            return Results.Problem(
                title: "Beleg ist nicht bearbeitbar",
                detail: $"Nur extrahierte oder bereits gepr\u00fcfte Belege d\u00fcrfen gepr\u00fcft werden; Status ist {receipt.Status}.",
                statusCode: StatusCodes.Status409Conflict);
        }

        if (currentUser.UserId is not Guid actorId)
        {
            return Unprocessable("Die Belegpr\u00fcfung erfordert einen angemeldeten Benutzer.");
        }

        var errors = ValidateReview(request, receipt.InboundDocumentId.HasValue);
        if (request.SupplierPartnerId is { } supplierPartnerId)
        {
            var supplierExists = await db.Set<BusinessPartner>()
                .AsNoTracking()
                .AnyAsync(partner => partner.Id == supplierPartnerId && partner.IsSupplier, ct)
                .ConfigureAwait(false);
            if (!supplierExists)
            {
                errors["supplierPartnerId"] = ["Der ausgew\u00e4hlte Lieferant wurde nicht gefunden."];
            }
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(
                errors,
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "Belegpr\u00fcfung ist unvollst\u00e4ndig");
        }

        var before = Snapshot(receipt);
        var now = DateTimeOffset.UtcNow;
        receipt.MatchedPartnerId = request.SupplierPartnerId;
        receipt.ExpenseAccountOverride = NormalizeOptional(request.ExpenseAccountOverride);
        receipt.VatRatePercent = request.VatRatePercent;
        receipt.NetAmount = request.NetAmount;
        receipt.VatAmount = request.VatAmount;
        receipt.GrossAmount = request.GrossAmount;
        receipt.InvoiceNumber = NormalizeOptional(request.InvoiceNumber);
        receipt.InvoiceDate = request.InvoiceDate;
        receipt.ExpenseDate = request.ExpenseDate;
        receipt.Status = ReceiptStatus.Reviewed;
        receipt.ReviewedByUserId = actorId;
        receipt.ReviewedAt = now;

        await audit.RecordAsync(
            new ReceiptAuditEvent("receipt.reviewed", receipt.Id, before, Snapshot(receipt)), ct)
            .ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return Results.Ok(new ReceiptContracts.ReviewReceiptResponse(
            receipt.Id, (int)receipt.Status, actorId, now));
    }

    internal static async Task<IResult> ConfirmBookAsync(
        Guid id,
        NumeraDbContext db,
        PostingEngine postingEngine,
        AccountResolver accountResolver,
        IAuditWriter audit,
        ICurrentTenant tenant,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var receipt = await db.Set<Receipt>()
            .FirstOrDefaultAsync(candidate => candidate.Id == id, ct)
            .ConfigureAwait(false);
        if (receipt is null)
        {
            return Results.NotFound();
        }

        if (receipt.Status == ReceiptStatus.Quarantined)
        {
            return Unprocessable("Ein quarant\u00e4nisierter Beleg darf nicht gebucht werden.");
        }

        var sourceRef = receipt.Id.ToString();
        if (await db.Set<JournalEntry>()
                .AnyAsync(
                    entry => entry.SourceType == LedgerSourceType.Expense && entry.SourceRef == sourceRef,
                    ct)
                .ConfigureAwait(false))
        {
            var existingEntryId = receipt.JournalEntryId
                ?? await db.Set<JournalEntry>()
                    .AsNoTracking()
                    .Where(entry => entry.SourceType == LedgerSourceType.Expense && entry.SourceRef == sourceRef)
                    .Select(entry => entry.Id)
                    .SingleAsync(ct)
                    .ConfigureAwait(false);
            return Results.Ok(new ReceiptContracts.ReceiptBookingResponse(
                receipt.Id,
                existingEntryId,
                (int)ReceiptStatus.Booked,
                AlreadyBooked: true));
        }

        if (receipt.Status != ReceiptStatus.Reviewed || receipt.ReviewedByUserId is null)
        {
            return Unprocessable("Beleg muss gepr\u00fcft und best\u00e4tigt sein");
        }

        var tenantId = tenant.TenantId
            ?? throw new InvalidOperationException("No tenant is active for receipt booking.");
        var chartVariant = await TryResolveChartVariant(
            db, loggerFactory, tenantId, receipt.Id, ct).ConfigureAwait(false);
        if (chartVariant is null)
        {
            return Unprocessable("F\u00fcr den Mandanten ist kein Kontenrahmen eingerichtet.");
        }

        ExpensePostingInput input;
        BusinessPartner? supplier;
        try
        {
            supplier = await LoadSupplierAsync(db, receipt.MatchedPartnerId, ct).ConfigureAwait(false);
            input = await BuildPostingInputAsync(receipt, supplier?.CreditorAccount, db, ct)
                .ConfigureAwait(false);
            EnsureBookingTotals(receipt, input);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or JsonException)
        {
            return Unprocessable(ex.Message);
        }

        var supplierName = supplier?.Name ?? receipt.SupplierName ?? "Unbekannter Lieferant";
        var description = $"Eingangsrechnung {supplierName} {receipt.InvoiceNumber}".TrimEnd();
        var header = new JournalEntry
        {
            TenantId = tenantId,
            EntryDate = input.EntryDate,
            SourceType = LedgerSourceType.Expense,
            SourceRef = sourceRef,
            Description = description,
            PostingType = PostingType.Normal,
        };

        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        await postingEngine.PostAsync(
            new ExpensePostingSource(tenantId, chartVariant.Value, input, accountResolver),
            header,
            ct).ConfigureAwait(false);

        var bookedNet = input.Breakdowns.Sum(row => row.Net);
        var bookedVat = input.Breakdowns.Sum(row => row.Tax);
        if (!AmountsMatch(bookedNet, receipt.NetAmount!.Value)
            || !AmountsMatch(bookedVat, receipt.VatAmount!.Value)
            || !AmountsMatch(bookedNet + bookedVat, receipt.GrossAmount!.Value))
        {
            throw new InvalidOperationException("Die gebuchten Netto-/Vorsteuerbetr\u00e4ge entsprechen nicht den Belegsummern.");
        }

        var before = Snapshot(receipt);
        receipt.Status = ReceiptStatus.Booked;
        receipt.JournalEntryId = header.Id;
        await audit.RecordAsync(
            new ReceiptAuditEvent("receipt.booked", receipt.Id, before, Snapshot(receipt)), ct)
            .ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);

        return Results.Ok(new ReceiptContracts.ReceiptBookingResponse(
            receipt.Id, header.Id, (int)receipt.Status, AlreadyBooked: false));
    }

    private static Dictionary<string, string[]> ValidateReview(
        ReceiptContracts.ReviewReceiptRequest request,
        bool hasInboundDocument)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.NetAmount < 0m || request.VatAmount < 0m || request.GrossAmount < 0m)
        {
            errors["amounts"] = ["Netto-, Vorsteuer- und Bruttobetrag d\u00fcrfen nicht negativ sein."];
        }

        if (!AmountsMatch(request.NetAmount + request.VatAmount, request.GrossAmount))
        {
            errors["grossAmount"] = ["Netto plus Vorsteuer muss bis auf einen Cent dem Bruttobetrag entsprechen."];
        }

        if (request.VatRatePercent is { } rate && !IsSupportedRate(rate))
        {
            errors["vatRatePercent"] = ["Unterst\u00fctzt werden 19 %, 7 % und 0 %/steuerfrei."];
        }
        else if (!hasInboundDocument && request.VatRatePercent is null)
        {
            errors["vatRatePercent"] = ["Ein Beleg ohne E-Rechnungs-Aufschl\u00fcsselung ben\u00f6tigt einen Umsatzsteuersatz."];
        }

        if (request.InvoiceDate is null && request.ExpenseDate is null)
        {
            errors["dates"] = ["Rechnungs- oder Aufwandsdatum ist erforderlich."];
        }

        return errors;
    }

    private static async Task<BusinessPartner?> LoadSupplierAsync(
        NumeraDbContext db,
        Guid? partnerId,
        CancellationToken ct) =>
        partnerId is null
            ? null
            : await db.Set<BusinessPartner>()
                .AsNoTracking()
                .FirstOrDefaultAsync(partner => partner.Id == partnerId && partner.IsSupplier, ct)
                .ConfigureAwait(false);

    private static async Task<ExpensePostingInput> BuildPostingInputAsync(
        Receipt receipt,
        string? creditorAccount,
        NumeraDbContext db,
        CancellationToken ct)
    {
        var breakdown = await LoadInboundBreakdownAsync(receipt, db, ct).ConfigureAwait(false);
        return ReceiptBookingProposal.Build(receipt, breakdown, creditorAccount);
    }

    private static async Task<IReadOnlyList<(decimal RatePercent, decimal Net, decimal Tax)>>
        LoadInboundBreakdownAsync(
            Receipt receipt,
            NumeraDbContext db,
            CancellationToken ct)
    {
        if (receipt.InboundDocumentId is not { } inboundDocumentId)
        {
            return [];
        }

        var readModelJson = await db.Set<InboundDocument>()
            .AsNoTracking()
            .Where(document => document.Id == inboundDocumentId)
            .Select(document => document.ReadModel)
            .SingleOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(readModelJson))
        {
            throw new InvalidOperationException("Die verkn\u00fcpfte E-Rechnung hat keine lesbare Steueraufschl\u00fcsselung.");
        }

        var readModel = JsonSerializer.Deserialize<InboundReadModel>(readModelJson, Json)
            ?? throw new InvalidOperationException("Das Lesemodell der E-Rechnung konnte nicht geladen werden.");
        if (readModel.BreakdownRows.Count == 0)
        {
            return [];
        }

        var rows = new List<(decimal RatePercent, decimal Net, decimal Tax)>(
            readModel.BreakdownRows.Count);
        foreach (var row in readModel.BreakdownRows)
        {
            if (row.VatRatePercent is not { } rate
                || row.TaxableBase is not { } net
                || row.TaxAmount is not { } tax)
            {
                throw new InvalidOperationException(
                    "Jede Steuerzeile der E-Rechnung ben\u00f6tigt Satz, Bemessungsgrundlage und Steuerbetrag.");
            }

            if (!IsSupportedRate(rate)
                || row.TaxCategory is "AE" or "K")
            {
                throw new InvalidOperationException(
                    $"Der Eingangsbeleg enth\u00e4lt die in dieser Phase nicht unterst\u00fctzte Steuerart {row.TaxCategory ?? "?"} mit {rate} %.");
            }

            rows.Add((rate, net, tax));
        }

        return rows;
    }

    private static void EnsureBookingTotals(Receipt receipt, ExpensePostingInput input)
    {
        if (receipt.NetAmount is not { } documentNet
            || receipt.VatAmount is not { } documentVat
            || receipt.GrossAmount is not { } documentGross)
        {
            throw new InvalidOperationException("Netto-, Vorsteuer- und Bruttobetrag m\u00fcssen best\u00e4tigt sein.");
        }

        var bookedNet = input.Breakdowns.Sum(row => row.Net);
        var bookedVat = input.Breakdowns.Sum(row => row.Tax);
        if (!AmountsMatch(bookedNet, documentNet)
            || !AmountsMatch(bookedVat, documentVat)
            || !AmountsMatch(bookedNet + bookedVat, documentGross))
        {
            throw new InvalidOperationException(
                "Die Steueraufschl\u00fcsselung entspricht nicht den best\u00e4tigten Belegsummern.");
        }
    }

    private static async Task<ChartVariant?> TryResolveChartVariant(
        NumeraDbContext db,
        ILoggerFactory loggerFactory,
        Guid tenantId,
        Guid receiptId,
        CancellationToken ct)
    {
        var chartVariant = await db.Set<LedgerSettings>()
            .AsNoTracking()
            .Where(settings => settings.TenantId == tenantId)
            .Select(settings => (ChartVariant?)settings.ChartVariant)
            .SingleOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (chartVariant is null)
        {
            loggerFactory.CreateLogger("LedgerExpensePosting").LogWarning(
                "Skipping ledger booking for receipt {ReceiptId}: tenant {TenantId} has no ledger settings.",
                receiptId,
                tenantId);
        }

        return chartVariant;
    }

    private static IResult Unprocessable(string detail) =>
        Results.Problem(
            title: "Beleg kann nicht gebucht werden",
            detail: detail,
            statusCode: StatusCodes.Status422UnprocessableEntity);

    private static bool AmountsMatch(decimal left, decimal right) => Math.Abs(left - right) <= 0.01m;

    private static bool IsSupportedRate(decimal rate) => rate is 0m or 7m or 19m;

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Snapshot(Receipt receipt) => JsonSerializer.Serialize(new
    {
        Status = receipt.Status.ToString(),
        receipt.SupplierName,
        receipt.InvoiceNumber,
        receipt.InvoiceDate,
        receipt.ExpenseDate,
        receipt.NetAmount,
        receipt.VatAmount,
        receipt.GrossAmount,
        receipt.VatRatePercent,
        receipt.MatchedPartnerId,
        receipt.ExpenseAccountOverride,
        receipt.ReviewedByUserId,
        receipt.ReviewedAt,
        receipt.JournalEntryId,
    }, Json);

    private static async Task<Dictionary<Guid, OriginalMetadata>> LoadArchivesAsync(
        NumeraDbContext db,
        IReadOnlyCollection<Receipt> receipts,
        CancellationToken ct)
    {
        var ids = receipts.Where(r => r.ArchiveId.HasValue).Select(r => r.ArchiveId!.Value).ToArray();
        return await db.Set<ReceiptArchive>()
            .AsNoTracking()
            .Where(a => ids.Contains(a.Id))
            .Select(a => new OriginalMetadata(
                a.Id,
                a.OriginalFileName,
                a.ContentType,
                a.ByteSize,
                a.ReceivedAt,
                a.UploadedByUserId))
            .ToDictionaryAsync(a => a.Id, ct)
            .ConfigureAwait(false);
    }

    private static async Task<Dictionary<Guid, OriginalMetadata>> LoadInboundAsync(
        NumeraDbContext db,
        IReadOnlyCollection<Receipt> receipts,
        CancellationToken ct)
    {
        var ids = receipts
            .Where(r => r.InboundDocumentId.HasValue)
            .Select(r => r.InboundDocumentId!.Value)
            .ToArray();
        return await db.Set<InboundDocument>()
            .AsNoTracking()
            .Where(d => ids.Contains(d.Id))
            .Select(d => new OriginalMetadata(
                d.Id,
                d.OriginalFileName,
                d.OriginalContentType,
                d.ByteSize,
                d.UploadedAt,
                null))
            .ToDictionaryAsync(d => d.Id, ct)
            .ConfigureAwait(false);
    }

    private static JsonElement? ParseConfidence(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record OriginalMetadata(
        Guid Id,
        string FileName,
        string ContentType,
        long ByteSize,
        DateTimeOffset ReceivedAt,
        Guid? UploadedByUserId)
    {
        public OriginalMetadata(
            string fileName,
            string contentType,
            long byteSize,
            DateTimeOffset receivedAt,
            Guid? uploadedByUserId)
            : this(Guid.Empty, fileName, contentType, byteSize, receivedAt, uploadedByUserId)
        {
        }
    }
}
