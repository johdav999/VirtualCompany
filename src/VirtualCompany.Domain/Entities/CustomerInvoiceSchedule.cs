namespace VirtualCompany.Domain.Entities;

public static class CustomerInvoiceScheduleStatuses
{
    public const string Draft = "draft";
    public const string Active = "active";
    public const string Paused = "paused";
    public const string Ended = "ended";
}

public static class CustomerInvoiceScheduleCadences
{
    public const string Monthly = "monthly";
    public const string Quarterly = "quarterly";
    public const string Yearly = "yearly";
    public static int Months(string value) => Normalize(value) switch { Monthly => 1, Quarterly => 3, Yearly => 12, _ => throw new ArgumentOutOfRangeException(nameof(value)) };
    public static string Normalize(string value) => value.Trim().ToLowerInvariant() is var normalized && normalized is Monthly or Quarterly or Yearly
        ? normalized : throw new ArgumentOutOfRangeException(nameof(value), "Use monthly, quarterly, or yearly cadence.");
}

public static class CustomerInvoiceScheduleBusinessDayConventions
{
    public const string Calendar = "calendar";
    public const string Following = "following";
    public const string Preceding = "preceding";
    public static string Normalize(string value) => value.Trim().ToLowerInvariant() is var normalized && normalized is Calendar or Following or Preceding
        ? normalized : throw new ArgumentOutOfRangeException(nameof(value), "Use calendar, following, or preceding business-day convention.");
}

public static class CustomerInvoiceScheduleProrationRules
{
    public const string None = "none";
    public const string Daily = "daily";
    public static string Normalize(string value) => value.Trim().ToLowerInvariant() is var normalized && normalized is None or Daily
        ? normalized : throw new ArgumentOutOfRangeException(nameof(value), "Use none or daily proration.");
}

public static class CustomerInvoiceScheduleOccurrenceStatuses
{
    public const string Pending = "pending";
    public const string Processing = "processing";
    public const string Generated = "generated";
    public const string Blocked = "blocked";
    public const string Failed = "failed";
}

public sealed class CustomerInvoiceSchedule : ICompanyOwnedEntity
{
    private CustomerInvoiceSchedule() { }
    public CustomerInvoiceSchedule(Guid id, Guid companyId, Guid customerId, string name, DateOnly startDate,
        DateOnly? endDate, string cadence, int billingDay, string timeZoneId, string businessDayConvention,
        string prorationRule, int dueDateOffsetDays, string documentType, string currency, string paymentTermKind,
        int paymentTermDays, string? buyerReference, string? sellerReference, string? notes, string deliveryIntent,
        bool autoIssueEnabled, string templateHash, Guid actorUserId, DateTime nowUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = RequiredId(companyId, nameof(companyId));
        CreatedByUserId = UpdatedByUserId = RequiredId(actorUserId, nameof(actorUserId)); CreatedUtc = UpdatedUtc = nowUtc;
        SetTerms(customerId, name, startDate, endDate, cadence, billingDay, timeZoneId, businessDayConvention,
            prorationRule, dueDateOffsetDays, documentType, currency, paymentTermKind, paymentTermDays,
            buyerReference, sellerReference, notes, deliveryIntent, autoIssueEnabled, templateHash);
        Status = CustomerInvoiceScheduleStatuses.Draft; Version = 1; TemplateVersion = 1;
        NextOccurrenceDate = ResolveOccurrence(startDate);
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid CustomerId { get; private set; }
    public string Name { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public DateOnly StartDate { get; private set; }
    public DateOnly? EndDate { get; private set; }
    public string Cadence { get; private set; } = null!;
    public int BillingDay { get; private set; }
    public string TimeZoneId { get; private set; } = null!;
    public string BusinessDayConvention { get; private set; } = null!;
    public string ProrationRule { get; private set; } = null!;
    public int DueDateOffsetDays { get; private set; }
    public string DocumentType { get; private set; } = null!;
    public string Currency { get; private set; } = null!;
    public string PaymentTermKind { get; private set; } = null!;
    public int PaymentTermDays { get; private set; }
    public string? BuyerReference { get; private set; }
    public string? SellerReference { get; private set; }
    public string? Notes { get; private set; }
    public string DeliveryIntent { get; private set; } = null!;
    public bool AutoIssueEnabled { get; private set; }
    public string TemplateHash { get; private set; } = null!;
    public long TemplateVersion { get; private set; }
    public DateOnly NextOccurrenceDate { get; private set; }
    public long Version { get; private set; }
    public Guid? ApprovalRequestId { get; private set; }
    public long? ApprovalTemplateVersion { get; private set; }
    public string? ApprovalTemplateHash { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public Guid UpdatedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public FinanceCounterparty Customer { get; private set; } = null!;
    public ApprovalRequest? ApprovalRequest { get; private set; }
    public ICollection<CustomerInvoiceScheduleLine> Lines { get; } = new List<CustomerInvoiceScheduleLine>();
    public ICollection<CustomerInvoiceScheduleEvidenceLink> EvidenceLinks { get; } = new List<CustomerInvoiceScheduleEvidenceLink>();
    public ICollection<CustomerInvoiceScheduleOccurrence> Occurrences { get; } = new List<CustomerInvoiceScheduleOccurrence>();
    public void Update(Guid customerId, string name, DateOnly startDate, DateOnly? endDate, string cadence, int billingDay,
        string timeZoneId, string businessDayConvention, string prorationRule, int dueDateOffsetDays, string documentType,
        string currency, string paymentTermKind, int paymentTermDays, string? buyerReference, string? sellerReference,
        string? notes, string deliveryIntent, bool autoIssueEnabled, string templateHash, Guid actorUserId, DateTime nowUtc)
    {
        if (Status == CustomerInvoiceScheduleStatuses.Ended) throw new InvalidOperationException("An ended invoice schedule cannot be changed.");
        SetTerms(customerId, name, startDate, endDate, cadence, billingDay, timeZoneId, businessDayConvention, prorationRule,
            dueDateOffsetDays, documentType, currency, paymentTermKind, paymentTermDays, buyerReference, sellerReference,
            notes, deliveryIntent, autoIssueEnabled, templateHash);
        ApprovalRequestId = null;
        ApprovalTemplateVersion = null;
        ApprovalTemplateHash = null;
        TemplateVersion++;
        Status = CustomerInvoiceScheduleStatuses.Draft;
        NextOccurrenceDate = NextOccurrenceDate < StartDate ? ResolveOccurrence(StartDate) : ResolveOccurrence(NextOccurrenceDate);
        Touch(actorUserId, nowUtc);
    }
    public void BindApproval(Guid approvalRequestId, Guid actorUserId, DateTime nowUtc)
    {
        EnsureNotEnded();
        ApprovalRequestId = RequiredId(approvalRequestId, nameof(approvalRequestId));
        ApprovalTemplateVersion = TemplateVersion;
        ApprovalTemplateHash = TemplateHash;
        UpdatedByUserId = RequiredId(actorUserId, nameof(actorUserId));
        UpdatedUtc = nowUtc;
        Version++;
    }
    public void Activate(Guid actorUserId, DateTime nowUtc, DateOnly localDate)
    {
        EnsureNotEnded();
        SkipPastOccurrences(localDate);
        Status = CustomerInvoiceScheduleStatuses.Active;
        Touch(actorUserId, nowUtc);
    }
    public void Pause(Guid actorUserId, DateTime nowUtc) { EnsureNotEnded(); Status = CustomerInvoiceScheduleStatuses.Paused; Touch(actorUserId, nowUtc); }
    public void Resume(Guid actorUserId, DateTime nowUtc, DateOnly localDate, bool allowBackdatedGeneration)
    {
        EnsureNotEnded();
        if (!allowBackdatedGeneration)
            SkipPastOccurrences(localDate);
        Status = CustomerInvoiceScheduleStatuses.Active;
        Touch(actorUserId, nowUtc);
    }
    public void End(Guid actorUserId, DateTime nowUtc) { Status = CustomerInvoiceScheduleStatuses.Ended; Touch(actorUserId, nowUtc); }
    public void PauseAfterBlockedOccurrence(DateTime nowUtc)
    {
        if (Status != CustomerInvoiceScheduleStatuses.Active) return;
        Status = CustomerInvoiceScheduleStatuses.Paused;
        UpdatedUtc = nowUtc;
        Version++;
    }
    public void AdvanceAfterGeneration(DateOnly occurrenceDate, Guid actorUserId, DateTime nowUtc)
    {
        if (occurrenceDate != NextOccurrenceDate) return;
        NextOccurrenceDate = NextOccurrenceAfter(occurrenceDate);
        if (EndDate.HasValue && NextOccurrenceDate > EndDate.Value) Status = CustomerInvoiceScheduleStatuses.Ended;
        Touch(actorUserId, nowUtc);
    }
    public DateOnly ResolveOccurrence(DateOnly anchor)
    {
        var nominal = NominalDate(anchor);
        while (nominal < StartDate || ApplyBusinessDayConvention(nominal) < StartDate)
            nominal = NominalDate(nominal.AddMonths(CustomerInvoiceScheduleCadences.Months(Cadence)));
        return ApplyBusinessDayConvention(nominal);
    }
    public DateOnly NextOccurrenceAfter(DateOnly occurrenceDate)
    {
        var nominal = NominalDate(occurrenceDate);
        for (var i = 0; i < 480; i++)
        {
            nominal = NominalDate(nominal.AddMonths(CustomerInvoiceScheduleCadences.Months(Cadence)));
            var candidate = ApplyBusinessDayConvention(nominal);
            if (candidate >= StartDate && candidate > occurrenceDate)
                return candidate;
        }
        throw new InvalidOperationException("Could not resolve the next invoice schedule occurrence.");
    }
    public decimal ProrationFactorFor(DateOnly occurrenceDate)
    {
        if (ProrationRule != CustomerInvoiceScheduleProrationRules.Daily || occurrenceDate != ResolveOccurrence(StartDate))
            return 1m;

        var firstNominal = FirstNominalOccurrence();
        var periodStart = NominalDate(firstNominal.AddMonths(-CustomerInvoiceScheduleCadences.Months(Cadence)));
        var chargeStart = StartDate > periodStart ? StartDate : periodStart;
        var periodDays = firstNominal.DayNumber - periodStart.DayNumber;
        var chargeDays = firstNominal.DayNumber - chargeStart.DayNumber;
        return periodDays == 0 ? 1m : decimal.Round((decimal)chargeDays / periodDays, 6, MidpointRounding.AwayFromZero);
    }
    public DateOnly DueDateFor(DateOnly issueDate) => ApplyBusinessDayConvention(issueDate.AddDays(DueDateOffsetDays));
    private DateOnly ApplyBusinessDayConvention(DateOnly date)
    {
        if (BusinessDayConvention == CustomerInvoiceScheduleBusinessDayConventions.Calendar) return date;
        var direction = BusinessDayConvention == CustomerInvoiceScheduleBusinessDayConventions.Following ? 1 : -1;
        while (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) date = date.AddDays(direction);
        return date;
    }
    private DateOnly NominalDate(DateOnly anchor) => new(anchor.Year, anchor.Month, Math.Min(BillingDay, DateTime.DaysInMonth(anchor.Year, anchor.Month)));
    private DateOnly FirstNominalOccurrence()
    {
        var nominal = NominalDate(StartDate);
        while (nominal < StartDate || ApplyBusinessDayConvention(nominal) < StartDate)
            nominal = NominalDate(nominal.AddMonths(CustomerInvoiceScheduleCadences.Months(Cadence)));
        return nominal;
    }
    private void SkipPastOccurrences(DateOnly localDate)
    {
        while (NextOccurrenceDate < localDate)
            NextOccurrenceDate = NextOccurrenceAfter(NextOccurrenceDate);
    }
    private void SetTerms(Guid customerId, string name, DateOnly startDate, DateOnly? endDate, string cadence, int billingDay,
        string timeZoneId, string businessDayConvention, string prorationRule, int dueDateOffsetDays, string documentType,
        string currency, string paymentTermKind, int paymentTermDays, string? buyerReference, string? sellerReference,
        string? notes, string deliveryIntent, bool autoIssueEnabled, string templateHash)
    {
        CustomerId = RequiredId(customerId, nameof(customerId)); Name = Required(name, 200); StartDate = startDate;
        EndDate = endDate; if (EndDate.HasValue && EndDate.Value < StartDate) throw new ArgumentException("End date cannot precede start date.");
        Cadence = CustomerInvoiceScheduleCadences.Normalize(cadence); if (billingDay is < 1 or > 31) throw new ArgumentOutOfRangeException(nameof(billingDay)); BillingDay = billingDay;
        TimeZoneId = Required(timeZoneId, 100); _ = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        BusinessDayConvention = CustomerInvoiceScheduleBusinessDayConventions.Normalize(businessDayConvention); ProrationRule = CustomerInvoiceScheduleProrationRules.Normalize(prorationRule);
        if (dueDateOffsetDays is < 0 or > 365) throw new ArgumentOutOfRangeException(nameof(dueDateOffsetDays)); DueDateOffsetDays = dueDateOffsetDays;
        DocumentType = CustomerInvoiceDraftDocumentTypes.Normalize(documentType); Currency = Required(currency, 3).ToUpperInvariant();
        PaymentTermKind = Required(paymentTermKind, 32).ToLowerInvariant(); if (paymentTermDays is < 0 or > 365) throw new ArgumentOutOfRangeException(nameof(paymentTermDays)); PaymentTermDays = paymentTermDays;
        BuyerReference = Optional(buyerReference, 100); SellerReference = Optional(sellerReference, 100); Notes = Optional(notes, 2000);
        DeliveryIntent = Required(deliveryIntent, 32).ToLowerInvariant(); AutoIssueEnabled = autoIssueEnabled;
        TemplateHash = Required(templateHash, 64).ToLowerInvariant();
    }
    private void EnsureNotEnded() { if (Status == CustomerInvoiceScheduleStatuses.Ended) throw new InvalidOperationException("This invoice schedule has ended."); }
    private void Touch(Guid actorUserId, DateTime nowUtc) { UpdatedByUserId = RequiredId(actorUserId, nameof(actorUserId)); UpdatedUtc = nowUtc; Version++; }
    internal static Guid RequiredId(Guid value, string name) => value == Guid.Empty ? throw new ArgumentException("A value is required.", name) : value;
    internal static string Required(string value, int maximum)
    {
        var result = value?.Trim() ?? string.Empty;
        if (result.Length == 0 || result.Length > maximum) throw new ArgumentException("A required value is invalid.");
        return result;
    }
    internal static string? Optional(string? value, int maximum) { var result = value?.Trim(); return string.IsNullOrEmpty(result) ? null : result.Length <= maximum ? result : throw new ArgumentException("A value is too long."); }
}

public sealed class CustomerInvoiceScheduleLine : ICompanyOwnedEntity
{
    private CustomerInvoiceScheduleLine() { }
    public CustomerInvoiceScheduleLine(Guid id, Guid companyId, Guid scheduleId, int sequence, string description,
        decimal quantity, string unit, decimal unitPrice, decimal discountPercent, string taxRuleKey, string taxClassification,
        string taxEvidenceJson, string dimensionFactsJson, string? revenueAccountRoleKey, string? sourceReference, string? orderReference)
    { Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = CustomerInvoiceSchedule.RequiredId(companyId, nameof(companyId)); ScheduleId = CustomerInvoiceSchedule.RequiredId(scheduleId, nameof(scheduleId)); if (sequence < 1) throw new ArgumentOutOfRangeException(nameof(sequence)); Sequence = sequence; Description = CustomerInvoiceSchedule.Required(description, 500); if (quantity <= 0 || unitPrice < 0 || discountPercent is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(quantity)); Quantity = quantity; Unit = CustomerInvoiceSchedule.Required(unit, 32); UnitPrice = unitPrice; DiscountPercent = discountPercent; TaxRuleKey = CustomerInvoiceSchedule.Required(taxRuleKey, 100); TaxClassification = CustomerInvoiceSchedule.Required(taxClassification, 100); TaxEvidenceJson = CustomerInvoiceSchedule.Required(taxEvidenceJson, 8000); DimensionFactsJson = CustomerInvoiceSchedule.Required(dimensionFactsJson, 8000); RevenueAccountRoleKey = CustomerInvoiceSchedule.Optional(revenueAccountRoleKey, 100); SourceReference = CustomerInvoiceSchedule.Optional(sourceReference, 200); OrderReference = CustomerInvoiceSchedule.Optional(orderReference, 200); }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid ScheduleId { get; private set; } public int Sequence { get; private set; } public string Description { get; private set; } = null!; public decimal Quantity { get; private set; } public string Unit { get; private set; } = null!; public decimal UnitPrice { get; private set; } public decimal DiscountPercent { get; private set; } public string TaxRuleKey { get; private set; } = null!; public string TaxClassification { get; private set; } = null!; public string TaxEvidenceJson { get; private set; } = null!; public string DimensionFactsJson { get; private set; } = null!; public string? RevenueAccountRoleKey { get; private set; } public string? SourceReference { get; private set; } public string? OrderReference { get; private set; } public CustomerInvoiceSchedule Schedule { get; private set; } = null!;
}

public sealed class CustomerInvoiceScheduleEvidenceLink : ICompanyOwnedEntity
{
    private CustomerInvoiceScheduleEvidenceLink() { }
    public CustomerInvoiceScheduleEvidenceLink(Guid id, Guid companyId, Guid scheduleId, Guid documentId)
    { Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = CustomerInvoiceSchedule.RequiredId(companyId, nameof(companyId)); ScheduleId = CustomerInvoiceSchedule.RequiredId(scheduleId, nameof(scheduleId)); DocumentId = CustomerInvoiceSchedule.RequiredId(documentId, nameof(documentId)); }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid ScheduleId { get; private set; } public Guid DocumentId { get; private set; } public CustomerInvoiceSchedule Schedule { get; private set; } = null!; public CompanyKnowledgeDocument Document { get; private set; } = null!;
}

public sealed class CustomerInvoiceScheduleOccurrence : ICompanyOwnedEntity
{
    private CustomerInvoiceScheduleOccurrence() { }
    public CustomerInvoiceScheduleOccurrence(Guid id, Guid companyId, Guid scheduleId, DateOnly occurrenceDate,
        DateOnly issueDate, DateOnly dueDate, long scheduleVersion, long templateVersion, string templateHash, DateTime nowUtc)
    { Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = CustomerInvoiceSchedule.RequiredId(companyId, nameof(companyId)); ScheduleId = CustomerInvoiceSchedule.RequiredId(scheduleId, nameof(scheduleId)); OccurrenceDate = occurrenceDate; IssueDate = issueDate; DueDate = dueDate; ScheduleVersion = scheduleVersion; TemplateVersion = templateVersion; TemplateHash = CustomerInvoiceSchedule.Required(templateHash, 64).ToLowerInvariant(); Status = CustomerInvoiceScheduleOccurrenceStatuses.Pending; Version = 1; CreatedUtc = UpdatedUtc = nowUtc; }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid ScheduleId { get; private set; } public DateOnly OccurrenceDate { get; private set; } public DateOnly IssueDate { get; private set; } public DateOnly DueDate { get; private set; } public long ScheduleVersion { get; private set; } public long TemplateVersion { get; private set; } public string TemplateHash { get; private set; } = null!; public long Version { get; private set; } public string Status { get; private set; } = null!; public Guid? DraftId { get; private set; } public Guid? TaskId { get; private set; } public int AttemptCount { get; private set; } public string? FailureCode { get; private set; } public string? FailureSummary { get; private set; } public string? LeaseOwner { get; private set; } public DateTime? LeaseExpiresUtc { get; private set; } public DateTime? NextAttemptUtc { get; private set; } public DateTime CreatedUtc { get; private set; } public DateTime UpdatedUtc { get; private set; } public CustomerInvoiceSchedule Schedule { get; private set; } = null!;
    public bool TryClaim(string owner, DateTime nowUtc, TimeSpan duration) { if (Status is CustomerInvoiceScheduleOccurrenceStatuses.Generated or CustomerInvoiceScheduleOccurrenceStatuses.Blocked || NextAttemptUtc > nowUtc || (LeaseExpiresUtc > nowUtc && LeaseOwner != owner)) return false; LeaseOwner = CustomerInvoiceSchedule.Required(owner, 128); LeaseExpiresUtc = nowUtc.Add(duration); NextAttemptUtc = null; Status = CustomerInvoiceScheduleOccurrenceStatuses.Processing; AttemptCount++; UpdatedUtc = nowUtc; Version++; return true; }
    public bool IsClaimedBy(string owner, DateTime nowUtc) => Status == CustomerInvoiceScheduleOccurrenceStatuses.Processing && string.Equals(LeaseOwner, owner, StringComparison.Ordinal) && LeaseExpiresUtc >= nowUtc;
    public bool TryMarkGenerated(string owner, Guid draftId, DateTime nowUtc) { if (!IsClaimedBy(owner, nowUtc)) return false; DraftId = CustomerInvoiceSchedule.RequiredId(draftId, nameof(draftId)); Status = CustomerInvoiceScheduleOccurrenceStatuses.Generated; FailureCode = FailureSummary = LeaseOwner = null; LeaseExpiresUtc = NextAttemptUtc = null; UpdatedUtc = nowUtc; Version++; return true; }
    public bool TryMarkBlocked(string owner, string code, string summary, DateTime nowUtc, Guid? taskId = null, Guid? draftId = null) { if (!IsClaimedBy(owner, nowUtc)) return false; Status = CustomerInvoiceScheduleOccurrenceStatuses.Blocked; FailureCode = CustomerInvoiceSchedule.Required(code, 100); FailureSummary = CustomerInvoiceSchedule.Optional(summary, 1000); TaskId = taskId; DraftId = draftId ?? DraftId; LeaseOwner = null; LeaseExpiresUtc = NextAttemptUtc = null; UpdatedUtc = nowUtc; Version++; return true; }
    public bool TryReleaseRetry(string owner, string code, string summary, DateTime nowUtc, TimeSpan retryDelay) { if (!IsClaimedBy(owner, nowUtc)) return false; Status = CustomerInvoiceScheduleOccurrenceStatuses.Failed; FailureCode = CustomerInvoiceSchedule.Required(code, 100); FailureSummary = CustomerInvoiceSchedule.Optional(summary, 1000); LeaseOwner = null; LeaseExpiresUtc = null; NextAttemptUtc = nowUtc.Add(retryDelay); UpdatedUtc = nowUtc; Version++; return true; }
    public void ResetBlockedForRetry(long scheduleVersion, long templateVersion, string templateHash, DateOnly issueDate, DateOnly dueDate, DateTime nowUtc) { if (Status is not (CustomerInvoiceScheduleOccurrenceStatuses.Blocked or CustomerInvoiceScheduleOccurrenceStatuses.Failed)) return; ScheduleVersion = scheduleVersion; TemplateVersion = templateVersion; TemplateHash = CustomerInvoiceSchedule.Required(templateHash, 64).ToLowerInvariant(); IssueDate = issueDate; DueDate = dueDate; Status = CustomerInvoiceScheduleOccurrenceStatuses.Pending; FailureCode = FailureSummary = LeaseOwner = null; LeaseExpiresUtc = NextAttemptUtc = null; UpdatedUtc = nowUtc; Version++; }
}

public sealed class CustomerInvoiceScheduleOperation : ICompanyOwnedEntity
{
    private CustomerInvoiceScheduleOperation() { }
    public CustomerInvoiceScheduleOperation(Guid id, Guid companyId, Guid scheduleId, string action, string idempotencyKey, string payloadHash, long resultVersion, DateTime createdUtc)
    { Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = CustomerInvoiceSchedule.RequiredId(companyId, nameof(companyId)); ScheduleId = CustomerInvoiceSchedule.RequiredId(scheduleId, nameof(scheduleId)); Action = CustomerInvoiceSchedule.Required(action, 32); IdempotencyKey = CustomerInvoiceSchedule.Required(idempotencyKey, 200); PayloadHash = CustomerInvoiceSchedule.Required(payloadHash, 64); ResultVersion = resultVersion; CreatedUtc = createdUtc; }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid ScheduleId { get; private set; } public string Action { get; private set; } = null!; public string IdempotencyKey { get; private set; } = null!; public string PayloadHash { get; private set; } = null!; public long ResultVersion { get; private set; } public DateTime CreatedUtc { get; private set; } public CustomerInvoiceSchedule Schedule { get; private set; } = null!;
}
