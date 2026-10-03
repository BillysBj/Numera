using System.Security.Claims;

using FluentValidation;

using Microsoft.EntityFrameworkCore;

using Numera.Api.Contracts;
using Numera.Api.Services;
using Numera.Api.Validators;
using Numera.Modules.Sales.Email;
using Numera.Platform.Db;
using Numera.Platform.Tenancy;

namespace Numera.Api.Endpoints;

public static class EmailSettingsEndpoints
{
    public static IEndpointRouteBuilder MapEmailSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/settings/email").RequireAuthorization("RequireOwner");
        group.MapGet("/", async (NumeraDbContext db, CancellationToken ct) =>
        {
            var settings = await db.Set<TenantEmailSettings>().AsNoTracking().SingleOrDefaultAsync(ct);
            return Results.Ok(EmailSettingsDto.FromEntity(settings ?? new TenantEmailSettings()));
        });
        group.MapPut("/", async (UpdateEmailSettingsRequest request,
            IValidator<UpdateEmailSettingsRequest> validator, NumeraDbContext db,
            ICurrentTenant tenant, SmtpPasswordProtector passwords, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

            var settings = await db.Set<TenantEmailSettings>().SingleOrDefaultAsync(ct);
            if (settings is null)
            {
                settings = new TenantEmailSettings { TenantId = tenant.TenantId!.Value };
                db.Add(settings);
            }

            Apply(settings, request, passwords);
            await db.SaveChangesAsync(ct);
            return Results.Ok(EmailSettingsDto.FromEntity(settings));
        });
        group.MapPost("/test", async (TestEmailRequest request, ClaimsPrincipal user,
            IEmailSender sender, CancellationToken ct) =>
        {
            var recipient = string.IsNullOrWhiteSpace(request.ToAddress)
                ? user.FindFirstValue("email") ?? user.FindFirstValue(ClaimTypes.Email)
                : request.ToAddress.Trim();
            if (!EmailAddressValidation.IsValid(recipient))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["toAddress"] = ["Enter a valid test email recipient."],
                });
            }

            try
            {
                await sender.SendAsync(new EmailMessage
                {
                    To = recipient!, Subject = "Numera SMTP test",
                    TextBody = "Die E-Mail-Konfiguration funktioniert. / Email configuration works.",
                    HtmlBody = "<p>Die E-Mail-Konfiguration funktioniert. / Email configuration works.</p>",
                }, ct);
                return Results.Ok(new TestEmailResult(true));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Results.Ok(new TestEmailResult(false, ex.Message));
            }
        });
        return app;
    }

    internal static void Apply(TenantEmailSettings settings, UpdateEmailSettingsRequest request,
        SmtpPasswordProtector passwords)
    {
        settings.Host = request.Host?.Trim();
        settings.Port = request.Port;
        settings.UseSsl = request.UseSsl;
        settings.Username = request.Username?.Trim();
        if (!string.IsNullOrEmpty(request.Password))
        {
            settings.PasswordCiphertext = passwords.Protect(request.Password);
        }

        settings.FromAddress = request.FromAddress?.Trim();
        settings.FromName = request.FromName?.Trim();
        settings.InvoiceSubject = request.InvoiceSubject;
        settings.InvoiceBody = request.InvoiceBody;
        settings.DunningSubject = request.DunningSubject;
        settings.DunningBody = request.DunningBody;
    }
}
