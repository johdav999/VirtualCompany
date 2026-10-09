namespace VirtualCompany.Application.Sales;
public sealed record SuppressionDto(Guid Id, string ScopeType, string ScopeValue, string Reason, string Source, DateTime CreatedUtc, DateTime? ExpiresUtc);
public sealed record SaveProspectContactRequest(string FullName, string? Title, string BuyingRoles, string? Department, string? Seniority, string? Email, string EmailStatus, string? Phone, string? ProfileUrl, decimal Confidence, string SourceKey, string SourceReference)
{
    public string FullName { get; set; } = FullName;
    public string? Title { get; set; } = Title;
    public string BuyingRoles { get; set; } = BuyingRoles;
    public string? Department { get; set; } = Department;
    public string? Seniority { get; set; } = Seniority;
    public string? Email { get; set; } = Email;
    public string EmailStatus { get; set; } = EmailStatus;
    public string? Phone { get; set; } = Phone;
    public string? ProfileUrl { get; set; } = ProfileUrl;
    public decimal Confidence { get; set; } = Confidence;
    public string SourceKey { get; set; } = SourceKey;
    public string SourceReference { get; set; } = SourceReference;
}

public sealed record IcpSuggestionDto(Guid RunId, Guid AgentId, string AgentName, SaveIcpProfileRequest Profile, string Rationale, decimal Confidence, IReadOnlyList<IcpSuggestionEvidenceDto> Evidence, IReadOnlyList<string> MissingEvidence, bool RequiresReview);
public sealed record SaveProspectSignalRequest(string Type, string SourceKey, string SourceReference, string Summary, DateTime EventUtc, decimal Confidence, int FreshnessDays)
{
    public string Type { get; set; } = Type;
    public string SourceKey { get; set; } = SourceKey;
    public string SourceReference { get; set; } = SourceReference;
    public string Summary { get; set; } = Summary;
    public DateTime EventUtc { get; set; } = EventUtc;
    public decimal Confidence { get; set; } = Confidence;
    public int FreshnessDays { get; set; } = FreshnessDays;
}

public sealed record CreateProspectingRunRequest(Guid IcpProfileId, string Name, int AccountLimit, int ContactLimit, string Sources, string Geography, int FreshnessDays, decimal EstimatedCost, string? Schedule)
{
    public Guid IcpProfileId { get; set; } = IcpProfileId;
    public string Name { get; set; } = Name;
    public int AccountLimit { get; set; } = AccountLimit;
    public int ContactLimit { get; set; } = ContactLimit;
    public string Sources { get; set; } = Sources;
    public string Geography { get; set; } = Geography;
    public int FreshnessDays { get; set; } = FreshnessDays;
    public decimal EstimatedCost { get; set; } = EstimatedCost;
    public string? Schedule { get; set; } = Schedule;
}

public sealed record SaveSuppressionRequest(string ScopeType, string ScopeValue, string Reason, string Source, DateTime? ExpiresUtc)
{
    public string ScopeType { get; set; } = ScopeType;
    public string ScopeValue { get; set; } = ScopeValue;
    public string Reason { get; set; } = Reason;
    public string Source { get; set; } = Source;
    public DateTime? ExpiresUtc { get; set; } = ExpiresUtc;
}

public sealed record ProspectPageDto(IReadOnlyList<ProspectAccountDto> Items, int Total, int Page, int PageSize);
public sealed record SaveIcpProfileRequest(string Name, string Countries, string Industries, int? EmployeeMin, int? EmployeeMax, decimal? RevenueMin, decimal? RevenueMax, string BuyerRoles, string Technologies, string PainHypotheses, string PositiveCriteria, string Disqualifiers)
{
    public string Name { get; set; } = Name;
    public string Countries { get; set; } = Countries;
    public string Industries { get; set; } = Industries;
    public int? EmployeeMin { get; set; } = EmployeeMin;
    public int? EmployeeMax { get; set; } = EmployeeMax;
    public decimal? RevenueMin { get; set; } = RevenueMin;
    public decimal? RevenueMax { get; set; } = RevenueMax;
    public string BuyerRoles { get; set; } = BuyerRoles;
    public string Technologies { get; set; } = Technologies;
    public string PainHypotheses { get; set; } = PainHypotheses;
    public string PositiveCriteria { get; set; } = PositiveCriteria;
    public string Disqualifiers { get; set; } = Disqualifiers;
}

public sealed record SuggestIcpRequest(Guid AgentId, string? Focus = null);
public sealed record IcpSuggestionEvidenceDto(string SourceId, string Type, string Title);
public sealed record IcpProfileDto(Guid Id, string Name, int Version, string Status, string Countries, string Industries, int? EmployeeMin, int? EmployeeMax, decimal? RevenueMin, decimal? RevenueMax, string BuyerRoles, string Technologies, string PainHypotheses, string PositiveCriteria, string Disqualifiers, DateTime UpdatedUtc, DateTime? ActivatedUtc);
public sealed record ProspectSignalDto(Guid Id, string Type, string Source, string Summary, DateTime EventUtc, DateTime FreshUntilUtc, decimal Confidence, decimal Relevance, string Status);
public sealed record SourcePolicyDto(Guid Id, int Version, string EnabledSources, string AllowedCountries, string AllowedFields, decimal PerRunBudget, decimal MonthlyBudget, decimal ApprovalThreshold, int RetentionDays, int RefreshDays, decimal ReservedThisMonth, decimal ActualThisMonth, IReadOnlyList<ProspectProviderDescriptor> Providers);
public sealed record LeadGenerationMetricsDto(int Candidates, int Qualified, int Accepted, int Converted, int Rejected, decimal AcceptanceRate, decimal AverageCompleteness, IReadOnlyDictionary<string, int> SourceYield);
public sealed record ReviewProspectRequest(string Action, string? Reason);
public sealed record ProspectAccountDto(Guid Id, Guid RunId, Guid ProfileId, string Name, string? Domain, string? Country, string? Industry, int? Employees, decimal? Revenue, string Technologies, string Source, string Status, string FitOutcome, decimal FitScore, decimal TimingScore, decimal RoleScore, decimal DataConfidenceScore, decimal OverallScore, string ScoreBand, string EvaluationJson, string ResearchBriefJson, string? RejectionReason, Guid? LeadId, DateTime LastObservedUtc, IReadOnlyList<ProspectContactDto> Contacts, IReadOnlyList<ProspectSignalDto> Signals, IReadOnlyList<string> AllowedActions);
public sealed record LeadConversionDto(Guid AccountId, Guid CustomerCompanyId, Guid? ContactId, Guid LeadId, bool ExistingLead);
public sealed record SaveSourcePolicyRequest(string EnabledSources, string AllowedCountries, string AllowedFields, decimal PerRunBudget, decimal MonthlyBudget, decimal ApprovalThreshold, int RetentionDays, int RefreshDays)
{
    public string EnabledSources { get; set; } = EnabledSources;
    public string AllowedCountries { get; set; } = AllowedCountries;
    public string AllowedFields { get; set; } = AllowedFields;
    public decimal PerRunBudget { get; set; } = PerRunBudget;
    public decimal MonthlyBudget { get; set; } = MonthlyBudget;
    public decimal ApprovalThreshold { get; set; } = ApprovalThreshold;
    public int RetentionDays { get; set; } = RetentionDays;
    public int RefreshDays { get; set; } = RefreshDays;
}

public sealed record ProspectContactDto(Guid Id, Guid AccountId, string FullName, string? Title, string? Department, string? Seniority, string BuyingRoles, string? Email, string EmailStatus, string? Phone, string? ProfileUrl, string EmploymentStatus, decimal Confidence, string Status, string? RejectionReason, Guid? ContactId);
public sealed record ProspectingRunDto(Guid Id, Guid IcpProfileId, string Name, string Status, string CurrentStep, int AccountLimit, int ContactLimit, int AccountsFound, int ContactsFound, string Sources, string Geography, decimal EstimatedCost, decimal ActualCost, string? FailureSummary, DateTime CreatedUtc, DateTime? StartedUtc, DateTime? CompletedUtc);
public sealed record ProspectProviderCapabilities(bool AccountSearch, bool ContactSearch, bool Enrichment, bool Signals, bool IsPaid);
public sealed record ProspectProviderDescriptor(string Key, string Label, ProspectProviderCapabilities Capabilities, string Health);
public sealed record ImportResultDto(int Imported, int Duplicates, int Rejected, IReadOnlyList<string> Errors);
