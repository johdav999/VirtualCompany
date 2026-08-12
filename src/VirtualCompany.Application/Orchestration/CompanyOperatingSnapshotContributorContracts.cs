using System.Text.Json.Nodes;

namespace VirtualCompany.Application.Orchestration;

public sealed record CompanyOperatingSnapshotContribution(
    string SectionName,
    JsonNode? Payload,
    int SourceCount,
    IReadOnlyList<string> DataGaps,
    bool IsTruncated,
    DateTime ObservedUtc);

public interface ICompanyOperatingSnapshotContributor
{
    string SectionName { get; }

    Task<CompanyOperatingSnapshotContribution> CaptureAsync(
        Guid companyId,
        CancellationToken cancellationToken);
}
