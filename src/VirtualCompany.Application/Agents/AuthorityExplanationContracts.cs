namespace VirtualCompany.Application.Agents;
public sealed record AgentToolPolicyPreviewDto(AgentEffectiveAuthorityDto Authority,
    IReadOnlyDictionary<string, AuthorityCheckDto> Checks);
public interface IAgentToolPolicyPreviewService
{
    Task<AgentToolPolicyPreviewDto> PreviewAsync(Guid companyId, Guid agentId, CancellationToken token);
}
public interface IAuthorityExplanationQueryService
{
    Task<AuthorityExplanationDto> GetAsync(Guid companyId, Guid? agentId, string? workKind, Guid? workId, CancellationToken token);
}
