using VirtualCompany.Application.Cockpit;

namespace VirtualCompany.Application.Orchestration;
public interface ICollaborationEvidenceQueryService
{
    Task<CollaborationEvidenceDto> GetAsync(Guid companyId, string kind, Guid workId, CancellationToken ct);
}
