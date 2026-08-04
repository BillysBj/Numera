using MailKit.Net.Imap;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Numera.Api.Jobs;
using Numera.Modules.Sales.Belege;
using Numera.Platform.Audit;
using Numera.Platform.Tenancy;

namespace Numera.Api.Services;

/// <summary>Configuration for the shared IMAP inbox and per-tenant catch-all domain.</summary>
public sealed class BelegeMailboxOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "BelegeMailbox";

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 993;

    public bool UseSsl { get; set; } = true;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string Domain { get; set; } = string.Empty;

    /// <summary>Hangfire cron expression; every five minutes by default.</summary>
    public string PollCron { get; set; } = "*/5 * * * *";
}

/// <summary>Registers the deliberately small worker-side intake dependency closure.</summary>
public static class BelegeMailboxWorkerServiceCollectionExtensions
{
    /// <summary>Adds polling, scanning, capture, audit, and MailKit without extraction or booking.</summary>
    public static IServiceCollection AddBelegeMailboxWorker(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<BelegeMailboxOptions>(
            configuration.GetSection(BelegeMailboxOptions.SectionName));
        services.Configure<ClamAvOptions>(
            configuration.GetSection(ClamAvOptions.SectionName));
        var clamAv = configuration.GetSection(ClamAvOptions.SectionName);
        if (!string.IsNullOrWhiteSpace(clamAv[nameof(ClamAvOptions.Host)]))
        {
            services.AddScoped<IAttachmentScanner, ClamAvAttachmentScanner>();
        }
        else
        {
            services.AddScoped<IAttachmentScanner, NoopAttachmentScanner>();
        }

        services.AddScoped<ICurrentUser, BelegeMailboxSystemCurrentUser>();
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<ReceiptDeduplicator>();
        services.AddScoped<ReceiptIngestService>();
        services.AddTransient<ImapClient>();
        services.AddTransient<PollBelegMailboxJob>();
        return services;
    }
}

/// <summary>Stable, non-human principal for audit events created by headless intake.</summary>
public sealed class BelegeMailboxSystemCurrentUser : ICurrentUser
{
    /// <inheritdoc />
    public Guid? UserId { get; } = Guid.Parse("00000000-0000-0000-0000-000000000001");
}
