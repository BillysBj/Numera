using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

using Numera.Api.Services;
using Numera.Modules.Sales.Email;
using Numera.Platform.Audit;
using Numera.Platform.Db;
using Numera.Platform.Db.Entities;
using Numera.Platform.Tenancy;

using Xunit;

namespace Numera.Platform.Tests;

public sealed class InvitationServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invite_sends_after_commit_using_tenant_config_and_correct_credentials(bool existingUser)
    {
        using var fixture = new InviteFixture(existingUser);
        var result = await fixture.Service.InviteAsync("member@example.test", MembershipRole.TaxAdvisor, default);

        Assert.Equal(fixture.UserId, result.UserId);
        Assert.True(result.EmailSent);
        Assert.True(fixture.Sender.MembershipWasCommitted);
        Assert.Equal(MembershipRole.TaxAdvisor, fixture.Sender.SavedRole);
        Assert.Equal("tenant.smtp.test", fixture.Sender.Options?.Host);
        Assert.Equal("tenant-secret", fixture.Sender.Options?.Password);
        var message = Assert.Single(fixture.Sender.Messages);
        Assert.Equal("member@example.test", message.To);
        Assert.Equal("Einladung zu Numera", message.Subject);
        Assert.Contains("Steuerberater", message.TextBody);
        Assert.Contains("Test & Company", message.TextBody);
        Assert.Contains("https://numera.test/login", message.TextBody);
        Assert.Contains("Benutzername: member@example.test", message.TextBody);
        if (existingUser)
        {
            Assert.Null(result.TemporaryPassword);
            Assert.Null(fixture.Keycloak.Password);
            Assert.Contains("bestehenden Zugangsdaten", message.TextBody);
            Assert.DoesNotContain("Temporäres Passwort", message.TextBody);
            Assert.DoesNotContain("Temporäres Passwort", message.HtmlBody);
        }
        else
        {
            Assert.False(string.IsNullOrWhiteSpace(result.TemporaryPassword));
            Assert.Equal(fixture.Keycloak.Password, result.TemporaryPassword);
            Assert.True(fixture.Keycloak.PasswordIsTemporary);
            Assert.Contains(result.TemporaryPassword, message.TextBody);
            Assert.Contains(WebUtility.HtmlEncode(result.TemporaryPassword), message.HtmlBody);
            Assert.Contains("bei der ersten Anmeldung ändern", message.TextBody);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Smtp_failure_or_missing_configuration_preserves_membership_and_password(bool missingSmtp)
    {
        using var fixture = new InviteFixture(missingSmtp: missingSmtp);
        if (!missingSmtp)
            fixture.Sender.Failure = new InvalidOperationException("SMTP unavailable");

        var result = await fixture.Service.InviteAsync("member@example.test", MembershipRole.Employee, default);

        Assert.False(result.EmailSent);
        Assert.Equal(fixture.UserId, result.UserId);
        Assert.False(string.IsNullOrWhiteSpace(result.TemporaryPassword));
        Assert.Equal(fixture.Keycloak.Password, result.TemporaryPassword);
        Assert.True(fixture.Sender.MembershipWasCommitted);
        Assert.Single(fixture.Sender.Messages);
        Assert.Equal(MembershipRole.Employee, (await fixture.Db.Memberships.SingleAsync()).Role);
    }

    [Fact]
    public async Task Email_cancellation_is_not_swallowed()
    {
        using var fixture = new InviteFixture();
        fixture.Sender.Failure = new OperationCanceledException();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            fixture.Service.InviteAsync("member@example.test", MembershipRole.Employee, default));
        Assert.True(fixture.Sender.MembershipWasCommitted);
    }

    [Fact]
    public void Template_escapes_injected_values_and_preserves_plain_text()
    {
        const string email = "<member>&\"'@example.test";
        const string company = "<script>alert('company')</script>&\"";
        const string password = "<img src=x onerror='alert(1)'>&\"";
        const string url = "https://numera.test/login?a=1&b=\"value\"";
        var message = InvitationEmailTemplate.Build(email, MembershipRole.Owner, company, password, url);
        foreach (var value in new[] { email, company, password, url })
        {
            Assert.Contains(value, message.TextBody);
            Assert.Contains(WebUtility.HtmlEncode(value), message.HtmlBody);
            Assert.DoesNotContain(value, message.HtmlBody);
        }
        Assert.Contains("Inhaber", message.HtmlBody);
    }

    [Theory]
    [InlineData("https://public.test/", "https://public.test/login")]
    [InlineData("https://public.test/numera/", "https://public.test/numera/login")]
    [InlineData("", "https://request.test:8443/login")]
    [InlineData(null, "https://request.test:8443/login")]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("not a url", null)]
    [InlineData("https://public.test/?query=value", null)]
    public void Login_url_prefers_configuration_and_falls_back_to_request(string? configured, string? expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("request.test", 8443);
        Assert.Equal(expected, InvitationEmailTemplate.LoginUrl(configured, context.Request));
    }

    [Fact]
    public void Missing_origin_produces_generic_instructions_without_a_link()
    {
        var url = InvitationEmailTemplate.LoginUrl(null, null);
        Assert.Null(url);
        Assert.Null(InvitationEmailTemplate.LoginUrl("", new DefaultHttpContext().Request));
        var message = InvitationEmailTemplate.Build("member@example.test", MembershipRole.Employee, null, null, url);
        Assert.Contains("Öffnen Sie Numera in Ihrem Browser", message.TextBody);
        Assert.DoesNotContain("href=", message.HtmlBody);
    }

    private sealed class InviteFixture : IDisposable
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public NumeraDbContext Db { get; }
        public KeycloakHandler Keycloak { get; }
        public CaptureSender Sender { get; }
        public InvitationService Service { get; }

        public InviteFixture(bool existingUser = false, bool missingSmtp = false)
        {
            var tenant = new TenantContext();
            var tenantId = Guid.NewGuid();
            tenant.SetTenant(tenantId);
            var options = new DbContextOptionsBuilder<NumeraDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            Db = new NumeraDbContext(options, tenant);
            var passwords = new SmtpPasswordProtector(new EphemeralDataProtectionProvider());
            Db.Add(new Tenant { Id = tenantId, Name = "Test & Company" });
            if (!missingSmtp)
                Db.Add(new TenantEmailSettings
                {
                    TenantId = tenantId, Host = "tenant.smtp.test", FromAddress = "sender@tenant.test",
                    PasswordCiphertext = passwords.Protect("tenant-secret"),
                });
            Db.Add(new TenantEmailSettings { TenantId = Guid.NewGuid(), Host = "other.smtp.test" });
            Db.SaveChanges();
            var resolver = new TenantEmailConfigResolver(Db, tenant,
                Microsoft.Extensions.Options.Options.Create(new EmailOptions { Host = missingSmtp ? "" : "global.smtp.test" }), passwords);
            Sender = new CaptureSender(resolver, () => new NumeraDbContext(options, tenant), UserId);
            Keycloak = new KeycloakHandler(UserId, existingUser);
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Keycloak:AdminBaseUrl"] = "https://keycloak.test/",
                ["Keycloak:Realm"] = "numera",
                ["Keycloak:AdminUsername"] = "admin",
                ["Keycloak:AdminPassword"] = "admin",
                ["App:PublicUrl"] = "https://numera.test",
            }).Build();
            Service = new InvitationService(new FakeHttpClientFactory(Keycloak), Db, tenant, config,
                NullLogger<InvitationService>.Instance, new NoOpAudit(), Sender, new HttpContextAccessor());
        }

        public void Dispose()
        {
            Db.Dispose();
            Keycloak.Dispose();
        }
    }

    private sealed class CaptureSender(ITenantEmailConfigResolver resolver, Func<NumeraDbContext> readDb, Guid userId) : IEmailSender
    {
        public List<EmailMessage> Messages { get; } = [];
        public Exception? Failure { get; set; }
        public EmailOptions? Options { get; private set; }
        public bool MembershipWasCommitted { get; private set; }
        public MembershipRole? SavedRole { get; private set; }

        public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            Messages.Add(message);
            await using var read = readDb();
            var membership = await read.Memberships.SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
            MembershipWasCommitted = membership is not null;
            SavedRole = membership?.Role;
            Options = await resolver.ResolveAsync(cancellationToken);
            if (Failure is not null) throw Failure;
            if (string.IsNullOrWhiteSpace(Options.Host)) throw new InvalidOperationException("SMTP not configured");
        }
    }

    private sealed class NoOpAudit : IAuditWriter
    {
        public Task RecordAsync(IAuditEvent evt, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeHttpClientFactory(KeycloakHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class KeycloakHandler(Guid userId, bool existingUser) : HttpMessageHandler
    {
        public string? Password { get; private set; }
        public bool PasswordIsTemporary { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/token", StringComparison.Ordinal))
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { access_token = "test-token" }) };
            if (path.EndsWith("/users", StringComparison.Ordinal))
            {
                if (request.Method == HttpMethod.Get)
                    return new(HttpStatusCode.OK) { Content = JsonContent.Create(new[] { new { id = userId.ToString() } }) };
                if (existingUser) return new(HttpStatusCode.Conflict);
                var created = new HttpResponseMessage(HttpStatusCode.Created);
                created.Headers.Location = new Uri($"https://keycloak.test/admin/realms/numera/users/{userId}");
                return created;
            }
            if (path.EndsWith("/reset-password", StringComparison.Ordinal))
            {
                var body = await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken);
                Password = body.GetProperty("value").GetString();
                PasswordIsTemporary = body.GetProperty("temporary").GetBoolean();
            }
            return new(HttpStatusCode.NoContent);
        }
    }
}
