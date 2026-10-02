using Microsoft.EntityFrameworkCore;

using Numera.Modules.Crm;
using Numera.Modules.Sales;
using Numera.Modules.Sales.Numbering;
using Numera.Modules.Sales.Vat;
using Numera.Platform.Audit;
using Numera.Platform.Db;
using Numera.Platform.Money;

using CrmAddress = Numera.Modules.Crm.Address;
using SalesAddress = Numera.Modules.Sales.Address;

namespace Numera.IntegrationTests;

/// <summary>
/// Shared seed + finalize harness for the FINALIZE-driven Sales integration suites
/// (plans 03-05/03-06 behaviours, proven by 03-08, gap-closed by 03-11). It seeds a
/// §14-complete issuer (<see cref="CompanyProfile"/>), a recipient
/// (<see cref="BusinessPartner"/>) and a finalizable draft, then delegates the actual
/// finalize to the REAL production core
/// <c>Numera.Api.Endpoints.SalesDocumentEndpoints.FinalizeCoreAsync</c> (made
/// <c>internal</c> + exposed via <c>[InternalsVisibleTo("Numera.IntegrationTests")]</c>,
/// with a <c>ProjectReference</c> to Numera.Api). The production core routes through the
/// real <see cref="NumberingService"/> (the atomic ON CONFLICT counter) and
/// <see cref="VatCalculationService"/> (the BG-23 authority), persists the breakdown via
/// <c>db.Add</c> while the parent is still Draft, then flips status LAST.
/// </summary>
/// <remarks>
/// Because these suites now drive the SHIPPED finalize/Storno/Gutschrift code path (not a
/// parallel reimplementation), a regression to the navigation-collection
/// <c>doc.TaxBreakdown.Add</c> pattern makes the breakdown persist emit a 0-row UPDATE and
/// throws <c>DbUpdateConcurrencyException</c> — failing these suites (GAP-3 regression
/// guard). Every DB-enforced invariant (immutability, uniqueness, open-item close) is
/// exercised against real Postgres as <c>numera_app</c>. Only the §14 completeness gate
/// (<see cref="CheckGate"/>) remains a harness helper, mirroring the Api-internal
/// FinalizeValidation, which is not where GAP 1 lives.
/// </remarks>
internal static class SalesTestData
{
    /// <summary>A minimal line specification the harness snapshots onto a draft line.</summary>
    internal readonly record struct LineSpec(
        string Name,
        decimal Quantity,
        decimal NetUnitPrice,
        TaxCategory Category,
        decimal RatePercent);

    // ---------------------------------------------------------------- seeds

    /// <summary>Seeds the tenant's single §14 issuer profile.</summary>
    public static async Task<CompanyProfile> SeedProfileAsync(
        PostgresFixture fixture,
        Guid tenant,
        bool kleinunternehmer = false,
        int? defaultNetDays = null,
        bool withProfile = true)
    {
        var profile = new CompanyProfile
        {
            TenantId = tenant,
            LegalName = "Aussteller GmbH",
            Address = new SalesAddress
            {
                Street = "Ausstellerweg 2",
                PostalCode = "10115",
                City = "Berlin",
                CountryCode = "DE",
            },
            VatId = "DE811907980",
            IsKleinunternehmer = kleinunternehmer,
            DefaultPaymentTermsNetDays = defaultNetDays,
        };

        if (withProfile)
        {
            await using var db = fixture.CreateAppContext(tenant);
            db.Set<CompanyProfile>().Add(profile);
            await db.SaveChangesAsync();
        }

        return profile;
    }

    /// <summary>Seeds a recipient partner with a billing address + payment terms.</summary>
    public static async Task<BusinessPartner> SeedPartnerAsync(
        PostgresFixture fixture,
        Guid tenant,
        int netDays = 30,
        decimal? skontoPercent = null,
        int? skontoDays = null,
        string? vatId = "DE229840993",
        string name = "Empfänger AG")
    {
        var partner = new BusinessPartner
        {
            TenantId = tenant,
            Name = name,
            IsCustomer = true,
            BillingAddress = new CrmAddress
            {
                Street = "Empfängerstr. 9",
                PostalCode = "80331",
                City = "München",
                CountryCode = "DE",
            },
            VatId = vatId,
            PaymentTermsNetDays = netDays,
            SkontoPercent = skontoPercent,
            SkontoDays = skontoDays,
        };

        await using var db = fixture.CreateAppContext(tenant);
        db.Set<BusinessPartner>().Add(partner);
        await db.SaveChangesAsync();
        return partner;
    }

    /// <summary>Seeds a <see cref="DocumentNumberFormat"/> row for a series.</summary>
    public static async Task SeedFormatAsync(
        PostgresFixture fixture,
        Guid tenant,
        DocumentType type,
        string prefix,
        int padding = 5,
        bool includeYear = true)
    {
        await using var db = fixture.CreateAppContext(tenant);
        db.Set<DocumentNumberFormat>().Add(new DocumentNumberFormat
        {
            TenantId = tenant,
            DocType = (int)type,
            Prefix = prefix,
            IncludeYear = includeYear,
            Padding = padding,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Seeds a Draft document with the given lines. Returns the new document id.</summary>
    public static async Task<Guid> SeedDraftAsync(
        PostgresFixture fixture,
        Guid tenant,
        DocumentType type,
        Guid? partnerId,
        IEnumerable<LineSpec> lines,
        DateOnly date)
    {
        await using var db = fixture.CreateAppContext(tenant);
        var doc = BuildDraft(tenant, type, partnerId, lines, date);
        db.Add(doc);
        await db.SaveChangesAsync();
        return doc.Id;
    }

    /// <summary>Builds (does not persist) a Draft document + snapshotted lines.</summary>
    public static SalesDocument BuildDraft(
        Guid tenant,
        DocumentType type,
        Guid? partnerId,
        IEnumerable<LineSpec> lines,
        DateOnly date)
    {
        var doc = new SalesDocument
        {
            TenantId = tenant,
            DocumentType = type,
            Status = DocumentStatus.Draft,
            PartnerId = partnerId,
            DocumentDate = date,
            Currency = "EUR",
        };

        var lineNumber = 1;
        foreach (var l in lines)
        {
            doc.Lines.Add(new SalesDocumentLine
            {
                TenantId = tenant,
                DocumentId = doc.Id,
                LineNumber = lineNumber++,
                Name = l.Name,
                Quantity = l.Quantity,
                UnitCode = "C62",
                NetUnitPrice = l.NetUnitPrice,
                LineNetAmount = Math.Round(l.Quantity * l.NetUnitPrice, 4, MidpointRounding.AwayFromZero),
                TaxCategory = l.Category,
                VatRatePercent = l.RatePercent,
            });
        }

        doc.TotalNet = doc.Lines.Sum(x => x.LineNetAmount);
        return doc;
    }

    // ---------------------------------------------------------------- finalize

    /// <summary>
    /// Finalizes a draft on the supplied context — a faithful mirror of the production
    /// <c>FinalizeCoreAsync</c>: freeze issuer/recipient snapshots, persist the BG-23
    /// breakdown + frozen totals, assign the race-safe number from the type's OWN series,
    /// compute the due date + create the open item (Rechnung only), flip status LAST. All
    /// inside one transaction (the real <see cref="NumberingService"/> enlists it).
    /// </summary>
    public static async Task FinalizeAsync(
        NumeraDbContext db,
        Guid docId,
        CancellationToken ct = default)
    {
        var doc = await db.Set<SalesDocument>()
            .Include(x => x.Lines)
            .FirstAsync(x => x.Id == docId, ct);
        var profile = await db.Set<CompanyProfile>().FirstOrDefaultAsync(ct);
        var partner = doc.PartnerId is null
            ? null
            : await db.Set<BusinessPartner>().FirstOrDefaultAsync(p => p.Id == doc.PartnerId, ct);

        // §14 completeness gate BEFORE any number is claimed (mirrors production
        // FinalizeValidation, which is Api-internal and thus unreferenceable here): a
        // document that could not produce a legal invoice never burns a number.
        var errors = CheckGate(doc, profile, partner);
        if (errors.Count > 0)
        {
            throw new FinalizeGateException(errors);
        }

        await FinalizeCoreAsync(db, doc, profile!, partner, ct);
    }

    /// <summary>
    /// The §14 UStG completeness gate — a faithful mirror of the Api-internal
    /// <c>FinalizeValidation.Check</c>: an issuer profile with a legal name, at least one of
    /// VAT ID / tax number and a complete address; at least one line; a recipient with a
    /// complete billing address; and AE/K lines require the recipient VAT ID. Returns the
    /// failures keyed by field group (empty = finalizable).
    /// </summary>
    public static Dictionary<string, string[]> CheckGate(
        SalesDocument doc,
        CompanyProfile? profile,
        BusinessPartner? partner)
    {
        var errors = new Dictionary<string, List<string>>();
        void Add(string key, string message)
        {
            if (!errors.TryGetValue(key, out var list))
            {
                list = [];
                errors[key] = list;
            }

            list.Add(message);
        }

        if (profile is null)
        {
            Add("Issuer", "An issuer company profile is required before finalizing (§14 UStG).");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(profile.LegalName))
            {
                Add("Issuer", "The issuer legal name is required (§14 UStG, BT-27).");
            }

            var hasVatId = !string.IsNullOrWhiteSpace(profile.VatId);
            var hasTaxNumber = !string.IsNullOrWhiteSpace(profile.TaxNumber);
            if (!hasVatId && !hasTaxNumber)
            {
                Add("Issuer", "At least one of the issuer VAT ID or tax number is required (§14 UStG, BT-31/BT-32).");
            }

            if (!AddressComplete(profile.Address?.Street, profile.Address?.PostalCode, profile.Address?.City))
            {
                Add("Issuer", "A complete issuer address is required (§14 UStG, BG-5).");
            }
        }

        if (doc.Lines.Count == 0)
        {
            Add("Lines", "A document must have at least one line before finalizing.");
        }

        if (partner is null)
        {
            Add("Recipient", "A recipient partner is required before finalizing (§14 UStG, BG-7).");
        }
        else if (!AddressComplete(partner.BillingAddress?.Street, partner.BillingAddress?.PostalCode, partner.BillingAddress?.City))
        {
            Add("Recipient", "A complete recipient billing address is required (§14 UStG, BG-8).");
        }

        var requiresRecipientVatId = doc.Lines.Any(l => l.TaxCategory is TaxCategory.AE or TaxCategory.K);
        if (requiresRecipientVatId && string.IsNullOrWhiteSpace(partner?.VatId))
        {
            Add("Recipient", "Reverse-charge (AE) or intra-EU (K) lines require the recipient VAT ID (§14a / §6a UStG).");
        }

        return errors.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
    }

    private static bool AddressComplete(string? street, string? postalCode, string? city)
        => !string.IsNullOrWhiteSpace(street)
        && !string.IsNullOrWhiteSpace(postalCode)
        && !string.IsNullOrWhiteSpace(city);

    /// <summary>
    /// The finalize core owning its OWN transaction (used by the finalize + concurrency
    /// suites). Storno reuses <see cref="ApplyFinalizeAsync"/> inside its own transaction.
    /// </summary>
    public static async Task FinalizeCoreAsync(
        NumeraDbContext db,
        SalesDocument doc,
        CompanyProfile profile,
        BusinessPartner? partner,
        CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await ApplyFinalizeAsync(db, doc, profile, partner, ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>
    /// The finalize body WITHOUT transaction management — the caller owns
    /// BeginTransaction/Commit (mirrors how production Storno reuses the finalize core inside
    /// its own transaction so the original mutation + open-item close are one atomic unit).
    /// Delegates to the REAL production
    /// <c>Numera.Api.Endpoints.SalesDocumentEndpoints.FinalizeCoreAsync</c> — the shipped
    /// finalize/Storno/Gutschrift code path — so the breakdown persist, numbering, open-item
    /// creation and status flip (the GAP-1 locus) are genuinely under test. The real
    /// <see cref="NumberingService"/> enlists the caller's ambient transaction.
    /// </summary>
    public static async Task ApplyFinalizeAsync(
        NumeraDbContext db,
        SalesDocument doc,
        CompanyProfile profile,
        BusinessPartner? partner,
        CancellationToken ct = default)
    {
        var numbering = new NumberingService(db);
        var audit = new NoOpAuditWriter();
        await Numera.Api.Endpoints.SalesDocumentEndpoints.FinalizeCoreAsync(
            doc, profile, partner, db, numbering, audit, doc.TenantId, "FinalizeTest", ct);
    }

    /// <summary>Extracts the trailing sequence integer from a rendered number (e.g. RE-2026-00007 → 7).</summary>
    public static int SequenceOf(string documentNumber)
    {
        var lastDash = documentNumber.LastIndexOf('-');
        var tail = lastDash >= 0 ? documentNumber[(lastDash + 1)..] : documentNumber;
        return int.Parse(tail, System.Globalization.CultureInfo.InvariantCulture);
    }
}

/// <summary>Thrown by the harness finalize when the §14 completeness gate fails (mirrors the 422 path).</summary>
internal sealed class FinalizeGateException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("Document is not ready to finalize (§14 UStG).")
{
    /// <summary>The gate failures keyed by field group (Issuer / Lines / Recipient).</summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

/// <summary>
/// No-op <see cref="IAuditWriter"/> for the finalize suites: the production
/// <c>FinalizeCoreAsync</c> records an audit event, but these tests assert on the
/// breakdown/numbering/open-item/immutability behaviour, not on audit rows, and the real
/// AuditWriter needs ICurrentTenant/ICurrentUser seams. This keeps the GAP-1 locus fully
/// exercised without wiring those seams.
/// </summary>
internal sealed class NoOpAuditWriter : IAuditWriter
{
    public Task RecordAsync(IAuditEvent evt, CancellationToken ct) => Task.CompletedTask;
}
