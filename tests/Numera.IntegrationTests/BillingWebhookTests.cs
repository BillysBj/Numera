using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Numera.Api.Services.Stripe;
using Numera.IntegrationTests.Fixtures;
using Numera.Modules.Billing;
using Numera.Platform.Db;
using Numera.Platform.Db.Entities;
using Numera.Platform.Tenancy;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>Signed webhook source-of-truth tests against least-privilege PostgreSQL.</summary>
[Collection(PostgresCollection.Name)]
public sealed class BillingWebhookTests(PostgresFixture fixture)
{
    private const string WebhookSecret = "whsec_billing_webhook_test";
    private const string PriceM = "price_test_m";
    private const string CustomerId = "cus_test_numera";
    private const string SubscriptionId = "sub_test_numera";

    [Theory]
    [InlineData("")]
    [InlineData("t=0,v1=invalid")]
    public async Task Missing_or_bad_signature_returns_400_without_changing_plan(
        string signatureHeader)
    {
        var tenantId = Guid.CreateVersion7();
        await SeedTenantAsync(tenantId, TenantPlan.S);
        var eventId = EventId();
        var json = StripeWebhookFixture.CheckoutSessionCompleted(
            tenantId,
            PriceM,
            CustomerId,
            SubscriptionId,
            eventId);
        using var services = CreateServices(tenantId);

        var result = await DeliverAsync(services, json, signatureHeader);

        AssertStatus(StatusCodes.Status400BadRequest, result);
        Assert.Equal(TenantPlan.S, (await ReadTenantAsync(tenantId)).Plan);
        Assert.Equal(0, await ProcessedCountAsync(eventId));
    }

    [Fact]
    public async Task Signed_events_drive_paid_to_free_to_paid_and_deduplicate_checkout()
    {
        var tenantId = Guid.CreateVersion7();
        await SeedTenantAsync(tenantId, TenantPlan.S);
        var currentPeriodEnd = DateTimeOffset.FromUnixTimeSeconds(
            DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds());
        using var services = CreateServices(tenantId, currentPeriodEnd);

        var checkoutEventId = EventId();
        var checkoutJson = StripeWebhookFixture.CheckoutSessionCompleted(
            tenantId,
            PriceM,
            CustomerId,
            SubscriptionId,
            checkoutEventId);
        var checkoutSignature = StripeWebhookFixture.CreateSignatureHeader(
            checkoutJson,
            WebhookSecret);

        AssertStatus(
            StatusCodes.Status200OK,
            await DeliverAsync(services, checkoutJson, checkoutSignature));
        var afterCheckout = await ReadTenantAsync(tenantId);
        Assert.Equal(TenantPlan.M, afterCheckout.Plan);
        Assert.Equal(CustomerId, afterCheckout.StripeCustomerId);
        Assert.Equal(SubscriptionId, afterCheckout.StripeSubscriptionId);
        Assert.Equal("active", afterCheckout.SubscriptionStatus);

        AssertStatus(
            StatusCodes.Status200OK,
            await DeliverAsync(services, checkoutJson, checkoutSignature));
        Assert.Equal(1, await ProcessedCountAsync(checkoutEventId));
        Assert.Equal(TenantPlan.M, (await ReadTenantAsync(tenantId)).Plan);

        var pastDueJson = StripeWebhookFixture.SubscriptionPastDue(
            tenantId,
            PriceM,
            CustomerId,
            SubscriptionId,
            EventId());
        AssertStatus(
            StatusCodes.Status200OK,
            await DeliverSignedAsync(services, pastDueJson));
        var afterPastDue = await ReadTenantAsync(tenantId);
        Assert.Equal(TenantPlan.M, afterPastDue.Plan);
        Assert.Equal("past_due", afterPastDue.SubscriptionStatus);

        var deletedJson = StripeWebhookFixture.SubscriptionDeleted(
            tenantId,
            PriceM,
            CustomerId,
            SubscriptionId,
            EventId());
        AssertStatus(
            StatusCodes.Status200OK,
            await DeliverSignedAsync(services, deletedJson));
        var afterDeletion = await ReadTenantAsync(tenantId);
        Assert.Equal(TenantPlan.Free, afterDeletion.Plan);
        Assert.Equal("canceled", afterDeletion.SubscriptionStatus);

        var invoicePaidJson = StripeWebhookFixture.InvoicePaid(
            tenantId,
            PriceM,
            CustomerId,
            SubscriptionId,
            EventId());
        AssertStatus(
            StatusCodes.Status200OK,
            await DeliverSignedAsync(services, invoicePaidJson));
        var afterInvoicePaid = await ReadTenantAsync(tenantId);
        Assert.Equal(TenantPlan.M, afterInvoicePaid.Plan);
        Assert.Equal("active", afterInvoicePaid.SubscriptionStatus);
        Assert.Equal(currentPeriodEnd, afterInvoicePaid.CurrentPeriodEnd);
    }

    private ServiceProvider CreateServices(
        Guid tenantId,
        DateTimeOffset? currentPeriodEnd = null)
    {
        var fakeProvider = new FakeBillingProvider
        {
            NextSubscription = new SubscriptionSnapshot(
                SubscriptionId,
                "active",
                PriceM,
                currentPeriodEnd ?? DateTimeOffset.UtcNow.AddDays(30),
                CustomerId,
                tenantId),
        };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<ICurrentTenant, TenantContext>();
        services.AddDbContext<NumeraDbContext>(
            options => options.UseNpgsql(fixture.AppConnectionString));
        services.Configure<StripeOptions>(options =>
        {
            options.WebhookSecret = WebhookSecret;
            options.PriceIds["M"] = PriceM;
        });
        services.AddSingleton(fakeProvider);
        services.AddScoped<IBillingProvider>(
            provider => provider.GetRequiredService<FakeBillingProvider>());
        services.AddScoped<IBillingWebhookHandler, BillingWebhookHandler>();
        return services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    private static async Task<IResult> DeliverSignedAsync(
        ServiceProvider services,
        string json) =>
        await DeliverAsync(
            services,
            json,
            StripeWebhookFixture.CreateSignatureHeader(json, WebhookSecret));

    private static async Task<IResult> DeliverAsync(
        ServiceProvider services,
        string json,
        string signatureHeader)
    {
        using var scope = services.CreateScope();
        return await scope.ServiceProvider
            .GetRequiredService<IBillingWebhookHandler>()
            .HandleAsync(json, signatureHeader, CancellationToken.None);
    }

    private async Task SeedTenantAsync(Guid tenantId, TenantPlan plan)
    {
        await using var db = fixture.CreateAppContext(tenantId);
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = $"Billing webhook test {tenantId}",
            Plan = plan,
        });
        await db.SaveChangesAsync();
    }

    private async Task<Tenant> ReadTenantAsync(Guid tenantId)
    {
        await using var db = fixture.CreateAppContext(tenantId);
        return await db.Tenants.AsNoTracking().SingleAsync();
    }

    private async Task<int> ProcessedCountAsync(string eventId)
    {
        await using var db = fixture.CreateAppContext(tenantId: null);
        return await db.ProcessedStripeEvents.CountAsync(entry => entry.EventId == eventId);
    }

    private static string EventId() => $"evt_{Guid.NewGuid():N}";

    private static void AssertStatus(int expected, IResult result) =>
        Assert.Equal(
            expected,
            Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
}

/// <summary>Network- and database-free contract checks for signed Stripe fixtures and mapping.</summary>
public sealed class BillingWebhookContractTests
{
    private const string WebhookSecret = "whsec_billing_webhook_contract";

    [Theory]
    [InlineData("")]
    [InlineData("t=0,v1=invalid")]
    public async Task Handler_rejects_missing_or_bad_signature_before_database_access(
        string signatureHeader)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<StripeOptions>(options => options.WebhookSecret = WebhookSecret);
        services.AddScoped<IBillingProvider, StubBillingProvider>();
        services.AddScoped<IBillingWebhookHandler, BillingWebhookHandler>();
        using var serviceProvider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var scope = serviceProvider.CreateScope();

        var result = await scope.ServiceProvider
            .GetRequiredService<IBillingWebhookHandler>()
            .HandleAsync("{}", signatureHeader, CancellationToken.None);

        Assert.Equal(
            StatusCodes.Status400BadRequest,
            Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
    }

    [Fact]
    public void Signed_subscription_fixture_deserializes_item_level_period()
    {
        var tenantId = Guid.CreateVersion7();
        var json = StripeWebhookFixture.SubscriptionPastDue(
            tenantId,
            "price_contract_m",
            "cus_contract",
            "sub_contract",
            $"evt_{Guid.NewGuid():N}");

        var stripeEvent = global::Stripe.EventUtility.ConstructEvent(
            json,
            StripeWebhookFixture.CreateSignatureHeader(json, WebhookSecret),
            WebhookSecret,
            throwOnApiVersionMismatch: false);
        var subscription = Assert.IsType<global::Stripe.Subscription>(stripeEvent.Data.Object);
        var item = Assert.Single(subscription.Items.Data);

        Assert.Equal("past_due", subscription.Status);
        Assert.Equal("price_contract_m", item.Price.Id);
        Assert.True(item.CurrentPeriodEnd > DateTimeOffset.UtcNow);
        Assert.Equal(tenantId.ToString(), subscription.Metadata["tenant_id"]);
    }

    [Fact]
    public void Signed_invoice_fixture_and_mapper_preserve_retry_then_reactivate()
    {
        var tenantId = Guid.CreateVersion7();
        var json = StripeWebhookFixture.InvoicePaid(
            tenantId,
            "price_contract_m",
            "cus_contract",
            "sub_contract",
            $"evt_{Guid.NewGuid():N}");
        var stripeEvent = global::Stripe.EventUtility.ConstructEvent(
            json,
            StripeWebhookFixture.CreateSignatureHeader(json, WebhookSecret),
            WebhookSecret,
            throwOnApiVersionMismatch: false);
        var invoice = Assert.IsType<global::Stripe.Invoice>(stripeEvent.Data.Object);
        Assert.Equal("sub_contract", invoice.Parent.SubscriptionDetails.SubscriptionId);
        Assert.Equal(tenantId.ToString(), invoice.Parent.SubscriptionDetails.Metadata["tenant_id"]);

        var options = new StripeOptions();
        options.PriceIds["M"] = "price_contract_m";
        var mapper = new SubscriptionPlanMapper(options);
        Assert.Equal(
            TenantPlan.M,
            mapper.ForSubscriptionStatus("past_due", "price_contract_m", TenantPlan.M));
        Assert.Equal(TenantPlan.Free, SubscriptionPlanMapper.ForSubscriptionDeleted());
        Assert.Equal(TenantPlan.M, mapper.ForInvoicePaid("price_contract_m"));
    }
}
