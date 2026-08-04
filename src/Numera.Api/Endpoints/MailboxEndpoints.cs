using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Numera.Api.Services;
using Numera.Modules.Crm;
using Numera.Platform.Db;
using Numera.Platform.Tenancy;

namespace Numera.Api.Endpoints;

/// <summary>Provisioning and read access for a tenant's receipt intake address.</summary>
public static class MailboxEndpoints
{
    /// <summary>Maps <c>GET /api/receipts/mailbox</c>.</summary>
    public static IEndpointRouteBuilder MapMailboxEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/receipts/mailbox", GetOrCreateAsync)
            .RequireAuthorization();
        return app;
    }

    private static async Task<IResult> GetOrCreateAsync(
        NumeraDbContext db,
        ICurrentTenant currentTenant,
        IOptions<BelegeMailboxOptions> configuredOptions,
        CancellationToken ct)
    {
        var tenantId = currentTenant.TenantId
            ?? throw new InvalidOperationException("No tenant is active for mailbox provisioning.");
        var domain = configuredOptions.Value.Domain.Trim().TrimStart('@');
        if (string.IsNullOrWhiteSpace(domain))
        {
            return Results.Problem(
                "BelegeMailbox:Domain ist nicht konfiguriert.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var mailbox = await db.Set<TenantBelegeMailbox>()
            .AsNoTracking()
            .SingleOrDefaultAsync(ct)
            .ConfigureAwait(false);

        for (var attempt = 0; mailbox is null && attempt < 3; attempt++)
        {
            var token = TenantBelegeMailbox.GenerateAddressToken();
            var candidate = new TenantBelegeMailbox
            {
                TenantId = tenantId,
                AddressToken = token,
                LocalPart = TenantBelegeMailbox.BuildLocalPart(token),
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            db.Add(candidate);

            try
            {
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                mailbox = candidate;
            }
            catch (DbUpdateException)
            {
                db.ChangeTracker.Clear();
                mailbox = await db.Set<TenantBelegeMailbox>()
                    .AsNoTracking()
                    .SingleOrDefaultAsync(ct)
                    .ConfigureAwait(false);
            }
        }

        if (mailbox is null)
        {
            return Results.Problem(
                "Die Beleg-E-Mail-Adresse konnte nicht provisioniert werden.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Ok(new
        {
            address = $"{mailbox.LocalPart}@{domain}",
            mailbox.IsActive,
            mailbox.CreatedAt,
        });
    }
}
