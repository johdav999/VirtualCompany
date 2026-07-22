using System.Net;
using System.Net.Http.Json;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class AgentApiClientTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UploadBriefDocumentAsync_UsesCompanyScopeAndStoresAgentSharingInMetadata(bool shareWithAgentTeam)
    {
        var companyId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var handler = new RecordingUploadHandler(companyId);
        var client = new AgentApiClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost/")
        });
        await using var content = new MemoryStream("# Virtual Company\nProduct information."u8.ToArray());

        await client.UploadBriefDocumentAsync(
            companyId,
            agentId,
            "company_information",
            shareWithAgentTeam,
            "company.md",
            "text/markdown",
            content);

        Assert.Equal($"/api/companies/{companyId:D}/documents", handler.RequestPath);
        Assert.Contains("company.md", handler.MultipartBody, StringComparison.Ordinal);
        Assert.Contains("text/markdown", handler.MultipartBody, StringComparison.Ordinal);
        Assert.Contains("\"visibility\":\"company\"", handler.MultipartBody, StringComparison.Ordinal);
        Assert.Contains("\"data_scopes\":[\"knowledge\",\"company_information\"]", handler.MultipartBody, StringComparison.Ordinal);
        Assert.DoesNotContain("agent_ids", handler.MultipartBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"\"agentId\":\"{agentId:D}\"", handler.MultipartBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"\"shareWithAgentTeam\":{shareWithAgentTeam.ToString().ToLowerInvariant()}", handler.MultipartBody, StringComparison.Ordinal);
    }

    private sealed class RecordingUploadHandler(Guid companyId) : HttpMessageHandler
    {
        public string RequestPath { get; private set; } = string.Empty;
        public string MultipartBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestPath = request.RequestUri?.AbsolutePath ?? string.Empty;
            MultipartBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = JsonContent.Create(new AgentBriefDocumentViewModel
                {
                    Id = Guid.NewGuid(),
                    CompanyId = companyId,
                    Title = "company",
                    OriginalFileName = "company.md",
                    FileSizeBytes = 39,
                    IngestionStatus = "uploaded",
                    IndexingStatus = "not_indexed",
                    UpdatedUtc = DateTime.UtcNow
                })
            };
        }
    }
}
