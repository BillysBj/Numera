using System.Text;

namespace Numera.Modules.Sales.Belege;

/// <summary>Provider-neutral malware scanning port for receipt attachments.</summary>
public interface IAttachmentScanner
{
    /// <summary>Scans an attachment before it can be archived or booked.</summary>
    Task<AttachmentScanResult> ScanAsync(
        byte[] bytes,
        string fileName,
        CancellationToken ct);
}

/// <summary>The scanner verdict and, for infected files, the detected malware signature.</summary>
public sealed record AttachmentScanResult(ScanVerdict Verdict, string? Signature);

/// <summary>Possible malware scanner outcomes. Error must be treated as fail-closed.</summary>
public enum ScanVerdict
{
    Clean,
    Infected,
    Error
}

/// <summary>
/// Dependency-free scanner double for local and integration-test use. It recognizes the
/// standard EICAR test payload so the infected path remains testable without live ClamAV.
/// </summary>
public sealed class NoopAttachmentScanner : IAttachmentScanner
{
    private const string EicarSignature =
        "X5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*";

    private static readonly byte[] EicarBytes = Encoding.ASCII.GetBytes(EicarSignature);

    /// <inheritdoc />
    public Task<AttachmentScanResult> ScanAsync(
        byte[] bytes,
        string fileName,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var result = bytes.AsSpan().IndexOf(EicarBytes) >= 0
            ? new AttachmentScanResult(ScanVerdict.Infected, "EICAR-Test-Signature")
            : new AttachmentScanResult(ScanVerdict.Clean, null);

        return Task.FromResult(result);
    }
}
