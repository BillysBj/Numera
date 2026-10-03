using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Npgsql;

using Numera.Api.Services;
using Numera.Modules.Sales.Email;
using Numera.Platform.Tenancy;

using Xunit;

namespace Numera.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class TenantEmailSettingsRlsTests(PostgresFixture fixture)
{
    [Fact]
    public async Task RLS_isolates_reads_and_updates_and_resolver_uses_only_current_tenant()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var passwords = new SmtpPasswordProtector(new EphemeralDataProtectionProvider());
        await using (var db = fixture.CreateAppContext(first))
        {
            db.Add(new TenantEmailSettings
            {
                TenantId = first, Host = "first.test", PasswordCiphertext = passwords.Protect("secret"),
            });
            await db.SaveChangesAsync();
        }
        await using var other = fixture.CreateAppContext(second);
        Assert.Empty(await other.Set<TenantEmailSettings>().IgnoreQueryFilters().ToListAsync());
        Assert.Equal(0, await other.Set<TenantEmailSettings>().IgnoreQueryFilters()
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.Host, "stolen.test")));

        var context = new TenantContext();
        context.SetTenant(second);
        var fallback = new EmailOptions { Host = "fallback.test" };
        Assert.Same(fallback, await new TenantEmailConfigResolver(other, context, Options.Create(fallback), passwords).ResolveAsync());
        await using var own = fixture.CreateAppContext(first);
        var row = await own.Set<TenantEmailSettings>().IgnoreQueryFilters().SingleAsync();
        row.Host = "updated.test";
        await own.SaveChangesAsync(); // Configuration is deliberately editable.
        context.SetTenant(first);
        var resolved = await new TenantEmailConfigResolver(own, context, Options.Create(fallback), passwords).ResolveAsync();
        Assert.Equal("updated.test", resolved.Host);
        Assert.Equal("secret", resolved.Password);
        Assert.NotEqual("secret", row.PasswordCiphertext);
    }

    [Fact]
    public async Task RLS_rejects_cross_tenant_insert_and_tenant_reassignment()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await using (var db = fixture.CreateAppContext(first))
        {
            db.Add(new TenantEmailSettings { TenantId = second });
            var failure = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, ((PostgresException)failure.InnerException!).SqlState);
        }
        await using var own = fixture.CreateAppContext(first);
        own.Add(new TenantEmailSettings { TenantId = first });
        await own.SaveChangesAsync();
        await Assert.ThrowsAsync<PostgresException>(() => own.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE tenant_email_settings SET tenant_id = {second} WHERE tenant_id = {first}"));
    }

    [Fact]
    public async Task One_row_per_tenant_is_enforced_and_missing_context_fails_closed()
    {
        var tenant = Guid.NewGuid();
        await using (var db = fixture.CreateAppContext(tenant))
        {
            db.Add(new TenantEmailSettings { TenantId = tenant });
            await db.SaveChangesAsync();
            db.Add(new TenantEmailSettings { TenantId = tenant });
            var failure = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.UniqueViolation, ((PostgresException)failure.InnerException!).SqlState);
        }
        try
        {
            await using var unset = fixture.CreateAppContext(null);
            Assert.Empty(await unset.Set<TenantEmailSettings>().IgnoreQueryFilters().ToListAsync());
        }
        catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.UndefinedObject or PostgresErrorCodes.InvalidTextRepresentation)
        {
            // An unset/empty tenant GUC also fails closed at the database.
        }
    }
}
