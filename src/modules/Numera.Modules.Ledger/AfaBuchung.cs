using System.ComponentModel.DataAnnotations.Schema;

using Microsoft.EntityFrameworkCore;

using Numera.Platform.Db;

namespace Numera.Modules.Ledger;

/// <summary>One booked annual depreciation amount, linked to its immutable journal entry.</summary>
[Table("afa_buchungen")]
[Index(nameof(TenantId), nameof(FixedAssetId), nameof(Jahr), IsUnique = true)]
public sealed class AfaBuchung : ITenantEntity
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid TenantId { get; init; }
    public Guid FixedAssetId { get; init; }
    public int Jahr { get; init; }
    [Column(TypeName = "numeric(19,4)")]
    public decimal Betrag { get; init; }
    public Guid? JournalEntryId { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
