// Background worker host. This is an empty generic host in plan 01-01 —
// Hangfire job wiring and scheduled processors arrive in plan 01-06.
// Phase 1 plan 01-01 only proves the worker host boots and composes DI.
var builder = Host.CreateApplicationBuilder(args);

var host = builder.Build();

host.Run();
