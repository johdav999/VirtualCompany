namespace VirtualCompany.Application.Sales;

public interface ILeadGenerationService
{
    Task<IReadOnlyList<IcpProfileDto>> ListProfilesAsync(Guid companyId, CancellationToken ct);
    Task<IcpProfileDto> CreateProfileAsync(Guid companyId, Guid userId, SaveIcpProfileRequest request, CancellationToken ct);
    Task<IcpProfileDto> UpdateProfileAsync(Guid companyId, Guid userId, Guid id, SaveIcpProfileRequest request, CancellationToken ct);
    Task<IcpProfileDto> ActivateProfileAsync(Guid companyId, Guid userId, Guid id, CancellationToken ct);
    Task<IcpProfileDto> CloneProfileAsync(Guid companyId, Guid userId, Guid id, CancellationToken ct);
    Task ArchiveProfileAsync(Guid companyId, Guid userId, Guid id, CancellationToken ct);
    Task<IcpPreviewDto> PreviewProfileAsync(Guid companyId, Guid id, ProspectAccountInput request, CancellationToken ct);
    Task<SourcePolicyDto> GetSourcePolicyAsync(Guid companyId, CancellationToken ct);
    Task<SourcePolicyDto> UpdateSourcePolicyAsync(Guid companyId, Guid userId, SaveSourcePolicyRequest request, CancellationToken ct);
    Task<IReadOnlyList<ProspectingRunDto>> ListRunsAsync(Guid companyId, CancellationToken ct);
    Task<ProspectingRunDto> CreateRunAsync(Guid companyId, Guid userId, CreateProspectingRunRequest request, CancellationToken ct);
    Task<ProspectingRunDto> StartRunAsync(Guid companyId, Guid userId, Guid id, CancellationToken ct);
    Task<ProspectingRunDto> ChangeRunAsync(Guid companyId, Guid userId, Guid id, string action, CancellationToken ct);
    Task<ImportResultDto> ImportCsvAsync(Guid companyId, Guid userId, Guid runId, Stream content, string fileName, CancellationToken ct);
    Task<ProspectPageDto> ListAccountsAsync(Guid companyId, ProspectQuery query, CancellationToken ct);
    Task<ProspectAccountDto?> GetAccountAsync(Guid companyId, Guid id, CancellationToken ct);
    Task<ProspectAccountDto> AddAccountAsync(Guid companyId, Guid userId, Guid runId, ProspectAccountInput request, CancellationToken ct);
    Task<ProspectAccountDto> ReviewAccountAsync(Guid companyId, Guid userId, Guid id, ReviewProspectRequest request, CancellationToken ct);
    Task<ProspectAccountDto> MergeAccountAsync(Guid companyId, Guid userId, Guid sourceId, Guid targetId, CancellationToken ct);
    Task<ProspectContactDto> AddContactAsync(Guid companyId, Guid userId, Guid accountId, SaveProspectContactRequest request, CancellationToken ct);
    Task<ProspectContactDto> ReviewContactAsync(Guid companyId, Guid userId, Guid id, ReviewProspectRequest request, CancellationToken ct);
    Task<ProspectContactDto> MergeContactAsync(Guid companyId, Guid userId, Guid sourceId, Guid targetId, CancellationToken ct);
    Task<ProspectSignalDto> AddSignalAsync(Guid companyId, Guid userId, Guid accountId, SaveProspectSignalRequest request, CancellationToken ct);
    Task<ProspectSignalDto> ReviewSignalAsync(Guid companyId, Guid userId, Guid id, string action, CancellationToken ct);
    Task<ProspectAccountDto> RefreshResearchAndScoreAsync(Guid companyId, Guid userId, Guid accountId, CancellationToken ct);
    Task<LeadConversionDto> ConvertAsync(Guid companyId, Guid userId, Guid accountId, Guid? contactId, CancellationToken ct);
    Task<IReadOnlyList<SuppressionDto>> ListSuppressionsAsync(Guid companyId, CancellationToken ct);
    Task<SuppressionDto> AddSuppressionAsync(Guid companyId, Guid userId, SaveSuppressionRequest request, CancellationToken ct);
    Task RemoveSuppressionAsync(Guid companyId, Guid userId, Guid id, CancellationToken ct);
    Task<LeadGenerationMetricsDto> GetMetricsAsync(Guid companyId, CancellationToken ct);
    Task<byte[]> ExportCsvAsync(Guid companyId, CancellationToken ct);
    Task<CrmDeliveryStatusDto> GetCrmStatusAsync(Guid companyId, CancellationToken ct);
    Task<CrmSyncResultDto> SyncLeadAsync(Guid companyId, Guid userId, Guid accountId, string providerKey, CancellationToken ct);
}

public interface IProspectDataProvider
{
    string Key { get; }
    ProspectProviderCapabilities Capabilities { get; }
    Task<ProspectProviderPage> SearchAccountsAsync(Guid companyId, ProspectProviderSearch request, CancellationToken ct);
}

public interface IProspectDataProviderRegistry
{
    IReadOnlyList<ProspectProviderDescriptor> List();
    IProspectDataProvider Resolve(string key);
}

public interface ICrmLeadAdapter
{
    string Key { get; }
    Task<CrmAdapterStatus> GetStatusAsync(Guid companyId, CancellationToken ct);
    Task<CrmSyncResultDto> UpsertLeadAsync(Guid companyId, Guid leadId, string idempotencyKey, CancellationToken ct);
}

public interface ICrmLeadAdapterRegistry
{
    IReadOnlyList<string> Keys { get; }
    ICrmLeadAdapter Resolve(string key);
}

public sealed record ProspectProviderCapabilities(bool AccountSearch, bool ContactSearch, bool Enrichment, bool Signals, bool IsPaid);
public sealed record ProspectProviderDescriptor(string Key, string Label, ProspectProviderCapabilities Capabilities, string Health);
public sealed record ProspectProviderSearch(Guid ProfileId, int Limit, string? Cursor, string Countries, string Industries);
public sealed record ProspectProviderPage(IReadOnlyList<ProspectAccountInput> Accounts, string? NextCursor, decimal Cost, bool Complete);

public sealed record SaveIcpProfileRequest(string Name, string Countries, string Industries, int? EmployeeMin, int? EmployeeMax, decimal? RevenueMin, decimal? RevenueMax, string BuyerRoles, string Technologies, string PainHypotheses, string PositiveCriteria, string Disqualifiers);
public sealed record IcpProfileDto(Guid Id, string Name, int Version, string Status, string Countries, string Industries, int? EmployeeMin, int? EmployeeMax, decimal? RevenueMin, decimal? RevenueMax, string BuyerRoles, string Technologies, string PainHypotheses, string PositiveCriteria, string Disqualifiers, DateTime UpdatedUtc, DateTime? ActivatedUtc);
public sealed record IcpCriterionDto(string Criterion, string Outcome, string Explanation);
public sealed record IcpPreviewDto(string Outcome, decimal FitScore, IReadOnlyList<IcpCriterionDto> Criteria);
public sealed record SaveSourcePolicyRequest(string EnabledSources, string AllowedCountries, string AllowedFields, decimal PerRunBudget, decimal MonthlyBudget, decimal ApprovalThreshold, int RetentionDays, int RefreshDays);
public sealed record SourcePolicyDto(Guid Id, int Version, string EnabledSources, string AllowedCountries, string AllowedFields, decimal PerRunBudget, decimal MonthlyBudget, decimal ApprovalThreshold, int RetentionDays, int RefreshDays, decimal ReservedThisMonth, decimal ActualThisMonth, IReadOnlyList<ProspectProviderDescriptor> Providers);
public sealed record CreateProspectingRunRequest(Guid IcpProfileId, string Name, int AccountLimit, int ContactLimit, string Sources, string Geography, int FreshnessDays, decimal EstimatedCost, string? Schedule);
public sealed record ProspectingRunDto(Guid Id, Guid IcpProfileId, string Name, string Status, string CurrentStep, int AccountLimit, int ContactLimit, int AccountsFound, int ContactsFound, string Sources, string Geography, decimal EstimatedCost, decimal ActualCost, string? FailureSummary, DateTime CreatedUtc, DateTime? StartedUtc, DateTime? CompletedUtc);
public sealed record ProspectAccountInput(string Name, string? Domain, string? Country, string? Industry, int? Employees, decimal? Revenue, string? Technologies, string SourceKey, string SourceReference, DateTime? ObservedUtc = null);
public sealed record ProspectQuery(string? Search, string? Status, string? Country, string? Source, int Page = 1, int PageSize = 50, string Sort = "score");
public sealed record ProspectPageDto(IReadOnlyList<ProspectAccountDto> Items, int Total, int Page, int PageSize);
public sealed record ProspectAccountDto(Guid Id, Guid RunId, Guid ProfileId, string Name, string? Domain, string? Country, string? Industry, int? Employees, decimal? Revenue, string Technologies, string Source, string Status, string FitOutcome, decimal FitScore, decimal TimingScore, decimal RoleScore, decimal DataConfidenceScore, decimal OverallScore, string ScoreBand, string EvaluationJson, string ResearchBriefJson, string? RejectionReason, Guid? LeadId, DateTime LastObservedUtc, IReadOnlyList<ProspectContactDto> Contacts, IReadOnlyList<ProspectSignalDto> Signals, IReadOnlyList<string> AllowedActions);
public sealed record ReviewProspectRequest(string Action, string? Reason);
public sealed record SaveProspectContactRequest(string FullName, string? Title, string BuyingRoles, string? Department, string? Seniority, string? Email, string EmailStatus, string? Phone, string? ProfileUrl, decimal Confidence, string SourceKey, string SourceReference);
public sealed record ProspectContactDto(Guid Id, Guid AccountId, string FullName, string? Title, string? Department, string? Seniority, string BuyingRoles, string? Email, string EmailStatus, string? Phone, string? ProfileUrl, string EmploymentStatus, decimal Confidence, string Status, string? RejectionReason, Guid? ContactId);
public sealed record SaveProspectSignalRequest(string Type, string SourceKey, string SourceReference, string Summary, DateTime EventUtc, decimal Confidence, int FreshnessDays);
public sealed record ProspectSignalDto(Guid Id, string Type, string Source, string Summary, DateTime EventUtc, DateTime FreshUntilUtc, decimal Confidence, decimal Relevance, string Status);
public sealed record SaveSuppressionRequest(string ScopeType, string ScopeValue, string Reason, string Source, DateTime? ExpiresUtc);
public sealed record SuppressionDto(Guid Id, string ScopeType, string ScopeValue, string Reason, string Source, DateTime CreatedUtc, DateTime? ExpiresUtc);
public sealed record ImportResultDto(int Imported, int Duplicates, int Rejected, IReadOnlyList<string> Errors);
public sealed record LeadConversionDto(Guid AccountId, Guid CustomerCompanyId, Guid? ContactId, Guid LeadId, bool ExistingLead);
public sealed record LeadGenerationMetricsDto(int Candidates, int Qualified, int Accepted, int Converted, int Rejected, decimal AcceptanceRate, decimal AverageCompleteness, IReadOnlyDictionary<string, int> SourceYield);
public sealed record MergeProspectRequest(Guid TargetId);
public sealed record SignalReviewRequest(string Action);
public sealed record CrmAdapterStatus(string Key, string Label, bool Connected, string Health, DateTime? LastSyncUtc);
public sealed record CrmDeliveryStatusDto(bool InternalWorkspaceActive, IReadOnlyList<CrmAdapterStatus> Providers, bool SpreadsheetExportAvailable);
public sealed record CrmSyncResultDto(string ProviderKey, Guid LeadId, string ExternalReference, string Status, bool ExistingRecord);

public sealed class LeadGenerationValidationException : Exception
{
    public LeadGenerationValidationException(string message) : base(message) { }
}
