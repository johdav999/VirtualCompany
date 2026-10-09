namespace VirtualCompany.Application.Support;

public interface ISupportQualityService
{
    Task<SupportQualityReport> ReportAsync(Guid company, SupportQualityQuery query, CancellationToken ct);
    Task<SupportQualityExport> ExportAsync(Guid company, SupportQualityQuery query, CancellationToken ct);
    Task<SupportIssueGroupingSummary> CorrectAsync(Guid company, CorrectSupportIssueGrouping command, CancellationToken ct);
    Task<IReadOnlyList<SupportIssueGroupingSummary>> GroupHistoryAsync(Guid company, Guid caseId, CancellationToken ct);
    Task<SupportCapacityPreview> PreviewAsync(Guid company, PreviewSupportCapacity input, CancellationToken ct);
    Task<SupportCapacityProposal> SaveAsync(Guid company, SaveSupportCapacityProposal command, CancellationToken ct);
    Task<SupportCapacityProposal> OpenAsync(Guid company, Guid id, CancellationToken ct);
    Task<IReadOnlyList<SupportCapacityProposalSummary>> HistoryAsync(Guid company, int skip, CancellationToken ct);
    Task<SupportQualityExport> ProposalExportAsync(Guid company, Guid id, CancellationToken ct);
}
