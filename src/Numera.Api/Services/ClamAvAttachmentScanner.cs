using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;

using Microsoft.Extensions.Options;

using Numera.Modules.Sales.Belege;

namespace Numera.Api.Services;

/// <summary>Configuration for the ClamAV TCP sidecar.</summary>
public sealed class ClamAvOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "ClamAv";

    /// <summary>The ClamAV daemon host. A blank host disables the adapter.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>The ClamAV daemon TCP port.</summary>
    public int Port { get; set; } = 3310;

    /// <summary>Maximum time for the complete scan exchange.</summary>
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>Scans attachments using ClamAV's length-prefixed INSTREAM TCP protocol.</summary>
public sealed class ClamAvAttachmentScanner : IAttachmentScanner
{
    private const int ChunkSize = 8192;
    private const int MaximumResponseBytes = 64 * 1024;

    private static readonly byte[] InstreamCommand = Encoding.ASCII.GetBytes("zINSTREAM\0");

    private readonly ClamAvOptions _options;

    /// <summary>Creates a scanner from the bound ClamAV configuration.</summary>
    public ClamAvAttachmentScanner(IOptions<ClamAvOptions> options)
    {
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<AttachmentScanResult> ScanAsync(
        byte[] bytes,
        string fileName,
        CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds)));

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(_options.Host, _options.Port, timeout.Token);

            await using var stream = client.GetStream();
            await stream.WriteAsync(InstreamCommand, timeout.Token);

            var lengthPrefix = new byte[sizeof(int)];
            for (var offset = 0; offset < bytes.Length; offset += ChunkSize)
            {
                var length = Math.Min(ChunkSize, bytes.Length - offset);
                BinaryPrimitives.WriteInt32BigEndian(lengthPrefix, length);
                await stream.WriteAsync(lengthPrefix, timeout.Token);
                await stream.WriteAsync(bytes.AsMemory(offset, length), timeout.Token);
            }

            Array.Clear(lengthPrefix);
            await stream.WriteAsync(lengthPrefix, timeout.Token);
            await stream.FlushAsync(timeout.Token);

            var response = await ReadResponseAsync(stream, timeout.Token);
            return ParseResponse(response);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new AttachmentScanResult(ScanVerdict.Error, null);
        }
        catch (SocketException)
        {
            return new AttachmentScanResult(ScanVerdict.Error, null);
        }
        catch (IOException)
        {
            return new AttachmentScanResult(ScanVerdict.Error, null);
        }
    }

    private static async Task<string> ReadResponseAsync(
        NetworkStream stream,
        CancellationToken ct)
    {
        var buffer = new byte[1024];
        using var response = new MemoryStream();

        while (response.Length < MaximumResponseBytes)
        {
            var read = await stream.ReadAsync(buffer, ct);
            if (read == 0)
            {
                break;
            }

            var terminator = buffer.AsSpan(0, read).IndexOf((byte)0);
            var count = terminator >= 0 ? terminator : read;
            response.Write(buffer, 0, count);

            if (terminator >= 0)
            {
                break;
            }
        }

        return Encoding.ASCII.GetString(response.ToArray()).TrimEnd('\r', '\n');
    }

    private static AttachmentScanResult ParseResponse(string response)
    {
        if (string.Equals(response, "stream: OK", StringComparison.Ordinal))
        {
            return new AttachmentScanResult(ScanVerdict.Clean, null);
        }

        const string foundSuffix = " FOUND";
        if (response.EndsWith(foundSuffix, StringComparison.Ordinal))
        {
            var colon = response.IndexOf(':');
            var signatureStart = colon >= 0 ? colon + 1 : 0;
            var signatureLength = response.Length - foundSuffix.Length - signatureStart;
            var signature = response.Substring(signatureStart, signatureLength).Trim();

            return new AttachmentScanResult(
                ScanVerdict.Infected,
                string.IsNullOrWhiteSpace(signature) ? null : signature);
        }

        return new AttachmentScanResult(ScanVerdict.Error, null);
    }
}
