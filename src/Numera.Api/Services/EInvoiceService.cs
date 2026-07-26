using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Numera.Modules.Crm;
using Numera.Modules.Sales;
using Numera.Modules.Sales.EInvoice;
using Numera.Modules.Sales.Pdf;
using Numera.Modules.Sales.Vat;
using Numera.Platform.Db;
using Numera.Platform.Money;
using Numera.Platform.Tenancy;

namespace Numera.Api.Services;

/// <summary>
/// The shared e-invoice entrypoint the finalize job, the download endpoint and the pre-finalize
/// dry-run all call (Phase-5 EINV-01/EINV-03). It generates an XRechnung (UBL/CII) from a
/// finalized invoice's FROZEN snapshot via <see cref="XRechnungGenerator"/>, validates the bytes
/// against the government-authoritative KoSIT validator (<see cref="IEInvoiceValidator"/>), and
/// stores the XML + verdict + structured report in <c>document_einvoice</c> — idempotently, one
/// row per (document, format).
/// </summary>
/// <remarks>
/// <para>
/// LOCKED (same GoBD rule as <see cref="DocumentPdfService"/>): the generated XML derives ONLY
/// from the frozen <c>IssuerSnapshot</c>/<c>RecipientSnapshot</c> + persisted lines/breakdown/
/// totals — never live master data. No logo is needed for XML.
/// </para>
/// <para>
/// Idempotency: <see cref="GenerateAndValidate"/> deletes any existing artifact for the
/// (document, format) pair and inserts a fresh one, so a job retry or explicit re-generate never
/// duplicates. Rows are inserted via <c>db.Add</c> — the file-wide client-set-UUIDv7-PK
/// convention, never a navigation-collection add.
/// </para>
/// <para>
/// The eagerly-generated set is <see cref="FinalizeFormats"/> (UBL + CII in this plan). It is the
/// single extension point: 05-04 appends <see cref="EInvoiceFormat.ZugferdPdfA3"/> there and the
/// finalize job picks it up with NO edit to <see cref="Numera.Api.Jobs.GenerateEInvoiceJob"/>.
/// </para>
/// </remarks>
public sealed class EInvoiceService
{
    /// <summary>
    /// A structural placeholder invoice number (BT-1) for the pre-finalize dry-run — the real
    /// gapless number is not yet burned, and KoSIT's structural checks (BT-10, electronic
    /// addresses, payment means) do not depend on the actual number, only on its presence.
    /// </summary>
    public const string DryRunNumberPlaceholder = "ENTWURF-VORAB";

    private static readonly JsonSerializerOptions ReportJson = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The e-invoice syntaxes eagerly generated + validated at finalize, so a later download of
    /// any is instant. THE extension point: 05-04 appends <see cref="EInvoiceFormat.ZugferdPdfA3"/>
    /// here and the finalize job serves it with no job-file edit.
    /// </summary>
    public static readonly IReadOnlyList<EInvoiceFormat> FinalizeFormats =
        [EInvoiceFormat.XRechnungUbl, EInvoiceFormat.XRechnungCii];

    private readonly NumeraDbContext _db;
    private readonly ICurrentTenant _tenant;
    private readonly IEInvoiceValidator _validator;

    /// <summary>Creates the service over the request/job-scoped DbContext + tenant + validator.</summary>
    public EInvoiceService(NumeraDbContext db, ICurrentTenant tenant, IEInvoiceValidator validator)
    {
        _db = db;
        _tenant = tenant;
        _validator = validator;
    }

    /// <summary>The outcome of a generate/download request.</summary>
    public enum Outcome
    {
        /// <summary>The XML is available (stored or freshly generated).</summary>
        Ok,

        /// <summary>No document with that id exists under the current tenant (RLS-scoped).</summary>
        NotFound,

        /// <summary>The document exists but is still a Draft — a draft has no frozen snapshot to serialize.</summary>
        NotFinalized,
    }

    /// <summary>A download result: the outcome plus (on <see cref="Outcome.Ok"/>) the bytes + filename.</summary>
    /// <param name="Result">Whether the XML is available, missing, or the document is a draft.</param>
    /// <param name="Xml">The XML bytes when <see cref="Result"/> is <see cref="Outcome.Ok"/>.</param>
    /// <param name="FileName">The download file name (e.g. RE-2026-00001-ubl.xml).</param>
    public readonly record struct XmlResult(Outcome Result, byte[]? Xml, string? FileName);

    /// <summary>
    /// Generates the <paramref name="format"/> XRechnung for <paramref name="documentId"/> from
    /// its frozen snapshot, validates it against KoSIT, and stores the bytes + status + report in
    /// <c>document_einvoice</c>, replacing any prior artifact for the same (document, format).
    /// Called by the finalize job. Throws if the document is missing or still a Draft (the finalize
    /// hook only ever enqueues finalized Rechnungen, so either is a programming/timing error).
    /// </summary>
    public async Task<EInvoiceArtifact> GenerateAndValidate(
        Guid documentId,
        EInvoiceFormat format,
        CancellationToken ct)
    {
        var doc = await LoadDocumentAsync(documentId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Cannot generate an e-invoice for document {documentId}: not found under the current tenant.");

        if (doc.Status == DocumentStatus.Draft)
        {
            throw new InvalidOperationException(
                $"Cannot generate an e-invoice for document {documentId}: it is still a Draft (no frozen snapshot).");
        }

        var model = SnapshotReader.FromDocument(doc);
        var xml = Serialize(model, format);

        var result = await _validator.ValidateAsync(xml, ct).ConfigureAwait(false);

        // Idempotent replace: drop any prior artifact for this (document, format), insert the fresh one.
        var stale = await _db.Set<EInvoiceArtifact>()
            .Where(a => a.DocumentId == documentId && a.Format == format)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (stale.Count > 0)
        {
            _db.RemoveRange(stale);
        }

        var now = DateTimeOffset.UtcNow;
        var artifact = new EInvoiceArtifact
        {
            TenantId = _tenant.TenantId!.Value,
            DocumentId = doc.Id,
            Format = format,
            Xml = xml,
            DocumentNumber = doc.DocumentNumber ?? doc.Id.ToString(),
            ValidationStatus = result.Status,
            ValidationReport = SerializeReport(result),
            ByteSize = xml.LongLength,
            GeneratedAt = now,
            ValidatedAt = now,
        };
        _db.Add(artifact);

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return artifact;
    }

    /// <summary>
    /// The render-if-absent download path (idempotent recovery): returns the stored artifact if one
    /// exists for the (document, format) pair, else generates + validates + stores it on demand.
    /// Distinguishes a missing document (<see cref="Outcome.NotFound"/>) from a still-draft one
    /// (<see cref="Outcome.NotFinalized"/>) so the endpoint can map clean 404 / 409 responses.
    /// </summary>
    public async Task<XmlResult> GetOrGenerate(Guid documentId, EInvoiceFormat format, CancellationToken ct)
    {
        var existing = await _db.Set<EInvoiceArtifact>()
            .AsNoTracking()
            .Where(a => a.DocumentId == documentId && a.Format == format)
            .OrderByDescending(a => a.GeneratedAt)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return new XmlResult(Outcome.Ok, existing.Xml, FileName(existing.DocumentNumber, format));
        }

        var doc = await LoadDocumentAsync(documentId, ct).ConfigureAwait(false);
        if (doc is null)
        {
            return new XmlResult(Outcome.NotFound, null, null);
        }

        if (doc.Status == DocumentStatus.Draft)
        {
            return new XmlResult(Outcome.NotFinalized, null, null);
        }

        var artifact = await GenerateAndValidate(documentId, format, ct).ConfigureAwait(false);
        return new XmlResult(Outcome.Ok, artifact.Xml, FileName(artifact.DocumentNumber, format));
    }

    /// <summary>
    /// The stored authoritative verdict for the send gate (Phase-5 EINV-03, stage 2): the
    /// <see cref="EInvoiceValidationStatus"/> of the stored (document, format) artifact, or null
    /// when none exists yet (the async job has not landed). The send endpoint refuses to dispatch
    /// unless this is <see cref="EInvoiceValidationStatus.Accepted"/>.
    /// </summary>
    public async Task<EInvoiceArtifact?> GetArtifactAsync(Guid documentId, EInvoiceFormat format, CancellationToken ct) =>
        await _db.Set<EInvoiceArtifact>()
            .AsNoTracking()
            .Where(a => a.DocumentId == documentId && a.Format == format)
            .OrderByDescending(a => a.GeneratedAt)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

    /// <summary>
    /// The pre-finalize dry-run (Phase-5 EINV-03, stage 1). Builds a PROVISIONAL
    /// <see cref="InvoicePdfModel"/> from the would-be-frozen issuer/recipient/lines WITHOUT a
    /// persisted number (a placeholder BT-1), generates the UBL XRechnung, and validates it —
    /// returning the structured verdict. The finalize handler calls this BEFORE a gapless number
    /// is burned, so a hard rejection strands no number; a validator outage does not block
    /// finalize (that decision lives in the handler).
    /// </summary>
    public Task<EInvoiceValidationResult> DryRunAsync(
        SalesDocument provisionalDoc,
        CompanyProfile profile,
        BusinessPartner? partner,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(provisionalDoc);
        ArgumentNullException.ThrowIfNull(profile);

        var model = BuildProvisionalModel(provisionalDoc, profile, partner);
        var xml = XRechnungGenerator.GenerateUbl(model);
        return _validator.ValidateAsync(xml, ct);
    }

    // Loads the document (RLS-scoped) with the persisted lines + BG-23 breakdown the mapper needs.
    private Task<SalesDocument?> LoadDocumentAsync(Guid documentId, CancellationToken ct) =>
        _db.Set<SalesDocument>()
            .AsNoTracking()
            .Include(x => x.Lines)
            .Include(x => x.TaxBreakdown)
            .FirstOrDefaultAsync(x => x.Id == documentId, ct);

    // Serializes ONE mapped descriptor to the requested XRechnung syntax. ZUGFeRD PDF/A-3 (05-04)
    // is not produced here; the finalize list only contains UBL + CII in this plan.
    private static byte[] Serialize(InvoicePdfModel model, EInvoiceFormat format) => format switch
    {
        EInvoiceFormat.XRechnungUbl => XRechnungGenerator.GenerateUbl(model),
        EInvoiceFormat.XRechnungCii => XRechnungGenerator.GenerateCii(model),
        EInvoiceFormat.ZugferdPdfA3 => throw new NotSupportedException(
            "ZUGFeRD PDF/A-3 generation lands in plan 05-04; EInvoiceService.FinalizeFormats does not yet include it."),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown e-invoice format."),
    };

    private static string FileName(string number, EInvoiceFormat format)
    {
        var suffix = format switch
        {
            EInvoiceFormat.XRechnungUbl => "ubl",
            EInvoiceFormat.XRechnungCii => "cii",
            EInvoiceFormat.ZugferdPdfA3 => "zugferd",
            _ => "xml",
        };

        return $"{number}-{suffix}.xml";
    }

    // Persists the structured findings + verdict as jsonb (the raw KoSIT XML is not jsonb, so the
    // structured findings are serialized; an outage yields a null report).
    private static string? SerializeReport(EInvoiceValidationResult result)
    {
        if (result.Status == EInvoiceValidationStatus.Unavailable && result.Findings.Count == 0)
        {
            return null;
        }

        return JsonSerializer.Serialize(
            new { status = result.Status.ToString(), findings = result.Findings },
            ReportJson);
    }

    // Builds the PROVISIONAL model the dry-run validates — the same projection finalize will
    // freeze, but from LIVE issuer/recipient (there is no snapshot yet) and with a placeholder
    // number. The VAT breakdown is computed via the single VatCalculationService authority so the
    // dry-run's numbers match what finalize persists.
    private static InvoicePdfModel BuildProvisionalModel(
        SalesDocument doc,
        CompanyProfile profile,
        BusinessPartner? partner)
    {
        var rows = VatCalculationService.Calculate(
            doc.Lines.Select(l => new VatLineInput(l.TaxCategory, l.VatRatePercent, l.LineNetAmount)),
            profile.IsKleinunternehmer);

        var totalNet = doc.Lines.Sum(l => l.LineNetAmount);
        var totalTax = VatCalculationService.DocumentVatTotal(rows);
        var totalGross = totalNet + totalTax;

        return new InvoicePdfModel
        {
            Issuer = new InvoicePdfModel.IssuerBlock
            {
                LegalName = profile.LegalName,
                Address = new InvoicePdfModel.AddressBlock
                {
                    Street = profile.Address?.Street,
                    Line2 = profile.Address?.Line2,
                    PostalCode = profile.Address?.PostalCode,
                    City = profile.Address?.City,
                    CountryCode = profile.Address?.CountryCode,
                    PoBox = profile.Address?.PoBox,
                },
                VatId = profile.VatId,
                TaxNumber = profile.TaxNumber,
                IsKleinunternehmer = profile.IsKleinunternehmer,
                Iban = profile.Iban,
                Bic = profile.Bic,
                BankName = profile.BankName,
                RegisterCourt = profile.RegisterCourt,
                RegisterNumber = profile.RegisterNumber,
                ManagingDirector = profile.ManagingDirector,
                ContactEmail = profile.ContactEmail,
                ContactPhone = profile.ContactPhone,
            },
            Recipient = new InvoicePdfModel.RecipientBlock
            {
                Name = partner?.Name,
                LegalForm = partner?.LegalForm,
                BillingAddress = new InvoicePdfModel.AddressBlock
                {
                    Street = partner?.BillingAddress?.Street,
                    Line2 = partner?.BillingAddress?.Line2,
                    PostalCode = partner?.BillingAddress?.PostalCode,
                    City = partner?.BillingAddress?.City,
                    CountryCode = partner?.BillingAddress?.CountryCode,
                    PoBox = partner?.BillingAddress?.PoBox,
                },
                VatId = partner?.VatId,
                TaxNumber = partner?.TaxNumber,
                Email = partner?.Email,
            },
            DocumentNumber = string.IsNullOrWhiteSpace(doc.DocumentNumber) ? DryRunNumberPlaceholder : doc.DocumentNumber,
            DocumentDate = doc.DocumentDate,
            ServiceDate = doc.ServiceDate,
            ServicePeriodEnd = doc.ServicePeriodEnd,
            DueDate = doc.DueDate,
            Currency = doc.Currency,
            BuyerReference = doc.BuyerReference,
            Notes = doc.Notes,
            Lines = [.. doc.Lines
                .OrderBy(l => l.LineNumber)
                .Select(l => new InvoicePdfModel.LineRow
                {
                    LineNumber = l.LineNumber,
                    Name = l.Name,
                    Description = l.Description,
                    Quantity = l.Quantity,
                    UnitCode = l.UnitCode,
                    NetUnitPrice = l.NetUnitPrice,
                    LineNetAmount = l.LineNetAmount,
                    TaxCategory = l.TaxCategory,
                    VatRatePercent = l.VatRatePercent,
                })],
            BreakdownRows = [.. rows.Select(b => new InvoicePdfModel.BreakdownRow
            {
                TaxCategory = b.Category,
                VatRatePercent = b.RatePercent,
                TaxableBase = b.TaxableBase,
                TaxAmount = b.TaxAmount,
                ExemptionReasonCode = b.ExemptionCode,
                ExemptionReasonText = b.ExemptionText,
            })],
            TotalNet = totalNet,
            TotalTax = totalTax,
            TotalGross = totalGross,
            AmountDue = totalGross,
            IsKleinunternehmer = profile.IsKleinunternehmer,
            ReverseCharge = doc.Lines.Any(l => l.TaxCategory == TaxCategory.AE),
        };
    }
}
