using System.Text.RegularExpressions;

using Microsoft.EntityFrameworkCore;

using Numera.Platform.Db;

namespace Numera.Modules.Banking.Reconciliation;

/// <summary>
/// Produces tenant-scoped receivable-match proposals without booking or mutating anything.
/// </summary>
public sealed partial class ReconciliationScorer(NumeraDbContext db)
{
    private const int OpenItemStatusOpen = 0;
    private const int OpenItemStatusPartiallyPaid = 1;

    private const decimal ReferenceWeight = 0.50m;
    private const decimal ExactAmountWeight = 0.35m;
    private const decimal PartialAmountWeight = 0.20m;
    private const decimal IbanWeight = 0.15m;
    private const decimal NameWeight = 0.08m;

    /// <summary>Scores candidate open receivables for one incoming bank transaction.</summary>
    public async Task<IReadOnlyList<MatchCandidate>> ScoreAsync(
        BankTransaction tx,
        CancellationToken ct = default)
    {
        if (tx.Amount <= 0m)
        {
            return [];
        }

        var references = ExtractReferences(tx.Purpose, tx.EndToEndId);
        var openItems = references.Length == 0
            ? await db.Database.SqlQuery<OpenItemRow>(
                $"""
                SELECT id AS "Id",
                       document_number AS "DocumentNumber",
                       open_amount AS "OpenAmount",
                       partner_id AS "PartnerId"
                FROM open_items
                WHERE status IN ({OpenItemStatusOpen}, {OpenItemStatusPartiallyPaid})
                  AND open_amount = {tx.Amount}
                """).ToListAsync(ct)
            : await db.Database.SqlQuery<OpenItemRow>(
                $"""
                SELECT id AS "Id",
                       document_number AS "DocumentNumber",
                       open_amount AS "OpenAmount",
                       partner_id AS "PartnerId"
                FROM open_items
                WHERE status IN ({OpenItemStatusOpen}, {OpenItemStatusPartiallyPaid})
                  AND (open_amount = {tx.Amount} OR upper(document_number) = ANY({references}))
                """).ToListAsync(ct);

        if (openItems.Count == 0)
        {
            return [];
        }

        var partnerIds = openItems
            .Where(item => item.PartnerId.HasValue)
            .Select(item => item.PartnerId!.Value)
            .Distinct()
            .ToArray();

        var partners = partnerIds.Length == 0
            ? []
            : await db.Database.SqlQuery<BusinessPartnerRow>(
                $"""
                SELECT id AS "Id", iban AS "Iban", name AS "Name"
                FROM partners
                WHERE id = ANY({partnerIds}) AND archived_at IS NULL
                """).ToListAsync(ct);
        var partnersById = partners.ToDictionary(partner => partner.Id);
        var referenceSet = references.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return openItems
            .Select(item => Score(item, tx, referenceSet, partnersById))
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.DocumentNumber, StringComparer.Ordinal)
            .ToArray();
    }

    private static MatchCandidate Score(
        OpenItemRow item,
        BankTransaction tx,
        IReadOnlySet<string> references,
        IReadOnlyDictionary<Guid, BusinessPartnerRow> partners)
    {
        var score = 0m;
        var reasons = new List<string>();
        var exactReference = references.Contains(item.DocumentNumber);
        var exactAmount = tx.Amount == item.OpenAmount;

        if (exactReference)
        {
            score += ReferenceWeight;
            reasons.Add("Rechnungsnummer im Verwendungszweck oder End-to-End-Verweis");
        }

        if (exactAmount)
        {
            score += ExactAmountWeight;
            reasons.Add("Betrag exakt");
        }
        else if (tx.Amount < item.OpenAmount)
        {
            score += PartialAmountWeight;
            reasons.Add("Teilbetrag möglich");
        }

        if (item.PartnerId is { } partnerId && partners.TryGetValue(partnerId, out var partner))
        {
            if (SameIban(tx.CounterpartyIban, partner.Iban))
            {
                score += IbanWeight;
                reasons.Add("IBAN stimmt überein");
            }
            else if (FuzzyNameMatch(tx.CounterpartyName, partner.Name))
            {
                score += NameWeight;
                reasons.Add("Name der Gegenpartei stimmt weitgehend überein");
            }
        }

        return new MatchCandidate(
            item.Id,
            item.DocumentNumber,
            item.OpenAmount,
            Math.Min(tx.Amount, item.OpenAmount),
            Math.Min(score, 1m),
            exactAmount && exactReference ? MatchTier.High : MatchTier.Review,
            reasons);
    }

    private static string[] ExtractReferences(params string?[] sources)
    {
        var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                continue;
            }

            foreach (Match match in CandidateReferenceRegex().Matches(source))
            {
                references.Add(match.Value.Trim().ToUpperInvariant());
            }
        }

        return references.ToArray();
    }

    private static bool SameIban(string? transactionIban, string? partnerIban)
    {
        var left = NormalizeWhitespace(transactionIban);
        var right = NormalizeWhitespace(partnerIban);
        return left.Length > 0 && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static bool FuzzyNameMatch(string? counterpartyName, string? partnerName)
    {
        var left = NormalizeWhitespace(counterpartyName);
        var right = NormalizeWhitespace(partnerName);
        return left.Length > 0
            && right.Length > 0
            && (left.Contains(right, StringComparison.OrdinalIgnoreCase)
                || right.Contains(left, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeWhitespace(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : string.Concat(value.Where(character => !char.IsWhiteSpace(character)));

    [GeneratedRegex(
        @"(?<![\p{L}\p{N}])(?:RE(?:CHNUNG)?|RG|R|INV)\s*[-._/#:]?\s*\d[\p{L}\p{N}]*(?:[-._/#:]\p{L}?\d+)*(?![\p{L}\p{N}])|(?<!\d)\d{4,}(?!\d)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CandidateReferenceRegex();

    private sealed class OpenItemRow
    {
        public Guid Id { get; set; }

        public string DocumentNumber { get; set; } = string.Empty;

        public decimal OpenAmount { get; set; }

        public Guid? PartnerId { get; set; }
    }

    private sealed class BusinessPartnerRow
    {
        public Guid Id { get; set; }

        public string? Iban { get; set; }

        public string Name { get; set; } = string.Empty;
    }
}
