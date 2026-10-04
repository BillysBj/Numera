using System.Data;
using System.Text;
using System.Text.Json;

using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Numera.Api.Contracts;
using Numera.Api.Endpoints;
using Numera.Api.Validators;
using Numera.Modules.Crm;
using Numera.Modules.Sales;
using Numera.Modules.Sales.Numbering;
using Numera.Platform.Audit;
using Numera.Platform.Db;
using Numera.Platform.Tenancy;

using Xunit;

namespace Numera.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class PartnerNumberingTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Claims_autocommit_from_a_closed_connection_and_use_independent_continuous_series()
    {
        var tenantId = Guid.CreateVersion7();
        await using (var db = fixture.CreateAppContext(tenantId))
        {
            var numbering = new NumberingService(db);
            Assert.Equal(ConnectionState.Closed, db.Database.GetDbConnection().State);
            Assert.Null(db.Database.CurrentTransaction);
            Assert.Equal("K-00001", await numbering.AssignPartnerNumberAsync(true, CancellationToken.None));
            Assert.Equal("K-00002", await numbering.AssignPartnerNumberAsync(true, CancellationToken.None));
            Assert.Equal("L-00001", await numbering.AssignPartnerNumberAsync(false, CancellationToken.None));
            Assert.Equal(ConnectionState.Closed, db.Database.GetDbConnection().State);
        }

        await using var read = fixture.CreateAppContext(tenantId);
        var series = await read.Set<NumberSequence>().AsNoTracking().OrderBy(s => s.DocType).ToListAsync();
        Assert.Equal(2, series.Count);
        Assert.Equal(1001, series[0].DocType);
        Assert.Equal(3, series[0].NextValue);
        Assert.Equal(1002, series[1].DocType);
        Assert.Equal(2, series[1].NextValue);
        Assert.All(series, s => Assert.Equal(0, s.Year));

        // Opening/closing a claim must preserve a caller's document-finalize transaction.
        await using var transaction = await read.Database.BeginTransactionAsync();
        Assert.Equal("RE-2026-00001", await new NumberingService(read)
            .AssignAsync(DocumentType.Rechnung, 2026, CancellationToken.None));
        Assert.Same(transaction, read.Database.CurrentTransaction);
        Assert.Equal(ConnectionState.Open, read.Database.GetDbConnection().State);
        await transaction.CommitAsync();
    }

    [Fact]
    public async Task Concurrent_claims_are_unique_with_independent_role_and_tenant_counters()
    {
        const int count = 20;
        var tenants = new[] { Guid.CreateVersion7(), Guid.CreateVersion7() };
        var tasks = from tenant in tenants
                    from customer in new[] { true, false }
                    from index in Enumerable.Range(0, count)
                    select ClaimAsync(tenant, customer);
        var claims = await Task.WhenAll(tasks);

        foreach (var tenant in tenants)
        foreach (var customer in new[] { true, false })
        {
            var actual = claims.Where(c => c.Tenant == tenant && c.Customer == customer)
                .Select(c => c.Number).OrderBy(n => n).ToArray();
            var prefix = customer ? "K-" : "L-";
            Assert.Equal(Enumerable.Range(1, count).Select(n => prefix + n.ToString("D5")), actual);
        }

        async Task<(Guid Tenant, bool Customer, string Number)> ClaimAsync(Guid tenant, bool customer)
        {
            await using var db = fixture.CreateAppContext(tenant);
            return (tenant, customer, await new NumberingService(db)
                .AssignPartnerNumberAsync(customer, CancellationToken.None));
        }
    }

    [Theory]
    [InlineData(true, false, null, null, "K-00001", null)]
    [InlineData(false, true, null, null, null, "L-00001")]
    [InlineData(true, true, "  ", "", "K-00001", "L-00001")]
    [InlineData(true, false, "CUSTOM-42", null, "CUSTOM-42", null)]
    [InlineData(true, true, "CUSTOM-42", "SUP-7", "CUSTOM-42", "SUP-7")]
    public async Task Create_endpoint_assigns_only_missing_numbers_for_enabled_roles(
        bool customer, bool supplier, string? customerNumber, string? supplierNumber,
        string? expectedCustomer, string? expectedSupplier)
    {
        var tenant = Guid.CreateVersion7();
        await using var app = BuildApp(tenant);
        await InvokeAsync(app, "POST", Payload(customer, supplier, customerNumber, supplierNumber));

        await using var db = fixture.CreateAppContext(tenant);
        var partner = await db.Set<BusinessPartner>().SingleAsync();
        Assert.Equal(expectedCustomer, partner.CustomerNumber);
        Assert.Equal(expectedSupplier, partner.SupplierNumber);
        var expectedClaims = (customer && string.IsNullOrWhiteSpace(customerNumber) ? 1 : 0)
            + (supplier && string.IsNullOrWhiteSpace(supplierNumber) ? 1 : 0);
        Assert.Equal(expectedClaims, await db.Set<NumberSequence>().CountAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Update_endpoint_assigns_new_role_and_preserves_existing_numbers(bool startsAsCustomer)
    {
        var tenant = Guid.CreateVersion7();
        await using var app = BuildApp(tenant);
        await InvokeAsync(app, "POST", Payload(startsAsCustomer, !startsAsCustomer,
            startsAsCustomer ? "CUSTOM-42" : null, startsAsCustomer ? null : "SUP-7"));
        await using var db = fixture.CreateAppContext(tenant);
        var id = await db.Set<BusinessPartner>().Select(p => p.Id).SingleAsync();

        // A blank PUT payload must retain the old number and allocate the newly enabled role.
        await InvokeAsync(app, "PUT", Payload(true, true, "  ", null), id);
        var partner = await db.Set<BusinessPartner>().AsNoTracking().SingleAsync();
        Assert.Equal(startsAsCustomer ? "CUSTOM-42" : "K-00001", partner.CustomerNumber);
        Assert.Equal(startsAsCustomer ? "L-00001" : "SUP-7", partner.SupplierNumber);

        // Disabling and re-enabling roles or submitting other values must not renumber them.
        await InvokeAsync(app, "PUT", Payload(!startsAsCustomer, startsAsCustomer, "CHANGED", "CHANGED"), id);
        await InvokeAsync(app, "PUT", Payload(true, true, null, ""), id);
        var updated = await db.Set<BusinessPartner>().AsNoTracking().SingleAsync();
        Assert.Equal(partner.CustomerNumber, updated.CustomerNumber);
        Assert.Equal(partner.SupplierNumber, updated.SupplierNumber);
        var sequence = await db.Set<NumberSequence>().SingleAsync();
        Assert.Equal(2, sequence.NextValue);
    }

    private WebApplication BuildApp(Guid tenantId)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing", ContentRootPath = AppContext.BaseDirectory,
        });
        var tenant = new TenantContext();
        tenant.SetTenant(tenantId);
        builder.Services.AddSingleton<ICurrentTenant>(tenant);
        builder.Services.AddSingleton<ICurrentUser, TestUser>();
        builder.Services.AddSingleton<IAuditWriter, NoOpAudit>();
        builder.Services.AddScoped(_ => fixture.CreateAppContext(tenantId));
        builder.Services.AddScoped<NumberingService>();
        builder.Services.AddScoped<IValidator<CreatePartnerRequest>, CreatePartnerRequestValidator>();
        builder.Services.AddScoped<IValidator<UpdatePartnerRequest>, UpdatePartnerRequestValidator>();
        builder.Services.AddScoped<IValidator<CreateContactRequest>, CreateContactRequestValidator>();
        builder.Services.AddScoped<IValidator<CreateNoteRequest>, CreateNoteRequestValidator>();
        var app = builder.Build();
        app.MapPartnerEndpoints();
        return app;
    }

    private static object Payload(bool customer, bool supplier, string? customerNumber, string? supplierNumber) => new
    {
        name = "Numbering Test",
        billingAddress = new { street = "Testweg 1", postalCode = "10115", city = "Berlin", countryCode = "DE" },
        defaultCurrency = "EUR",
        isCustomer = customer,
        isSupplier = supplier,
        customerNumber,
        supplierNumber,
    };

    private static async Task InvokeAsync(WebApplication app, string method, object payload, Guid? id = null)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var route = id is null ? "/api/partners/" : "/api/partners/{id:guid}";
        var endpoint = ((IEndpointRouteBuilder)app).DataSources.SelectMany(s => s.Endpoints)
            .Cast<RouteEndpoint>().Single(e => e.RoutePattern.RawText == route
                && e.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains(method));
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        http.Request.Method = method;
        if (id is not null) http.Request.RouteValues["id"] = id.Value.ToString();
        http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)));
        http.Request.ContentType = "application/json";
        http.Request.ContentLength = http.Request.Body.Length;
        http.Features.Set<IHttpRequestBodyDetectionFeature>(new BodyDetection());
        http.Response.Body = new MemoryStream();
        await endpoint.RequestDelegate!(http);
        Assert.Equal(method == "POST" ? StatusCodes.Status201Created : StatusCodes.Status200OK,
            http.Response.StatusCode);
    }

    private sealed class BodyDetection : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }

    private sealed class TestUser : ICurrentUser
    {
        public Guid? UserId => null;
    }

    private sealed class NoOpAudit : IAuditWriter
    {
        public Task RecordAsync(IAuditEvent evt, CancellationToken ct) => Task.CompletedTask;
    }
}
