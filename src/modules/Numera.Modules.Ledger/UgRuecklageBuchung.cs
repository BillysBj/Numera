using System.ComponentModel.DataAnnotations.Schema;

using Microsoft.EntityFrameworkCore;

using Numera.Platform.Db;

namespace Numera.Modules.Ledger;

/// <summary>One immutable reserve allocation per fiscal year.</summary>
[Table("ug_ruecklage_buchungen")]
[Index(nameof(TenantId), nameof(Jahr), IsUnique = true)]
public sealed class UgRuecklageBuchung : ITenantEntity
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid TenantId { get; init; }
    public int Jahr { get; init; }
    [Column(TypeName = "numeric(19,4)")]
    public decimal Betrag { get; init; }
    [Column(TypeName = "numeric(19,4)")]
    public decimal VerlustvortragVorjahr { get; init; }
    public Guid? JournalEntryId { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
