namespace VirtualCompany.Domain.Entities;

public static class LeadGenerationStatuses
{
    public const string Draft = "draft";
    public const string Active = "active";
    public const string Superseded = "superseded";
    public const string Archived = "archived";
    public const string Planned = "planned";
    public const string Running = "running";
    public const string Paused = "paused";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
    public const string Candidate = "candidate";
    public const string Researching = "researching";
    public const string Qualified = "qualified";
    public const string Rejected = "rejected";
    public const string Merged = "merged";
    public const string Stale = "stale";
    public const string Converted = "converted";
    public const string Discovered = "discovered";
    public const string Enriching = "enriching";
    public const string ReadyForReview = "ready_for_review";
    public const string Accepted = "accepted";
}

public sealed class IdealCustomerProfile : ICompanyOwnedEntity
{
    private IdealCustomerProfile() { }

    public IdealCustomerProfile(Guid id, Guid companyId, string name, int version, Guid createdByUserId,
        string countries, string industries, int? employeeMin, int? employeeMax, decimal? revenueMin,
        decimal? revenueMax, string buyerRoles, string technologies, string painHypotheses,
        string positiveCriteria, string disqualifiers, Guid? previousVersionId = null)
    {
        EnsureCompany(companyId);
        if (createdByUserId == Guid.Empty) throw new ArgumentException("CreatedByUserId is required.", nameof(createdByUserId));
        if (version < 1) throw new ArgumentOutOfRangeException(nameof(version));
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        Name = Required(name, 160);
        Version = version;
        CreatedByUserId = createdByUserId;
        PreviousVersionId = previousVersionId;
        Status = LeadGenerationStatuses.Draft;
        CreatedUtc = UpdatedUtc = DateTime.UtcNow;
        UpdateDraft(countries, industries, employeeMin, employeeMax, revenueMin, revenueMax, buyerRoles,
            technologies, painHypotheses, positiveCriteria, disqualifiers, createdByUserId);
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid? PreviousVersionId { get; private set; }
    public string Name { get; private set; } = null!;
    public int Version { get; private set; }
    public string Status { get; private set; } = null!;
    public string Countries { get; private set; } = "";
    public string Industries { get; private set; } = "";
    public int? EmployeeMin { get; private set; }
    public int? EmployeeMax { get; private set; }
    public decimal? RevenueMin { get; private set; }
    public decimal? RevenueMax { get; private set; }
    public string BuyerRoles { get; private set; } = "";
    public string Technologies { get; private set; } = "";
    public string PainHypotheses { get; private set; } = "";
    public string PositiveCriteria { get; private set; } = "";
    public string Disqualifiers { get; private set; } = "";
    public Guid CreatedByUserId { get; private set; }
    public Guid UpdatedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime? ActivatedUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public void UpdateDraft(string countries, string industries, int? employeeMin, int? employeeMax,
        decimal? revenueMin, decimal? revenueMax, string buyerRoles, string technologies,
        string painHypotheses, string positiveCriteria, string disqualifiers, Guid userId)
    {
        if (Status != LeadGenerationStatuses.Draft) throw new InvalidOperationException("Only a draft profile can be edited.");
        if (userId == Guid.Empty) throw new ArgumentException("UserId is required.", nameof(userId));
        if (employeeMin is < 0 || employeeMax is < 0 || employeeMin > employeeMax) throw new ArgumentException("Employee range is invalid.");
        if (revenueMin is < 0 || revenueMax is < 0 || revenueMin > revenueMax) throw new ArgumentException("Revenue range is invalid.");
        Countries = List(countries, 1000);
        Industries = List(industries, 1000);
        EmployeeMin = employeeMin;
        EmployeeMax = employeeMax;
        RevenueMin = revenueMin;
        RevenueMax = revenueMax;
        BuyerRoles = List(buyerRoles, 2000);
        Technologies = List(technologies, 2000);
        PainHypotheses = Optional(painHypotheses, 4000);
        PositiveCriteria = Optional(positiveCriteria, 4000);
        Disqualifiers = Optional(disqualifiers, 4000);
        if (Countries.Length == 0 && Industries.Length == 0 && BuyerRoles.Length == 0)
            throw new ArgumentException("Add at least one target country, industry, or buyer role.");
        UpdatedByUserId = userId;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void Activate()
    {
        if (Status != LeadGenerationStatuses.Draft) throw new InvalidOperationException("Only a draft profile can be activated.");
        Status = LeadGenerationStatuses.Active;
        ActivatedUtc = UpdatedUtc = DateTime.UtcNow;
    }

    public void Supersede() { if (Status == LeadGenerationStatuses.Active) { Status = LeadGenerationStatuses.Superseded; UpdatedUtc = DateTime.UtcNow; } }
    public void Archive() { if (Status == LeadGenerationStatuses.Active) throw new InvalidOperationException("Activate a replacement before archiving this profile."); Status = LeadGenerationStatuses.Archived; UpdatedUtc = DateTime.UtcNow; }

    private static void EnsureCompany(Guid id) { if (id == Guid.Empty) throw new ArgumentException("CompanyId is required."); }
    private static string Required(string value, int max) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A value is required.") : value.Trim()[..Math.Min(value.Trim().Length, max)];
    private static string Optional(string? value, int max) { var x = value?.Trim() ?? ""; return x[..Math.Min(x.Length, max)]; }
    private static string List(string? value, int max) => string.Join(',', (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => x.ToLowerInvariant()).Distinct()).Trim()[..Math.Min(string.Join(',', (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => x.ToLowerInvariant()).Distinct()).Trim().Length, max)];
}

public sealed class ProspectSourcePolicy : ICompanyOwnedEntity
{
    private ProspectSourcePolicy() { }
    public ProspectSourcePolicy(Guid id, Guid companyId)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("CompanyId is required.");
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; Version = 1;
        EnabledSources = "first_party,csv"; AllowedFields = "company,domain,industry,country,name,title,email,phone";
        PerRunBudget = 0; MonthlyBudget = 0; ApprovalThreshold = 0; RetentionDays = 365; RefreshDays = 30;
        IsActive = true; CreatedUtc = UpdatedUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public int Version { get; private set; }
    public string EnabledSources { get; private set; } = null!;
    public string AllowedCountries { get; private set; } = "";
    public string AllowedFields { get; private set; } = null!;
    public decimal PerRunBudget { get; private set; }
    public decimal MonthlyBudget { get; private set; }
    public decimal ApprovalThreshold { get; private set; }
    public int RetentionDays { get; private set; }
    public int RefreshDays { get; private set; }
    public decimal ReservedThisMonth { get; private set; }
    public decimal ActualThisMonth { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public void Update(string sources, string countries, string fields, decimal runBudget, decimal monthlyBudget, decimal approvalThreshold, int retentionDays, int refreshDays)
    {
        if (runBudget < 0 || monthlyBudget < 0 || approvalThreshold < 0) throw new ArgumentOutOfRangeException(nameof(runBudget));
        if (retentionDays is < 1 or > 3650 || refreshDays is < 1 or > 365) throw new ArgumentOutOfRangeException(nameof(retentionDays));
        EnabledSources = sources.Trim().ToLowerInvariant(); AllowedCountries = countries.Trim().ToLowerInvariant(); AllowedFields = fields.Trim().ToLowerInvariant();
        PerRunBudget = runBudget; MonthlyBudget = monthlyBudget; ApprovalThreshold = approvalThreshold; RetentionDays = retentionDays; RefreshDays = refreshDays; Version++; UpdatedUtc = DateTime.UtcNow;
    }
    public void Reserve(decimal amount) { if (amount < 0 || amount > PerRunBudget || ReservedThisMonth + ActualThisMonth + amount > MonthlyBudget) throw new InvalidOperationException("The prospect data budget would be exceeded."); ReservedThisMonth += amount; UpdatedUtc = DateTime.UtcNow; }
    public void Reconcile(decimal reserved, decimal actual) { ReservedThisMonth = Math.Max(0, ReservedThisMonth - reserved); ActualThisMonth += Math.Max(0, actual); UpdatedUtc = DateTime.UtcNow; }
}

public sealed class ProspectingRun : ICompanyOwnedEntity
{
    private ProspectingRun() { }
    public ProspectingRun(Guid id, Guid companyId, Guid idealCustomerProfileId, Guid ownerUserId, string name, int accountLimit, int contactLimit, string sources, string geography, int freshnessDays, decimal estimatedCost, string? schedule)
    {
        if (companyId == Guid.Empty || idealCustomerProfileId == Guid.Empty || ownerUserId == Guid.Empty) throw new ArgumentException("Company, profile, and owner are required.");
        if (accountLimit is < 1 or > 10000 || contactLimit is < 0 or > 50000 || freshnessDays is < 1 or > 365 || estimatedCost < 0) throw new ArgumentOutOfRangeException(nameof(accountLimit));
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; IdealCustomerProfileId = idealCustomerProfileId; OwnerUserId = ownerUserId;
        Name = name.Trim(); AccountLimit = accountLimit; ContactLimit = contactLimit; Sources = sources.Trim().ToLowerInvariant(); Geography = geography.Trim().ToLowerInvariant(); FreshnessDays = freshnessDays; EstimatedCost = estimatedCost; Schedule = schedule?.Trim();
        Status = LeadGenerationStatuses.Planned; CurrentStep = "Awaiting start"; CreatedUtc = UpdatedUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid IdealCustomerProfileId { get; private set; } public Guid OwnerUserId { get; private set; } public Guid? ApprovalId { get; private set; }
    public string Name { get; private set; } = null!; public int AccountLimit { get; private set; } public int ContactLimit { get; private set; } public string Sources { get; private set; } = null!; public string Geography { get; private set; } = null!; public int FreshnessDays { get; private set; } public decimal EstimatedCost { get; private set; } public decimal ActualCost { get; private set; } public string? Schedule { get; private set; }
    public string Status { get; private set; } = null!; public string CurrentStep { get; private set; } = null!; public int AccountsFound { get; private set; } public int ContactsFound { get; private set; } public string? Cursor { get; private set; } public string? FailureSummary { get; private set; } public DateTime CreatedUtc { get; private set; } public DateTime UpdatedUtc { get; private set; } public DateTime? StartedUtc { get; private set; } public DateTime? CompletedUtc { get; private set; } public byte[] RowVersion { get; private set; } = [];
    public void SetApproval(Guid id) { ApprovalId = id == Guid.Empty ? throw new ArgumentException("ApprovalId is required.") : id; UpdatedUtc = DateTime.UtcNow; }
    public void Start() { if (Status is not (LeadGenerationStatuses.Planned or LeadGenerationStatuses.Paused or LeadGenerationStatuses.Failed)) throw new InvalidOperationException("This run cannot be started."); Status = LeadGenerationStatuses.Running; CurrentStep = "Discovering accounts"; FailureSummary = null; StartedUtc ??= DateTime.UtcNow; UpdatedUtc = DateTime.UtcNow; }
    public void Progress(int accounts, int contacts, string step, string? cursor, decimal actualCost) { if (Status != LeadGenerationStatuses.Running) throw new InvalidOperationException("Only a running prospecting run can progress."); AccountsFound = Math.Min(AccountLimit, Math.Max(AccountsFound, accounts)); ContactsFound = Math.Min(ContactLimit, Math.Max(ContactsFound, contacts)); CurrentStep = step.Trim(); Cursor = cursor; ActualCost = Math.Max(ActualCost, actualCost); UpdatedUtc = DateTime.UtcNow; }
    public void Complete() { if (Status != LeadGenerationStatuses.Running) throw new InvalidOperationException("Only a running run can complete."); Status = LeadGenerationStatuses.Completed; CurrentStep = "Complete"; CompletedUtc = UpdatedUtc = DateTime.UtcNow; }
    public void Pause() { if (Status != LeadGenerationStatuses.Running) throw new InvalidOperationException("Only a running run can pause."); Status = LeadGenerationStatuses.Paused; CurrentStep = "Paused"; UpdatedUtc = DateTime.UtcNow; }
    public void Cancel() { if (Status == LeadGenerationStatuses.Completed) throw new InvalidOperationException("A completed run cannot be cancelled."); Status = LeadGenerationStatuses.Cancelled; CurrentStep = "Cancelled"; UpdatedUtc = DateTime.UtcNow; }
    public void Fail(string summary) { Status = LeadGenerationStatuses.Failed; FailureSummary = summary.Trim()[..Math.Min(summary.Trim().Length, 1000)]; CurrentStep = "Needs attention"; UpdatedUtc = DateTime.UtcNow; }
}

public sealed class ProspectAccount : ICompanyOwnedEntity
{
    private ProspectAccount() { }
    public ProspectAccount(Guid id, Guid companyId, Guid runId, Guid profileId, string name, string? domain, string? country, string? industry, int? employees, decimal? revenue, string sourceKey, string sourceReference, DateTime observedUtc)
    {
        if (companyId == Guid.Empty || runId == Guid.Empty || profileId == Guid.Empty) throw new ArgumentException("Company, run, and profile are required.");
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; ProspectingRunId = runId; IdealCustomerProfileId = profileId; Name = name.Trim(); Domain = NormalizeDomain(domain); Country = country?.Trim().ToLowerInvariant(); Industry = industry?.Trim().ToLowerInvariant(); Employees = employees; Revenue = revenue; SourceKey = sourceKey.Trim().ToLowerInvariant(); SourceReference = sourceReference.Trim(); LastObservedUtc = observedUtc.Kind == DateTimeKind.Utc ? observedUtc : observedUtc.ToUniversalTime(); Status = LeadGenerationStatuses.Candidate; CreatedUtc = UpdatedUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid ProspectingRunId { get; private set; } public Guid IdealCustomerProfileId { get; private set; } public Guid? CustomerCompanyId { get; private set; } public Guid? LeadId { get; private set; } public Guid? MergedIntoId { get; private set; }
    public string Name { get; private set; } = null!; public string? LegalName { get; private set; } public string? Domain { get; private set; } public string? Country { get; private set; } public string? Industry { get; private set; } public int? Employees { get; private set; } public decimal? Revenue { get; private set; } public string Technologies { get; private set; } = ""; public string SourceKey { get; private set; } = null!; public string SourceReference { get; private set; } = null!; public DateTime LastObservedUtc { get; private set; }
    public string Status { get; private set; } = null!; public string FitOutcome { get; private set; } = "unknown"; public decimal FitScore { get; private set; } public decimal TimingScore { get; private set; } public decimal RoleScore { get; private set; } public decimal DataConfidenceScore { get; private set; } public decimal OverallScore { get; private set; } public string ScoreBand { get; private set; } = "Not scored"; public string EvaluationJson { get; private set; } = "{}"; public string ResearchBriefJson { get; private set; } = "{}"; public string? RejectionReason { get; private set; } public DateTime CreatedUtc { get; private set; } public DateTime UpdatedUtc { get; private set; } public byte[] RowVersion { get; private set; } = [];
    public void SetCanonical(string? legalName, string? country, string? industry, int? employees, decimal? revenue, string? technologies, DateTime observedUtc) { LegalName = legalName?.Trim(); Country = country?.Trim().ToLowerInvariant() ?? Country; Industry = industry?.Trim().ToLowerInvariant() ?? Industry; Employees = employees ?? Employees; Revenue = revenue ?? Revenue; Technologies = technologies?.Trim().ToLowerInvariant() ?? Technologies; LastObservedUtc = observedUtc; UpdatedUtc = DateTime.UtcNow; }
    public void ApplyEvaluation(string outcome, decimal fit, string evaluationJson) { FitOutcome = outcome.Trim().ToLowerInvariant(); FitScore = Clamp(fit); EvaluationJson = evaluationJson; Status = outcome == "disqualified" ? LeadGenerationStatuses.Rejected : outcome == "matched" ? LeadGenerationStatuses.Qualified : LeadGenerationStatuses.ReadyForReview; UpdatedUtc = DateTime.UtcNow; }
    public void ApplyScores(decimal timing, decimal role, decimal confidence, decimal overall, string band, string evaluationJson) { TimingScore = Clamp(timing); RoleScore = Clamp(role); DataConfidenceScore = Clamp(confidence); OverallScore = Clamp(overall); ScoreBand = band.Trim(); EvaluationJson = evaluationJson; UpdatedUtc = DateTime.UtcNow; }
    public void SetResearchBrief(string json) { ResearchBriefJson = json; UpdatedUtc = DateTime.UtcNow; }
    public void Accept() { if (Status is LeadGenerationStatuses.Rejected or LeadGenerationStatuses.Merged or LeadGenerationStatuses.Converted) throw new InvalidOperationException("This account cannot be accepted."); Status = LeadGenerationStatuses.Accepted; UpdatedUtc = DateTime.UtcNow; }
    public void Reject(string reason) { if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A rejection reason is required."); Status = LeadGenerationStatuses.Rejected; RejectionReason = reason.Trim(); UpdatedUtc = DateTime.UtcNow; }
    public void MarkStale() { if (Status is not (LeadGenerationStatuses.Converted or LeadGenerationStatuses.Merged or LeadGenerationStatuses.Rejected)) { Status = LeadGenerationStatuses.Stale; UpdatedUtc = DateTime.UtcNow; } }
    public void MergeInto(Guid id) { if (id == Guid.Empty || id == Id) throw new ArgumentException("A different target account is required."); MergedIntoId = id; Status = LeadGenerationStatuses.Merged; UpdatedUtc = DateTime.UtcNow; }
    public void Convert(Guid customerCompanyId, Guid leadId) { if (customerCompanyId == Guid.Empty || leadId == Guid.Empty) throw new ArgumentException("Customer company and lead are required."); CustomerCompanyId = customerCompanyId; LeadId = leadId; Status = LeadGenerationStatuses.Converted; UpdatedUtc = DateTime.UtcNow; }
    private static decimal Clamp(decimal value) => Math.Clamp(value, 0m, 100m);
    private static string? NormalizeDomain(string? value) { if (string.IsNullOrWhiteSpace(value)) return null; var x = value.Trim().ToLowerInvariant(); x = x.Replace("https://", "").Replace("http://", "").TrimEnd('/'); return x.StartsWith("www.") ? x[4..] : x; }
}

public sealed class ProspectContact : ICompanyOwnedEntity
{
    private ProspectContact() { }
    public ProspectContact(Guid id, Guid companyId, Guid prospectAccountId, string fullName, string? title, string roles, string sourceKey, string sourceReference)
    {
        if (companyId == Guid.Empty || prospectAccountId == Guid.Empty) throw new ArgumentException("Company and account are required.");
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; ProspectAccountId = prospectAccountId; FullName = fullName.Trim(); Title = title?.Trim(); BuyingRoles = roles.Trim().ToLowerInvariant(); SourceKey = sourceKey.Trim().ToLowerInvariant(); SourceReference = sourceReference.Trim(); Status = LeadGenerationStatuses.Discovered; EmailStatus = "unknown"; EmploymentStatus = "current_unverified"; CreatedUtc = UpdatedUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid ProspectAccountId { get; private set; } public Guid? ContactId { get; private set; } public Guid? MergedIntoId { get; private set; }
    public string FullName { get; private set; } = null!; public string? Title { get; private set; } public string? Department { get; private set; } public string? Seniority { get; private set; } public string BuyingRoles { get; private set; } = null!; public string? Email { get; private set; } public string EmailStatus { get; private set; } = null!; public string? Phone { get; private set; } public string? ProfileUrl { get; private set; } public string EmploymentStatus { get; private set; } = null!; public decimal Confidence { get; private set; } public string SourceKey { get; private set; } = null!; public string SourceReference { get; private set; } = null!; public DateTime? VerifiedUtc { get; private set; } public string Status { get; private set; } = null!; public string? RejectionReason { get; private set; } public DateTime CreatedUtc { get; private set; } public DateTime UpdatedUtc { get; private set; } public byte[] RowVersion { get; private set; } = [];
    public void Enrich(string? title, string? department, string? seniority, string? email, string emailStatus, string? phone, string? profileUrl, decimal confidence, DateTime observedUtc) { Title = title?.Trim() ?? Title; Department = department?.Trim() ?? Department; Seniority = seniority?.Trim() ?? Seniority; Email = email?.Trim().ToLowerInvariant() ?? Email; EmailStatus = emailStatus.Trim().ToLowerInvariant(); Phone = phone?.Trim() ?? Phone; ProfileUrl = profileUrl?.Trim() ?? ProfileUrl; Confidence = Math.Clamp(confidence, 0, 1); VerifiedUtc = observedUtc; Status = LeadGenerationStatuses.ReadyForReview; UpdatedUtc = DateTime.UtcNow; }
    public void Accept(Guid? contactId = null) { ContactId = contactId; Status = LeadGenerationStatuses.Accepted; UpdatedUtc = DateTime.UtcNow; }
    public void Reject(string reason) { Status = LeadGenerationStatuses.Rejected; RejectionReason = string.IsNullOrWhiteSpace(reason) ? throw new ArgumentException("A rejection reason is required.") : reason.Trim(); UpdatedUtc = DateTime.UtcNow; }
    public void MarkEmploymentChanged() { EmploymentStatus = "changed"; Status = LeadGenerationStatuses.Stale; UpdatedUtc = DateTime.UtcNow; }
    public void MergeInto(Guid id) { if (id == Guid.Empty || id == Id) throw new ArgumentException("A different contact is required."); MergedIntoId = id; Status = LeadGenerationStatuses.Merged; UpdatedUtc = DateTime.UtcNow; }
    public void ReassignAccount(Guid accountId) { ProspectAccountId = accountId == Guid.Empty ? throw new ArgumentException("AccountId is required.") : accountId; UpdatedUtc = DateTime.UtcNow; }
}

public sealed class ProspectSignal : ICompanyOwnedEntity
{
    private ProspectSignal() { }
    public ProspectSignal(Guid id, Guid companyId, Guid prospectAccountId, string type, string sourceKey, string sourceReference, string summary, DateTime eventUtc, decimal confidence, int freshnessDays, string dedupeKey)
    {
        if (companyId == Guid.Empty || prospectAccountId == Guid.Empty) throw new ArgumentException("Company and account are required.");
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; ProspectAccountId = prospectAccountId; SignalType = type.Trim().ToLowerInvariant(); SourceKey = sourceKey.Trim().ToLowerInvariant(); SourceReference = sourceReference.Trim(); Summary = summary.Trim(); EventUtc = eventUtc; Confidence = Math.Clamp(confidence, 0, 1); FreshUntilUtc = eventUtc.AddDays(Math.Clamp(freshnessDays, 1, 365)); DedupeKey = dedupeKey.Trim().ToLowerInvariant(); Status = "confirmed"; CreatedUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid ProspectAccountId { get; private set; } public string SignalType { get; private set; } = null!; public string SourceKey { get; private set; } = null!; public string SourceReference { get; private set; } = null!; public string Summary { get; private set; } = null!; public DateTime EventUtc { get; private set; } public DateTime FreshUntilUtc { get; private set; } public decimal Confidence { get; private set; } public decimal Relevance { get; private set; } public string Status { get; private set; } = null!; public string DedupeKey { get; private set; } = null!; public DateTime CreatedUtc { get; private set; }
    public void SetRelevance(decimal value) => Relevance = Math.Clamp(value, 0, 100);
    public void Dismiss() => Status = "dismissed";
    public void Contradict() => Status = "contradicted";
    public void ReassignAccount(Guid accountId) => ProspectAccountId = accountId == Guid.Empty ? throw new ArgumentException("AccountId is required.") : accountId;
}

public sealed class SalesSuppression : ICompanyOwnedEntity
{
    private SalesSuppression() { }
    public SalesSuppression(Guid id, Guid companyId, string scopeType, string scopeValue, string reason, string source, Guid createdByUserId, DateTime? expiresUtc)
    {
        if (companyId == Guid.Empty || createdByUserId == Guid.Empty) throw new ArgumentException("Company and creator are required.");
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; ScopeType = scopeType.Trim().ToLowerInvariant(); ScopeValue = scopeValue.Trim().ToLowerInvariant(); Reason = reason.Trim(); Source = source.Trim().ToLowerInvariant(); CreatedByUserId = createdByUserId; ExpiresUtc = expiresUtc; IsActive = true; CreatedUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public string ScopeType { get; private set; } = null!; public string ScopeValue { get; private set; } = null!; public string Reason { get; private set; } = null!; public string Source { get; private set; } = null!; public Guid CreatedByUserId { get; private set; } public bool IsActive { get; private set; } public DateTime CreatedUtc { get; private set; } public DateTime? ExpiresUtc { get; private set; }
    public void Deactivate() => IsActive = false;
}
