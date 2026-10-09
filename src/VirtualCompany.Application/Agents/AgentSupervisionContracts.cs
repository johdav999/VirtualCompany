namespace VirtualCompany.Application.Agents;
public interface IAgentSupervisionReportService
{
    Task<AgentSupervisionReport> GetAsync(AgentSupervisionQuery query, CancellationToken cancellationToken);
    Task<SupervisionCsv> ExportAsync(AgentSupervisionQuery query, CancellationToken cancellationToken);
}
