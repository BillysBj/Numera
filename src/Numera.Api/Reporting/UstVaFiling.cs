using System.ComponentModel.DataAnnotations.Schema;

using Microsoft.EntityFrameworkCore;

using Numera.Modules.Ledger;
using Numera.Platform.Db;

namespace Numera.Api.Reporting;

/// <summary>Lifecycle of a persisted Umsatzsteuer-Voranmeldung snapshot.</summary>
public enum UstVaFilingStatus
{
    /// <summary>Generated review artifact which has not been submitted to ELSTER.</summary>
    Draft,

    /// <summary>Submitted declaration; the database makes the row immutable.</summary>
    Submitted,
}

/// <summary>
/// Tenant-scoped snapshot of one generated Umsatzsteuer-Voranmeldung. A correction is
/// represented by a new row linked through <see cref="BerichtigtVonFilingId"/>; a
/// submitted row is never edited or deleted.
/// </summary>
[Table("ust_va_filing")]
[Index(nameof(TenantId), nameof(Jahr), nameof(Zeitraum))]
public sealed class UstVaFiling : ITenantEntity
{
    /// <summary>UUIDv7 primary key.</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid TenantId { get; init; }

    /// <summary>Four-digit declaration year.</summary>
    public int Jahr { get; init; }

    /// <summary>ELSTER month (01-12) or quarter (41-44) period code.</summary>
    public required string Zeitraum { get; init; }

    /// <summary>Tax recognition method frozen with the generated report.</summary>
    public Besteuerungsart Besteuerungsart { get; init; }

    /// <summary>JSON snapshot of exactly the Kennziffer lines used for the filing.</summary>
    [Column(TypeName = "jsonb")]
    public required string KzSnapshotJson { get; init; }

    /// <summary>Computed payable VAT (Kennziffer 83).</summary>
    [Precision(19, 4)]
    public decimal Zahllast { get; init; }

    /// <summary>Generated ISO-8859-15 ELSTER Nutzdaten bytes, when exported.</summary>
    public byte[]? XmlBytes { get; init; }

    /// <summary>Draft or submitted lifecycle status.</summary>
    public UstVaFilingStatus Status { get; init; } = UstVaFilingStatus.Draft;

    /// <summary>When the snapshot was created.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>When the declaration was submitted, if applicable.</summary>
    public DateTimeOffset? SubmittedAt { get; init; }

    /// <summary>Prior filing corrected by this row; null for an original filing.</summary>
    public Guid? BerichtigtVonFilingId { get; init; }
}
