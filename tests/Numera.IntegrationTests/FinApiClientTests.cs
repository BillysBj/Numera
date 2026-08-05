using System.Net;
using System.Text;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

using Numera.Api.Services.FinApi;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>Offline contract tests for the narrow finAPI HTTP boundary.</summary>
public sealed class FinApiClientTests
{
    [Fact]
    public async Task Client_maps_token_web_form_accounts_transactions_and_consent_without_network()
    {
        using var handler = new FinApiStubHandler();
        using var http = new HttpClient(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://finapi.test/"),
        };
        var client = new FinApiClient(
            http,
            Options.Create(new FinApiOptions
            {
                ClientId = "test-client",
                ClientSecret = "test-client-secret",
                BaseUrl = "https://finapi.test/",
                WebFormBaseUrl = "https://webform.finapi.test/",
            }));

        var clientToken = await client.GetClientTokenAsync();
        var user = await client.CreateUserAsync();
        var userToken = await client.GetUserTokenAsync(user.UserId, user.Secret);
        var webForm = await client.StartWebFormImportAsync(userToken);
        var accounts = await client.GetAccountsAsync(userToken);
        var transactions = await client.GetTransactionsAsync(
            userToken,
            accounts.Single().Id,
            "2026-07-01");
        var consent = await client.GetConsentStatusAsync(userToken, "connection-17");

        Assert.Equal("client-token", clientToken);
        Assert.Equal("fin-user-17", user.UserId);
        Assert.NotEmpty(user.Secret);
        Assert.Equal("user-token", userToken);
        Assert.Equal("web-form-17", webForm.WebFormId);
        Assert.Equal("https://webform.finapi.test/session/web-form-17", webForm.RedirectUrl);
        Assert.Equal("account-17", accounts.Single().Id);

        Assert.Collection(
            transactions,
            credit =>
            {
                Assert.Equal("transaction-credit", credit.ProviderId);
                Assert.Equal(125.50m, credit.Amount);
            },
            debit =>
            {
                Assert.Equal("transaction-debit", debit.ProviderId);
                Assert.Equal(-42.15m, debit.Amount);
            });
        Assert.Equal("ACTIVE", consent.Status);
        Assert.Equal(
            DateTimeOffset.Parse("2026-10-31T22:00:00Z"),
            consent.ExpiresAt);

        Assert.True(handler.WasUsed);
        Assert.Equal(9, handler.CallCount);
        Assert.All(
            handler.RequestUris,
            uri => Assert.Equal("finapi.test", uri.Host));
    }

    [Fact]
    public void Credential_protector_round_trips_without_storing_plaintext()
    {
        var protector = new DataProtectionBankCredentialProtector(
            new EphemeralDataProtectionProvider());
        const string original = "finapi-user-secret-and-token";

        var encrypted = protector.Protect(original);

        Assert.NotEqual(original, encrypted);
        Assert.Equal(original, protector.Unprotect(encrypted));
    }

    private sealed class FinApiStubHandler : HttpMessageHandler
    {
        private readonly List<Uri> _requestUris = [];

        public bool WasUsed { get; private set; }

        public int CallCount { get; private set; }

        public IReadOnlyList<Uri> RequestUris => _requestUris;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            WasUsed = true;
            CallCount++;
            _requestUris.Add(request.RequestUri!);

            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/oauth/token", StringComparison.Ordinal))
            {
                var form = await request.Content!.ReadAsStringAsync(cancellationToken);
                return form.Contains("grant_type=client_credentials", StringComparison.Ordinal)
                    ? Json(HttpStatusCode.OK, """{"access_token":"client-token"}""")
                    : Json(HttpStatusCode.OK, """{"access_token":"user-token"}""");
            }

            if (request.Method == HttpMethod.Post
                && path.EndsWith("/users", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.Created, """{"id":"fin-user-17"}""");
            }

            if (path.EndsWith("/webForms/bankConnectionImport", StringComparison.Ordinal))
            {
                return Json(
                    HttpStatusCode.Created,
                    """
                    {
                      "webFormId": "web-form-17",
                      "redirectUrl": "https://webform.finapi.test/session/web-form-17"
                    }
                    """);
            }

            if (path.EndsWith("/accounts", StringComparison.Ordinal))
            {
                return Json(
                    HttpStatusCode.OK,
                    """
                    {
                      "accounts": [
                        {
                          "id": "account-17",
                          "iban": "DE02120300000000202051",
                          "accountName": "Geschäftskonto",
                          "currency": "EUR",
                          "bankConnectionId": "connection-17"
                        }
                      ]
                    }
                    """);
            }

            if (path.EndsWith("/transactions", StringComparison.Ordinal))
            {
                var page = GetQueryValue(request.RequestUri, "page");
                return page == "1"
                    ? Json(
                        HttpStatusCode.OK,
                        """
                        {
                          "transactions": [
                            {
                              "id": "transaction-credit",
                              "amount": "125.50",
                              "valueDate": "2026-07-15",
                              "bankBookingDate": "2026-07-15",
                              "purpose": "RE-2026-00017",
                              "counterpartName": "Kunde AG",
                              "counterpartIban": "DE89370400440532013000",
                              "endToEndReference": "E2E-CREDIT"
                            }
                          ],
                          "paging": { "page": 1, "totalPages": 2 }
                        }
                        """)
                    : Json(
                        HttpStatusCode.OK,
                        """
                        {
                          "transactions": [
                            {
                              "id": "transaction-debit",
                              "amount": -42.15,
                              "valueDate": "2026-07-16",
                              "bankBookingDate": "2026-07-16",
                              "purpose": "Kontoführung"
                            }
                          ],
                          "paging": { "page": 2, "totalPages": 2 }
                        }
                        """);
            }

            if (path.EndsWith("/bankConnections/connection-17", StringComparison.Ordinal))
            {
                return Json(
                    HttpStatusCode.OK,
                    """
                    {
                      "consentStatus": "ACTIVE",
                      "consentExpiresAt": "2026-10-31T22:00:00Z"
                    }
                    """);
            }

            return Json(HttpStatusCode.NotFound, """{"code":"unexpected-test-request"}""");
        }

        private static string? GetQueryValue(Uri uri, string name)
        {
            foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split('=', 2);
                if (parts.Length == 2
                    && string.Equals(
                        Uri.UnescapeDataString(parts[0]),
                        name,
                        StringComparison.Ordinal))
                {
                    return Uri.UnescapeDataString(parts[1]);
                }
            }

            return null;
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }
}
