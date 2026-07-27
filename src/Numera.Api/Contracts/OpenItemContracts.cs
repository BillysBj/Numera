using Numera.Modules.Sales;

namespace Numera.Api.Contracts;

/// <summary>
/// Response DTO for the OP-Übersicht (offene Posten) list (plan 03-04) — the read half
/// of OPDN-01. Denormalizes the document number + amounts from the <see cref="OpenItem"/>
/// so the list needs no join. Open items are CREATED at invoice finalize (plan 03-05) and
/// reduced by payments (Phase 6); this plan only reads them.
/// </summary>
/// <remarks>
/// Enums cross the wire as NUMBERS (the Catalog convention). <see cref="Overdue"/> is a
/// server-computed convenience flag: the due date has passed and the item is still
/// (partially) open.
/// </remarks>
public sealed record OpenItemListItem(
    Guid Id,
    Guid DocumentId,
    string DocumentNumber,
    Guid? PartnerId,
    string Currency,
    decimal OriginalAmount,
    decimal OpenAmount,
    OpenItemStatus Status,
    DateOnly IssuedOn,
    DateOnly DueDate,
    bool Overdue,
    int CurrentDunningLevel,
    DateOnly? LastDunnedOn);
