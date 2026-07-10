var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

// Liveness probe. Auth, controllers, tenancy middleware, and OIDC wiring
// arrive in later plans (01-05). Phase 1 plan 01-01 only proves the host boots.
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();
