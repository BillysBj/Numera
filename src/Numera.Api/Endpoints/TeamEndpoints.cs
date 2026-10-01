using Microsoft.EntityFrameworkCore;

using Numera.Api.Services;
using Numera.Platform.Db;
using Numera.Platform.Db.Entities;
using Numera.Platform.Entitlements;

namespace Numera.Api.Endpoints;

/// <summary>Owner-only team membership management.</summary>
public static class TeamEndpoints
{
    /// <summary>Maps team list, invitation, role-change and removal endpoints.</summary>
    public static IEndpointRouteBuilder MapTeamEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/team").RequireAuthorization("RequireOwner");

        group.MapGet("/", async (NumeraDbContext db, InvitationService service, CancellationToken ct) =>
        {
            var rows = await db.Set<Membership>()
                .AsNoTracking()
                .OrderBy(x => x.Role)
                .ThenBy(x => x.UserId)
                .Select(x => new { x.UserId, x.Role })
                .ToListAsync(ct)
                .ConfigureAwait(false);

            // The e-mail lives in Keycloak, not the local Membership table — resolve it so the
            // UI shows the address instead of the raw user id.
            var emails = await service
                .GetUserEmailsAsync(rows.Select(r => r.UserId).ToList(), ct)
                .ConfigureAwait(false);

            var members = rows
                .Select(r => new TeamMemberResponse(r.UserId, r.Role, emails.GetValueOrDefault(r.UserId)))
                .ToList();
            return Results.Ok(members);
        });

        group.MapPost("/invite", async (
            InviteTeamMemberRequest request,
            InvitationService service,
            IEntitlementService entitlements,
            CancellationToken ct) =>
        {
            var gate = await RequireMultiUserAsync(entitlements, ct).ConfigureAwait(false);
            if (gate is not null)
            {
                return gate;
            }

            var errors = Validate(request.Email, request.Role);
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            var result = await service.InviteAsync(request.Email.Trim(), request.Role, ct)
                .ConfigureAwait(false);
            return Results.Created(
                $"/api/team/{result.UserId}",
                new { userId = result.UserId, temporaryPassword = result.TemporaryPassword });
        });

        group.MapPut("/{userId:guid}/role", async (
            Guid userId,
            ChangeTeamMemberRoleRequest request,
            InvitationService service,
            IEntitlementService entitlements,
            CancellationToken ct) =>
        {
            var gate = await RequireMultiUserAsync(entitlements, ct).ConfigureAwait(false);
            if (gate is not null)
            {
                return gate;
            }

            if (!Enum.IsDefined(request.Role))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["role"] = ["Die Rolle ist ungültig."],
                });
            }

            var outcome = await service.ChangeRoleAsync(userId, request.Role, ct).ConfigureAwait(false);
            return ToResult(outcome);
        });

        group.MapDelete("/{userId:guid}", async (
            Guid userId,
            InvitationService service,
            IEntitlementService entitlements,
            CancellationToken ct) =>
        {
            var gate = await RequireMultiUserAsync(entitlements, ct).ConfigureAwait(false);
            if (gate is not null)
            {
                return gate;
            }

            var outcome = await service.RemoveAsync(userId, ct).ConfigureAwait(false);
            return ToResult(outcome);
        });

        return app;
    }

    private static Dictionary<string, string[]> Validate(string email, MembershipRole role)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(email))
        {
            errors["email"] = ["Eine E-Mail-Adresse ist erforderlich."];
        }
        else if (!System.Net.Mail.MailAddress.TryCreate(email, out _))
        {
            errors["email"] = ["Die E-Mail-Adresse ist ungültig."];
        }

        if (!Enum.IsDefined(role))
        {
            errors["role"] = ["Die Rolle ist ungültig."];
        }

        return errors;
    }

    private static async Task<IResult?> RequireMultiUserAsync(
        IEntitlementService entitlements,
        CancellationToken ct)
    {
        if (await entitlements.HasCapabilityAsync(Capability.MultiUser, ct).ConfigureAwait(false))
        {
            return null;
        }

        return Results.Problem(
            title: "upgrade_required",
            detail: "Team-Verwaltung erfordert Tarif M oder höher.",
            statusCode: StatusCodes.Status403Forbidden);
    }

    private static IResult ToResult(MemberMutationOutcome outcome) => outcome switch
    {
        MemberMutationOutcome.Success => Results.NoContent(),
        MemberMutationOutcome.NotFound => Results.NotFound(),
        MemberMutationOutcome.LastOwner => Results.Problem(
            title: "last_owner",
            detail: "Der letzte Inhaber kann nicht herabgestuft oder entfernt werden.",
            statusCode: StatusCodes.Status422UnprocessableEntity),
        _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
    };
}

internal sealed record TeamMemberResponse(Guid UserId, MembershipRole Role, string? Email);
internal sealed record InviteTeamMemberRequest(string Email, MembershipRole Role);
internal sealed record ChangeTeamMemberRoleRequest(MembershipRole Role);
