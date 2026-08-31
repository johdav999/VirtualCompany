namespace VirtualCompany.Domain.Entities;

public static class AccountingScheduleStatuses
{
    public const string Draft = "draft";
    public const string AwaitingApproval = "awaiting_approval";
    public const string Active = "active";
    public const string Paused = "paused";
    public const string Ended = "ended";

    public static string Normalize(string value) => Required(value, nameof(value), 32).ToLowerInvariant() switch
    {
        Draft => Draft,
        AwaitingApproval => AwaitingApproval,
        Active => Active,
        Paused => Paused,
        Ended => Ended,
        _ => throw new ArgumentOutOfRangeException(nameof(value), "The accounting schedule status is not supported.")
    };

    internal static string Required(string value, string name, int maximum)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || normalized.Length > maximum)
            throw new ArgumentException($"{name} is required and must be {maximum} characters or fewer.", name);
        return normalized;
    }
}

public static class AccountingScheduleTypes
{
    public const string RecurringFixed = "recurring_fixed";
    public const string DateAllocation = "date_allocation";
    public const string Accrual = "accrual";
    public const string Prepayment = "prepayment";

    public static string Normalize(string value) => AccountingScheduleStatuses.Required(value, nameof(value), 32).ToLowerInvariant() switch
    {
        RecurringFixed => RecurringFixed,
        DateAllocation => DateAllocation,
        Accrual => Accrual,
        Prepayment => Prepayment,
        _ => throw new ArgumentOutOfRangeException(nameof(value), "Use recurring_fixed, date_allocation, accrual, or prepayment.")
    };
}

public static class AccountingScheduleCadences
{
    public const string Once = "once";
    public const string Monthly = "monthly";
    public const string Quarterly = "quarterly";
    public const string Yearly = "yearly";

    public static string Normalize(string value) => AccountingScheduleStatuses.Required(value, nameof(value), 24).ToLowerInvariant() switch
    {
        Once => Once,
        Monthly => Monthly,
        Quarterly => Quarterly,
        Yearly => Yearly,
        _ => throw new ArgumentOutOfRangeException(nameof(value), "Use once, monthly, quarterly, or yearly cadence.")
    };

    public static int Months(string value) => Normalize(value) switch
    {
        Monthly => 1,
        Quarterly => 3,
        Yearly => 12,
        _ => 0
    };
}

public static class AccountingScheduleAmountBases
{
    public const string PerOccurrence = "per_occurrence";
    public const string TotalSchedule = "total_schedule";

    public static string Normalize(string value) => AccountingScheduleStatuses.Required(value, nameof(value), 24).ToLowerInvariant() switch
    {
        PerOccurrence => PerOccurrence,
        TotalSchedule => TotalSchedule,
        _ => throw new ArgumentOutOfRangeException(nameof(value), "Use per_occurrence or total_schedule amount basis.")
    };
}

public static class AccountingScheduleProrationRules
{
    public const string None = "none";
    public const string Daily = "daily";

    public static string Normalize(string value) => AccountingScheduleStatuses.Required(value, nameof(value), 16).ToLowerInvariant() switch
    {
        None => None,
        Daily => Daily,
        _ => throw new ArgumentOutOfRangeException(nameof(value), "Use none or daily proration.")
    };
}

public static class AccountingScheduleReversalRules
{
    public const string None = "none";
    public const string NextDay = "next_day";
    public const string NextPeriodStart = "next_period_start";

    public static string Normalize(string value) => AccountingScheduleStatuses.Required(value, nameof(value), 24).ToLowerInvariant() switch
    {
        None => None,
        NextDay => NextDay,
        NextPeriodStart => NextPeriodStart,
        _ => throw new ArgumentOutOfRangeException(nameof(value), "Use none, next_day, or next_period_start reversal rule.")
    };
}

public static class AccountingScheduleOccurrenceStatuses
{
    public const string Pending = "pending";
    public const string Processing = "processing";
    public const string Posted = "posted";
    public const string Reversed = "reversed";
    public const string Blocked = "blocked";
    public const string Failed = "failed";
}

public sealed class AccountingSchedule : ICompanyOwnedEntity
{
    private AccountingSchedule() { }

    public AccountingSchedule(Guid id, Guid companyId, string code, string name, string scheduleType,
        string cadence, string amountBasis, string prorationRule, DateOnly startDate, DateOnly? endDate,
        int occurrenceDay, string timeZoneId, string voucherSeriesCode, string currency,
        string reversalRule, Guid createdByUserId, DateTime createdUtc)
    {
        Id = RequiredId(id == Guid.Empty ? Guid.NewGuid() : id, nameof(id));
        CompanyId = RequiredId(companyId, nameof(companyId));
        CreatedByUserId = UpdatedByUserId = RequiredId(createdByUserId, nameof(createdByUserId));
        CreatedUtc = UpdatedUtc = Utc(createdUtc);
        Code = Required(code, nameof(code), 64).ToUpperInvariant();
        SetTerms(name, scheduleType, cadence, amountBasis, prorationRule, startDate, endDate,
            occurrenceDay, timeZoneId, voucherSeriesCode, currency, reversalRule);
        Status = AccountingScheduleStatuses.Draft;
        Version = 1;
        CurrentVersionNumber = 0;
        NextOccurrenceDate = ResolveOccurrence(startDate);
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string ScheduleType { get; private set; } = null!;
    public string Cadence { get; private set; } = null!;
    public string AmountBasis { get; private set; } = null!;
    public string ProrationRule { get; private set; } = null!;
    public DateOnly StartDate { get; private set; }
    public DateOnly? EndDate { get; private set; }
    public int OccurrenceDay { get; private set; }
    public string TimeZoneId { get; private set; } = null!;
    public string VoucherSeriesCode { get; private set; } = null!;
    public string Currency { get; private set; } = null!;
    public string ReversalRule { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public DateOnly NextOccurrenceDate { get; private set; }
    public Guid? CurrentVersionId { get; private set; }
    public int CurrentVersionNumber { get; private set; }
    public string? CurrentVersionHash { get; private set; }
    public Guid? ApprovalRequestId { get; private set; }
    public int? ApprovalVersionNumber { get; private set; }
    public string? ApprovalPayloadHash { get; private set; }
    public long Version { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public Guid UpdatedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public ApprovalRequest? ApprovalRequest { get; private set; }
    public AccountingScheduleVersion? CurrentVersion { get; private set; }
    public ICollection<AccountingScheduleVersion> Versions { get; } = new List<AccountingScheduleVersion>();
    public ICollection<AccountingScheduleOccurrence> Occurrences { get; } = new List<AccountingScheduleOccurrence>();

    public void ApplyProspectiveVersion(string name, string scheduleType, string cadence, string amountBasis,
        string prorationRule, DateOnly startDate, DateOnly? endDate, int occurrenceDay, string timeZoneId,
        string voucherSeriesCode, string currency, string reversalRule, Guid versionId, int versionNumber,
        string payloadHash, Guid actorUserId, DateTime updatedUtc)
    {
        if (Status == AccountingScheduleStatuses.Ended)
            throw new InvalidOperationException("An ended accounting schedule cannot be changed.");
        SetTerms(name, scheduleType, cadence, amountBasis, prorationRule, startDate, endDate,
            occurrenceDay, timeZoneId, voucherSeriesCode, currency, reversalRule);
        CurrentVersionId = RequiredId(versionId, nameof(versionId));
        if (versionNumber <= CurrentVersionNumber) throw new ArgumentOutOfRangeException(nameof(versionNumber));
        CurrentVersionNumber = versionNumber;
        CurrentVersionHash = Hash(payloadHash);
        ApprovalRequestId = null;
        ApprovalVersionNumber = null;
        ApprovalPayloadHash = null;
        Status = AccountingScheduleStatuses.Draft;
        if (NextOccurrenceDate < StartDate) NextOccurrenceDate = ResolveOccurrence(StartDate);
        Touch(actorUserId, updatedUtc);
    }

    public void Submit(Guid approvalRequestId, Guid actorUserId, DateTime now)
    {
        EnsureCurrentVersion();
        ApprovalRequestId = RequiredId(approvalRequestId, nameof(approvalRequestId));
        ApprovalVersionNumber = CurrentVersionNumber;
        ApprovalPayloadHash = CurrentVersionHash;
        Status = AccountingScheduleStatuses.AwaitingApproval;
        Touch(actorUserId, now);
    }

    public void Activate(Guid actorUserId, DateTime now, DateOnly localDate)
    {
        EnsureCurrentVersion();
        if (Status is AccountingScheduleStatuses.Ended)
            throw new InvalidOperationException("An ended accounting schedule cannot be activated.");
        while (NextOccurrenceDate < localDate) NextOccurrenceDate = NextAfter(NextOccurrenceDate);
        Status = AccountingScheduleStatuses.Active;
        Touch(actorUserId, now);
    }

    public void Pause(Guid actorUserId, DateTime now)
    {
        if (Status != AccountingScheduleStatuses.Active)
            throw new InvalidOperationException("Only an active accounting schedule can be paused.");
        Status = AccountingScheduleStatuses.Paused;
        Touch(actorUserId, now);
    }

    public void Resume(Guid actorUserId, DateTime now, DateOnly localDate, bool generateMissed)
    {
        if (Status != AccountingScheduleStatuses.Paused)
            throw new InvalidOperationException("Only a paused accounting schedule can be resumed.");
        if (!generateMissed)
            while (NextOccurrenceDate < localDate) NextOccurrenceDate = NextAfter(NextOccurrenceDate);
        Status = AccountingScheduleStatuses.Active;
        Touch(actorUserId, now);
    }

    public void End(Guid actorUserId, DateTime now)
    {
        Status = AccountingScheduleStatuses.Ended;
        Touch(actorUserId, now);
    }

    public void PauseForException(DateTime now)
    {
        if (Status != AccountingScheduleStatuses.Active) return;
        Status = AccountingScheduleStatuses.Paused;
        UpdatedUtc = Utc(now);
        Version++;
    }

    public void Advance(DateOnly occurrenceDate, Guid actorUserId, DateTime now)
    {
        if (occurrenceDate != NextOccurrenceDate) return;
        if (Cadence == AccountingScheduleCadences.Once)
        {
            Status = AccountingScheduleStatuses.Ended;
        }
        else
        {
            NextOccurrenceDate = NextAfter(occurrenceDate);
            if (EndDate.HasValue && NextOccurrenceDate > EndDate.Value)
                Status = AccountingScheduleStatuses.Ended;
        }
        Touch(actorUserId, now);
    }

    public DateOnly ResolveOccurrence(DateOnly anchor)
    {
        if (Cadence == AccountingScheduleCadences.Once) return StartDate;
        var date = new DateOnly(anchor.Year, anchor.Month, Math.Min(OccurrenceDay, DateTime.DaysInMonth(anchor.Year, anchor.Month)));
        while (date < StartDate)
            date = AddCadence(date);
        return date;
    }

    public DateOnly NextAfter(DateOnly occurrenceDate)
    {
        if (Cadence == AccountingScheduleCadences.Once) return occurrenceDate;
        var next = AddCadence(occurrenceDate);
        if (next <= occurrenceDate) throw new InvalidOperationException("The next schedule occurrence could not be resolved.");
        return next;
    }

    public DateOnly LocalDate(DateTime utcNow) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
        DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId)));

    private DateOnly AddCadence(DateOnly date)
    {
        var next = date.AddMonths(AccountingScheduleCadences.Months(Cadence));
        return new DateOnly(next.Year, next.Month, Math.Min(OccurrenceDay, DateTime.DaysInMonth(next.Year, next.Month)));
    }

    private void SetTerms(string name, string scheduleType, string cadence, string amountBasis,
        string prorationRule, DateOnly startDate, DateOnly? endDate, int occurrenceDay, string timeZoneId,
        string voucherSeriesCode, string currency, string reversalRule)
    {
        Name = Required(name, nameof(name), 200);
        ScheduleType = AccountingScheduleTypes.Normalize(scheduleType);
        Cadence = AccountingScheduleCadences.Normalize(cadence);
        AmountBasis = AccountingScheduleAmountBases.Normalize(amountBasis);
        ProrationRule = AccountingScheduleProrationRules.Normalize(prorationRule);
        if (startDate == default) throw new ArgumentException("StartDate is required.", nameof(startDate));
        if (endDate.HasValue && endDate.Value < startDate) throw new ArgumentOutOfRangeException(nameof(endDate));
        if (occurrenceDay is < 1 or > 31) throw new ArgumentOutOfRangeException(nameof(occurrenceDay));
        if (AmountBasis == AccountingScheduleAmountBases.TotalSchedule && !endDate.HasValue)
            throw new ArgumentException("A total-schedule allocation requires an end date.", nameof(endDate));
        if (Cadence == AccountingScheduleCadences.Once && endDate.HasValue && endDate != startDate)
            throw new ArgumentException("A one-time schedule must end on its start date.", nameof(endDate));
        ReversalRule = AccountingScheduleReversalRules.Normalize(reversalRule);
        if (ScheduleType == AccountingScheduleTypes.Accrual && ReversalRule == AccountingScheduleReversalRules.None)
            throw new ArgumentException("An accrual schedule requires an automatic reversal rule.", nameof(reversalRule));
        StartDate = startDate;
        EndDate = endDate;
        OccurrenceDay = occurrenceDay;
        TimeZoneId = Required(timeZoneId, nameof(timeZoneId), 100);
        _ = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        VoucherSeriesCode = Required(voucherSeriesCode, nameof(voucherSeriesCode), 32).ToUpperInvariant();
        Currency = Required(currency, nameof(currency), 3).ToUpperInvariant();
    }

    private void EnsureCurrentVersion()
    {
        if (!CurrentVersionId.HasValue || CurrentVersionNumber < 1 || string.IsNullOrWhiteSpace(CurrentVersionHash))
            throw new InvalidOperationException("The accounting schedule has no complete version to approve.");
    }

    private void Touch(Guid actorUserId, DateTime now)
    {
        UpdatedByUserId = RequiredId(actorUserId, nameof(actorUserId));
        UpdatedUtc = Utc(now);
        Version++;
    }

    internal static Guid RequiredId(Guid value, string name) => value == Guid.Empty
        ? throw new ArgumentException($"{name} is required.", name) : value;
    internal static string Required(string value, string name, int maximum) =>
        AccountingScheduleStatuses.Required(value, name, maximum);
    internal static string? Optional(string? value, string name, int maximum) =>
        string.IsNullOrWhiteSpace(value) ? null : Required(value, name, maximum);
    internal static string Hash(string value) => Required(value, nameof(value), 64).ToLowerInvariant();
    internal static DateTime Utc(DateTime value) => EntityTimestampNormalizer.NormalizeUtc(value, nameof(value));
}

public sealed class AccountingScheduleVersion : ICompanyOwnedEntity
{
    private AccountingScheduleVersion() { }
    public AccountingScheduleVersion(Guid id, Guid companyId, Guid scheduleId, int versionNumber,
        string payloadHash, string description, DateOnly effectiveFrom, Guid createdByUserId, DateTime createdUtc)
    {
        Id = AccountingSchedule.RequiredId(id, nameof(id)); CompanyId = AccountingSchedule.RequiredId(companyId, nameof(companyId));
        ScheduleId = AccountingSchedule.RequiredId(scheduleId, nameof(scheduleId));
        if (versionNumber < 1) throw new ArgumentOutOfRangeException(nameof(versionNumber));
        VersionNumber = versionNumber; PayloadHash = AccountingSchedule.Hash(payloadHash);
        Description = AccountingSchedule.Required(description, nameof(description), 500); EffectiveFrom = effectiveFrom;
        CreatedByUserId = AccountingSchedule.RequiredId(createdByUserId, nameof(createdByUserId)); CreatedUtc = AccountingSchedule.Utc(createdUtc);
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid ScheduleId { get; private set; }
    public int VersionNumber { get; private set; } public string PayloadHash { get; private set; } = null!;
    public string Description { get; private set; } = null!; public DateOnly EffectiveFrom { get; private set; }
    public Guid CreatedByUserId { get; private set; } public DateTime CreatedUtc { get; private set; }
    public AccountingSchedule Schedule { get; private set; } = null!;
    public ICollection<AccountingScheduleLine> Lines { get; } = new List<AccountingScheduleLine>();
    public ICollection<AccountingScheduleEvidenceLink> EvidenceLinks { get; } = new List<AccountingScheduleEvidenceLink>();
}

public sealed class AccountingScheduleLine : ICompanyOwnedEntity
{
    private AccountingScheduleLine() { }
    public AccountingScheduleLine(Guid id, Guid companyId, Guid scheduleVersionId, int sequence,
        Guid financeAccountId, decimal debitAmount, decimal creditAmount, string description)
    {
        Id = AccountingSchedule.RequiredId(id, nameof(id)); CompanyId = AccountingSchedule.RequiredId(companyId, nameof(companyId));
        ScheduleVersionId = AccountingSchedule.RequiredId(scheduleVersionId, nameof(scheduleVersionId));
        FinanceAccountId = AccountingSchedule.RequiredId(financeAccountId, nameof(financeAccountId));
        if (sequence < 1 || debitAmount < 0 || creditAmount < 0 || debitAmount > 0 && creditAmount > 0 || debitAmount == 0 && creditAmount == 0)
            throw new ArgumentException("A schedule line must have a positive debit or credit amount and a valid sequence.");
        Sequence = sequence; DebitAmount = debitAmount; CreditAmount = creditAmount;
        Description = AccountingSchedule.Required(description, nameof(description), 500);
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid ScheduleVersionId { get; private set; }
    public int Sequence { get; private set; } public Guid FinanceAccountId { get; private set; }
    public decimal DebitAmount { get; private set; } public decimal CreditAmount { get; private set; }
    public string Description { get; private set; } = null!; public AccountingScheduleVersion ScheduleVersion { get; private set; } = null!;
    public FinanceAccount FinanceAccount { get; private set; } = null!;
    public ICollection<AccountingScheduleLineDimension> DimensionAssignments { get; } = new List<AccountingScheduleLineDimension>();
}

public sealed class AccountingScheduleLineDimension : ICompanyOwnedEntity
{
    private AccountingScheduleLineDimension() { }
    public AccountingScheduleLineDimension(Guid id, Guid companyId, Guid scheduleLineId, Guid dimensionMemberId)
    { Id = AccountingSchedule.RequiredId(id, nameof(id)); CompanyId = AccountingSchedule.RequiredId(companyId, nameof(companyId)); ScheduleLineId = AccountingSchedule.RequiredId(scheduleLineId, nameof(scheduleLineId)); DimensionMemberId = AccountingSchedule.RequiredId(dimensionMemberId, nameof(dimensionMemberId)); }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid ScheduleLineId { get; private set; }
    public Guid DimensionMemberId { get; private set; } public AccountingScheduleLine ScheduleLine { get; private set; } = null!;
    public AccountingDimensionMember DimensionMember { get; private set; } = null!;
}

public sealed class AccountingScheduleEvidenceLink : ICompanyOwnedEntity
{
    private AccountingScheduleEvidenceLink() { }
    public AccountingScheduleEvidenceLink(Guid id, Guid companyId, Guid scheduleVersionId, Guid documentId,
        string title, string contentHash, DateTime linkedUtc)
    { Id = AccountingSchedule.RequiredId(id, nameof(id)); CompanyId = AccountingSchedule.RequiredId(companyId, nameof(companyId)); ScheduleVersionId = AccountingSchedule.RequiredId(scheduleVersionId, nameof(scheduleVersionId)); DocumentId = AccountingSchedule.RequiredId(documentId, nameof(documentId)); Title = AccountingSchedule.Required(title, nameof(title), 300); ContentHash = AccountingSchedule.Hash(contentHash); LinkedUtc = AccountingSchedule.Utc(linkedUtc); }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid ScheduleVersionId { get; private set; }
    public Guid DocumentId { get; private set; } public string Title { get; private set; } = null!;
    public string ContentHash { get; private set; } = null!; public DateTime LinkedUtc { get; private set; }
    public AccountingScheduleVersion ScheduleVersion { get; private set; } = null!; public CompanyKnowledgeDocument Document { get; private set; } = null!;
}

public sealed class AccountingScheduleApprovalBinding : ICompanyOwnedEntity
{
    private AccountingScheduleApprovalBinding() { }
    public AccountingScheduleApprovalBinding(Guid id, Guid companyId, Guid scheduleId, Guid scheduleVersionId,
        int versionNumber, string payloadHash, Guid approvalRequestId, DateTime boundUtc)
    { Id = AccountingSchedule.RequiredId(id, nameof(id)); CompanyId = AccountingSchedule.RequiredId(companyId, nameof(companyId)); ScheduleId = AccountingSchedule.RequiredId(scheduleId, nameof(scheduleId)); ScheduleVersionId = AccountingSchedule.RequiredId(scheduleVersionId, nameof(scheduleVersionId)); if (versionNumber < 1) throw new ArgumentOutOfRangeException(nameof(versionNumber)); VersionNumber = versionNumber; PayloadHash = AccountingSchedule.Hash(payloadHash); ApprovalRequestId = AccountingSchedule.RequiredId(approvalRequestId, nameof(approvalRequestId)); BoundUtc = AccountingSchedule.Utc(boundUtc); }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid ScheduleId { get; private set; }
    public Guid ScheduleVersionId { get; private set; } public int VersionNumber { get; private set; }
    public string PayloadHash { get; private set; } = null!; public Guid ApprovalRequestId { get; private set; }
    public DateTime BoundUtc { get; private set; } public AccountingSchedule Schedule { get; private set; } = null!;
    public AccountingScheduleVersion ScheduleVersion { get; private set; } = null!; public ApprovalRequest ApprovalRequest { get; private set; } = null!;
}

public sealed class AccountingScheduleOccurrence : ICompanyOwnedEntity
{
    private AccountingScheduleOccurrence() { }
    public AccountingScheduleOccurrence(Guid id, Guid companyId, Guid scheduleId, Guid scheduleVersionId,
        int scheduleVersionNumber, string scheduleVersionHash, DateOnly occurrenceDate, DateOnly postingDate,
        decimal scheduledAmount, string currency, string reversalRule, DateOnly? reversalDueDate, DateTime createdUtc)
    {
        Id = AccountingSchedule.RequiredId(id, nameof(id)); CompanyId = AccountingSchedule.RequiredId(companyId, nameof(companyId));
        ScheduleId = AccountingSchedule.RequiredId(scheduleId, nameof(scheduleId)); ScheduleVersionId = AccountingSchedule.RequiredId(scheduleVersionId, nameof(scheduleVersionId));
        if (scheduleVersionNumber < 1 || scheduledAmount < 0) throw new ArgumentOutOfRangeException(nameof(scheduleVersionNumber));
        ScheduleVersionNumber = scheduleVersionNumber; ScheduleVersionHash = AccountingSchedule.Hash(scheduleVersionHash);
        OccurrenceDate = occurrenceDate; PostingDate = postingDate; ScheduledAmount = scheduledAmount;
        Currency = AccountingSchedule.Required(currency, nameof(currency), 3).ToUpperInvariant();
        ReversalRule = AccountingScheduleReversalRules.Normalize(reversalRule); ReversalDueDate = reversalDueDate;
        Status = AccountingScheduleOccurrenceStatuses.Pending; Version = 1;
        CreatedUtc = UpdatedUtc = AccountingSchedule.Utc(createdUtc); NextAttemptUtc = CreatedUtc;
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid ScheduleId { get; private set; }
    public Guid ScheduleVersionId { get; private set; } public int ScheduleVersionNumber { get; private set; }
    public string ScheduleVersionHash { get; private set; } = null!; public DateOnly OccurrenceDate { get; private set; }
    public DateOnly PostingDate { get; private set; } public decimal ScheduledAmount { get; private set; }
    public decimal ReleasedAmount { get; private set; } public decimal ReversedAmount { get; private set; }
    public string Currency { get; private set; } = null!; public string ReversalRule { get; private set; } = null!;
    public DateOnly? ReversalDueDate { get; private set; } public string Status { get; private set; } = null!;
    public Guid? LedgerEntryId { get; private set; } public Guid? ReversalLedgerEntryId { get; private set; }
    public int AttemptCount { get; private set; } public DateTime? NextAttemptUtc { get; private set; }
    public string? LeaseOwner { get; private set; } public DateTime? LeaseExpiresUtc { get; private set; }
    public string? FailureCode { get; private set; } public string? FailureSummary { get; private set; }
    public DateTime CreatedUtc { get; private set; } public DateTime UpdatedUtc { get; private set; }
    public DateTime? PostedUtc { get; private set; } public DateTime? ReversedUtc { get; private set; }
    public long Version { get; private set; } public AccountingSchedule Schedule { get; private set; } = null!;
    public AccountingScheduleVersion ScheduleVersion { get; private set; } = null!;
    public ICollection<AccountingScheduleOccurrenceException> Exceptions { get; } = new List<AccountingScheduleOccurrenceException>();

    public bool TryClaim(string owner, DateTime now, TimeSpan lease)
    {
        now = AccountingSchedule.Utc(now);
        if (Status is AccountingScheduleOccurrenceStatuses.Posted or AccountingScheduleOccurrenceStatuses.Reversed) return false;
        if (NextAttemptUtc.HasValue && NextAttemptUtc > now) return false;
        if (LeaseExpiresUtc.HasValue && LeaseExpiresUtc > now && !string.Equals(LeaseOwner, owner, StringComparison.Ordinal)) return false;
        LeaseOwner = AccountingSchedule.Required(owner, nameof(owner), 160); LeaseExpiresUtc = now.Add(lease);
        Status = AccountingScheduleOccurrenceStatuses.Processing; AttemptCount++; UpdatedUtc = now; Version++; return true;
    }

    public bool TryClaimReversal(string owner, DateTime now, TimeSpan lease)
    {
        now = AccountingSchedule.Utc(now);
        if (Status != AccountingScheduleOccurrenceStatuses.Posted || ReversalRule == AccountingScheduleReversalRules.None ||
            !ReversalDueDate.HasValue || ReversalLedgerEntryId.HasValue) return false;
        if (LeaseExpiresUtc.HasValue && LeaseExpiresUtc > now && !string.Equals(LeaseOwner, owner, StringComparison.Ordinal)) return false;
        LeaseOwner = AccountingSchedule.Required(owner, nameof(owner), 160); LeaseExpiresUtc = now.Add(lease);
        AttemptCount++; UpdatedUtc = now; Version++; return true;
    }

    public bool IsClaimedBy(string owner, DateTime now) => string.Equals(LeaseOwner, owner, StringComparison.Ordinal) && LeaseExpiresUtc > AccountingSchedule.Utc(now);
    public void MarkPosted(string owner, Guid ledgerEntryId, DateTime now)
    { EnsureLease(owner, now); LedgerEntryId = AccountingSchedule.RequiredId(ledgerEntryId, nameof(ledgerEntryId)); ReleasedAmount = ScheduledAmount; Status = AccountingScheduleOccurrenceStatuses.Posted; PostedUtc = UpdatedUtc = AccountingSchedule.Utc(now); ClearLease(); Version++; }
    public void MarkReversed(string owner, Guid ledgerEntryId, DateTime now)
    { EnsureLease(owner, now); ReversalLedgerEntryId = AccountingSchedule.RequiredId(ledgerEntryId, nameof(ledgerEntryId)); ReversedAmount = ReleasedAmount; Status = AccountingScheduleOccurrenceStatuses.Reversed; ReversedUtc = UpdatedUtc = AccountingSchedule.Utc(now); ClearLease(); Version++; }
    public void ReleaseForRetry(string owner, string code, string summary, DateTime now, TimeSpan delay)
    { EnsureLease(owner, now); Status = AccountingScheduleOccurrenceStatuses.Failed; FailureCode = AccountingSchedule.Required(code, nameof(code), 100); FailureSummary = AccountingSchedule.Required(summary, nameof(summary), 1000); UpdatedUtc = AccountingSchedule.Utc(now); NextAttemptUtc = UpdatedUtc.Add(delay); ClearLease(); Version++; }
    public void MarkBlocked(string owner, string code, string summary, DateTime now)
    { EnsureLease(owner, now); Status = AccountingScheduleOccurrenceStatuses.Blocked; FailureCode = AccountingSchedule.Required(code, nameof(code), 100); FailureSummary = AccountingSchedule.Required(summary, nameof(summary), 1000); UpdatedUtc = AccountingSchedule.Utc(now); NextAttemptUtc = null; ClearLease(); Version++; }
    public void Regenerate(DateTime now)
    { if (Status is not (AccountingScheduleOccurrenceStatuses.Blocked or AccountingScheduleOccurrenceStatuses.Failed)) throw new InvalidOperationException("Only a blocked or failed occurrence can be regenerated."); Status = AccountingScheduleOccurrenceStatuses.Pending; FailureCode = FailureSummary = null; NextAttemptUtc = AccountingSchedule.Utc(now); ClearLease(); UpdatedUtc = NextAttemptUtc.Value; Version++; }
    private void EnsureLease(string owner, DateTime now) { if (!IsClaimedBy(owner, now)) throw new InvalidOperationException("The accounting schedule occurrence lease is no longer held."); }
    private void ClearLease() { LeaseOwner = null; LeaseExpiresUtc = null; }
}

public sealed class AccountingScheduleOccurrenceException : ICompanyOwnedEntity
{
    private AccountingScheduleOccurrenceException() { }
    public AccountingScheduleOccurrenceException(Guid id, Guid companyId, Guid scheduleId, Guid occurrenceId,
        string reasonCode, string explanation, string safeNextAction, DateTime createdUtc)
    { Id = AccountingSchedule.RequiredId(id, nameof(id)); CompanyId = AccountingSchedule.RequiredId(companyId, nameof(companyId)); ScheduleId = AccountingSchedule.RequiredId(scheduleId, nameof(scheduleId)); OccurrenceId = AccountingSchedule.RequiredId(occurrenceId, nameof(occurrenceId)); ReasonCode = AccountingSchedule.Required(reasonCode, nameof(reasonCode), 100); Explanation = AccountingSchedule.Required(explanation, nameof(explanation), 1000); SafeNextAction = AccountingSchedule.Required(safeNextAction, nameof(safeNextAction), 1000); Status = "open"; CreatedUtc = AccountingSchedule.Utc(createdUtc); }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid ScheduleId { get; private set; }
    public Guid OccurrenceId { get; private set; } public string ReasonCode { get; private set; } = null!;
    public string Explanation { get; private set; } = null!; public string SafeNextAction { get; private set; } = null!;
    public string Status { get; private set; } = null!; public DateTime CreatedUtc { get; private set; }
    public DateTime? ResolvedUtc { get; private set; } public AccountingScheduleOccurrence Occurrence { get; private set; } = null!;
    public void Resolve(DateTime now) { Status = "resolved"; ResolvedUtc = AccountingSchedule.Utc(now); }
}

public sealed class AccountingScheduleOperation : ICompanyOwnedEntity
{
    private AccountingScheduleOperation() { }
    public AccountingScheduleOperation(Guid id, Guid companyId, Guid scheduleId, string action,
        string idempotencyKey, string payloadHash, long resultVersion, DateTime createdUtc)
    { Id = AccountingSchedule.RequiredId(id, nameof(id)); CompanyId = AccountingSchedule.RequiredId(companyId, nameof(companyId)); ScheduleId = AccountingSchedule.RequiredId(scheduleId, nameof(scheduleId)); Action = AccountingSchedule.Required(action, nameof(action), 32).ToLowerInvariant(); IdempotencyKey = AccountingSchedule.Required(idempotencyKey, nameof(idempotencyKey), 200); PayloadHash = AccountingSchedule.Hash(payloadHash); ResultVersion = resultVersion; CreatedUtc = AccountingSchedule.Utc(createdUtc); }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid ScheduleId { get; private set; }
    public string Action { get; private set; } = null!; public string IdempotencyKey { get; private set; } = null!;
    public string PayloadHash { get; private set; } = null!; public long ResultVersion { get; private set; }
    public DateTime CreatedUtc { get; private set; } public AccountingSchedule Schedule { get; private set; } = null!;
}
