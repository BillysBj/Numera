using System.Text;
using System.Text.Json;

using FluentValidation;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Numera.Api.Contracts;
using Numera.Api.Endpoints;
using Numera.Api.Jobs;
using Numera.Api.Services;
using Numera.Modules.Ledger;
using Numera.Modules.Sales;
using Numera.Modules.Sales.EInvoice;
using Numera.Modules.Sales.Email;
using Numera.Modules.Sales.Events;
using Numera.Modules.Sales.Numbering;
using Numera.Platform.Audit;
using Numera.Platform.Db;
using Numera.Platform.Entitlements;
using Numera.Platform.Tenancy;
using Xunit;

namespace Numera.Platform.Tests.Sales;

public sealed class RecipientLanguageTests
{
    [Theory]
    [InlineData(null, "de")]
    [InlineData("", "de")]
    [InlineData("{}", "de")]
    [InlineData("not json", "de")]
    [InlineData("null", "de")]
    [InlineData("[]", "de")]
    [InlineData("42", "de")]
    [InlineData("{\"language\":null}", "de")]
    [InlineData("{\"language\":42}", "de")]
    [InlineData("{\"language\":\"fr\"}", "de")]
    [InlineData("{\"language\":\"de\"}", "de")]
    [InlineData("{\"language\":\"en\"}", "en")]
    [InlineData("{\"Language\":\"EN\"}", "en")]
    public void Snapshot_language_handles_legacy_and_malformed_values(string? snapshot, string expected)
        => Assert.Equal(expected, SalesDocumentEndpoints.ResolveRecipientLanguage(snapshot));

    public static IEnumerable<object?[]> SendCases()
    {
        foreach (var einvoice in new[] { false, true })
        {
            yield return [einvoice, "en", null, "en"];
            yield return [einvoice, "en", "{}", "en"];
            yield return [einvoice, "en", "{\"language\":\"de\"}", "de"];
            yield return [einvoice, "en", "{\"language\":\"fr\"}", "en"];
            yield return [einvoice, "de", "{\"language\":\"en\"}", "en"];
            yield return [einvoice, null, "{}", "de"];
        }
    }

    [Theory]
    [MemberData(nameof(SendCases))]
    public async Task Send_uses_frozen_language_unless_explicitly_overridden(
        bool einvoice, string? recipientLanguage, string? body, string expected)
    {
        var tenant = new TenantContext();
        tenant.SetTenant(Guid.NewGuid());
        var jobs = new RecordingJobs();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing", ContentRootPath = AppContext.BaseDirectory,
        });
        var database = Guid.NewGuid().ToString();
        builder.Services.AddSingleton<ICurrentTenant>(tenant);
        builder.Services.AddDbContext<NumeraDbContext>(o => o.UseInMemoryDatabase(database));
        builder.Services.AddSingleton<IBackgroundJobClient>(jobs);
        builder.Services.AddSingleton<IAuditWriter, NoOpAudit>();
        builder.Services.AddSingleton<IEntitlementService, GrantEntitlements>();
        builder.Services.AddScoped<EInvoiceService>();
        builder.Services.AddSingleton<IEInvoiceValidator, UnusedValidator>();
        // Register dependencies of the other mapped routes for minimal-API service inference.
        foreach (var type in new[] { typeof(DocumentPdfService), typeof(NumberingService),
            typeof(IDomainEventPublisher), typeof(PostingEngine), typeof(AccountResolver),
            typeof(IValidator<CreateSalesDocumentRequest>), typeof(IValidator<UpdateSalesDocumentRequest>) })
        {
            builder.Services.AddScoped(type, _ => throw new InvalidOperationException("Unused route dependency"));
        }
        await using var app = builder.Build();
        app.MapSalesDocumentEndpoints();
        app.MapEInvoiceEndpoints();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NumeraDbContext>();
        var snapshot = new Dictionary<string, string> { ["email"] = "customer@example.com" };
        if (recipientLanguage is not null) snapshot["language"] = recipientLanguage;
        var doc = new SalesDocument
        {
            TenantId = tenant.TenantId!.Value, DocumentNumber = "RE-42",
            DocumentType = DocumentType.Rechnung, Status = DocumentStatus.Finalized,
            RecipientSnapshot = JsonSerializer.Serialize(snapshot),
        };
        db.Add(doc);
        db.Add(new EInvoiceArtifact
        {
            TenantId = doc.TenantId, DocumentId = doc.Id, DocumentNumber = doc.DocumentNumber,
            Format = EInvoiceFormat.XRechnungUbl, ValidationStatus = EInvoiceValidationStatus.Accepted,
            Xml = [1, 2, 3],
        });
        await db.SaveChangesAsync();

        var route = einvoice ? "/api/documents/{id:guid}/send-einvoice" : "/api/documents/{id:guid}/send";
        var endpoint = ((IEndpointRouteBuilder)app).DataSources.SelectMany(s => s.Endpoints)
            .Cast<RouteEndpoint>().Single(e => e.RoutePattern.RawText == route);
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        http.Request.Method = "POST";
        http.Request.RouteValues["id"] = doc.Id.ToString();
        http.Response.Body = new MemoryStream();
        if (body is not null)
        {
            http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
            http.Request.ContentType = "application/json";
            http.Request.ContentLength = http.Request.Body.Length;
            http.Features.Set<IHttpRequestBodyDetectionFeature>(new BodyDetection());
        }
        await endpoint.RequestDelegate!(http);

        Assert.Equal(202, http.Response.StatusCode);
        var job = Assert.Single(jobs.Created);
        Assert.Equal(typeof(SendDocumentEmailJob), job.Type);
        Assert.Equal(expected, job.Args[2]);
        Assert.Equal(einvoice, job.Args[3]);
        var email = await db.Set<DocumentEmail>().SingleAsync();
        Assert.Equal(DocumentEmailTemplates.Build(expected, "RE-42").Subject, email.Subject);
        Assert.Equal("customer@example.com", email.ToAddress);
    }

    private sealed class BodyDetection : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }

    private sealed class RecordingJobs : IBackgroundJobClient
    {
        public List<Job> Created { get; } = [];
        public string Create(Job job, IState state) { Created.Add(job); return Guid.NewGuid().ToString(); }
        public bool ChangeState(string jobId, IState state, string expectedState) => true;
    }

    private sealed class NoOpAudit : IAuditWriter
    {
        public Task RecordAsync(IAuditEvent evt, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class GrantEntitlements : IEntitlementService
    {
        public Task<bool> HasCapabilityAsync(Capability capability, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
        public Task<IReadOnlySet<Capability>> CurrentCapabilitiesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlySet<Capability>>(new HashSet<Capability>(Enum.GetValues<Capability>()));
    }

    private sealed class UnusedValidator : IEInvoiceValidator
    {
        public Task<EInvoiceValidationResult> ValidateAsync(byte[] xml, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The accepted artifact should be reused");
    }
}
