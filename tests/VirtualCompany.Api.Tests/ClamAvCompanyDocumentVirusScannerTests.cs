using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Documents;
using VirtualCompany.Infrastructure.Documents;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class ClamAvCompanyDocumentVirusScannerTests
{
    [Theory]
    [InlineData("stream: OK", CompanyDocumentVirusScanOutcome.Clean)]
    [InlineData("stream: Test-Signature FOUND", CompanyDocumentVirusScanOutcome.Blocked)]
    [InlineData("stream: scan failed ERROR", CompanyDocumentVirusScanOutcome.Error)]
    [InlineData("stream: NOT OK", CompanyDocumentVirusScanOutcome.Error)]
    public async Task Uses_matching_newline_framing_and_fails_closed(string reply, CompanyDocumentVirusScanOutcome expected)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var server = ServeAsync();
        var scanner = new ClamAvCompanyDocumentVirusScanner(new MemoryStorage(), Options.Create(new CompanyDocumentVirusScannerOptions
        {
            Enabled = true, Host = "127.0.0.1", Port = ((IPEndPoint)listener.LocalEndpoint).Port
        }));
        var result = await scanner.ScanAsync(new(Guid.NewGuid(), Guid.NewGuid(), "test", null, "test.txt", "text/plain", 4,
            new Dictionary<string, System.Text.Json.Nodes.JsonNode?>()), timeout.Token);
        await server;
        Assert.Equal(expected, result.Outcome);

        async Task ServeAsync()
        {
            using var client = await listener.AcceptTcpClientAsync(timeout.Token);
            await using var stream = client.GetStream();
            var command = new byte[10];
            await stream.ReadExactlyAsync(command, timeout.Token);
            Assert.Equal("nINSTREAM\n", Encoding.ASCII.GetString(command));
            var header = new byte[4];
            await stream.ReadExactlyAsync(header, timeout.Token);
            Assert.Equal(4, BinaryPrimitives.ReadInt32BigEndian(header));
            var content = new byte[4];
            await stream.ReadExactlyAsync(content, timeout.Token);
            Assert.Equal("test", Encoding.ASCII.GetString(content));
            await stream.ReadExactlyAsync(header, timeout.Token);
            Assert.Equal(0, BinaryPrimitives.ReadInt32BigEndian(header));
            await stream.WriteAsync(Encoding.ASCII.GetBytes(reply + "\n"), timeout.Token);
        }
    }

    private sealed class MemoryStorage : ICompanyDocumentStorage
    {
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult<Stream>(new MemoryStream("test"u8.ToArray()));
        public Task<DocumentStorageWriteResult> WriteAsync(DocumentStorageWriteRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(string key, CancellationToken ct) => throw new NotSupportedException();
    }
}
