using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Orchestration;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class CompanyExternalActionReadinessRegistry : ICompanyExternalActionReadinessRegistry
{
    private static readonly CompanyExternalActionReadiness OperatorNotification = new(
        "operator_notification",
        "company_operations",
        "company_member",
        CompanyOutboxTopics.NotificationDeliveryRequested,
        nameof(ICompanyNotificationDispatcher),
        "bounded_transient_retry",
        "query the idempotent company notification record before any retry",
        "company notification related to the operating decision",
        true,
        true);

    public CompanyExternalActionReadiness? Find(string actionType) =>
        string.Equals(actionType, OperatorNotification.ActionType, StringComparison.OrdinalIgnoreCase)
            ? OperatorNotification : null;

    public IReadOnlyList<CompanyExternalActionReadiness> ListReady() => [OperatorNotification];
}
