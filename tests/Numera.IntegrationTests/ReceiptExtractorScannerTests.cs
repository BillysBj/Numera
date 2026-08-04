using System.Text;

using Numera.Modules.Sales.Belege;

using Xunit;

namespace Numera.IntegrationTests;

public sealed class ReceiptExtractorScannerTests
{
    [Fact]
    public async Task Stub_extractor_returns_empty_manual_entry_proposal()
    {
        var extractor = new StubReceiptExtractor();

        var extraction = await extractor.ExtractAsync(
            [1, 2, 3],
            "application/pdf",
            CancellationToken.None);

        Assert.Null(extraction.SupplierName);
        Assert.Null(extraction.SupplierVatId);
        Assert.Null(extraction.InvoiceNumber);
        Assert.Null(extraction.InvoiceDate);
        Assert.Null(extraction.NetAmount);
        Assert.Null(extraction.VatAmount);
        Assert.Null(extraction.GrossAmount);
        Assert.Null(extraction.VatRatePercent);
        Assert.Null(extraction.Currency);
    }

    [Fact]
    public async Task Noop_scanner_returns_clean_for_benign_bytes()
    {
        var scanner = new NoopAttachmentScanner();

        var result = await scanner.ScanAsync(
            Encoding.UTF8.GetBytes("benign receipt content"),
            "receipt.pdf",
            CancellationToken.None);

        Assert.Equal(ScanVerdict.Clean, result.Verdict);
        Assert.Null(result.Signature);
    }

    [Fact]
    public async Task Noop_scanner_reports_Eicar_test_payload_as_infected()
    {
        const string eicar =
            "X5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*";
        var scanner = new NoopAttachmentScanner();

        var result = await scanner.ScanAsync(
            Encoding.ASCII.GetBytes(eicar),
            "eicar.com",
            CancellationToken.None);

        Assert.Equal(ScanVerdict.Infected, result.Verdict);
        Assert.Equal("EICAR-Test-Signature", result.Signature);
    }
}
