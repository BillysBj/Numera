using Hangfire;

using Microsoft.EntityFrameworkCore;

using Numera.Api.Contracts;
using Numera.Api.Jobs;
using Numera.Modules.Banking;
using Numera.Modules.Banking.Import;
using Numera.Platform.Db;
using Numera.Platform.Tenancy;

namespace Numera.Api.Endpoints;

/// <summary>Authenticated bank connection, account synchronization, and statement import endpoints.</summary>
public static class BankAccountEndpoints
{
    private const long MaxStatementBytes = 10 * 1024 * 1024;

    /// <summary>Maps the tenant-scoped <c>/api/bank-accounts</c> surface.</summary>
    public static IEndpointRouteBuilder MapBankAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/bank-accounts").RequireAuthorization();

        group.MapPost("/connect", ConnectAsync);
        group.MapPost("/{connectionId:guid}/reauth", ReauthAsync);
        group.MapPost(
            "/connections/{connectionId:guid}/refresh-accounts",
            RefreshAccountsAsync);
        group.MapGet("/", ListAsync);
        group.MapGet("/connections/{id:guid}/consent", GetConsentAsync);
        group.MapPost("/{bankAccountId:guid}/sync", SyncAsync);
        group.MapPost("/import", ImportAsync).DisableAntiforgery();

        return app;
    }

    internal static async Task<IResult> ConnectAsync(
        NumeraDbContext db,
        ICurrentTenant currentTenant,
        IBankConnectionProvider provider,
        CancellationToken ct)
    {
        var tenantId = currentTenant.TenantId
            ?? throw new InvalidOperationException("A tenant is required to connect a bank account.");
        var connection = new BankConnection
        {
            TenantId = tenantId,
            ConsentStatus = ConsentStatus.Pending,
        };
        db.Add(connection);

        try
        {
            var session = await provider.StartImportAsync(connection, ct).ConfigureAwait(false);
            connection.WebFormId = session.WebFormId;
            connection.WebFormStatus = "PENDING";
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Results.Ok(new BankingContracts.BankWebFormResponse(
                session.WebFormId,
                session.RedirectUrl));
        }
        catch (NotSupportedException)
        {
            return LiveBankingNotConfigured();
        }
    }

    internal static async Task<IResult> ReauthAsync(
        Guid connectionId,
        NumeraDbContext db,
        IBankConnectionProvider provider,
        CancellationToken ct)
    {
        var connection = await db.Set<BankConnection>()
            .FirstOrDefaultAsync(candidate => candidate.Id == connectionId, ct)
            .ConfigureAwait(false);
        if (connection is null)
        {
            return Results.NotFound();
        }

        try
        {
            var session = await provider.StartReauthAsync(connection, ct).ConfigureAwait(false);
            connection.ConsentStatus = ConsentStatus.Pending;
            connection.WebFormId = session.WebFormId;
            connection.WebFormStatus = "PENDING";
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Results.Ok(new BankingContracts.BankWebFormResponse(
                session.WebFormId,
                session.RedirectUrl));
        }
        catch (NotSupportedException)
        {
            return LiveBankingNotConfigured();
        }
    }

    internal static async Task<IResult> ListAsync(
        NumeraDbContext db,
        CancellationToken ct)
    {
        var accounts = await db.Set<BankAccount>()
            .AsNoTracking()
            .OrderBy(account => account.DisplayName)
            .ThenBy(account => account.Id)
            .Select(account => new BankingContracts.BankAccountListItem(
                account.Id,
                account.Iban,
                account.DisplayName,
                account.Currency,
                account.BankConnectionId,
                account.BankConnection == null ? null : (int?)account.BankConnection.ConsentStatus,
                account.BankConnection == null ? null : account.BankConnection.LastSyncedAt,
                account.BankConnection == null ? null : account.BankConnection.ConsentExpiresAt))
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return Results.Ok(accounts);
    }

    internal static async Task<IResult> RefreshAccountsAsync(
        Guid connectionId,
        NumeraDbContext db,
        IBankConnectionProvider provider,
        CancellationToken ct)
    {
        var connection = await db.Set<BankConnection>()
            .FirstOrDefaultAsync(candidate => candidate.Id == connectionId, ct)
            .ConfigureAwait(false);
        if (connection is null)
        {
            return Results.NotFound();
        }

        var drafts = await provider.ListAccountsAsync(connection, ct).ConfigureAwait(false);
        var providerAccountIds = drafts
            .Select(draft => draft.FinApiAccountId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var persistedAccounts = providerAccountIds.Length == 0
            ? []
            : await db.Set<BankAccount>()
                .Where(account =>
                    account.FinApiAccountId != null
                    && providerAccountIds.Contains(account.FinApiAccountId))
                .ToListAsync(ct)
                .ConfigureAwait(false);
        var accountsByProviderId = persistedAccounts.ToDictionary(
            account => account.FinApiAccountId!,
            StringComparer.Ordinal);

        foreach (var draft in drafts)
        {
            if (!accountsByProviderId.TryGetValue(draft.FinApiAccountId, out var account))
            {
                account = new BankAccount
                {
                    TenantId = connection.TenantId,
                    Iban = draft.Iban,
                    DisplayName = draft.DisplayName,
                    Currency = draft.Currency,
                    FinApiAccountId = draft.FinApiAccountId,
                    BankConnectionId = connection.Id,
                };
                db.Add(account);
                accountsByProviderId.Add(draft.FinApiAccountId, account);
            }

            account.Iban = draft.Iban;
            account.DisplayName = draft.DisplayName;
            account.Currency = draft.Currency;
            account.FinApiAccountId = draft.FinApiAccountId;
            account.BankConnectionId = connection.Id;
        }

        var consent = await provider.GetConsentStatusAsync(connection, ct).ConfigureAwait(false);
        connection.ConsentStatus = consent.Status;
        connection.ConsentExpiresAt = consent.ExpiresAt;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var refreshedAccounts = await db.Set<BankAccount>()
            .AsNoTracking()
            .Where(account => account.BankConnectionId == connection.Id)
            .OrderBy(account => account.DisplayName)
            .ThenBy(account => account.Id)
            .Select(account => new BankingContracts.BankAccountListItem(
                account.Id,
                account.Iban,
                account.DisplayName,
                account.Currency,
                account.BankConnectionId,
                account.BankConnection == null ? null : (int?)account.BankConnection.ConsentStatus,
                account.BankConnection == null ? null : account.BankConnection.LastSyncedAt,
                account.BankConnection == null ? null : account.BankConnection.ConsentExpiresAt))
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return Results.Ok(refreshedAccounts);
    }

    internal static async Task<IResult> GetConsentAsync(
        Guid id,
        NumeraDbContext db,
        IBankConnectionProvider provider,
        CancellationToken ct)
    {
        var connection = await db.Set<BankConnection>()
            .FirstOrDefaultAsync(candidate => candidate.Id == id, ct)
            .ConfigureAwait(false);
        if (connection is null)
        {
            return Results.NotFound();
        }

        var snapshot = await provider.GetConsentStatusAsync(connection, ct).ConfigureAwait(false);
        connection.ConsentStatus = snapshot.Status;
        connection.ConsentExpiresAt = snapshot.ExpiresAt;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(new BankingContracts.BankConsentResponse(
            connection.Id,
            (int)snapshot.Status,
            snapshot.ExpiresAt));
    }

    internal static async Task<IResult> SyncAsync(
        Guid bankAccountId,
        NumeraDbContext db,
        ICurrentTenant currentTenant,
        IBackgroundJobClient jobs,
        CancellationToken ct)
    {
        var accountExists = await db.Set<BankAccount>()
            .AsNoTracking()
            .AnyAsync(account =>
                account.Id == bankAccountId && account.BankConnectionId != null,
                ct)
            .ConfigureAwait(false);
        if (!accountExists)
        {
            return Results.NotFound();
        }

        var tenantId = currentTenant.TenantId
            ?? throw new InvalidOperationException("A tenant is required to synchronize bank transactions.");
        var jobId = jobs.Enqueue<SyncBankTransactionsJob>(job =>
            job.RunAsync(tenantId, bankAccountId, CancellationToken.None));
        return Results.Accepted(
            $"/api/bank-accounts/{bankAccountId}/sync",
            new { bankAccountId, jobId });
    }

    internal static async Task<IResult> ImportAsync(
        IFormFile? file,
        Guid? bankAccountId,
        NumeraDbContext db,
        ICurrentTenant currentTenant,
        BankStatementImportDispatcher dispatcher,
        BankTransactionIngestService ingest,
        CancellationToken ct)
    {
        if (file is null || file.Length <= 0 || file.Length > MaxStatementBytes)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["file"] =
                [
                    $"Die Datei muss zwischen 1 Byte und {MaxStatementBytes} Bytes gro\u00df sein.",
                ],
            });
        }

        var fileName = string.IsNullOrWhiteSpace(file.FileName) ? "upload" : file.FileName.Trim();
        var contentType = file.ContentType?.Trim() ?? string.Empty;
        if (!IsSupportedStatement(fileName, contentType))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["file"] = ["Es werden nur CSV-, MT940- oder CAMT.053-Dateien akzeptiert."],
            });
        }

        BankAccount? account;
        if (bankAccountId is { } requestedAccountId)
        {
            account = await db.Set<BankAccount>()
                .FirstOrDefaultAsync(candidate => candidate.Id == requestedAccountId, ct)
                .ConfigureAwait(false);
            if (account is null)
            {
                return Results.NotFound();
            }
        }
        else
        {
            account = await GetOrCreateImportAccountAsync(db, currentTenant, ct)
                .ConfigureAwait(false);
        }

        using var stream = new MemoryStream(checked((int)file.Length));
        await file.CopyToAsync(stream, ct).ConfigureAwait(false);
        stream.Position = 0;

        try
        {
            var drafts = await dispatcher
                .ImportAsync(stream, fileName, contentType, ct)
                .ConfigureAwait(false);
            var inserted = await ingest.IngestAsync(account.Id, drafts, ct).ConfigureAwait(false);
            return Results.Ok(new BankingContracts.BankStatementImportResponse(
                account.Id,
                inserted));
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or FormatException
                or InvalidDataException
                or System.Xml.XmlException)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]> { ["file"] = [exception.Message] },
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "Kontoauszug konnte nicht importiert werden");
        }
    }

    private static async Task<BankAccount> GetOrCreateImportAccountAsync(
        NumeraDbContext db,
        ICurrentTenant currentTenant,
        CancellationToken ct)
    {
        var existing = await db.Set<BankAccount>()
            .FirstOrDefaultAsync(account =>
                account.BankConnectionId == null && account.FinApiAccountId == null,
                ct)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var tenantId = currentTenant.TenantId
            ?? throw new InvalidOperationException("A tenant is required to import bank transactions.");
        var account = new BankAccount
        {
            TenantId = tenantId,
            Iban = "IMPORT",
            DisplayName = "Kontoauszug-Import",
            Currency = "EUR",
        };
        db.Add(account);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return account;
    }

    private static bool IsSupportedStatement(string fileName, string contentType)
    {
        var extension = Path.GetExtension(fileName);
        if (extension.Equals(".csv", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".sta", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".mt940", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".txt", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".xml", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var mediaType = contentType.Split(';', 2)[0].Trim();
        return mediaType.Equals("text/csv", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("application/csv", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("application/mt940", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("application/x-mt940", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("application/xml", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("text/xml", StringComparison.OrdinalIgnoreCase);
    }

    private static IResult LiveBankingNotConfigured() =>
        Results.Problem(
            title: "Bankverbindung nicht verf\u00fcgbar",
            detail: "Live-Bankanbindung ist nicht konfiguriert.",
            statusCode: StatusCodes.Status422UnprocessableEntity);
}
