namespace VirtualCompany.Application.Orchestration;

public sealed record CompanyExternalActionReadiness(
    string ActionType,
    string OwningCapability,
    string TargetType,
    string OutboxTopic,
    string DispatcherOwner,
    string RetryClassification,
    string ReconciliationStrategy,
    string StatusQuery,
    bool RequiresApproval,
    bool Ready);

public interface ICompanyExternalActionReadinessRegistry
{
    CompanyExternalActionReadiness? Find(string actionType);
    IReadOnlyList<CompanyExternalActionReadiness> ListReady();
}
