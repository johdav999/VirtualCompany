namespace VirtualCompany.Application.Sales;

public sealed record CreateSalesCampaignDraftCommand(Guid CompanyId, Guid OwnerUserId, Guid? OwnerAgentId,
    string Name, string Purpose, string CampaignType, string AudienceType, string ObjectiveType,
    decimal ObjectiveTarget, string ObjectiveUnit, DateTime ObjectiveTargetUtc, DateTime PlanningStartsUtc,
    DateTime ScheduledLaunchUtc, DateTime ReviewDueUtc, DateTime EndsUtc, string TimeZoneId,
    decimal? PlannedBudget, string? BudgetCurrency, string? CommunicationLanguage, string IdempotencyKey,
    string OfferName = "Planning basis", string OfferSourceType = "marketing_plan", string OfferSourceReference = "internal-plan",
    Guid? OfferKnowledgeDocumentId = null, bool NoOfferRequired = false,
    IReadOnlyList<SalesCampaignDraftActivityCommand>? Activities = null);

public sealed record SalesCampaignDraftActivityCommand(string Name, string ActivityType, string Channel,
    DateTime PlannedStartUtc, DateTime DueUtc, string TimeZoneId);
public sealed record SalesCampaignDraftStepCommand(int StepOrder, int DelayDays, string Subject, string Body);
public sealed record PopulateSalesCampaignDraftCommand(Guid CompanyId, Guid CampaignId, Guid OwnerUserId,
    Guid? OwnerAgentId, IReadOnlyList<SalesCampaignDraftStepCommand> Steps, string IdempotencyKey);

public sealed record SalesCampaignDraftResult(Guid CampaignId, Guid SequenceId, string Status, string LifecycleStatus);

public interface ISalesCampaignDraftService
{
    Task<SalesCampaignDraftResult> CreateDraftAsync(CreateSalesCampaignDraftCommand command, CancellationToken ct);
    Task<SalesCampaignDraftResult> PopulateDraftAsync(PopulateSalesCampaignDraftCommand command, CancellationToken ct);
}
