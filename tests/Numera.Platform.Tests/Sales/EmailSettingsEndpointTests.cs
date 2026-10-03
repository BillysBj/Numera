using System.Security.Claims;
using System.Text;
using System.Text.Json;

using FluentValidation;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Numera.Api.Contracts;
using Numera.Api.Endpoints;
using Numera.Api.Services;
using Numera.Api.Validators;
using Numera.Platform.Db;
using Numera.Platform.Tenancy;

using Xunit;

namespace Numera.Platform.Tests.Sales;

public sealed class EmailSettingsEndpointTests
{
    [Fact]
    public async Task All_routes_require_owner_and_handlers_keep_password_out_of_responses()
    {
        await using var app = CreateApp(new TestSender());
        var endpoints = Endpoints(app);
        Assert.Equal(3, endpoints.Length);
        Assert.All(endpoints, endpoint => Assert.Contains(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
            auth => auth.Policy == "RequireOwner"));

        var first = await Invoke(app, "GET", "/api/settings/email/");
        Assert.False(first.Body.GetProperty("hasPassword").GetBoolean());
        var put = await Invoke(app, "PUT", "/api/settings/email/",
            """{"host":"smtp.test","port":587,"fromAddress":"from@example.com","password":"secret"}""");
        Assert.Equal(200, put.Status);
        Assert.True(put.Body.GetProperty("hasPassword").GetBoolean());
        Assert.False(put.Body.TryGetProperty("password", out _));
        Assert.False(put.Body.TryGetProperty("passwordCiphertext", out _));
        Assert.DoesNotContain("secret", put.Body.GetRawText());
        var get = await Invoke(app, "GET", "/api/settings/email/");
        Assert.True(get.Body.GetProperty("hasPassword").GetBoolean());
        Assert.Equal("smtp.test", get.Body.GetProperty("host").GetString());

        var invalid = await Invoke(app, "PUT", "/api/settings/email/",
            """{"host":"smtp.test","port":0,"fromAddress":"invalid"}""");
        Assert.Equal(400, invalid.Status);
        Assert.True(invalid.Body.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task Test_endpoint_uses_supplied_or_owner_address_and_returns_SMTP_failure()
    {
        var sender = new TestSender();
        await using var app = CreateApp(sender);
        var result = await Invoke(app, "POST", "/api/settings/email/test", "{}");
        Assert.True(result.Body.GetProperty("success").GetBoolean());
        Assert.Equal("owner@example.com", sender.LastMessage!.To);
        Assert.Null(sender.LastMessage.Attachment);
        await Invoke(app, "POST", "/api/settings/email/test", """{"toAddress":"test@example.com"}""");
        Assert.Equal("test@example.com", sender.LastMessage!.To);
        sender.Fail = true;
        result = await Invoke(app, "POST", "/api/settings/email/test", "{}");
        Assert.False(result.Body.GetProperty("success").GetBoolean());
        Assert.Equal("535 Authentication failed", result.Body.GetProperty("error").GetString());
        result = await Invoke(app, "POST", "/api/settings/email/test", """{"toAddress":"invalid"}""");
        Assert.Equal(400, result.Status);
    }

    private static WebApplication CreateApp(TestSender sender)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing", ContentRootPath = AppContext.BaseDirectory,
        });
        var tenant = new TenantContext();
        tenant.SetTenant(Guid.NewGuid());
        var database = Guid.NewGuid().ToString();
        builder.Services.AddSingleton<ICurrentTenant>(tenant);
        builder.Services.AddDbContext<NumeraDbContext>(o => o.UseInMemoryDatabase(database));
        builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        builder.Services.AddScoped<SmtpPasswordProtector>();
        builder.Services.AddScoped<IValidator<UpdateEmailSettingsRequest>, UpdateEmailSettingsValidator>();
        builder.Services.AddSingleton<IEmailSender>(sender);
        var app = builder.Build();
        app.MapEmailSettingsEndpoints();
        return app;
    }

    private static RouteEndpoint[] Endpoints(WebApplication app) => ((IEndpointRouteBuilder)app).DataSources
        .SelectMany(source => source.Endpoints).Cast<RouteEndpoint>().ToArray();

    // Invoke the real minimal-API binders/handlers without a server or Docker. Authorization
    // metadata is asserted above; authentication/middleware is covered by the existing auth suite.
    private static async Task<(int Status, JsonElement Body)> Invoke(WebApplication app, string method, string route, string? body = null)
    {
        var endpoint = Endpoints(app).Single(e => e.RoutePattern.RawText == route
            && e.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains(method));
        using var scope = app.Services.CreateScope();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("email", "owner@example.com")], "test"));
        http.Request.Method = method;
        http.Request.Path = route;
        http.Response.Body = new MemoryStream();
        if (body is not null)
        {
            http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
            http.Request.ContentType = "application/json";
            http.Request.ContentLength = http.Request.Body.Length;
            http.Features.Set<IHttpRequestBodyDetectionFeature>(new BodyDetection());
        }
        await endpoint.RequestDelegate!(http);
        http.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(http.Response.Body);
        return (http.Response.StatusCode, json.RootElement.Clone());
    }

    private sealed class BodyDetection : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }

    private sealed class TestSender : IEmailSender
    {
        public bool Fail { get; set; }
        public EmailMessage? LastMessage { get; private set; }
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            if (Fail) throw new InvalidOperationException("535 Authentication failed");
            LastMessage = message;
            return Task.CompletedTask;
        }
    }
}
