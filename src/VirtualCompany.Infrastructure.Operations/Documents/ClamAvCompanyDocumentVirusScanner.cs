using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Documents;

namespace VirtualCompany.Infrastructure.Documents;

public sealed class CompanyDocumentVirusScannerOptions
{
    public const string SectionName = "CompanyDocumentVirusScanner";
    public bool Enabled { get; set; }
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 3310;
    public int TimeoutSeconds { get; set; } = 30;
    public int ChunkBytes { get; set; } = 64 * 1024;
}

public sealed class ClamAvCompanyDocumentVirusScanner(ICompanyDocumentStorage storage, IOptions<CompanyDocumentVirusScannerOptions> configured) : ICompanyDocumentVirusScanner
{
    private readonly CompanyDocumentVirusScannerOptions _options = configured.Value;

    public async Task<CompanyDocumentVirusScanResult> ScanAsync(CompanyDocumentVirusScanRequest request, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
            return CompanyDocumentVirusScanResult.Error("clamav", null, "virus_scanner_unavailable", "ClamAV scanning is not configured.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 1, 300)));
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(_options.Host, _options.Port, timeout.Token);
            await using var network = client.GetStream();
            await network.WriteAsync(Encoding.ASCII.GetBytes("zINSTREAM\0"), timeout.Token);
            await using var input = await storage.OpenReadAsync(request.StorageKey, timeout.Token);
            var buffer = new byte[Math.Clamp(_options.ChunkBytes, 4096, 1024 * 1024)];
            var header = new byte[4];
            while (true)
            {
                var read = await input.ReadAsync(buffer, timeout.Token);
                if (read == 0) break;
                BinaryPrimitives.WriteInt32BigEndian(header, read);
                await network.WriteAsync(header, timeout.Token);
                await network.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
            }
            Array.Clear(header);
            await network.WriteAsync(header, timeout.Token);
            await network.FlushAsync(timeout.Token);

            using var reader = new StreamReader(network, Encoding.UTF8, false, leaveOpen: true);
            var response = await reader.ReadLineAsync(timeout.Token) ?? string.Empty;
            if (response.EndsWith("OK", StringComparison.OrdinalIgnoreCase))
                return new CompanyDocumentVirusScanResult(CompanyDocumentVirusScanOutcome.Clean, "clamav", null, DateTime.UtcNow);
            if (response.Contains("FOUND", StringComparison.OrdinalIgnoreCase))
                return CompanyDocumentVirusScanResult.Blocked("clamav", null, "malware_detected", "Malware was detected and the document was blocked.");
            return CompanyDocumentVirusScanResult.Error("clamav", null, "virus_scan_error", "ClamAV could not complete the scan.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return CompanyDocumentVirusScanResult.Error("clamav", null, "virus_scan_timeout", "ClamAV scanning timed out.");
        }
        catch (SocketException)
        {
            return CompanyDocumentVirusScanResult.Error("clamav", null, "virus_scanner_unavailable", "ClamAV is unavailable.");
        }
    }
}
