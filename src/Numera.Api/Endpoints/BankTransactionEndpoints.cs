using Microsoft.EntityFrameworkCore;

using Numera.Api.Contracts;
using Numera.Api.Services;
using Numera.Modules.Banking;
using Numera.Modules.Banking.Reconciliation;
using Numera.Modules.Sales.Payments;
using Numera.Platform.Db;

namespace Numera.Api.Endpoints;

/// <summary>Human-gated receivable reconciliation endpoints for normalized bank transactions.</summary>
public static class BankTransactionEndpoints
{
    private const int MaxPageSize = 100;

    /// <summary>Maps the authorized <c>/api/bank-transactions</c> review and command surface.</summary>
    public static IEndpointRouteBuilder MapBankTransactionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/bank-transactions").RequireAuthorization();

        group.MapGet("/", ListAsync);
        group.MapGet("/{id:guid}/suggestions", SuggestionsAsync);
        group.MapPost("/{id:guid}/confirm", ConfirmAsync);
        group.MapPost("/{id:guid}/unmatch", UnmatchAsync);
        group.MapPost("/{id:guid}/ignore", IgnoreAsync);

        return app;
    }

    internal static async Task<IResult> ListAsync(
        NumeraDbContext db,
        CancellationToken ct,
        MatchStatus? matchStatus = null,
        Guid? bankAccountId = null,
        int page = 1,
        int pageSize = 25)
    {
        var normalizedPage = Math.Max(page, 1);
        var take = Math.Clamp(pageSize, 1, MaxPageSize);
        var query = db.Set<BankTransaction>().AsNoTracking();

        if (matchStatus is { } requestedStatus)
        {
            query = query.Where(transaction => transaction.MatchStatus == requestedStatus);
        }
        else
        {
            query = query.Where(transaction =>
                transaction.MatchStatus == MatchStatus.Unmatched
                || transaction.MatchStatus == MatchStatus.Suggested
                || transaction.MatchStatus == MatchStatus.Review);
        }

        if (bankAccountId is { } requestedAccountId)
        {
            query = query.Where(transaction => transaction.BankAccountId == requestedAccountId);
        }

        var total = await query.CountAsync(ct).ConfigureAwait(false);
        var items = await query
            .OrderByDescending(transaction => transaction.ValueDate)
            .ThenByDescending(transaction => transaction.CreatedAt)
            .ThenBy(transaction => transaction.Id)
            .Skip((normalizedPage - 1) * take)
            .Take(take)
            .Select(transaction => new BankingContracts.BankTransactionListItem(
                transaction.Id,
                transaction.BankAccountId,
                (int)transaction.Source,
                transaction.Amount,
                transaction.ValueDate,
                transaction.BookingDate,
                transaction.Purpose,
                transaction.CounterpartyName,
                transaction.CounterpartyIban,
                transaction.EndToEndId,
                (int)transaction.MatchStatus,
                transaction.ConfidenceScore,
                transaction.MatchedPaymentId,
                transaction.CreatedAt))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return Results.Ok(new BankingContracts.BankTransactionListResponse(
            items,
            normalizedPage,
            take,
            total));
    }

    internal static async Task<IResult> SuggestionsAsync(
        Guid id,
        NumeraDbContext db,
        ReconciliationScorer scorer,
        CancellationToken ct)
    {
        var transaction = await db.Set<BankTransaction>()
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == id, ct)
            .ConfigureAwait(false);
        if (transaction is null)
        {
            return Results.NotFound();
        }

        var candidates = await scorer.ScoreAsync(transaction, ct).ConfigureAwait(false);
        return Results.Ok(candidates.Select(candidate =>
            new BankingContracts.MatchCandidateResponse(
                candidate.OpenItemId,
                candidate.DocumentNumber,
                candidate.OpenAmount,
                candidate.SuggestedAllocation,
                candidate.Score,
                (int)candidate.Tier,
                candidate.Reasons)).ToList());
    }

    internal static async Task<IResult> ConfirmAsync(
        Guid id,
        BankingContracts.ConfirmBankTransactionRequest request,
        NumeraDbContext db,
        PaymentService payments,
        CancellationToken ct)
    {
        var transaction = await db.Set<BankTransaction>()
            .FirstOrDefaultAsync(candidate => candidate.Id == id, ct)
            .ConfigureAwait(false);
        if (transaction is null)
        {
            return Results.NotFound();
        }

        if (transaction.MatchStatus == MatchStatus.Confirmed)
        {
            return transaction.MatchedPaymentId is { } existingPaymentId
                ? Results.Ok(new BankingContracts.BankTransactionMatchResponse(
                    transaction.Id,
                    (int)transaction.MatchStatus,
                    existingPaymentId,
                    AlreadyConfirmed: true))
                : Results.Conflict(new { error = "Der best\u00e4tigten Transaktion fehlt die verkn\u00fcpfte Zahlung." });
        }

        if (transaction.Amount <= 0m)
        {
            return Invalid(
                "transaction",
                "Nur eingehende Banktransaktionen k\u00f6nnen Forderungen ausgleichen.");
        }

        if (request.Allocations is null || request.Allocations.Count == 0)
        {
            return Invalid("allocations", "Mindestens eine Zuordnung ist erforderlich.");
        }

        if (request.Method is { } method && method != PaymentMethod.BankTransfer)
        {
            return Invalid("method", "Banktransaktionen werden als Bank\u00fcberweisung gebucht.");
        }

        if (request.Allocations.Sum(allocation => allocation.Amount) != transaction.Amount)
        {
            return Invalid(
                "allocations",
                "Transaktionsbetrag und Summe der Zuordnungen m\u00fcssen \u00fcbereinstimmen.");
        }

        var result = await payments.RecordAsync(
            new RecordPaymentRequest(
                Amount: transaction.Amount,
                ValueDate: transaction.ValueDate,
                Method: PaymentMethod.BankTransfer,
                Reference: transaction.EndToEndId ?? transaction.Purpose,
                Allocations: request.Allocations),
            ct).ConfigureAwait(false);
        if (result.Status != PaymentOperationStatus.Success)
        {
            return MapPaymentFailure(result);
        }

        transaction.MatchedPaymentId = result.PaymentId;
        transaction.MatchStatus = MatchStatus.Confirmed;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Created(
            $"/api/payments/{result.PaymentId}",
            new BankingContracts.BankTransactionMatchResponse(
                transaction.Id,
                (int)transaction.MatchStatus,
                result.PaymentId));
    }

    internal static async Task<IResult> UnmatchAsync(
        Guid id,
        NumeraDbContext db,
        PaymentService payments,
        CancellationToken ct)
    {
        var transaction = await db.Set<BankTransaction>()
            .FirstOrDefaultAsync(candidate => candidate.Id == id, ct)
            .ConfigureAwait(false);
        if (transaction is null)
        {
            return Results.NotFound();
        }

        Guid? reversalPaymentId = null;
        if (transaction.MatchedPaymentId is { } matchedPaymentId)
        {
            var result = await payments.ReverseAsync(matchedPaymentId, ct).ConfigureAwait(false);
            if (result.Status != PaymentOperationStatus.Success)
            {
                return MapPaymentFailure(result);
            }

            reversalPaymentId = result.PaymentId;
        }

        transaction.MatchStatus = MatchStatus.Unmatched;
        transaction.MatchedPaymentId = null;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(new BankingContracts.BankTransactionMatchResponse(
            transaction.Id,
            (int)transaction.MatchStatus,
            reversalPaymentId));
    }

    internal static async Task<IResult> IgnoreAsync(
        Guid id,
        NumeraDbContext db,
        CancellationToken ct)
    {
        var transaction = await db.Set<BankTransaction>()
            .FirstOrDefaultAsync(candidate => candidate.Id == id, ct)
            .ConfigureAwait(false);
        if (transaction is null)
        {
            return Results.NotFound();
        }

        if (transaction.MatchStatus == MatchStatus.Confirmed || transaction.MatchedPaymentId is not null)
        {
            return Results.Conflict(new
            {
                error = "Eine gebuchte Zuordnung muss zuerst aufgehoben werden.",
            });
        }

        transaction.MatchStatus = MatchStatus.Ignored;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(new BankingContracts.BankTransactionMatchResponse(
            transaction.Id,
            (int)transaction.MatchStatus,
            null));
    }

    private static IResult MapPaymentFailure(PaymentOperationResult result) =>
        result.Status switch
        {
            PaymentOperationStatus.NotFound => Results.NotFound(),
            PaymentOperationStatus.Conflict => Results.Conflict(new { error = result.Error }),
            _ => Invalid(result.ErrorKey ?? "payment", result.Error ?? "Ung\u00fcltige Zahlung."),
        };

    private static IResult Invalid(string key, string error) =>
        Results.ValidationProblem(
            new Dictionary<string, string[]> { [key] = [error] },
            statusCode: StatusCodes.Status422UnprocessableEntity);
}
