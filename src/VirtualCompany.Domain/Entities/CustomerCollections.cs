namespace VirtualCompany.Domain.Entities;

public static class CustomerCollectionCaseStatuses
{
    public const string Open = "open";
    public const string Disputed = "disputed";
    public const string PromisePending = "promise_pending";
    public const string Resolved = "resolved";
}

public static class CustomerReminderDraftStatuses
{
    public const string Prepared = "prepared";
    public const string AwaitingApproval = "awaiting_approval";
    public const string Queued = "queued";
    public const string Accepted = "accepted";
    public const string Blocked = "blocked";
    public const string Failed = "failed";
    public const string ReconciliationRequired = "reconciliation_required";
}

public sealed class CustomerCollectionPolicy : ICompanyOwnedEntity
{
    private readonly List<CustomerCollectionPolicyStage> _stages = [];
    private readonly List<CustomerCollectionPolicyException> _exceptions = [];
    private CustomerCollectionPolicy() { }

    public CustomerCollectionPolicy(Guid id, Guid companyId, int gracePeriodDays, decimal materialityThreshold,
        string defaultLocale, bool requireApproval, DateTime nowUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = Required(companyId, nameof(companyId));
        Apply(gracePeriodDays, materialityThreshold, defaultLocale, requireApproval, false, false, nowUtc);
        CreatedUtc = UpdatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public int GracePeriodDays { get; private set; }
    public decimal MaterialityThreshold { get; private set; }
    public string DefaultLocale { get; private set; } = null!;
    public bool RequireApproval { get; private set; }
    public bool FeesEnabled { get; private set; }
    public bool InterestEnabled { get; private set; }
    public long Version { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public IReadOnlyCollection<CustomerCollectionPolicyStage> Stages => _stages;
    public IReadOnlyCollection<CustomerCollectionPolicyException> Exceptions => _exceptions;

    public void Update(int gracePeriodDays, decimal materialityThreshold, string defaultLocale, bool requireApproval,
        bool feesEnabled, bool interestEnabled, DateTime nowUtc)
    {
        Apply(gracePeriodDays, materialityThreshold, defaultLocale, requireApproval, feesEnabled, interestEnabled, nowUtc);
        Version++;
    }

    private void Apply(int gracePeriodDays, decimal materialityThreshold, string defaultLocale, bool requireApproval,
        bool feesEnabled, bool interestEnabled, DateTime nowUtc)
    {
        if (gracePeriodDays is < 0 or > 90) throw new ArgumentOutOfRangeException(nameof(gracePeriodDays));
        if (materialityThreshold < 0m) throw new ArgumentOutOfRangeException(nameof(materialityThreshold));
        if (feesEnabled || interestEnabled) throw new InvalidOperationException("Statutory reminder fees and interest are not supported by the active policy packs.");
        GracePeriodDays = gracePeriodDays;
        MaterialityThreshold = decimal.Round(materialityThreshold, 2, MidpointRounding.AwayFromZero);
        DefaultLocale = Locale(defaultLocale);
        RequireApproval = requireApproval;
        FeesEnabled = false;
        InterestEnabled = false;
        UpdatedUtc = Utc(nowUtc);
    }

    private static Guid Required(Guid value, string name) => value == Guid.Empty ? throw new ArgumentException($"{name} is required.", name) : value;
    private static string Locale(string value) => value?.Trim().ToLowerInvariant() switch
    { "sv" or "sv-se" => "sv-SE", "en" or "en-us" => "en-US", _ => throw new ArgumentOutOfRangeException(nameof(value), "Only English and Swedish are supported.") };
    internal static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}

public sealed class CustomerCollectionPolicyStage : ICompanyOwnedEntity
{
    private CustomerCollectionPolicyStage() { }
    public CustomerCollectionPolicyStage(Guid id, Guid companyId, Guid policyId, int stage, int daysAfterDue,
        string channel, string templateKey, bool requiresApproval)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId == Guid.Empty ? throw new ArgumentException("CompanyId is required.") : companyId;
        PolicyId = policyId == Guid.Empty ? throw new ArgumentException("PolicyId is required.") : policyId;
        if (stage is < 1 or > 20) throw new ArgumentOutOfRangeException(nameof(stage));
        if (daysAfterDue is < 0 or > 730) throw new ArgumentOutOfRangeException(nameof(daysAfterDue));
        Stage = stage; DaysAfterDue = daysAfterDue;
        Channel = Required(channel, 32).ToLowerInvariant();
        if (Channel != "email") throw new InvalidOperationException("Only durable email reminders are currently supported.");
        TemplateKey = Required(templateKey, 100); RequiresApproval = requiresApproval;
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid PolicyId { get; private set; }
    public int Stage { get; private set; } public int DaysAfterDue { get; private set; } public string Channel { get; private set; } = null!;
    public string TemplateKey { get; private set; } = null!; public bool RequiresApproval { get; private set; }
    public CustomerCollectionPolicy Policy { get; private set; } = null!;
    private static string Required(string value, int max) { var x = value?.Trim(); return string.IsNullOrWhiteSpace(x) || x.Length > max ? throw new ArgumentException("A collection policy stage value is invalid.") : x; }
}

public sealed class CustomerCollectionPolicyException : ICompanyOwnedEntity
{
    private CustomerCollectionPolicyException() { }
    public CustomerCollectionPolicyException(Guid id, Guid companyId, Guid policyId, Guid customerId,
        string reason, DateOnly? excludedUntilDate, Guid createdByUserId, DateTime nowUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = Required(companyId, nameof(companyId)); PolicyId = Required(policyId, nameof(policyId));
        CustomerId = Required(customerId, nameof(customerId)); CreatedByUserId = Required(createdByUserId, nameof(createdByUserId));
        var normalized = reason?.Trim(); Reason = string.IsNullOrWhiteSpace(normalized) || normalized.Length > 500
            ? throw new ArgumentException("A customer collection exception reason is required.", nameof(reason)) : normalized;
        ExcludedUntilDate = excludedUntilDate; CreatedUtc = CustomerCollectionPolicy.Utc(nowUtc);
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid PolicyId { get; private set; }
    public Guid CustomerId { get; private set; } public string Reason { get; private set; } = null!; public DateOnly? ExcludedUntilDate { get; private set; }
    public Guid CreatedByUserId { get; private set; } public DateTime CreatedUtc { get; private set; }
    public CustomerCollectionPolicy Policy { get; private set; } = null!;
    private static Guid Required(Guid value, string name) => value == Guid.Empty ? throw new ArgumentException($"{name} is required.", name) : value;
}

public sealed class CustomerStatementSnapshot : ICompanyOwnedEntity
{
    private readonly List<CustomerStatementItem> _items = [];
    private CustomerStatementSnapshot() { }
    public CustomerStatementSnapshot(Guid id, Guid companyId, Guid customerId, string customerName, DateOnly fromDate,
        DateOnly cutoffDate, string timeZoneId, string locale, string currency, decimal openingBalance,
        decimal invoiceActivity, decimal allocationActivity, decimal creditActivity, decimal closingBalance,
        string checksum, string sourceManifestJson, string sourceManifestHash, string fileName, byte[] renderedContent,
        string contentHash, string idempotencyKey, Guid createdByUserId, DateTime createdUtc)
    {
        if (cutoffDate < fromDate) throw new ArgumentException("Statement cutoff must not precede its start date.");
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = Req(companyId); CustomerId = Req(customerId);
        CreatedByUserId = Req(createdByUserId); CustomerName = Text(customerName, 300); FromDate = fromDate; CutoffDate = cutoffDate;
        TimeZoneId = Text(timeZoneId, 100); Locale = Text(locale, 16); Currency = Text(currency, 3).ToUpperInvariant();
        OpeningBalance = Money(openingBalance); InvoiceActivity = Money(invoiceActivity); AllocationActivity = Money(allocationActivity);
        CreditActivity = Money(creditActivity); ClosingBalance = Money(closingBalance); Checksum = Text(checksum, 64);
        SourceManifestJson = Text(sourceManifestJson, 1000000); SourceManifestHash = Text(sourceManifestHash, 64);
        MediaType = "text/csv"; FileName = Text(fileName, 255); RenderedContent = renderedContent?.ToArray() ?? throw new ArgumentNullException(nameof(renderedContent));
        ContentHash = Text(contentHash, 64); ContentLength = RenderedContent.LongLength; IdempotencyKey = Text(idempotencyKey, 200);
        CreatedUtc = CustomerCollectionPolicy.Utc(createdUtc);
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid CustomerId { get; private set; }
    public string CustomerName { get; private set; } = null!; public DateOnly FromDate { get; private set; } public DateOnly CutoffDate { get; private set; }
    public string TimeZoneId { get; private set; } = null!; public string Locale { get; private set; } = null!; public string Currency { get; private set; } = null!;
    public decimal OpeningBalance { get; private set; } public decimal InvoiceActivity { get; private set; } public decimal AllocationActivity { get; private set; }
    public decimal CreditActivity { get; private set; } public decimal ClosingBalance { get; private set; } public string Checksum { get; private set; } = null!;
    public string SourceManifestJson { get; private set; } = null!; public string SourceManifestHash { get; private set; } = null!;
    public string MediaType { get; private set; } = null!; public string FileName { get; private set; } = null!; public byte[] RenderedContent { get; private set; } = [];
    public string ContentHash { get; private set; } = null!; public long ContentLength { get; private set; } public string IdempotencyKey { get; private set; } = null!;
    public Guid CreatedByUserId { get; private set; } public DateTime CreatedUtc { get; private set; } public IReadOnlyCollection<CustomerStatementItem> Items => _items;
    private static Guid Req(Guid v) => v == Guid.Empty ? throw new ArgumentException("A statement identity is required.") : v;
    private static string Text(string v, int max) { var x = v?.Trim(); return string.IsNullOrWhiteSpace(x) || x.Length > max ? throw new ArgumentException("A statement value is invalid.") : x; }
    private static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}

public sealed class CustomerStatementItem : ICompanyOwnedEntity
{
    private CustomerStatementItem() { }
    public CustomerStatementItem(Guid id, Guid companyId, Guid statementId, int sequence, string itemType, Guid? invoiceId,
        Guid? paymentAllocationId, DateOnly effectiveDate, string reference, decimal debitAmount, decimal creditAmount,
        decimal runningBalance, string sourceHash)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = Req(companyId); StatementId = Req(statementId);
        if (sequence <= 0) throw new ArgumentOutOfRangeException(nameof(sequence)); Sequence = sequence; ItemType = Text(itemType, 32);
        InvoiceId = invoiceId; PaymentAllocationId = paymentAllocationId; EffectiveDate = effectiveDate; Reference = Text(reference, 200);
        DebitAmount = Money(debitAmount); CreditAmount = Money(creditAmount); RunningBalance = Money(runningBalance); SourceHash = Text(sourceHash, 64);
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid StatementId { get; private set; } public int Sequence { get; private set; }
    public string ItemType { get; private set; } = null!; public Guid? InvoiceId { get; private set; } public Guid? PaymentAllocationId { get; private set; }
    public DateOnly EffectiveDate { get; private set; } public string Reference { get; private set; } = null!; public decimal DebitAmount { get; private set; }
    public decimal CreditAmount { get; private set; } public decimal RunningBalance { get; private set; } public string SourceHash { get; private set; } = null!;
    public CustomerStatementSnapshot Statement { get; private set; } = null!;
    private static Guid Req(Guid v) => v == Guid.Empty ? throw new ArgumentException("A statement item identity is required.") : v;
    private static string Text(string v, int max) { var x = v?.Trim(); return string.IsNullOrWhiteSpace(x) || x.Length > max ? throw new ArgumentException("A statement item value is invalid.") : x; }
    private static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}

public sealed class CustomerCollectionCase : ICompanyOwnedEntity
{
    private CustomerCollectionCase() { }
    public CustomerCollectionCase(Guid id, Guid companyId, Guid customerId, Guid invoiceId, DateTime nowUtc)
    { Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = Req(companyId); CustomerId = Req(customerId); InvoiceId = Req(invoiceId); Status = CustomerCollectionCaseStatuses.Open; CreatedUtc = UpdatedUtc = CustomerCollectionPolicy.Utc(nowUtc); }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid CustomerId { get; private set; } public Guid InvoiceId { get; private set; }
    public string Status { get; private set; } = null!; public int ReminderStage { get; private set; } public bool IsOnHold { get; private set; } public string? HoldReason { get; private set; }
    public string? DisputeStatus { get; private set; } public string? DisputeReason { get; private set; } public decimal? DisputedAmount { get; private set; }
    public string? PromiseStatus { get; private set; } public decimal? PromiseAmount { get; private set; } public DateOnly? PromiseDueDate { get; private set; }
    public Guid? OwnerUserId { get; private set; } public DateTime? FollowUpDueUtc { get; private set; } public Guid? WorkTaskId { get; private set; }
    public long Version { get; private set; } public DateTime CreatedUtc { get; private set; } public DateTime UpdatedUtc { get; private set; }
    public void RecordDispute(decimal amount, string reason, Guid? owner, DateTime? followUp, DateTime now) { if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount)); DisputedAmount = decimal.Round(amount, 2); DisputeReason = Text(reason, 1000); DisputeStatus = "open"; IsOnHold = true; HoldReason = "customer_dispute"; Status = CustomerCollectionCaseStatuses.Disputed; OwnerUserId = owner; FollowUpDueUtc = followUp; Touch(now); }
    public void ResolveDispute(string resolution, DateTime now) { if (DisputeStatus != "open") throw new InvalidOperationException("There is no open dispute."); DisputeStatus = "resolved"; DisputeReason = Text(resolution, 1000); IsOnHold = false; HoldReason = null; Status = PromiseStatus == "pending" ? CustomerCollectionCaseStatuses.PromisePending : CustomerCollectionCaseStatuses.Open; Touch(now); }
    public void RecordPromise(decimal amount, DateOnly dueDate, Guid? owner, DateTime? followUp, DateTime now) { if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount)); PromiseAmount = decimal.Round(amount, 2); PromiseDueDate = dueDate; PromiseStatus = "pending"; OwnerUserId = owner; FollowUpDueUtc = followUp; Status = IsOnHold ? CustomerCollectionCaseStatuses.Disputed : CustomerCollectionCaseStatuses.PromisePending; Touch(now); }
    public void ResolvePromise(bool kept, string resolution, DateTime now) { if (PromiseStatus != "pending") throw new InvalidOperationException("There is no pending promise."); PromiseStatus = kept ? "kept" : "broken"; HoldReason = Text(resolution, 1000); Status = IsOnHold ? CustomerCollectionCaseStatuses.Disputed : CustomerCollectionCaseStatuses.Open; Touch(now); }
    public void RecordCustomerResponse(Guid? owner, DateTime? followUp, DateTime now) { OwnerUserId = owner; FollowUpDueUtc = followUp; Touch(now); }
    public void MarkReminderPrepared(int stage, DateTime now) { if (stage <= ReminderStage) return; ReminderStage = stage; Touch(now); }
    public void LinkTask(Guid taskId, DateTime now) { WorkTaskId = Req(taskId); Touch(now); }
    public void Resolve(DateTime now) { Status = CustomerCollectionCaseStatuses.Resolved; IsOnHold = false; Touch(now); }
    private void Touch(DateTime now) { Version++; UpdatedUtc = CustomerCollectionPolicy.Utc(now); }
    private static Guid Req(Guid v) => v == Guid.Empty ? throw new ArgumentException("A collection case identity is required.") : v;
    private static string Text(string v, int max) { var x = v?.Trim(); return string.IsNullOrWhiteSpace(x) || x.Length > max ? throw new ArgumentException("A collection case value is invalid.") : x; }
}

public sealed class CustomerCollectionAction : ICompanyOwnedEntity
{
    private CustomerCollectionAction() { }
    public CustomerCollectionAction(Guid id, Guid companyId, Guid caseId, string actionType, string outcome, string summary,
        string sourceHash, string idempotencyKey, Guid? actorUserId, DateTime occurredUtc)
    { Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = Req(companyId); CaseId = Req(caseId); ActionType = Text(actionType, 64); Outcome = Text(outcome, 32); Summary = Text(summary, 1000); SourceHash = Text(sourceHash, 64); IdempotencyKey = Text(idempotencyKey, 200); ActorUserId = actorUserId; OccurredUtc = CustomerCollectionPolicy.Utc(occurredUtc); }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid CaseId { get; private set; } public string ActionType { get; private set; } = null!;
    public string Outcome { get; private set; } = null!; public string Summary { get; private set; } = null!; public string SourceHash { get; private set; } = null!; public string IdempotencyKey { get; private set; } = null!;
    public Guid? ActorUserId { get; private set; } public DateTime OccurredUtc { get; private set; }
    private static Guid Req(Guid v) => v == Guid.Empty ? throw new ArgumentException("A collection action identity is required.") : v;
    private static string Text(string v, int max) { var x = v?.Trim(); return string.IsNullOrWhiteSpace(x) || x.Length > max ? throw new ArgumentException("A collection action value is invalid.") : x; }
}

public sealed class CustomerReminderDraft : ICompanyOwnedEntity
{
    private CustomerReminderDraft() { }
    public CustomerReminderDraft(Guid id, Guid companyId, Guid caseId, Guid invoiceId, Guid customerId, Guid? statementId,
        int stage, string recipientEmail, string subject, string body, decimal preparedOpenAmount, string currency,
        string sourceHash, string idempotencyKey, bool approvalRequired, Guid? approvalRequestId, Guid actorUserId, DateTime nowUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = Req(companyId); CaseId = Req(caseId); InvoiceId = Req(invoiceId); CustomerId = Req(customerId);
        if (stage <= 0) throw new ArgumentOutOfRangeException(nameof(stage)); Stage = stage; StatementId = statementId;
        RecipientEmail = Text(recipientEmail, 320).ToLowerInvariant(); Subject = Text(subject, 300); Body = Text(body, 8000);
        PreparedOpenAmount = decimal.Round(preparedOpenAmount, 2); Currency = Text(currency, 3).ToUpperInvariant(); SourceHash = Text(sourceHash, 64);
        IdempotencyKey = Text(idempotencyKey, 200); ApprovalRequestId = approvalRequestId; PreparedByUserId = Req(actorUserId);
        Status = approvalRequired ? CustomerReminderDraftStatuses.AwaitingApproval : CustomerReminderDraftStatuses.Prepared; CreatedUtc = UpdatedUtc = CustomerCollectionPolicy.Utc(nowUtc);
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid CaseId { get; private set; } public Guid InvoiceId { get; private set; } public Guid CustomerId { get; private set; }
    public Guid? StatementId { get; private set; } public int Stage { get; private set; } public string RecipientEmail { get; private set; } = null!; public string Subject { get; private set; } = null!; public string Body { get; private set; } = null!;
    public decimal PreparedOpenAmount { get; private set; } public string Currency { get; private set; } = null!; public string SourceHash { get; private set; } = null!; public string IdempotencyKey { get; private set; } = null!;
    public string Status { get; private set; } = null!; public Guid? ApprovalRequestId { get; private set; } public Guid PreparedByUserId { get; private set; } public long Version { get; private set; }
    public DateTime CreatedUtc { get; private set; } public DateTime UpdatedUtc { get; private set; }
    public void Queue(DateTime now) { Status = CustomerReminderDraftStatuses.Queued; Touch(now); }
    public void Accept(DateTime now) { Status = CustomerReminderDraftStatuses.Accepted; Touch(now); }
    public void Block(DateTime now) { Status = CustomerReminderDraftStatuses.Blocked; Touch(now); }
    public void Fail(bool ambiguous, DateTime now) { Status = ambiguous ? CustomerReminderDraftStatuses.ReconciliationRequired : CustomerReminderDraftStatuses.Failed; Touch(now); }
    private void Touch(DateTime now) { Version++; UpdatedUtc = CustomerCollectionPolicy.Utc(now); }
    private static Guid Req(Guid v) => v == Guid.Empty ? throw new ArgumentException("A reminder identity is required.") : v;
    private static string Text(string v, int max) { var x = v?.Trim(); return string.IsNullOrWhiteSpace(x) || x.Length > max ? throw new ArgumentException("A reminder value is invalid.") : x; }
}

public sealed class CustomerReminderDelivery : ICompanyOwnedEntity
{
    private CustomerReminderDelivery() { }
    public CustomerReminderDelivery(Guid id, Guid companyId, Guid reminderDraftId, string sourceHash, string recipientEmail,
        string idempotencyKey, Guid requestedByUserId, DateTime nowUtc)
    { Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = Req(companyId); ReminderDraftId = Req(reminderDraftId); SourceHash = Text(sourceHash, 64); RecipientEmail = Text(recipientEmail, 320); IdempotencyKey = Text(idempotencyKey, 200); RequestedByUserId = Req(requestedByUserId); Status = "pending"; CreatedUtc = UpdatedUtc = CustomerCollectionPolicy.Utc(nowUtc); }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid ReminderDraftId { get; private set; } public string SourceHash { get; private set; } = null!;
    public string RecipientEmail { get; private set; } = null!; public string IdempotencyKey { get; private set; } = null!; public Guid RequestedByUserId { get; private set; }
    public string Status { get; private set; } = null!; public int Attempts { get; private set; } public string? ProviderReference { get; private set; } public string? FailureCode { get; private set; }
    public string? FailureSummary { get; private set; } public DateTime CreatedUtc { get; private set; } public DateTime UpdatedUtc { get; private set; } public DateTime? AcceptedUtc { get; private set; }
    public void Start(DateTime now) { Status = "sending"; Attempts++; FailureCode = FailureSummary = null; UpdatedUtc = CustomerCollectionPolicy.Utc(now); }
    public void Accept(string? reference, DateTime now) { Status = "accepted"; ProviderReference = Trim(reference, 256); AcceptedUtc = UpdatedUtc = CustomerCollectionPolicy.Utc(now); }
    public void Block(string code, string summary, DateTime now) { Status = "blocked"; FailureCode = Text(code, 100); FailureSummary = Text(summary, 1000); UpdatedUtc = CustomerCollectionPolicy.Utc(now); }
    public void Fail(string code, string summary, bool ambiguous, DateTime now) { Status = ambiguous ? "reconciliation_required" : "failed"; FailureCode = Text(code, 100); FailureSummary = Text(summary, 1000); UpdatedUtc = CustomerCollectionPolicy.Utc(now); }
    private static Guid Req(Guid v) => v == Guid.Empty ? throw new ArgumentException("A reminder delivery identity is required.") : v;
    private static string Text(string v, int max) { var x = v?.Trim(); return string.IsNullOrWhiteSpace(x) || x.Length > max ? throw new ArgumentException("A reminder delivery value is invalid.") : x; }
    private static string? Trim(string? v, int max) => string.IsNullOrWhiteSpace(v) ? null : v.Trim()[..Math.Min(v.Trim().Length, max)];
}

public sealed class CustomerCollectionWorkerLease : ICompanyOwnedEntity
{
    private CustomerCollectionWorkerLease() { }
    public CustomerCollectionWorkerLease(Guid id, Guid companyId, DateTime nowUtc)
    { Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId == Guid.Empty ? throw new ArgumentException("CompanyId is required.") : companyId; UpdatedUtc = CustomerCollectionPolicy.Utc(nowUtc); }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public string? LeaseOwner { get; private set; }
    public DateTime? LeaseExpiresUtc { get; private set; } public int AttemptCount { get; private set; } public DateTime? NextAttemptUtc { get; private set; }
    public string? LastFailureCode { get; private set; } public string? LastFailureSummary { get; private set; } public long Version { get; private set; }
    public bool IsBlocked { get; private set; } public DateTime? BlockedUtc { get; private set; } public DateTime UpdatedUtc { get; private set; }
    public bool TryClaim(string owner, DateTime nowUtc, TimeSpan lease)
    {
        var now = CustomerCollectionPolicy.Utc(nowUtc);
        if (IsBlocked || NextAttemptUtc > now || LeaseExpiresUtc > now && !string.Equals(LeaseOwner, owner, StringComparison.Ordinal)) return false;
        LeaseOwner = Required(owner, 128); LeaseExpiresUtc = now.Add(lease); AttemptCount++; UpdatedUtc = now; Version++; return true;
    }
    public bool IsClaimedBy(string owner, DateTime nowUtc) => LeaseOwner == owner && LeaseExpiresUtc > CustomerCollectionPolicy.Utc(nowUtc);
    public void Complete(string owner, DateTime nowUtc)
    {
        if (!IsClaimedBy(owner, nowUtc)) return; LeaseOwner = null; LeaseExpiresUtc = null; NextAttemptUtc = null;
        AttemptCount = 0; LastFailureCode = LastFailureSummary = null; UpdatedUtc = CustomerCollectionPolicy.Utc(nowUtc); Version++;
    }
    public void Retry(string owner, string code, string summary, DateTime nextAttemptUtc, bool block, DateTime nowUtc)
    {
        if (LeaseOwner != owner) return; LeaseOwner = null; LeaseExpiresUtc = null; LastFailureCode = Required(code, 100);
        LastFailureSummary = Required(summary, 1000); IsBlocked = block; BlockedUtc = block ? CustomerCollectionPolicy.Utc(nowUtc) : null;
        NextAttemptUtc = block ? null : CustomerCollectionPolicy.Utc(nextAttemptUtc); UpdatedUtc = CustomerCollectionPolicy.Utc(nowUtc); Version++;
    }
    public void Reset(DateTime nowUtc) { LeaseOwner = null; LeaseExpiresUtc = null; NextAttemptUtc = null; AttemptCount = 0; IsBlocked = false; BlockedUtc = null; LastFailureCode = LastFailureSummary = null; UpdatedUtc = CustomerCollectionPolicy.Utc(nowUtc); Version++; }
    private static string Required(string value, int max) { var x = value?.Trim(); return string.IsNullOrWhiteSpace(x) || x.Length > max ? throw new ArgumentException("A collection worker lease value is invalid.") : x; }
}
