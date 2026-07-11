using System.ComponentModel.DataAnnotations.Schema;

using Microsoft.EntityFrameworkCore;

using Numera.Platform.Db;
using Numera.Platform.Money;

namespace Numera.Modules.Crm;

/// <summary>
/// A single business partner (Geschäftspartner) — the one master record for anyone
/// this tenant transacts with. Role flags decide whether the partner is a customer
/// (Debitor), a supplier (Kreditor), or both; there is deliberately NOT a separate
/// customer and supplier entity (RESEARCH.md Q1: a dual-role partner must be one
/// record, not two divergent ones).
/// </summary>
/// <remarks>
/// <para>
/// When this tenant sells to the partner, the partner is the EN 16931 <b>Buyer</b>
/// (BG-7/BG-8); the fields here populate BT-44..BT-57 directly so Phase 3/5 invoicing
/// can consume them without a schema break. Cheap nullable B2G / bank / DATEV seams
/// (<see cref="Iban"/>, <see cref="LeitwegId"/>, <see cref="DebtorAccount"/>, …) are
/// present now — columns are cheap, migrations touching RLS tables are not.
/// </para>
/// <para>
/// <see cref="ITenantEntity"/> gives it a DB RLS policy + tenant query filter;
/// <see cref="IArchivable"/> makes it soft-deletable (hidden by the "NotArchived"
/// filter, never physically removed).
/// </para>
/// </remarks>
[Table("partners")]
[Index(nameof(TenantId), nameof(Name))]
public sealed class BusinessPartner : ITenantEntity, IArchivable
{
    /// <summary>UUIDv7 primary key (time-ordered; maps to PG18 <c>uuidv7()</c>).</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid TenantId { get; init; }

    // --- Identity (EN 16931 BT-44/BG-8) --------------------------------------

    /// <summary>Display name / company (BT-44 Buyer name).</summary>
    public required string Name { get; set; }

    /// <summary>Legal form, free text in v1 (e.g. GmbH, e.K.).</summary>
    public string? LegalForm { get; set; }

    /// <summary>Primary / billing postal address (owned, BG-8). Required.</summary>
    public required Address BillingAddress { get; set; }

    /// <summary>Optional deviating shipping address (Lieferanschrift, owned, nullable).</summary>
    public Address? ShippingAddress { get; set; }

    // --- Tax identity --------------------------------------------------------

    /// <summary>USt-IdNr / VAT identifier (BT-48). Offline-validated by <see cref="VatId"/>.</summary>
    public string? VatId { get; set; }

    /// <summary>National tax number (Steuernummer) — distinct from the VAT ID.</summary>
    public string? TaxNumber { get; set; }

    // --- Contact -------------------------------------------------------------

    /// <summary>Primary e-mail (BT-49 electronic-address seam).</summary>
    public string? Email { get; set; }

    /// <summary>Primary phone number.</summary>
    public string? Phone { get; set; }

    /// <summary>Website URL.</summary>
    public string? Website { get; set; }

    // --- Payment terms -------------------------------------------------------

    /// <summary>Net payment term in days (e.g. 14). Invoice-level detail lands in Phase 3.</summary>
    public int? PaymentTermsNetDays { get; set; }

    /// <summary>Cash-discount (Skonto) percentage.</summary>
    [Precision(5, 2)]
    public decimal? SkontoPercent { get; set; }

    /// <summary>Number of days within which the <see cref="SkontoPercent"/> discount applies.</summary>
    public int? SkontoDays { get; set; }

    /// <summary>Default document currency as an ISO 4217 code (BT-5). Defaults to EUR.</summary>
    public string DefaultCurrency { get; set; } = "EUR";

    /// <summary>Preferred document/communication language.</summary>
    public PartnerLanguage Language { get; set; } = PartnerLanguage.De;

    /// <summary>Optional default VAT category (EN 16931 BT-151 seam) for convenience.</summary>
    public TaxCategory? DefaultTaxCategory { get; set; }

    // --- Roles ---------------------------------------------------------------

    /// <summary>True if this partner is a customer (Debitor). At least one role must be true.</summary>
    public bool IsCustomer { get; set; }

    /// <summary>True if this partner is a supplier (Kreditor). At least one role must be true.</summary>
    public bool IsSupplier { get; set; }

    /// <summary>Customer number (Debitor-facing). Unique per tenant among non-archived rows when set.</summary>
    public string? CustomerNumber { get; set; }

    /// <summary>Supplier number (Kreditor-facing). Unique per tenant among non-archived rows when set.</summary>
    public string? SupplierNumber { get; set; }

    // --- Deferred cheap nullable seams (no v1 UI/validation) ------------------

    /// <summary>Bank IBAN (payments/dunning — Phase 6 seam).</summary>
    public string? Iban { get; set; }

    /// <summary>Bank BIC (payments/dunning — Phase 6 seam).</summary>
    public string? Bic { get; set; }

    /// <summary>Leitweg-ID (B2G buyer reference, BT-10) — mandatory only for public-sector XRechnung (Phase 5).</summary>
    public string? LeitwegId { get; set; }

    /// <summary>DATEV debtor account (Debitorenkonto) — DATEV-export seam.</summary>
    public string? DebtorAccount { get; set; }

    /// <summary>DATEV creditor account (Kreditorenkonto) — DATEV-export seam.</summary>
    public string? CreditorAccount { get; set; }

    // --- Lifecycle -----------------------------------------------------------

    /// <inheritdoc />
    public DateTimeOffset? ArchivedAt { get; set; }
}
