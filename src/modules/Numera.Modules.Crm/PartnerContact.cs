using System.ComponentModel.DataAnnotations.Schema;

using Microsoft.EntityFrameworkCore;

using Numera.Platform.Db;

namespace Numera.Modules.Crm;

/// <summary>
/// A contact person (Ansprechpartner) belonging to a <see cref="BusinessPartner"/>.
/// A partner has 0..n contacts. Maps to EN 16931 BG-9 (BT-56 contact name, BT-58
/// contact e-mail).
/// </summary>
[Table("partner_contacts")]
[Index(nameof(TenantId), nameof(PartnerId))]
public sealed class PartnerContact : ITenantEntity
{
    /// <summary>UUIDv7 primary key.</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid TenantId { get; init; }

    /// <summary>Owning partner (FK to <c>partners.id</c>).</summary>
    public Guid PartnerId { get; init; }

    /// <summary>Salutation (Anrede), e.g. Herr / Frau. Optional.</summary>
    public string? Salutation { get; set; }

    /// <summary>First name (Vorname). Optional.</summary>
    public string? FirstName { get; set; }

    /// <summary>Last name (Nachname). Required.</summary>
    public required string LastName { get; set; }

    /// <summary>Contact e-mail (BT-58). Optional.</summary>
    public string? Email { get; set; }

    /// <summary>Contact phone number. Optional.</summary>
    public string? Phone { get; set; }

    /// <summary>Role / position (Funktion), e.g. Buchhaltung. Optional.</summary>
    public string? Position { get; set; }

    /// <summary>True if this is the partner's primary contact.</summary>
    public bool IsPrimary { get; set; }
}
