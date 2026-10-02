using System.Net.Sockets;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Numera.Api.Services;
using Numera.Modules.Sales.EInvoice;
using Numera.Modules.Sales.Pdf;
using Numera.Platform.Money;

using Xunit;

namespace Numera.IntegrationTests;

/// <summary>
/// The LIVE-KoSIT conformance harness (Phase-5 EINV-02/EINV-03, RESEARCH Pitfall 2). Feeds the
/// REAL generated XRechnung — every VAT scenario, in BOTH UBL and CII — through the REAL
/// government-authoritative KoSIT validator sidecar and asserts it is <b>Accepted</b> with zero
/// error-severity findings. This is the ONLY coverage that proves the government validator accepts
/// Numera's ACTUAL output; it is distinct from the deterministic fake-validator suite
/// (<see cref="EInvoiceOutboundTests"/>) and the 05-02 parser golden test.
/// </summary>
/// <remarks>
/// <para>
/// Trait-gated <c>Category=KositConformance</c> so the fast deterministic Postgres unit gate is
/// unaffected (run it explicitly: <c>dotnet test --filter Category=KositConformance</c>). It needs
/// the Java KoSIT sidecar: <see cref="KositFactAttribute"/>/<see cref="KositTheoryAttribute"/> probe
/// the configured <see cref="KoSitProbe.BaseUrl"/> at discovery and SKIP with a clear message when
/// it is unreachable — NEVER a silent pass and NEVER a hard fail on missing infra.
/// </para>
/// <para>
/// It drives the REAL <see cref="KoSitValidatorClient"/> against the docker-compose
/// <c>kosit-validator</c> sidecar. Set <c>KOSIT_FIXTURE_DIR</c> to capture the real accept-reports
/// per scenario × syntax (committed under <c>Fixtures/KoSit/</c> as regression fixtures +
/// provenance of the pinned config version).
/// </para>
/// </remarks>
[Trait("Category", "KositConformance")]
public sealed class KoSitConformanceTests
{
    public static IEnumerable<object[]> Scenarios() =>
    [
        ["S", "ubl"], ["S", "cii"],
        ["discount", "ubl"], ["discount", "cii"],
        ["discount-rounding", "ubl"], ["discount-rounding", "cii"],
        ["discount-precision", "ubl"], ["discount-precision", "cii"],
        ["AE", "ubl"], ["AE", "cii"],
        ["E", "ubl"], ["E", "cii"],
        ["K", "ubl"], ["K", "cii"],
        ["G", "ubl"], ["G", "cii"],
        ["Z", "ubl"], ["Z", "cii"],
        ["prepay", "ubl"], ["prepay", "cii"],
        ["currency", "ubl"], ["currency", "cii"],
    ];

    [KositTheory]
    [MemberData(nameof(Scenarios))]
    public async Task Generated_xrechnung_is_accepted_by_the_live_kosit_validator(string scenario, string syntax)
    {
        var model = BuildScenario(scenario);
        var xml = syntax == "cii"
            ? XRechnungGenerator.GenerateCii(model)
            : XRechnungGenerator.GenerateUbl(model);

        var dumpDir = Environment.GetEnvironmentVariable("KOSIT_DUMP_DIR");
        if (!string.IsNullOrWhiteSpace(dumpDir))
        {
            Directory.CreateDirectory(dumpDir);
            await File.WriteAllBytesAsync(Path.Combine(dumpDir, $"{scenario}-{syntax}.xml"), xml);
        }

        var client = KoSitProbe.CreateClient();
        var result = await client.ValidateAsync(xml, CancellationToken.None);

        // The generated XRechnung MUST be Accepted by the real validator with no error-severity findings.
        var errors = result.Findings.Where(f => IsError(f.Severity)).ToList();
        Assert.True(
            result.Status == EInvoiceValidationStatus.Accepted && errors.Count == 0,
            $"Scenario {scenario}/{syntax}: expected Accepted with 0 errors but got {result.Status} with "
            + $"{errors.Count} error findings:\n" + string.Join("\n", errors.Select(e => $"  [{e.RuleId}] {e.Message}")));

        CaptureReport(scenario, syntax, result.RawReport);
    }

    // ---------------------------------------------------------------- scenario builders

    [KositTheory]
    [MemberData(nameof(Scenarios))]
    public async Task Invoice_date_supply_fallback_is_accepted_without_findings(string scenario, string syntax)
    {
        var model = BuildScenario(scenario) with { ServiceDate = null, ServicePeriodEnd = null };
        var xml = syntax == "cii"
            ? XRechnungGenerator.GenerateCii(model)
            : XRechnungGenerator.GenerateUbl(model);

        var result = await KoSitProbe.CreateClient().ValidateAsync(xml, CancellationToken.None);

        Assert.True(result.Status == EInvoiceValidationStatus.Accepted && result.Findings.Count == 0,
            $"Supply-date fallback {scenario}/{syntax}: {result.Status}\n"
            + string.Join("\n", result.Findings.Select(f => $"[{f.Severity}/{f.RuleId}] {f.Message}")));
    }

    private static InvoicePdfModel BuildScenario(string scenario) => scenario switch
    {
        "discount" => Model(
            lines:
            [
                Line(1, "Beratung", 3m, "HUR", 100m, 262.5m, TaxCategory.S, 19m) with { DiscountPercent = 12.5m },
                Line(2, "Buch", 10m, "C62", 10m, 90m, TaxCategory.S, 7m) with { DiscountPercent = 10m },
            ],
            rows: [Row(TaxCategory.S, 19m, 262.5m, 49.88m), Row(TaxCategory.S, 7m, 90m, 6.3m)],
            net: 352.5m, tax: 56.18m, gross: 408.68m),
        "discount-precision" => Model(
            lines: [Line(1, "Service", 1m, "HUR", 1000m, 876.5432m, TaxCategory.S, 19m) with { DiscountPercent = 12.345678m }],
            rows: [Row(TaxCategory.S, 19m, 876.54m, 166.54m)],
            net: 876.54m, tax: 166.54m, gross: 1043.08m),
        "discount-rounding" => Model(
            lines: Enumerable.Range(1, 3).Select(i =>
                Line(i, "Material", 1m, "C62", 19.99m, 17.4913m, TaxCategory.S, 19m) with { DiscountPercent = 12.5m }).ToArray(),
            rows: [Row(TaxCategory.S, 19m, 52.47m, 9.97m)],
            net: 52.47m, tax: 9.97m, gross: 62.44m),

        // Standard rate: two taxed buckets (19 % + 7 %).
        "S" => Model(
            lines:
            [
                Line(1, "Beratungsleistung", 3m, "HUR", 100m, 300m, TaxCategory.S, 19m),
                Line(2, "Fachbuch", 10m, "C62", 10m, 100m, TaxCategory.S, 7m),
            ],
            rows:
            [
                Row(TaxCategory.S, 19m, 300m, 57m),
                Row(TaxCategory.S, 7m, 100m, 7m),
            ],
            net: 400m, tax: 64m, gross: 464m),

        // Reverse charge §13b: buyer VAT id required; exemption code + Pflichttext.
        "AE" => Model(
            lines: [Line(1, "Bauleistung §13b", 1m, "C62", 500m, 500m, TaxCategory.AE, 0m)],
            rows: [Row(TaxCategory.AE, 0m, 500m, 0m, "VATEX-EU-AE", "Steuerschuldnerschaft des Leistungsempfängers (§13b UStG)")],
            net: 500m, tax: 0m, gross: 500m),

        // §19 Kleinunternehmer: category E exempt, document-wide.
        "E" => Model(
            lines: [Line(1, "Dienstleistung", 1m, "C62", 200m, 200m, TaxCategory.E, 0m)],
            rows: [Row(TaxCategory.E, 0m, 200m, 0m, "VATEX-EU-D", "Kein Ausweis von Umsatzsteuer, da Kleinunternehmer gemäß §19 UStG")],
            net: 200m, tax: 0m, gross: 200m,
            isKleinunternehmer: true),

        // Intra-community supply: buyer VAT id required; exemption code + reason.
        "K" => Model(
            lines: [Line(1, "Warenlieferung EU", 1m, "C62", 250m, 250m, TaxCategory.K, 0m)],
            rows: [Row(TaxCategory.K, 0m, 250m, 0m, "VATEX-EU-IC", "Innergemeinschaftliche Lieferung")],
            net: 250m, tax: 0m, gross: 250m),

        // Free export (outside EU): exemption code + reason.
        "G" => Model(
            lines: [Line(1, "Ausfuhrlieferung", 1m, "C62", 250m, 250m, TaxCategory.G, 0m)],
            rows: [Row(TaxCategory.G, 0m, 250m, 0m, "VATEX-EU-G", "Ausfuhrlieferung")],
            net: 250m, tax: 0m, gross: 250m),

        // Zero-rated.
        "Z" => Model(
            lines: [Line(1, "Nullsatz-Position", 1m, "C62", 250m, 250m, TaxCategory.Z, 0m)],
            rows: [Row(TaxCategory.Z, 0m, 250m, 0m)],
            net: 250m, tax: 0m, gross: 250m),

        "prepay" => Model(
            lines: [Line(1, "Gesamtprojekt", 1m, "C62", 1000m, 1000m, TaxCategory.S, 19m)],
            rows: [Row(TaxCategory.S, 19m, 1000m, 190m)],
            net: 1000m,
            tax: 190m,
            gross: 1190m,
            prepayments:
            [
                new InvoicePdfModel.PrepaymentRow
                {
                    LineNumber = 1,
                    AbschlagNumber = "AR-2026-00001",
                    AbschlagDate = new DateOnly(2026, 6, 1),
                    NetAmount = 300m,
                    VatAmount = 57m,
                    GrossAmount = 357m,
                },
            ],
            amountDue: 833m),

        "currency" => Model(
            lines: [Line(1, "Consulting", 1m, "HUR", 100m, 100m, TaxCategory.S, 19m)],
            rows: [Row(TaxCategory.S, 19m, 100m, 19m)],
            net: 100m,
            tax: 19m,
            gross: 119m,
            currency: "USD",
            exchangeRate: 1.185m,
            exchangeRateDate: new DateOnly(2026, 7, 10),
            totalTaxEur: 16.03m),

        _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown VAT scenario."),
    };

    private static InvoicePdfModel.LineRow Line(
        int number, string name, decimal qty, string unit,
        decimal unitPrice, decimal lineNet, TaxCategory category, decimal rate) => new()
        {
            LineNumber = number,
            Name = name,
            Quantity = qty,
            UnitCode = unit,
            NetUnitPrice = unitPrice,
            LineNetAmount = lineNet,
            TaxCategory = category,
            VatRatePercent = rate,
        };

    private static InvoicePdfModel.BreakdownRow Row(
        TaxCategory category, decimal rate, decimal @base, decimal tax,
        string? exemptionCode = null, string? exemptionText = null) => new()
        {
            TaxCategory = category,
            VatRatePercent = rate,
            TaxableBase = @base,
            TaxAmount = tax,
            ExemptionReasonCode = exemptionCode,
            ExemptionReasonText = exemptionText,
        };

    // A §14-complete, CIUS-mandatory-field-carrying model (BT-10 buyer reference, seller+buyer
    // electronic addresses scheme EM, BG-6 seller contact, BT-81 payment means + IBAN, buyer VAT id).
    private static InvoicePdfModel Model(
        IReadOnlyList<InvoicePdfModel.LineRow> lines,
        IReadOnlyList<InvoicePdfModel.BreakdownRow> rows,
        decimal net, decimal tax, decimal gross,
        bool isKleinunternehmer = false,
        IReadOnlyList<InvoicePdfModel.PrepaymentRow>? prepayments = null,
        decimal? amountDue = null,
        string currency = "EUR",
        decimal? exchangeRate = null,
        DateOnly? exchangeRateDate = null,
        decimal? totalTaxEur = null) => new()
        {
            DocumentNumber = "RE-2026-00001",
            DocumentDate = new DateOnly(2026, 7, 13),
            ServiceDate = new DateOnly(2026, 7, 1),
            DueDate = new DateOnly(2026, 7, 27),
            Currency = currency,
            ExchangeRate = exchangeRate,
            ExchangeRateDate = exchangeRateDate,
            TotalTaxEur = totalTaxEur,
            BuyerReference = "LW-991-2026",
            Notes = "Vielen Dank für Ihren Auftrag.",
            IsKleinunternehmer = isKleinunternehmer,
            ReverseCharge = lines.Any(l => l.TaxCategory == TaxCategory.AE),
            Issuer = new InvoicePdfModel.IssuerBlock
            {
                LegalName = "Muster Handels GmbH",
                Address = new InvoicePdfModel.AddressBlock
                {
                    Street = "Hauptstraße 1",
                    PostalCode = "10115",
                    City = "Berlin",
                    CountryCode = "DE",
                },
                VatId = "DE123456789",
                IsKleinunternehmer = isKleinunternehmer,
                Iban = "DE02120300000000202051",
                Bic = "BYLADEM1001",
                BankName = "Musterbank Berlin",
                ManagingDirector = "Max Muster",
                ContactEmail = "rechnung@muster.de",
                ContactPhone = "+49 30 1234560",
            },
            Recipient = new InvoicePdfModel.RecipientBlock
            {
                Name = "Kunde AG",
                BillingAddress = new InvoicePdfModel.AddressBlock
                {
                    Street = "Kundenweg 5",
                    PostalCode = "80331",
                    City = "München",
                    CountryCode = "DE",
                },
                VatId = "DE987654321",
                Email = "einkauf@kunde-ag.de",
            },
            Lines = lines,
            BreakdownRows = rows,
            Prepayments = prepayments ?? [],
            TotalNet = net,
            TotalTax = tax,
            TotalGross = gross,
            AmountDue = amountDue ?? gross,
        };

    private static bool IsError(string severity) =>
        severity.Contains("error", StringComparison.OrdinalIgnoreCase)
        || severity.Contains("fatal", StringComparison.OrdinalIgnoreCase);

    // Writes the captured REAL accept-report to KOSIT_FIXTURE_DIR (when set) so the committed
    // Fixtures/KoSit/ set is the real, re-runnable regression + provenance of the pinned config.
    private static void CaptureReport(string scenario, string syntax, string? rawReport)
    {
        var dir = Environment.GetEnvironmentVariable("KOSIT_FIXTURE_DIR");
        if (string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(rawReport))
        {
            return;
        }

        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, $"{scenario}-{syntax}-accept-report.xml"), rawReport);
    }
}

/// <summary>
/// Reachability probe + client factory for the live KoSIT validator sidecar. The
/// <see cref="KositFactAttribute"/>/<see cref="KositTheoryAttribute"/> use <see cref="IsReachable"/>
/// at discovery to skip cleanly when the daemon is absent.
/// </summary>
internal static class KoSitProbe
{
    /// <summary>The validator base URL (env <c>KOSIT_BASEURL</c>, default <c>http://localhost:8081</c>).</summary>
    public static string BaseUrl =>
        Environment.GetEnvironmentVariable("KOSIT_BASEURL") is { Length: > 0 } url ? url : "http://localhost:8081";

    /// <summary>A short synchronous TCP connect probe (host:port of <see cref="BaseUrl"/>).</summary>
    public static bool IsReachable()
    {
        try
        {
            var uri = new Uri(BaseUrl);
            using var tcp = new TcpClient();
            return tcp.ConnectAsync(uri.Host, uri.Port).Wait(TimeSpan.FromSeconds(2)) && tcp.Connected;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Builds the REAL typed KoSIT client pointed at <see cref="BaseUrl"/> (60s timeout).</summary>
    public static KoSitValidatorClient CreateClient()
    {
        var options = Options.Create(new EInvoiceValidationOptions
        {
            BaseUrl = BaseUrl,
            ValidationPath = "/",
            TimeoutSeconds = 60,
        });

        var http = new HttpClient { BaseAddress = new Uri(BaseUrl), Timeout = TimeSpan.FromSeconds(60) };
        return new KoSitValidatorClient(http, options, NullLogger<KoSitValidatorClient>.Instance);
    }
}

/// <summary>A <see cref="FactAttribute"/> that SKIPS (never fails) when the KoSIT sidecar is unreachable.</summary>
internal sealed class KositFactAttribute : FactAttribute
{
    public KositFactAttribute()
    {
        if (!KoSitProbe.IsReachable())
        {
            Skip = $"KoSIT validator sidecar not reachable at {KoSitProbe.BaseUrl} — skipping live conformance.";
        }
    }
}

/// <summary>A <see cref="TheoryAttribute"/> that SKIPS (never fails) when the KoSIT sidecar is unreachable.</summary>
internal sealed class KositTheoryAttribute : TheoryAttribute
{
    public KositTheoryAttribute()
    {
        if (!KoSitProbe.IsReachable())
        {
            Skip = $"KoSIT validator sidecar not reachable at {KoSitProbe.BaseUrl} — skipping live conformance.";
        }
    }
}
