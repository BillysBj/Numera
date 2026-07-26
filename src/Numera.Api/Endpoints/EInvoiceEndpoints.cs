using System.Text.Json;

using Hangfire;

using Microsoft.EntityFrameworkCore;

using Numera.Api.Contracts;
using Numera.Api.Jobs;
using Numera.Api.Services;
using Numera.Modules.Sales;
using Numera.Modules.Sales.EInvoice;
using Numera.Modules.Sales.Email;
using Numera.Platform.Audit;
using Numera.Platform.Db;
using Numera.Platform.Tenancy;

namespace Numera.Api.Endpoints;

/// <summary>
/// The outbound e-invoice HTTP surface (Phase-5 EINV-01/EINV-03): download a finalized invoice's
/// XRechnung as UBL or CII (render-if-absent), and the AUTHORITATIVE stage-2 send gate that
/// refuses to dispatch the e-invoice unless its stored KoSIT verdict is
/// <see cref="EInvoiceValidationStatus.Accepted"/>.
/// </summary>
/// <remarks>
/// Mapped after <c>MapSalesDocumentEndpoints</c> so both share the <c>/api/documents/{id}</c>
/// space. The send gate reuses the 04-04 <see cref="DocumentEmail"/> + <see cref="SendDocumentEmailJob"/>
/// seam (enqueue-after-commit + audit), enqueuing the send job with <c>asEInvoice: true</c> so it
/// attaches the stored XRechnung XML instead of the PDF.
/// </remarks>
public static class EInvoiceEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Maps <c>/api/documents/{id}/xrechnung</c> download + <c>/send-einvoice</c> gate.</summary>
    public static IEndpointRouteBuilder MapEInvoiceEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/documents").RequireAuthorization();

        // GET /api/documents/{id}/xrechnung?syntax=ubl|cii — download the finalized invoice's
        // XRechnung (render-if-absent from the frozen snapshot). 404 unknown; 409 Draft.
        g.MapGet("/{id:guid}/xrechnung", async (
            Guid id,
            EInvoiceService einvoice,
            CancellationToken ct,
            string? syntax = null) =>
        {
            var format = ParseSyntax(syntax);
            var result = await einvoice.GetOrGenerate(id, format, ct).ConfigureAwait(false);
            return result.Result switch
            {
                EInvoiceService.Outcome.NotFound => Results.NotFound(),
                EInvoiceService.Outcome.NotFinalized => Results.Problem(
                    title: "Document is not finalized",
                    detail: "Only a finalized invoice has an XRechnung; this document is still a Draft.",
                    statusCode: StatusCodes.Status409Conflict),
                _ => Results.File(result.Xml!, "application/xml", result.FileName),
            };
        });

        // POST /api/documents/{id}/send-einvoice — the AUTHORITATIVE stage-2 send gate. Refuses to
        // dispatch unless the stored XRechnung (UBL) was Accepted by KoSIT (errors block the
        // Versand, explained). On Accepted: records a Queued document_email + enqueues the send job
        // (asEInvoice) after commit, which attaches the stored XRechnung XML. 404 unknown; 409 Draft
        // or validation-not-performed; 422 rejected content; 422 no recipient e-mail.
        g.MapPost("/{id:guid}/send-einvoice", async (
            Guid id,
            SendDocumentEmailRequest? req,
            NumeraDbContext db,
            EInvoiceService einvoice,
            IAuditWriter audit,
            ICurrentTenant tenant,
            IBackgroundJobClient jobs,
            CancellationToken ct) =>
        {
            var doc = await db.Set<SalesDocument>()
                .AsNoTracking()
                .Select(d => new { d.Id, d.Status, d.DocumentType, d.DocumentNumber, d.RecipientSnapshot })
                .FirstOrDefaultAsync(d => d.Id == id, ct)
                .ConfigureAwait(false);
            if (doc is null)
            {
                return Results.NotFound();
            }

            if (doc.Status == DocumentStatus.Draft)
            {
                return Results.Problem(
                    title: "Document is not finalized",
                    detail: "Only a finalized invoice can be sent as an e-invoice; this document is still a Draft.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            // THE gate: the stored (or on-demand generated) UBL artifact MUST be Accepted.
            const EInvoiceFormat format = EInvoiceFormat.XRechnungUbl;
            var artifact = await einvoice.GetArtifactAsync(id, format, ct).ConfigureAwait(false)
                ?? await einvoice.GenerateAndValidate(id, format, ct).ConfigureAwait(false);

            if (artifact.ValidationStatus == EInvoiceValidationStatus.Rejected)
            {
                return Results.ValidationProblem(
                    EInvoiceGate.ToProblemDictionary(EInvoiceGate.ReadFindings(artifact.ValidationReport)),
                    statusCode: StatusCodes.Status422UnprocessableEntity,
                    title: "Versand blockiert: die E-Rechnung wurde abgelehnt");
            }

            if (artifact.ValidationStatus != EInvoiceValidationStatus.Accepted)
            {
                // Unavailable — the check could not be performed; do not dispatch an unverified e-invoice.
                return Results.Problem(
                    title: "Versand blockiert: die E-Rechnung wurde nicht validiert",
                    detail: "Die KoSIT-Validierung konnte nicht durchgeführt werden. Bitte später erneut versuchen.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            var toAddress = string.IsNullOrWhiteSpace(req?.ToAddress)
                ? ResolveRecipientEmail(doc.RecipientSnapshot)
                : req!.ToAddress!.Trim();
            if (string.IsNullOrWhiteSpace(toAddress))
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]>
                    {
                        ["toAddress"] = ["No recipient e-mail: supply toAddress or set the customer's e-mail."],
                    },
                    statusCode: StatusCodes.Status422UnprocessableEntity,
                    title: "No recipient e-mail address");
            }

            var language = string.Equals(req?.Language, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "de";
            var subject = DocumentEmailTemplates
                .Build(language, doc.DocumentNumber ?? doc.Id.ToString())
                .Subject;

            var tenantId = tenant.TenantId!.Value;
            var email = new DocumentEmail
            {
                TenantId = tenantId,
                DocumentId = doc.Id,
                ToAddress = toAddress,
                Subject = subject,
                Status = EmailStatus.Queued,
                CreatedAt = DateTimeOffset.UtcNow,
            };

            db.Add(email);
            await audit.RecordAsync(
                new SalesDocumentAuditEvent(
                    "sales_document.einvoice_send_queued", doc.Id, Before: null,
                    After: JsonSerializer.Serialize(new { email.Id, email.ToAddress, language, format = format.ToString() }, Json)),
                ct).ConfigureAwait(false);

            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            // Enqueue AFTER the row commits; the job attaches the stored XRechnung XML (asEInvoice).
            jobs.Enqueue<SendDocumentEmailJob>(
                j => j.RunAsync(tenantId, email.Id, language, true, CancellationToken.None));

            return Results.Accepted(
                $"/api/documents/{doc.Id}/send-einvoice", new { id = email.Id, status = email.Status });
        });

        return app;
    }

    // Maps the ?syntax query to a format (default UBL). CII on an explicit "cii".
    private static EInvoiceFormat ParseSyntax(string? syntax) =>
        string.Equals(syntax, "cii", StringComparison.OrdinalIgnoreCase)
            ? EInvoiceFormat.XRechnungCii
            : EInvoiceFormat.XRechnungUbl;

    // Reads the frozen recipient e-mail from the RecipientSnapshot jsonb (camelCase; PascalCase
    // tolerated). Returns null when absent so the endpoint can 422 rather than enqueue.
    private static string? ResolveRecipientEmail(string? recipientSnapshot)
    {
        if (string.IsNullOrWhiteSpace(recipientSnapshot))
        {
            return null;
        }

        try
        {
            using var json = JsonDocument.Parse(recipientSnapshot);
            var root = json.RootElement;
            if ((root.TryGetProperty("email", out var e) || root.TryGetProperty("Email", out e))
                && e.ValueKind == JsonValueKind.String)
            {
                var value = e.GetString();
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }
        catch (JsonException)
        {
            // A malformed snapshot is treated as "no recipient e-mail" (endpoint 422s cleanly).
        }

        return null;
    }
}

/// <summary>
/// Shared helpers that turn KoSIT findings (from a pre-finalize dry-run or a stored artifact
/// report) into an explained ASP.NET <c>ValidationProblem</c> dictionary — the DE/EN
/// "verständlich erklärt" surface behind both gate stages (EINV-03).
/// </summary>
internal static class EInvoiceGate
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Builds a field-keyed problem dictionary from the findings: keyed by rule id (or a generic
    /// "E-Rechnung"), each value carrying the German (authoritative) + English explanation, or the
    /// raw KoSIT message when no explanation is mapped. Prefers error-severity findings.
    /// </summary>
    public static Dictionary<string, string[]> ToProblemDictionary(IReadOnlyList<EInvoiceFinding> findings)
    {
        var relevant = findings.Where(f => IsError(f.Severity)).ToList();
        if (relevant.Count == 0)
        {
            relevant = findings.ToList();
        }

        if (relevant.Count == 0)
        {
            return new Dictionary<string, string[]>
            {
                ["E-Rechnung"] = ["Die E-Rechnung wurde vom KoSIT-Validator abgelehnt."],
            };
        }

        var grouped = new Dictionary<string, List<string>>();
        foreach (var f in relevant)
        {
            var key = string.IsNullOrWhiteSpace(f.RuleId) ? "E-Rechnung" : f.RuleId!;
            if (!grouped.TryGetValue(key, out var list))
            {
                list = [];
                grouped[key] = list;
            }

            list.Add(string.IsNullOrWhiteSpace(f.ExplanationDe) ? f.Message : f.ExplanationDe!);
            if (!string.IsNullOrWhiteSpace(f.ExplanationEn))
            {
                list.Add(f.ExplanationEn!);
            }
        }

        return grouped.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
    }

    /// <summary>
    /// Deserializes the stored jsonb validation report (<c>{ status, findings: [...] }</c>) back
    /// into its findings; an empty list when the report is null/malformed.
    /// </summary>
    public static IReadOnlyList<EInvoiceFinding> ReadFindings(string? reportJson)
    {
        if (string.IsNullOrWhiteSpace(reportJson))
        {
            return [];
        }

        try
        {
            using var doc = JsonDocument.Parse(reportJson);
            if (doc.RootElement.TryGetProperty("findings", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                return JsonSerializer.Deserialize<List<EInvoiceFinding>>(arr.GetRawText(), Json) ?? [];
            }
        }
        catch (JsonException)
        {
            // Malformed report → no structured findings (the generic reject message still stands).
        }

        return [];
    }

    private static bool IsError(string severity) =>
        severity.Contains("error", StringComparison.OrdinalIgnoreCase)
        || severity.Contains("fatal", StringComparison.OrdinalIgnoreCase);
}
