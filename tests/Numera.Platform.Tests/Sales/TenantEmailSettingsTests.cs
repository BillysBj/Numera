using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Numera.Api.Contracts;
using Numera.Api.Endpoints;
using Numera.Api.Services;
using Numera.Api.Validators;
using Numera.Modules.Sales;
using Numera.Modules.Sales.Email;
using Numera.Platform.Db;
using Numera.Platform.Tenancy;

using Xunit;

namespace Numera.Platform.Tests.Sales;

public sealed class TenantEmailSettingsTests
{
    private readonly SmtpPasswordProtector _passwords = new(new EphemeralDataProtectionProvider());

    [Fact]
    public void Password_is_encrypted_and_read_DTO_has_only_presence_flag()
    {
        var settings = new TenantEmailSettings();
        EmailSettingsEndpoints.Apply(settings, new() { Password = "secret-SMTP-password" }, _passwords);
        Assert.NotEqual("secret-SMTP-password", settings.PasswordCiphertext);
        Assert.Equal("secret-SMTP-password", _passwords.Unprotect(settings.PasswordCiphertext!));
        var json = JsonSerializer.Serialize(EmailSettingsDto.FromEntity(settings), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("secret-SMTP-password", json);
        Assert.DoesNotContain(settings.PasswordCiphertext!, json);
        using var response = JsonDocument.Parse(json);
        Assert.True(response.RootElement.GetProperty("hasPassword").GetBoolean());
        Assert.False(response.RootElement.TryGetProperty("password", out _));
        Assert.False(response.RootElement.TryGetProperty("passwordCiphertext", out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Omitted_or_empty_password_keeps_existing_ciphertext(string? replacement)
    {
        var settings = new TenantEmailSettings { PasswordCiphertext = _passwords.Protect("original") };
        var original = settings.PasswordCiphertext;
        EmailSettingsEndpoints.Apply(settings, new() { Password = replacement, InvoiceBody = "Changed" }, _passwords);
        Assert.Equal(original, settings.PasswordCiphertext);
        Assert.Equal("Changed", settings.InvoiceBody);
        EmailSettingsEndpoints.Apply(settings, new() { Password = "replacement" }, _passwords);
        Assert.Equal("replacement", _passwords.Unprotect(settings.PasswordCiphertext!));
    }

    [Fact]
    public void Independent_API_and_worker_providers_share_key_ring_and_purpose()
    {
        var directory = Directory.CreateTempSubdirectory("numera-email-keys-");
        try
        {
            using var api = Provider(directory, "Numera");
            using var worker = Provider(directory, "Numera");
            using var otherApp = Provider(directory, "OtherApp");
            var ciphertext = new SmtpPasswordProtector(api.GetRequiredService<IDataProtectionProvider>()).Protect("smtp secret");
            Assert.Equal("smtp secret", new SmtpPasswordProtector(worker.GetRequiredService<IDataProtectionProvider>()).Unprotect(ciphertext));
            Assert.Throws<CryptographicException>(() => new SmtpPasswordProtector(otherApp.GetRequiredService<IDataProtectionProvider>()).Unprotect(ciphertext));
            Assert.Throws<CryptographicException>(() => worker.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("WrongPurpose").Unprotect(ciphertext));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Resolver_uses_tenant_credentials_and_falls_back_without_host_or_row()
    {
        var tenant = new TenantContext();
        tenant.SetTenant(Guid.NewGuid());
        await using var db = Db(tenant);
        var global = new EmailOptions { Host = "global", Password = "global secret" };
        var resolver = new TenantEmailConfigResolver(db, tenant, Options.Create(global), _passwords);
        Assert.Same(global, await resolver.ResolveAsync());
        var settings = new TenantEmailSettings
        {
            TenantId = tenant.TenantId!.Value, Host = "smtp.tenant.test", Port = 465, UseSsl = true,
            Username = "tenant user", PasswordCiphertext = _passwords.Protect("tenant secret"),
            FromAddress = "billing@tenant.test", FromName = "Tenant",
        };
        db.Add(settings);
        await db.SaveChangesAsync();
        var options = await resolver.ResolveAsync();
        Assert.Equal("smtp.tenant.test", options.Host);
        Assert.Equal(465, options.Port);
        Assert.True(options.UseSsl);
        Assert.Equal("tenant user", options.Username);
        Assert.Equal("tenant secret", options.Password);
        Assert.Equal("billing@tenant.test", options.FromAddress);
        Assert.Equal("Tenant", options.FromName);

        settings.PasswordCiphertext = null;
        await db.SaveChangesAsync();
        Assert.Null((await resolver.ResolveAsync()).Password); // Never mix global credentials into a tenant config.
        settings.Host = " ";
        settings.PasswordCiphertext = "invalid-but-unused";
        await db.SaveChangesAsync();
        Assert.Same(global, await resolver.ResolveAsync());
    }

    [Fact]
    public async Task Resolver_isolates_tenants_and_fails_closed_for_invalid_ciphertext_or_missing_context()
    {
        var tenant = new TenantContext();
        var first = Guid.NewGuid();
        tenant.SetTenant(first);
        await using var db = Db(tenant);
        db.Add(new TenantEmailSettings { TenantId = first, Host = "first.test", PasswordCiphertext = "broken" });
        await db.SaveChangesAsync();
        var global = new EmailOptions();
        var resolver = new TenantEmailConfigResolver(db, tenant, Options.Create(global), _passwords);
        await Assert.ThrowsAsync<CryptographicException>(() => resolver.ResolveAsync());
        tenant.SetTenant(Guid.NewGuid());
        Assert.Same(global, await resolver.ResolveAsync());
        var unset = new TenantContext();
        await using var unsetDb = Db(unset);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new TenantEmailConfigResolver(unsetDb, unset, Options.Create(global), _passwords).ResolveAsync());
    }

    [Theory]
    [InlineData("de", "Rechnung")]
    [InlineData("en", "Invoice")]
    public void Invoice_placeholders_are_formatted_and_HTML_encoded_in_one_pass(string language, string label)
    {
        var doc = Document();
        var settings = new TenantEmailSettings
        {
            InvoiceSubject = "{Belegart} {Rechnungsnummer} — {Kundenname}",
            InvoiceBody = "{Kundenname}\n{Firmenname}\n{Belegart} {Rechnungsnummer}\n{Betrag} {Fälligkeitsdatum} {Unknown}",
        };
        var result = TenantEmailTemplates.Invoice(settings, doc, language, doc.DocumentNumber!);
        Assert.StartsWith(label + " RE-001", result.Subject);
        Assert.Contains("<script>alert('x')</script> {Firmenname}", result.TextBody);
        Assert.Contains("A & B GmbH", result.TextBody);
        Assert.Contains(1234.56m.ToString("C", CultureInfo.GetCultureInfo("de-DE")), result.TextBody);
        Assert.Contains("15.10.2026 {Unknown}", result.TextBody);
        Assert.DoesNotContain("<script>", result.HtmlBody);
        Assert.Contains("&lt;script&gt;", result.HtmlBody);
        Assert.Contains("A &amp; B GmbH", result.HtmlBody);
        Assert.Contains("<br/>", result.HtmlBody);
        Assert.Contains("{Firmenname}", result.HtmlBody); // Inserted values are not expanded recursively.
    }

    [Theory]
    [InlineData("de")]
    [InlineData("en")]
    public void Empty_fields_fall_back_independently_and_default_document_numbers_are_escaped(string language)
    {
        var doc = Document();
        var defaults = DocumentEmailTemplates.Build(language, "<unsafe>");
        Assert.DoesNotContain("<unsafe>", defaults.HtmlBody);
        Assert.Contains("<unsafe>", defaults.TextBody);
        var result = TenantEmailTemplates.Invoice(null, doc, language, "<unsafe>");
        Assert.Equal(defaults, result);
        result = TenantEmailTemplates.Invoice(new() { InvoiceSubject = "Custom", InvoiceBody = "" }, doc, language, "<unsafe>");
        Assert.Equal("Custom", result.Subject);
        Assert.Equal(defaults.HtmlBody, result.HtmlBody);
        result = TenantEmailTemplates.Invoice(new() { InvoiceBody = "<b>{Kundenname}</b>" }, doc, language, "RE-001");
        Assert.Equal(DocumentEmailTemplates.Build(language, "RE-001").Subject, result.Subject);
        Assert.Contains("&lt;b&gt;", result.HtmlBody);
    }

    [Theory]
    [InlineData("de", "Anbei erhalten Sie")]
    [InlineData("en", "Please find")]
    public void Dunning_uses_notice_fee_and_level_with_original_defaults(string language, string defaultStart)
    {
        var doc = Document();
        var defaults = TenantEmailTemplates.Dunning(null, doc, language, "1. Mahnung", 5m);
        Assert.Equal("1. Mahnung – RE-001", defaults.Subject);
        Assert.StartsWith(defaultStart, defaults.TextBody);
        var result = TenantEmailTemplates.Dunning(new()
        {
            DunningSubject = "{Mahnstufe}: {Rechnungsnummer}",
            DunningBody = "{Mahngebühr} {Kundenname}",
        }, doc, language, "<level>", 5m);
        Assert.Equal("<level>: RE-001", result.Subject);
        Assert.StartsWith(5m.ToString("C", CultureInfo.GetCultureInfo("de-DE")), result.TextBody);
        Assert.DoesNotContain("<script>", result.HtmlBody);
    }

    [Theory]
    [InlineData("smtp.test", 0, "from@test.invalid", false)]
    [InlineData("smtp.test", 65536, "from@test.invalid", false)]
    [InlineData("https://smtp.test", 587, "from@test.invalid", false)]
    [InlineData("smtp.test", 587, "bad", false)]
    [InlineData("smtp.test", 587, "from@test.invalid\r\nBcc: other@test.invalid", false)]
    [InlineData("smtp.test", 587, "from@test.invalid", true)]
    [InlineData("", 0, "", true)]
    public void Validates_SMTP_when_host_is_set(string host, int port, string from, bool valid)
    {
        Assert.Equal(valid, new UpdateEmailSettingsValidator().Validate(new UpdateEmailSettingsRequest
        {
            Host = host, Port = port, FromAddress = from,
        }).IsValid);
    }

    private static ServiceProvider Provider(DirectoryInfo keys, string name)
    {
        var services = new ServiceCollection();
        services.AddDataProtection().SetApplicationName(name).PersistKeysToFileSystem(keys);
        return services.BuildServiceProvider();
    }

    private static NumeraDbContext Db(ICurrentTenant tenant) => new(
        new DbContextOptionsBuilder<NumeraDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, tenant);

    private static SalesDocument Document() => new()
    {
        DocumentType = DocumentType.Rechnung, DocumentNumber = "RE-001", TotalGross = 1234.56m,
        DueDate = new(2026, 10, 15),
        RecipientSnapshot = JsonSerializer.Serialize(new { name = "<script>alert('x')</script> {Firmenname}" }),
        IssuerSnapshot = JsonSerializer.Serialize(new { legalName = "A & B GmbH" }),
    };
}
