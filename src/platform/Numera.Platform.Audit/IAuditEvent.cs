namespace Numera.Platform.Audit;

/// <summary>
/// A recordable, finance-relevant change that a caller hands to
/// <see cref="IAuditWriter.RecordAsync"/>. Describes only the change itself;
/// tenant and actor are stamped by the writer from ambient context, so callers
/// never supply (or spoof) them.
/// </summary>
public interface IAuditEvent
{
    /// <summary>The action performed, e.g. <c>"invoice.finalized"</c>.</summary>
    string Action { get; }

    /// <summary>The type of entity affected, e.g. <c>"Invoice"</c>.</summary>
    string EntityType { get; }

    /// <summary>The id of the affected entity, when applicable.</summary>
    Guid? EntityId { get; }

    /// <summary>The entity state before the change, serialized as JSON (or null).</summary>
    string? Before { get; }

    /// <summary>The entity state after the change, serialized as JSON (or null).</summary>
    string? After { get; }
}
