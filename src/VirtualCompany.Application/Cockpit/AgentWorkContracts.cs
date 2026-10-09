namespace VirtualCompany.Application.Cockpit;
public interface IAgentWorkQueryService
{
    Task<AgentWorkBoardDto> ListAsync(AgentWorkQuery query, CancellationToken cancellationToken);
    Task<AgentWorkItemDto> GetAsync(Guid companyId, string kind, Guid id, CancellationToken cancellationToken);
}

public interface IBusinessWorkEvidenceQueryService
{
    Task<BusinessWorkEvidenceDto> GetAsync(Guid companyId, string kind, Guid id, CancellationToken token);
}
