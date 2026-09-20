using System.Text.Json.Nodes;
using Bunit;
using VirtualCompany.Api.Tests;
using VirtualCompany.Web.Components;
using VirtualCompany.Web.Services;
using Xunit;

namespace VirtualCompany.Web.Tests;

public sealed class DocumentUpdateApprovalDetailTests
{
    [Fact]
    public void Pending_update_shows_exact_versions_artifact_review_and_replacement_action()
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        var approval = UpdateApproval("pending");
        approval.CurrentStep = new ApprovalStepViewModel { Id = Guid.NewGuid(), SequenceNo = 1, Status = "pending" };

        var cut = context.RenderComponent<ApprovalDetail>(parameters => parameters
            .Add(x => x.Approval, approval));

        Assert.Contains("Replace repository file", cut.Markup);
        Assert.Contains("etag-12", cut.Markup);
        Assert.Contains("Download original", cut.Markup);
        Assert.Contains("Review text changes", cut.Markup);
        Assert.Contains("Approve replacement", cut.Markup);
        Assert.Contains("Any intervening human edit stops delivery", cut.Markup);
    }

    [Fact]
    public void Stale_update_explains_that_it_cannot_be_approved()
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        var cut = context.RenderComponent<ApprovalDetail>(parameters => parameters
            .Add(x => x.Approval, UpdateApproval("stale")));

        Assert.Contains("newer human edit was detected", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cannot be approved", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Approve replacement", cut.Markup);
    }

    private static ApprovalRequestViewModel UpdateApproval(string status) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = Guid.NewGuid(),
        TargetEntityType = "document_publication_request",
        TargetEntityId = Guid.NewGuid(),
        Status = status,
        DisplayStatus = status,
        DisplayDecisionSummary = "Replace the reviewed whole file only while its Microsoft version is unchanged.",
        CreatedAt = DateTime.UtcNow,
        ThresholdContext = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
        {
            ["toolName"] = JsonValue.Create("documents.update"),
            ["publicationRequestId"] = JsonValue.Create(Guid.NewGuid()),
            ["repositoryName"] = JsonValue.Create("Shared company library"),
            ["targetFolderItemId"] = JsonValue.Create("approved-output"),
            ["targetItemId"] = JsonValue.Create("file-1"),
            ["fileName"] = JsonValue.Create("Q4-customer-brief.md"),
            ["sizeBytes"] = JsonValue.Create(1024L),
            ["contentSha256"] = JsonValue.Create(new string('a', 64)),
            ["expectedRemoteVersion"] = JsonValue.Create("etag-12"),
            ["originalEvidenceVersion"] = JsonValue.Create("etag-12")
        }
    };
}