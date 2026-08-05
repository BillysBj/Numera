using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

using Microsoft.Extensions.Options;

namespace Numera.Api.Services.FinApi;

/// <summary>Thin typed client for the finAPI Access AIS surface used by Numera.</summary>
public sealed class FinApiClient
{
    private const string TokenPath = "api/v1/oauth/token";
    private const string UsersPath = "api/v1/users";
    private const string ImportWebFormPath = "api/v2/webForms/bankConnectionImport";
    private const string UpdateWebFormPath = "api/v2/webForms/bankConnectionUpdate";
    private const string AccountsPath = "api/v1/accounts";
    private const int TransactionsPerPage = 100;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly FinApiOptions _options;

    /// <summary>Creates a client over the typed <see cref="HttpClient"/> registration.</summary>
    public FinApiClient(HttpClient http, IOptions<FinApiOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    /// <summary>Obtains an application token with the OAuth2 client-credentials grant.</summary>
    public async Task<string> GetClientTokenAsync(CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenPath)
        {
            Content = new FormUrlEncodedContent(
            [
                new("grant_type", "client_credentials"),
                new("client_id", _options.ClientId),
                new("client_secret", _options.ClientSecret),
            ]),
        };
        using var document = await SendAsync(request, "client token", ct).ConfigureAwait(false);
        return RequiredString(document.RootElement, "access_token");
    }

    /// <summary>Creates a finAPI sub-user and returns the generated id and secret.</summary>
    public async Task<FinApiUserCredentials> CreateUserAsync(CancellationToken ct = default)
    {
        var clientToken = await GetClientTokenAsync(ct).ConfigureAwait(false);
        var requestedId = Guid.CreateVersion7().ToString("N", CultureInfo.InvariantCulture);
        var secret = CreateSecret();
        using var request = CreateBearerRequest(HttpMethod.Post, UsersPath, clientToken);
        request.Content = JsonContent.Create(
            new { id = requestedId, password = secret },
            options: Json);

        using var document = await SendAsync(request, "create user", ct).ConfigureAwait(false);
        var id = OptionalString(document.RootElement, "id", "userId") ?? requestedId;
        return new FinApiUserCredentials(id, secret);
    }

    /// <summary>Obtains a user-scoped access token for a provisioned finAPI sub-user.</summary>
    public async Task<string> GetUserTokenAsync(
        string userId,
        string secret,
        CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenPath)
        {
            Content = new FormUrlEncodedContent(
            [
                new("grant_type", "password"),
                new("client_id", _options.ClientId),
                new("client_secret", _options.ClientSecret),
                new("username", userId),
                new("password", secret),
            ]),
        };
        using var document = await SendAsync(request, "user token", ct).ConfigureAwait(false);
        return RequiredString(document.RootElement, "access_token");
    }

    /// <summary>Starts Web Form 2.0 bank-connection import.</summary>
    public Task<FinApiWebForm> StartWebFormImportAsync(
        string userToken,
        CancellationToken ct = default) =>
        StartWebFormAsync(
            ImportWebFormPath,
            userToken,
            new { },
            "start bank connection import",
            ct);

    /// <summary>Starts Web Form 2.0 update/SCA for an existing bank connection.</summary>
    public Task<FinApiWebForm> StartWebFormUpdateAsync(
        string userToken,
        string bankConnectionId,
        CancellationToken ct = default) =>
        StartWebFormAsync(
            UpdateWebFormPath,
            userToken,
            new { bankConnectionId },
            "start bank connection update",
            ct);

    /// <summary>Lists the user-visible accounts.</summary>
    public async Task<IReadOnlyList<FinApiAccount>> GetAccountsAsync(
        string userToken,
        CancellationToken ct = default)
    {
        using var request = CreateBearerRequest(HttpMethod.Get, AccountsPath, userToken);
        using var document = await SendAsync(request, "list accounts", ct).ConfigureAwait(false);
        var items = ArrayPayload(document.RootElement, "accounts");
        var accounts = new List<FinApiAccount>(items.GetArrayLength());
        foreach (var item in items.EnumerateArray())
        {
            accounts.Add(new FinApiAccount(
                RequiredIdentifier(item, "id", "accountId"),
                OptionalString(item, "iban") ?? string.Empty,
                OptionalString(item, "accountName", "name", "displayName") ?? "Bankkonto",
                OptionalString(item, "currency") ?? "EUR",
                OptionalIdentifier(item, "bankConnectionId")));
        }

        return accounts;
    }

    /// <summary>
    /// Fetches all transaction pages for one account, clamping the incremental start
    /// date to finAPI's supported 89-day download window.
    /// </summary>
    public async Task<IReadOnlyList<FinApiTransaction>> GetTransactionsAsync(
        string userToken,
        string accountId,
        string? sinceCursor,
        CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var earliest = today.AddDays(-89);
        if (DateOnly.TryParseExact(
                sinceCursor,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var cursorDate)
            && cursorDate > earliest)
        {
            earliest = cursorDate;
        }

        var transactions = new List<FinApiTransaction>();
        var page = 1;
        var totalPages = 1;
        do
        {
            var path = string.Create(
                CultureInfo.InvariantCulture,
                $"api/v1/transactions?accountIds={Uri.EscapeDataString(accountId)}&minBankBookingDate={earliest:yyyy-MM-dd}&page={page}&perPage={TransactionsPerPage}");
            using var request = CreateBearerRequest(HttpMethod.Get, path, userToken);
            using var document = await SendAsync(request, "list transactions", ct).ConfigureAwait(false);
            var items = ArrayPayload(document.RootElement, "transactions");
            foreach (var item in items.EnumerateArray())
            {
                transactions.Add(MapTransaction(item));
            }

            totalPages = ReadTotalPages(document.RootElement, page);
            page++;
        }
        while (page <= totalPages);

        return transactions;
    }

    /// <summary>Reads the current consent status for one finAPI bank connection.</summary>
    public async Task<FinApiConsent> GetConsentStatusAsync(
        string userToken,
        string bankConnectionId,
        CancellationToken ct = default)
    {
        var path = $"api/v1/bankConnections/{Uri.EscapeDataString(bankConnectionId)}";
        using var request = CreateBearerRequest(HttpMethod.Get, path, userToken);
        using var document = await SendAsync(request, "get bank consent", ct).ConfigureAwait(false);
        var payload = document.RootElement;
        if (TryProperty(payload, out var nested, "bankConnection"))
        {
            payload = nested;
        }

        var status = OptionalString(payload, "consentStatus", "status") ?? "PENDING";
        var expiryText = OptionalString(
            payload,
            "consentExpiresAt",
            "consentValidUntil",
            "validUntil");
        DateTimeOffset? expiresAt = null;
        if (DateTimeOffset.TryParse(
                expiryText,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsedExpiry))
        {
            expiresAt = parsedExpiry;
        }

        return new FinApiConsent(status, expiresAt);
    }

    private async Task<FinApiWebForm> StartWebFormAsync(
        string path,
        string userToken,
        object body,
        string operation,
        CancellationToken ct)
    {
        using var request = CreateBearerRequest(HttpMethod.Post, path, userToken);
        request.Content = JsonContent.Create(body, options: Json);
        using var document = await SendAsync(request, operation, ct).ConfigureAwait(false);
        var id = RequiredIdentifier(document.RootElement, "webFormId", "id");
        var returnedUrl = OptionalString(document.RootElement, "redirectUrl", "url");
        return new FinApiWebForm(id, ResolveWebFormUrl(id, returnedUrl));
    }

    private async Task<JsonDocument> SendAsync(
        HttpRequestMessage request,
        string operation,
        CancellationToken ct)
    {
        using var response = await _http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                ct)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new FinApiException(response.StatusCode, operation);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
    }

    private string ResolveWebFormUrl(string id, string? returnedUrl)
    {
        if (!string.IsNullOrWhiteSpace(returnedUrl)
            && Uri.TryCreate(returnedUrl, UriKind.Absolute, out var absolute))
        {
            return absolute.AbsoluteUri;
        }

        var baseUrl = !string.IsNullOrWhiteSpace(_options.WebFormBaseUrl)
            ? _options.WebFormBaseUrl
            : _options.BaseUrl;
        var normalizedBase = new Uri(baseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        return new Uri(normalizedBase, returnedUrl?.TrimStart('/') ?? id).AbsoluteUri;
    }

    private static HttpRequestMessage CreateBearerRequest(
        HttpMethod method,
        string path,
        string token)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static FinApiTransaction MapTransaction(JsonElement item) => new(
        RequiredIdentifier(item, "id", "transactionId"),
        RequiredDecimal(item, "amount"),
        RequiredDate(item, "valueDate", "bankBookingDate"),
        OptionalDate(item, "bankBookingDate", "bookingDate"),
        OptionalString(item, "purpose", "remittanceInformation"),
        OptionalString(item, "counterpartName", "counterpartyName"),
        OptionalString(item, "counterpartIban", "counterpartyIban"),
        OptionalString(item, "endToEndReference", "endToEndId"));

    private static JsonElement ArrayPayload(JsonElement root, string propertyName)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return root;
        }

        if (TryProperty(root, out var array, propertyName)
            && array.ValueKind == JsonValueKind.Array)
        {
            return array;
        }

        throw new JsonException($"finAPI response is missing the '{propertyName}' array.");
    }

    private static int ReadTotalPages(JsonElement root, int currentPage)
    {
        var paging = root;
        if (TryProperty(root, out var nested, "paging"))
        {
            paging = nested;
        }

        if (TryProperty(paging, out var total, "totalPages", "pageCount")
            && total.ValueKind == JsonValueKind.Number
            && total.TryGetInt32(out var value))
        {
            return Math.Max(currentPage, value);
        }

        return currentPage;
    }

    private static string RequiredString(JsonElement element, params string[] names) =>
        OptionalString(element, names)
        ?? throw new JsonException($"finAPI response is missing '{names[0]}'.");

    private static string RequiredIdentifier(JsonElement element, params string[] names) =>
        OptionalIdentifier(element, names)
        ?? throw new JsonException($"finAPI response is missing '{names[0]}'.");

    private static string? OptionalIdentifier(JsonElement element, params string[] names)
    {
        if (!TryProperty(element, out var value, names))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
    }

    private static string? OptionalString(JsonElement element, params string[] names)
    {
        if (!TryProperty(element, out var value, names))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static decimal RequiredDecimal(JsonElement element, params string[] names)
    {
        if (!TryProperty(element, out var value, names))
        {
            throw new JsonException($"finAPI response is missing '{names[0]}'.");
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String
            && decimal.TryParse(
                value.GetString(),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out number))
        {
            return number;
        }

        throw new JsonException($"finAPI response field '{names[0]}' is not a decimal.");
    }

    private static DateOnly RequiredDate(JsonElement element, params string[] names) =>
        OptionalDate(element, names)
        ?? throw new JsonException($"finAPI response is missing '{names[0]}'.");

    private static DateOnly? OptionalDate(JsonElement element, params string[] names)
    {
        var value = OptionalString(element, names);
        return DateOnly.TryParseExact(
            value,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date)
            ? date
            : null;
    }

    private static bool TryProperty(
        JsonElement element,
        out JsonElement value,
        params string[] names)
    {
        foreach (var name in names)
        {
            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(name, out value))
            {
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string CreateSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}

/// <summary>Credentials generated for a finAPI sub-user.</summary>
public sealed record FinApiUserCredentials(string UserId, string Secret);

/// <summary>Browser hand-off for a finAPI Web Form 2.0 flow.</summary>
public sealed record FinApiWebForm(string WebFormId, string RedirectUrl);

/// <summary>Minimal finAPI account payload.</summary>
public sealed record FinApiAccount(
    string Id,
    string Iban,
    string DisplayName,
    string Currency,
    string? BankConnectionId);

/// <summary>Minimal finAPI transaction with money normalized to decimal.</summary>
public sealed record FinApiTransaction(
    string ProviderId,
    decimal Amount,
    DateOnly ValueDate,
    DateOnly? BookingDate,
    string? Purpose,
    string? CounterpartyName,
    string? CounterpartyIban,
    string? EndToEndId);

/// <summary>Minimal finAPI consent state.</summary>
public sealed record FinApiConsent(string Status, DateTimeOffset? ExpiresAt);

/// <summary>Sanitized failure raised for a non-success finAPI response.</summary>
public sealed class FinApiException : HttpRequestException
{
    /// <summary>Creates an exception without including response bodies or credentials.</summary>
    public FinApiException(HttpStatusCode statusCode, string operation)
        : base(
            $"finAPI operation '{operation}' failed with HTTP {(int)statusCode}.",
            inner: null,
            statusCode)
    {
        Operation = operation;
    }

    /// <summary>The sanitized operation name.</summary>
    public string Operation { get; }
}
