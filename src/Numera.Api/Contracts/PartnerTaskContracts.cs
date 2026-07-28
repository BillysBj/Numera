using FluentValidation;

using Numera.Modules.Crm;

namespace Numera.Api.Contracts;

/// <summary>Payload for creating a partner task.</summary>
public sealed record CreatePartnerTaskRequest(
    string Title,
    string? Description,
    DateOnly? DueDate,
    Guid? AssignedUserId);

/// <summary>Payload for fully updating a partner task.</summary>
public sealed record UpdatePartnerTaskRequest(
    string Title,
    string? Description,
    DateOnly? DueDate,
    PartnerTaskStatus Status,
    Guid? AssignedUserId);

/// <summary>Compact task projection used by the partner task list.</summary>
public sealed record PartnerTaskListItem(
    Guid Id,
    string Title,
    string? Description,
    DateOnly? DueDate,
    PartnerTaskStatus Status,
    Guid? AssignedUserId,
    bool IsOverdue);

/// <summary>Full partner task representation returned after creation.</summary>
public sealed record PartnerTaskDetail(
    Guid Id,
    Guid PartnerId,
    string Title,
    string? Description,
    DateOnly? DueDate,
    PartnerTaskStatus Status,
    Guid? AssignedUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);

/// <summary>Validates the required task title.</summary>
public sealed class CreatePartnerTaskRequestValidator : AbstractValidator<CreatePartnerTaskRequest>
{
    /// <summary>Creates the validator.</summary>
    public CreatePartnerTaskRequestValidator() =>
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
}
