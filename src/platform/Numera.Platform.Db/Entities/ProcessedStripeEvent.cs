using System.ComponentModel.DataAnnotations.Schema;

using Microsoft.EntityFrameworkCore;

namespace Numera.Platform.Db.Entities;

/// <summary>
/// Global idempotency record for a processed Stripe webhook event. This entity is
/// intentionally tenant-agnostic and has no RLS policy because deduplication happens
/// before a tenant can be resolved; Stripe event ids are globally unique.
/// </summary>
[Table("processed_stripe_event")]
[Index(nameof(EventId), IsUnique = true)]
public sealed class ProcessedStripeEvent
{
    /// <summary>UUIDv7 primary key.</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>The globally unique Stripe event id (for example <c>evt_...</c>).</summary>
    public required string EventId { get; init; }

    /// <summary>The Stripe event type that was processed.</summary>
    public required string EventType { get; init; }

    /// <summary>When processing completed.</summary>
    public DateTimeOffset ProcessedAt { get; init; } = DateTimeOffset.UtcNow;
}
