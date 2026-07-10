using System.Data.Common;

using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Numera.Platform.Tenancy;

/// <summary>
/// Sets the Postgres <c>app.current_tenant</c> GUC on every opened connection so
/// RLS policies (plan 01-02) filter by the active tenant, and RESETs it when the
/// connection returns to the pool so a pooled connection never leaks a stale
/// tenant into another request.
/// </summary>
/// <remarks>
/// This is the injection- and pool-safe correction of the widely-copied bytefish
/// blog sample (RESEARCH.md Pattern 1). Two deviations from that sample are
/// deliberate and non-negotiable:
/// <list type="number">
///   <item>The tenant id is bound as a <b>command parameter</b>, never string
///   interpolated into SQL — the blog's <c>$"SET app.current_tenant='{name}'"</c>
///   is SQL-injectable.</item>
///   <item>Session-scoped <c>set_config(..., is_local: false)</c> is paired with a
///   <c>RESET</c> on connection close so context does not survive pool reuse.</item>
/// </list>
/// </remarks>
public sealed class TenantConnectionInterceptor(ICurrentTenant tenant) : DbConnectionInterceptor
{
    private const string TenantGuc = "app.current_tenant";

    /// <inheritdoc />
    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (tenant.TenantId is not { } tenantId)
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT set_config('app.current_tenant', @tenant, false)";

        var parameter = command.CreateParameter();
        parameter.ParameterName = "tenant";
        parameter.Value = tenantId.ToString();
        command.Parameters.Add(parameter);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        if (tenant.TenantId is not { } tenantId)
        {
            return;
        }

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT set_config('app.current_tenant', @tenant, false)";

        var parameter = command.CreateParameter();
        parameter.ParameterName = "tenant";
        parameter.Value = tenantId.ToString();
        command.Parameters.Add(parameter);

        command.ExecuteNonQuery();
    }

    /// <inheritdoc />
    public override async ValueTask<InterceptionResult> ConnectionClosingAsync(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"RESET {TenantGuc}";
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        return result;
    }

    /// <inheritdoc />
    public override InterceptionResult ConnectionClosing(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"RESET {TenantGuc}";
        command.ExecuteNonQuery();
        return result;
    }
}
