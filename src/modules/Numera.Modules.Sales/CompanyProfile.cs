using System.ComponentModel.DataAnnotations.Schema;

using Microsoft.EntityFrameworkCore;

using Numera.Platform.Db;
using Numera.Platform.Money;

namespace Numera.Modules.Sales;

/// <summary>German state used to normalize a local Steuernummer for ELSTER.</summary>
public enum Bundesland
{
    BadenWuerttemberg = 1,
    Bayern = 2,
    Berlin = 3,
    Brandenburg = 4,
    Bremen = 5,
    Hamburg = 6,
    Hessen = 7,
    MecklenburgVorpommern = 8,
    Niedersachsen = 9,
    NordrheinWestfalen = 10,
    RheinlandPfalz = 11,
    Saarland = 12,
    Sachsen = 13,
    SachsenAnhalt = 14,
    SchleswigHolstein = 15,
    Thueringen = 16,
}

/// <summary>
/// The tenant's own §14 UStG <b>issuer</b> master data (Ausstellerstammdaten) — the one
/// record carrying the legal name, address, tax identity and §19 status that every
/// finalized invoice must snapshot (RESEARCH.md Q4: "MISSING, must add"). The lean
/// <c>tenants</c> table intentionally holds none of this; this is the single source of
/// issuer truth that plan 03-05 (finalize) copies onto documents at posting time.
/// </summary>
/// <remarks>
/// <para>
/// A tenant has EXACTLY ONE company profile — enforced by the unique index on
/// <see cref="TenantId"/>. That same index is the tenant-leading access index, so the
/// reflective <c>RegisterModuleTenantEntities</c> discovery does NOT add a second.
/// </para>
/// <para>
/// The seller identity here maps onto the EN 16931 Seller group (BG-4/BG-5): legal name
/// (BT-27), postal address (BG-5), VAT identifier (BT-31) and tax registration (BT-32).
/// Cheap nullable bank / registry / contact seams are present now — columns are cheap,
/// migrations touching an RLS table are not — but carry no v1 validation.
/// </para>
/// <para>
/// <see cref="ITenantEntity"/> gives it a DB RLS policy + tenant query filter; the
/// entity self-describes via attributes so <c>NumeraDbContext</c> is not edited.
/// </para>
/// </remarks>
[Table("company_profile")]
[Index(nameof(TenantId), IsUnique = true)]
public sealed class CompanyProfile : ITenantEntity
{
    /// <summary>UUIDv7 primary key (time-ordered; maps to PG18 <c>uuidv7()</c>).</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid TenantId { get; init; }

    // --- Seller identity (EN 16931 BT-27 / BG-5) -----------------------------

    /// <summary>Registered legal name of the issuer (BT-27 Seller name). Required.</summary>
    public required string LegalName { get; set; }

    /// <summary>Issuer postal address (owned, BG-5). Required.</summary>
    public required Address Address { get; set; }

    // --- Tax identity --------------------------------------------------------

    /// <summary>USt-IdNr / VAT identifier (BT-31). Exactly one of this or <see cref="TaxNumber"/> is required.</summary>
    public string? VatId { get; set; }

    /// <summary>National tax number (Steuernummer, BT-32). Exactly one of this or <see cref="VatId"/> is required.</summary>
    public string? TaxNumber { get; set; }

    /// <summary>
    /// German state whose Landes-format applies to <see cref="TaxNumber"/>. Required only
    /// when the local Steuernummer is exported to a nationwide ELSTER format.
    /// </summary>
    public Bundesland? Bundesland { get; set; }

    /// <summary>
    /// §19 UStG small-business flag (Kleinunternehmer). When true, invoices carry no VAT
    /// and the mandatory §19 note instead. Defaults to false.
    /// </summary>
    public bool IsKleinunternehmer { get; set; }

    // --- Defaults ------------------------------------------------------------

    /// <summary>Default net payment term in days applied to new invoices. Nullable.</summary>
    public int? DefaultPaymentTermsNetDays { get; set; }

    /// <summary>Optional default VAT category (EN 16931 BT-151 seam) for new invoice lines.</summary>
    public TaxCategory? DefaultTaxCategory { get; set; }

    // --- Deferred cheap nullable seams (no v1 UI/validation) -----------------

    /// <summary>Bank IBAN for the "please pay to" block (Phase 4/5 seam).</summary>
    public string? Iban { get; set; }

    /// <summary>Bank BIC for the "please pay to" block (Phase 4/5 seam).</summary>
    public string? Bic { get; set; }

    /// <summary>Bank name shown alongside the IBAN/BIC (Phase 4/5 seam).</summary>
    public string? BankName { get; set; }

    /// <summary>Register court (Registergericht) for the imprint (Handelsregister seam).</summary>
    public string? RegisterCourt { get; set; }

    /// <summary>Register number (Handelsregisternummer) for the imprint.</summary>
    public string? RegisterNumber { get; set; }

    /// <summary>Managing director / authorised representative (Geschäftsführer) for the imprint.</summary>
    public string? ManagingDirector { get; set; }

    /// <summary>Issuer contact e-mail shown on documents.</summary>
    public string? ContactEmail { get; set; }

    /// <summary>Issuer contact phone shown on documents.</summary>
    public string? ContactPhone { get; set; }

    /// <summary>Reference/key to the issuer logo asset for PDF rendering (Phase 4 seam).</summary>
    public string? LogoRef { get; set; }

    // --- Logo asset (Phase 4 — presentation, RLS-scoped, stored in Postgres) -

    /// <summary>
    /// The tenant logo bytes shown on the rendered PDF letterhead (Phase-4 DOCS-02).
    /// Stored as <c>bytea</c> on the already-RLS-scoped, one-per-tenant profile per the LOCKED
    /// decision (object storage is out of scope for v1). The logo is presentation, not part of
    /// the frozen §14 legal snapshot, so reading it live at render time is fine. The existing
    /// <see cref="LogoRef"/> seam is left untouched. Nullable.
    /// </summary>
    public byte[]? LogoBytes { get; set; }

    /// <summary>MIME content type of <see cref="LogoBytes"/> (e.g. image/png, image/jpeg). Nullable.</summary>
    public string? LogoContentType { get; set; }
}
