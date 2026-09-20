using VirtualCompany.Domain.Entities;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class CompanyDocumentPublicationRequestTests
{
    [Fact]
    public void Retention_expires_only_abandoned_staged_artifacts()
    {
        var request = Create(DateTime.UtcNow);
        request.MarkLocalArtifactsPurged(DateTime.UtcNow);
        Assert.Equal(DocumentPublicationStatuses.Expired, request.Status);
        Assert.NotNull(request.LocalArtifactsPurgedUtc);
    }

    [Fact]
    public void Retention_preserves_artifacts_needed_for_reconciliation()
    {
        var request = Create(DateTime.UtcNow);
        request.Queue(Guid.NewGuid(), Guid.NewGuid(), "{}", DateTime.UtcNow, DateTime.UtcNow);
        request.MarkSending(DateTime.UtcNow);
        request.MarkReconciliationRequired("ambiguous_provider_outcome", "Provider outcome is uncertain.", DateTime.UtcNow);
        Assert.Throws<InvalidOperationException>(() => request.MarkLocalArtifactsPurged(DateTime.UtcNow));
    }

    [Fact]
    public void Publication_lifecycle_is_durable_idempotent_and_records_provider_identity()
    {
        var now = DateTime.UtcNow;
        var request = Create(now);
        var approvalId = Guid.NewGuid();
        var executionId = Guid.NewGuid();

        request.Queue(approvalId, executionId, "{\"decision\":\"allow_with_approval\"}", now.AddSeconds(30), now.AddMinutes(1));
        request.Queue(approvalId, executionId, "{\"decision\":\"allow_with_approval\"}", now.AddSeconds(30), now.AddMinutes(2));
        request.MarkSending(now.AddMinutes(3));
        request.MarkReconciliationRequired("ambiguous_provider_outcome", "The provider outcome is unknown.", now.AddMinutes(4));
        request.MarkSending(now.AddMinutes(5));
        request.MarkDelivered("provider-item", "etag-1", "https://tenant.sharepoint.com/file", now.AddMinutes(6));

        Assert.Equal(DocumentPublicationStatuses.Delivered, request.Status);
        Assert.Equal(2, request.AttemptCount);
        Assert.Equal(approvalId, request.ApprovalRequestId);
        Assert.Equal(executionId, request.ToolExecutionAttemptId);
        Assert.Contains("allow_with_approval", request.PolicyDecisionJson);
        Assert.Equal(now.AddSeconds(30), request.ApprovalVersionUtc);
        Assert.Equal("provider-item", request.ProviderItemId);
        Assert.Equal("etag-1", request.ProviderVersion);
        Assert.NotNull(request.CompletedUtc);
    }

    [Theory]
    [InlineData("../brief.docx")]
    [InlineData("folder/brief.docx")]
    [InlineData("brief?.docx")]
    public void Publication_rejects_unsafe_filenames(string fileName)
    {
        Assert.Throws<ArgumentException>(() => Create(DateTime.UtcNow, fileName));
    }

    private static CompanyDocumentPublicationRequest Create(DateTime now, string fileName = "board-brief.docx") => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "approved-output-folder", fileName,
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document", 128,
        new string('a', 64), "companies/company/publications/file", Guid.NewGuid(), "user",
        Guid.NewGuid(), "publication-idempotency-key", now);
    [Fact]
    public void Update_proposal_records_exact_versions_and_terminal_conflict_states()
    {
        var now = DateTime.UtcNow;
        var request = new CompanyDocumentPublicationRequest(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "approved-output-folder", "provider-item",
            "etag-12", "etag-12", "board-brief.md", "text/markdown", 64, new string('b', 64),
            "companies/company/publications/replacement", 48, new string('a', 64),
            "companies/company/publications/original", Guid.NewGuid(), "agent", Guid.NewGuid(),
            "update-idempotency-key", null, now);

        Assert.Equal(DocumentPublicationOperationKinds.Update, request.OperationKind);
        Assert.Equal("etag-12", request.ExpectedRemoteVersion);
        Assert.Equal("etag-12", request.OriginalEvidenceVersion);
        request.MarkConflict("etag-13", now.AddMinutes(1));
        Assert.Equal(DocumentPublicationStatuses.Conflict, request.Status);
        Assert.Equal("etag-13", request.ConflictRemoteVersion);

        var unresolved = new CompanyDocumentPublicationRequest(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "approved-output-folder", "provider-item",
            "etag-20", "etag-20", "board-brief.md", "text/markdown", 64, new string('d', 64),
            "companies/company/publications/replacement-2", 48, new string('c', 64),
            "companies/company/publications/original-2", Guid.NewGuid(), "agent", Guid.NewGuid(),
            "update-idempotency-key-2", request.Id, now);
        unresolved.MarkUnresolved("etag-22", now.AddMinutes(2));
        Assert.Equal(DocumentPublicationStatuses.Unresolved, unresolved.Status);
        Assert.Equal(request.Id, unresolved.StalePublicationRequestId);
    }
}
