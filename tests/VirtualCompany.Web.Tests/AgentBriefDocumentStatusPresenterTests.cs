using System.Text.Json.Nodes;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class AgentBriefDocumentStatusPresenterTests
{
    [Theory]
    [InlineData("pending_scan", "not_indexed", "Scanning", false)]
    [InlineData("scan_clean", "queued", "Queued", false)]
    [InlineData("processing", "indexing", "Indexing", false)]
    [InlineData("processed", "indexed", "Ready", true)]
    [InlineData("failed", "failed", "Needs attention", true)]
    [InlineData("blocked", "not_indexed", "Needs attention", true)]
    public void PresentsDocumentState(
        string ingestionStatus,
        string indexingStatus,
        string expectedLabel,
        bool expectedTerminal)
    {
        var document = new AgentBriefDocumentViewModel
        {
            IngestionStatus = ingestionStatus,
            IndexingStatus = indexingStatus
        };

        Assert.Equal(expectedLabel, AgentBriefDocumentStatusPresenter.GetLabel(document));
        Assert.Equal(expectedTerminal, AgentBriefDocumentStatusPresenter.IsTerminal(document));
    }

    [Fact]
    public void FailureDetail_PrefersActionableIngestionMessage()
    {
        var document = new AgentBriefDocumentViewModel
        {
            IngestionStatus = "failed",
            IndexingStatus = "failed",
            FailureMessage = "The document could not be read.",
            FailureAction = "Upload a UTF-8 Markdown file."
        };

        Assert.Equal(
            "The document could not be read. Upload a UTF-8 Markdown file.",
            AgentBriefDocumentStatusPresenter.GetDetail(document));
    }

    [Fact]
    public void Deduplicate_collapses_matching_shared_content_and_prefers_ready_document()
    {
        var metadata = new Dictionary<string, JsonNode?>
        {
            ["checksum_sha256"] = "same-content",
            ["briefingCategory"] = "products_and_services",
            ["shareWithAgentTeam"] = true,
            ["agentId"] = Guid.NewGuid().ToString()
        };
        var queued = CreateDocument(metadata, "queued", "scan_clean", DateTime.UtcNow);
        var indexed = CreateDocument(metadata, "indexed", "processed", DateTime.UtcNow.AddMinutes(-1));

        var result = AgentBriefDocumentStatusPresenter.Deduplicate([queued, indexed]);

        Assert.Equal(indexed.Id, Assert.Single(result).Id);
    }

    [Fact]
    public void Deduplicate_keeps_private_documents_for_different_agents()
    {
        var first = CreateDocument(
            BriefMetadata("same-content", Guid.NewGuid(), shared: false),
            "indexed",
            "processed",
            DateTime.UtcNow);
        var second = CreateDocument(
            BriefMetadata("same-content", Guid.NewGuid(), shared: false),
            "indexed",
            "processed",
            DateTime.UtcNow);

        Assert.Equal(2, AgentBriefDocumentStatusPresenter.Deduplicate([first, second]).Count);
    }

    private static AgentBriefDocumentViewModel CreateDocument(
        IReadOnlyDictionary<string, JsonNode?> metadata,
        string indexingStatus,
        string ingestionStatus,
        DateTime updatedUtc) =>
        new()
        {
            Id = Guid.NewGuid(),
            OriginalFileName = "product-catalog.md",
            FileSizeBytes = 100,
            Metadata = metadata.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone()),
            IndexingStatus = indexingStatus,
            IngestionStatus = ingestionStatus,
            UpdatedUtc = updatedUtc
        };

    private static Dictionary<string, JsonNode?> BriefMetadata(string checksum, Guid agentId, bool shared) =>
        new()
        {
            ["checksum_sha256"] = checksum,
            ["briefingCategory"] = "products_and_services",
            ["shareWithAgentTeam"] = shared,
            ["agentId"] = agentId.ToString()
        };
}
