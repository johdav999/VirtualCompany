using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Finance;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class AccountingScheduleService : IAccountingScheduleService
{
    private const string SourceType = "accounting_schedule_occurrence";
    private readonly VirtualCompanyDbContext _db;
    private readonly IAccountingPostingService _posting;
    private readonly IApprovalRequestService _approvals;
    private readonly IAuditEventWriter _audit;
    private readonly TimeProvider _clock;

    public AccountingScheduleService(VirtualCompanyDbContext db, IAccountingPostingService posting,
        IApprovalRequestService approvals, IAuditEventWriter audit, TimeProvider clock)
    { _db = db; _posting = posting; _approvals = approvals; _audit = audit; _clock = clock; }

    public async Task<AccountingScheduleDto> CreateAsync(CreateAccountingScheduleCommand command,
        CancellationToken cancellationToken)
    {
        ValidateCommand(command.CompanyId, command.ActorUserId, command.IdempotencyKey);
        var payloadHash = HashInput(command.Schedule);
        var replay = await FindOperationAsync(command.CompanyId, command.IdempotencyKey, cancellationToken);
        if (replay is not null) return await ReplayAsync(replay, payloadHash, cancellationToken);
        ValidateInput(command.Schedule);
        var now = Now();
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var schedule = new AccountingSchedule(Guid.NewGuid(), command.CompanyId, command.Schedule.Code,
            command.Schedule.Name, command.Schedule.ScheduleType, command.Schedule.Cadence,
            command.Schedule.AmountBasis, command.Schedule.ProrationRule, command.Schedule.StartDate,
            command.Schedule.EndDate, command.Schedule.OccurrenceDay, command.Schedule.TimeZoneId,
            command.Schedule.VoucherSeriesCode, command.Schedule.Currency, command.Schedule.ReversalRule,
            command.ActorUserId, now);
        _db.AccountingSchedules.Add(schedule);
        await _db.SaveChangesAsync(cancellationToken);
        var version = await BuildVersionAsync(schedule, 1, command.Schedule, payloadHash, command.ActorUserId,
            now, cancellationToken);
        _db.AccountingScheduleVersions.Add(version);
        schedule.ApplyProspectiveVersion(command.Schedule.Name, command.Schedule.ScheduleType,
            command.Schedule.Cadence, command.Schedule.AmountBasis, command.Schedule.ProrationRule,
            command.Schedule.StartDate, command.Schedule.EndDate, command.Schedule.OccurrenceDay,
            command.Schedule.TimeZoneId, command.Schedule.VoucherSeriesCode, command.Schedule.Currency,
            command.Schedule.ReversalRule, version.Id, version.VersionNumber, version.PayloadHash,
            command.ActorUserId, now);
        _db.AccountingScheduleOperations.Add(new(Guid.NewGuid(), command.CompanyId, schedule.Id, "create",
            command.IdempotencyKey, payloadHash, schedule.Version, now));
        await WriteAuditAsync(command.CompanyId, command.ActorUserId, AuditEventActions.AccountingScheduleCreated,
            schedule.Id, "Created a versioned accounting schedule draft.", command.CorrelationId, now, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(new(command.CompanyId, schedule.Id), cancellationToken);
    }

    public async Task<AccountingScheduleDto> UpdateAsync(UpdateAccountingScheduleCommand command,
        CancellationToken cancellationToken)
    {
        ValidateCommand(command.CompanyId, command.ActorUserId, command.IdempotencyKey);
        var payloadHash = HashInput(command.Schedule);
        var replay = await FindOperationAsync(command.CompanyId, command.IdempotencyKey, cancellationToken);
        if (replay is not null) return await ReplayAsync(replay, payloadHash, cancellationToken);
        ValidateInput(command.Schedule);
        var schedule = await LoadAsync(command.CompanyId, command.ScheduleId, true, cancellationToken);
        EnsureVersion(schedule, command.ExpectedVersion);
        if (schedule.ApprovalRequest is { Status: ApprovalRequestStatus.Pending })
            schedule.ApprovalRequest.MarkCancelled("Superseded by a prospective accounting schedule version.");
        var now = Now();
        var version = await BuildVersionAsync(schedule, schedule.CurrentVersionNumber + 1, command.Schedule,
            payloadHash, command.ActorUserId, now, cancellationToken);
        _db.AccountingScheduleVersions.Add(version);
        schedule.ApplyProspectiveVersion(command.Schedule.Name, command.Schedule.ScheduleType,
            command.Schedule.Cadence, command.Schedule.AmountBasis, command.Schedule.ProrationRule,
            command.Schedule.StartDate, command.Schedule.EndDate, command.Schedule.OccurrenceDay,
            command.Schedule.TimeZoneId, command.Schedule.VoucherSeriesCode, command.Schedule.Currency,
            command.Schedule.ReversalRule, version.Id, version.VersionNumber, version.PayloadHash,
            command.ActorUserId, now);
        _db.AccountingScheduleOperations.Add(new(Guid.NewGuid(), command.CompanyId, schedule.Id, "update",
            command.IdempotencyKey, payloadHash, schedule.Version, now));
        await WriteAuditAsync(command.CompanyId, command.ActorUserId, AuditEventActions.AccountingScheduleVersioned,
            schedule.Id, "Created a prospective accounting schedule version without changing posted occurrences.",
            command.CorrelationId, now, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return await GetAsync(new(command.CompanyId, schedule.Id), cancellationToken);
    }

    public async Task<AccountingSchedulePreviewDto> PreviewAsync(PreviewAccountingScheduleQuery query,
        CancellationToken cancellationToken)
    {
        var schedule = await LoadAsync(query.CompanyId, query.ScheduleId, false, cancellationToken);
        EnsureVersion(schedule, query.ExpectedVersion);
        var calculation = AccountingScheduleCalculator.Calculate(schedule, schedule.CurrentVersion!, schedule.NextOccurrenceDate);
        var period = await ResolvePeriodAsync(schedule.CompanyId, schedule.NextOccurrenceDate, cancellationToken);
        var occurrenceId = DeterministicOccurrenceId(schedule.Id, schedule.NextOccurrenceDate, schedule.CurrentVersionNumber);
        var proposed = ToProposed(schedule, schedule.CurrentVersion!, occurrenceId, period.Id,
            schedule.NextOccurrenceDate, calculation, query.ActorUserId, requiresApproval: false,
            approvalRequestId: null, idempotencyKey: $"preview:{schedule.Id:N}:{schedule.CurrentVersionNumber}:{schedule.NextOccurrenceDate:yyyyMMdd}");
        var preview = await _posting.PreviewAsync(new(proposed), cancellationToken);
        return new(await MapAsync(schedule, cancellationToken), preview, calculation.DebitTotal,
            schedule.NextOccurrenceDate, calculation.PlannedOccurrences,
            preview.Issues);
    }

    public async Task<AccountingScheduleDto> SubmitAsync(SubmitAccountingScheduleCommand command,
        CancellationToken cancellationToken)
    {
        ValidateCommand(command.CompanyId, command.ActorUserId, command.IdempotencyKey);
        var schedule = await LoadAsync(command.CompanyId, command.ScheduleId, true, cancellationToken);
        EnsureVersion(schedule, command.ExpectedVersion);
        if (schedule.ApprovalRequest is { Status: ApprovalRequestStatus.Pending })
            throw Error(AccountingScheduleReasonCodes.ApprovalPending,
                "This schedule version is already waiting for approval.");
        if (schedule.ApprovalRequest is { Status: ApprovalRequestStatus.Approved } && HasCurrentApproval(schedule))
            throw Error(AccountingScheduleReasonCodes.InvalidState,
                "This schedule version is already approved and can be activated.");
        var requestHash = HashText($"submit|{schedule.Id:N}|{schedule.CurrentVersionNumber}|{schedule.CurrentVersionHash}");
        var replay = await FindOperationAsync(command.CompanyId, command.IdempotencyKey, cancellationToken);
        if (replay is not null) return await ReplayAsync(replay, requestHash, cancellationToken);
        var preview = await PreviewAsync(new(command.CompanyId, command.ScheduleId, command.ExpectedVersion,
            command.ActorUserId), cancellationToken);
        var issue = preview.PostingPreview.Issues.FirstOrDefault();
        if (issue is not null) throw Error(issue.ReasonCode, issue.Explanation);
        var now = Now();
        var approval = await _approvals.CreateAsync(command.CompanyId, new(
            ApprovalTargetEntityType.AccountingSchedule.ToStorageValue(), schedule.Id,
            AuditActorTypes.User, command.ActorUserId, "accounting_schedule_activation",
            new Dictionary<string, JsonNode?>
            {
                ["sourceVersion"] = JsonValue.Create(schedule.CurrentVersionNumber.ToString(CultureInfo.InvariantCulture)),
                ["payloadHash"] = JsonValue.Create(schedule.CurrentVersionHash),
                ["scheduleType"] = JsonValue.Create(schedule.ScheduleType),
                ["nextOccurrenceDate"] = JsonValue.Create(schedule.NextOccurrenceDate.ToString("O", CultureInfo.InvariantCulture)),
                ["occurrenceAmount"] = JsonValue.Create(preview.OccurrenceAmount)
            }, Steps: [new(1, ApprovalStepApproverType.Role.ToStorageValue(), "finance_approver")]), cancellationToken);
        schedule.Submit(approval.Id, command.ActorUserId, now);
        _db.AccountingScheduleApprovalBindings.Add(new(Guid.NewGuid(), command.CompanyId, schedule.Id,
            schedule.CurrentVersionId!.Value, schedule.CurrentVersionNumber, schedule.CurrentVersionHash!,
            approval.Id, now));
        _db.AccountingScheduleOperations.Add(new(Guid.NewGuid(), command.CompanyId, schedule.Id, "submit",
            command.IdempotencyKey, requestHash, schedule.Version, now));
        await WriteAuditAsync(command.CompanyId, command.ActorUserId,
            AuditEventActions.AccountingScheduleApprovalRequested, schedule.Id,
            "Requested separate approval for the exact retained accounting schedule version.",
            command.CorrelationId, now, cancellationToken, approval.Id);
        await _db.SaveChangesAsync(cancellationToken);
        return await GetAsync(new(command.CompanyId, schedule.Id), cancellationToken);
    }

    public async Task<AccountingScheduleDto> DecideApprovalAsync(DecideAccountingScheduleApprovalCommand command,
        CancellationToken cancellationToken)
    {
        var schedule = await LoadAsync(command.CompanyId, command.ScheduleId, true, cancellationToken);
        EnsureVersion(schedule, command.ExpectedVersion);
        if (!schedule.ApprovalRequestId.HasValue) throw Error(AccountingScheduleReasonCodes.ApprovalRequired,
            "Submit the exact schedule version for approval first.");
        if (command.ClientRequestId == Guid.Empty) throw Error(AccountingScheduleReasonCodes.InvalidState,
            "ClientRequestId is required for an idempotent approval decision.");
        await _approvals.DecideAsync(command.CompanyId, new(schedule.ApprovalRequestId.Value,
            command.Approve ? "approve" : "reject", Comment: command.Comment, ClientRequestId: command.ClientRequestId),
            cancellationToken);
        var now = Now();
        await WriteAuditAsync(command.CompanyId, command.ActorUserId,
            AuditEventActions.AccountingScheduleApprovalDecided, schedule.Id,
            command.Approve ? "Approved the retained accounting schedule version." : "Rejected the retained accounting schedule version.",
            command.CorrelationId, now, cancellationToken, schedule.ApprovalRequestId);
        await _db.SaveChangesAsync(cancellationToken);
        return await GetAsync(new(command.CompanyId, schedule.Id), cancellationToken);
    }

    public async Task<AccountingScheduleDto> ActivateAsync(ActivateAccountingScheduleCommand command,
        CancellationToken cancellationToken)
    {
        var schedule = await LoadAsync(command.CompanyId, command.ScheduleId, true, cancellationToken);
        EnsureVersion(schedule, command.ExpectedVersion); EnsureCurrentApproval(schedule);
        var now = Now(); schedule.Activate(command.ActorUserId, now, schedule.LocalDate(now));
        await WriteStateAuditAsync(command.CompanyId, command.ActorUserId, schedule, "activated",
            command.CorrelationId, now, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return await GetAsync(new(command.CompanyId, schedule.Id), cancellationToken);
    }

    public async Task<AccountingScheduleDto> ChangeStateAsync(ChangeAccountingScheduleStateCommand command,
        CancellationToken cancellationToken)
    {
        var schedule = await LoadAsync(command.CompanyId, command.ScheduleId, true, cancellationToken);
        EnsureVersion(schedule, command.ExpectedVersion);
        var now = Now(); var action = command.Action?.Trim().ToLowerInvariant();
        switch (action)
        {
            case "pause": schedule.Pause(command.ActorUserId, now); break;
            case "resume": EnsureCurrentApproval(schedule); schedule.Resume(command.ActorUserId, now,
                schedule.LocalDate(now), command.GenerateMissed); break;
            case "end": schedule.End(command.ActorUserId, now); break;
            default: throw Error(AccountingScheduleReasonCodes.InvalidState, "Use pause, resume, or end.");
        }
        await WriteStateAuditAsync(command.CompanyId, command.ActorUserId, schedule, action!,
            command.CorrelationId, now, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return await GetAsync(new(command.CompanyId, schedule.Id), cancellationToken);
    }

    public async Task<AccountingScheduleDto> RegenerateOccurrenceAsync(
        RegenerateAccountingScheduleOccurrenceCommand command, CancellationToken cancellationToken)
    {
        var schedule = await LoadAsync(command.CompanyId, command.ScheduleId, true, cancellationToken);
        EnsureCurrentApproval(schedule);
        var occurrence = schedule.Occurrences.SingleOrDefault(x => x.Id == command.OccurrenceId)
            ?? throw Error(AccountingScheduleReasonCodes.NotFound, "The accounting schedule occurrence was not found.");
        if (occurrence.Version != command.ExpectedVersion) throw Error(AccountingScheduleReasonCodes.VersionConflict,
            $"This occurrence is now version {occurrence.Version}. Reload it before retrying.", true, occurrence.Version);
        var now = Now();
        foreach (var exception in occurrence.Exceptions.Where(x => x.Status == "open")) exception.Resolve(now);
        occurrence.Regenerate(now);
        if (schedule.Status == AccountingScheduleStatuses.Paused)
            schedule.Resume(command.ActorUserId, now, schedule.LocalDate(now), true);
        await _audit.WriteAsync(new(command.CompanyId, AuditActorTypes.User, command.ActorUserId,
            AuditEventActions.AccountingScheduleOccurrenceRegenerated,
            AuditTargetTypes.AccountingScheduleOccurrence, occurrence.Id.ToString("D"), AuditEventOutcomes.Succeeded,
            "Reset a blocked accounting schedule occurrence for controlled regeneration.", ["accounting_schedule"],
            CorrelationId: command.CorrelationId, OccurredUtc: now), cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return await GetAsync(new(command.CompanyId, schedule.Id), cancellationToken);
    }

    public async Task<AccountingScheduleDto> GetAsync(GetAccountingScheduleQuery query,
        CancellationToken cancellationToken) => await MapAsync(
        await LoadAsync(query.CompanyId, query.ScheduleId, false, cancellationToken), cancellationToken);

    public async Task<AccountingScheduleListResult> ListAsync(ListAccountingSchedulesQuery query,
        CancellationToken cancellationToken)
    {
        var skip = Math.Max(0, query.Skip); var take = Math.Clamp(query.Take, 1, 250);
        var source = ScheduleQuery(false).Where(x => x.CompanyId == query.CompanyId);
        if (!string.IsNullOrWhiteSpace(query.Status)) source = source.Where(x => x.Status == query.Status.Trim().ToLowerInvariant());
        var total = await source.CountAsync(cancellationToken);
        var schedules = await source.OrderBy(x => x.NextOccurrenceDate).ThenBy(x => x.Code)
            .Skip(skip).Take(take).ToListAsync(cancellationToken);
        var items = new List<AccountingScheduleDto>(schedules.Count);
        foreach (var schedule in schedules) items.Add(await MapAsync(schedule, cancellationToken));
        var currency = items.Select(x => x.Currency).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1
            ? items.FirstOrDefault()?.Currency ?? "SEK" : "MIXED";
        return new(items, total, skip, take, items.Sum(x => x.Reconciliation.ReleasedAmount),
            items.Sum(x => x.Reconciliation.ReversedAmount), items.Sum(x => x.Reconciliation.RemainingAmount ?? 0m),
            items.Count(x => x.Status == AccountingScheduleStatuses.Active),
            items.Count(x => x.Status == AccountingScheduleStatuses.Active && x.NextOccurrenceDate <= DateOnly.FromDateTime(Now())),
            items.Sum(x => x.Reconciliation.ExceptionOccurrences), currency);
    }

    internal static ProposedAccountingEntry ToProposed(AccountingSchedule schedule,
        AccountingScheduleVersion version, Guid occurrenceId, Guid fiscalPeriodId, DateOnly postingDate,
        AccountingScheduleCalculation calculation, Guid actorUserId, bool requiresApproval,
        Guid? approvalRequestId, string idempotencyKey, string actorType = AuditActorTypes.User)
    {
        var postingType = schedule.ScheduleType == AccountingScheduleTypes.RecurringFixed
            ? LedgerPostingTypeValues.Manual : LedgerPostingTypeValues.Adjustment;
        return new(schedule.CompanyId, fiscalPeriodId, schedule.VoucherSeriesCode, postingDate, postingDate,
            postingType, $"{schedule.Name} · {postingDate:yyyy-MM-dd}", SourceType,
            occurrenceId.ToString("N"), version.VersionNumber.ToString(CultureInfo.InvariantCulture), idempotencyKey,
            calculation.Lines.Select(x => new ProposedAccountingLine(x.FinanceAccountId, x.DebitAmount,
                x.CreditAmount, schedule.Currency, x.Description, DimensionMemberIds: x.DimensionMemberIds)).ToArray(),
            actorUserId, approvalRequestId, requiresApproval,
            new Dictionary<string, string>
            {
                ["accountingScheduleId"] = schedule.Id.ToString("D"),
                ["accountingScheduleVersionId"] = version.Id.ToString("D"),
                ["accountingScheduleVersion"] = version.VersionNumber.ToString(CultureInfo.InvariantCulture),
                ["occurrenceDate"] = postingDate.ToString("O", CultureInfo.InvariantCulture),
                ["scheduleType"] = schedule.ScheduleType,
                ["amountBasis"] = schedule.AmountBasis,
                ["prorationFactor"] = calculation.ProrationFactor.ToString(CultureInfo.InvariantCulture)
            }, "post_accounting_schedule_occurrence", version.PayloadHash,
            version.EvidenceLinks.Select(x => new ProposedAccountingEvidence(x.DocumentId, x.ContentHash, x.Title)).ToArray(),
            ActorType: actorType);
    }

    internal async Task<FiscalPeriod> ResolvePeriodAsync(Guid companyId, DateOnly postingDate,
        CancellationToken cancellationToken)
    {
        var start = postingDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        return await _db.FiscalPeriods.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
            x.CompanyId == companyId && x.StartUtc <= start && x.EndUtc > start, cancellationToken)
            ?? throw Error(AccountingScheduleReasonCodes.PeriodUnavailable,
                $"No fiscal period contains {postingDate:yyyy-MM-dd}.");
    }

    private IQueryable<AccountingSchedule> ScheduleQuery(bool tracking)
    {
        var source = tracking ? _db.AccountingSchedules : _db.AccountingSchedules.AsNoTracking();
        return source.Include(x => x.ApprovalRequest)
            .Include(x => x.CurrentVersion).ThenInclude(x => x!.Lines).ThenInclude(x => x.FinanceAccount)
            .Include(x => x.CurrentVersion).ThenInclude(x => x!.Lines).ThenInclude(x => x.DimensionAssignments)
            .Include(x => x.CurrentVersion).ThenInclude(x => x!.EvidenceLinks).ThenInclude(x => x.Document)
            .Include(x => x.Occurrences).ThenInclude(x => x.Exceptions);
    }

    private async Task<AccountingSchedule> LoadAsync(Guid companyId, Guid scheduleId, bool tracking,
        CancellationToken cancellationToken) => await ScheduleQuery(tracking).SingleOrDefaultAsync(x =>
        x.CompanyId == companyId && x.Id == scheduleId, cancellationToken)
        ?? throw Error(AccountingScheduleReasonCodes.NotFound, "The accounting schedule was not found.");

    private async Task<AccountingScheduleDto> MapAsync(AccountingSchedule schedule, CancellationToken cancellationToken)
    {
        if (!_db.Entry(schedule).Reference(x => x.CurrentVersion).IsLoaded)
            schedule = await LoadAsync(schedule.CompanyId, schedule.Id, false, cancellationToken);
        AccountingScheduleVersionDto? version = null;
        if (schedule.CurrentVersion is not null)
            version = new(schedule.CurrentVersion.Id, schedule.CurrentVersion.VersionNumber,
                schedule.CurrentVersion.PayloadHash, schedule.CurrentVersion.Description,
                schedule.CurrentVersion.EffectiveFrom, schedule.CurrentVersion.CreatedUtc,
                schedule.CurrentVersion.Lines.OrderBy(x => x.Sequence).Select(x => new AccountingScheduleLineDto(
                    x.Id, x.Sequence, x.FinanceAccountId, x.FinanceAccount.Code, x.FinanceAccount.Name,
                    x.DebitAmount, x.CreditAmount, x.Description,
                    x.DimensionAssignments.Select(y => y.DimensionMemberId).OrderBy(y => y).ToArray())).ToArray(),
                schedule.CurrentVersion.EvidenceLinks.Select(x => new AccountingScheduleEvidenceDto(
                    x.DocumentId, x.Title, x.ContentHash, x.Document.OriginalFileName)).ToArray());
        AccountingScheduleApprovalDto? approval = null;
        if (schedule.ApprovalRequest is not null && schedule.ApprovalVersionNumber.HasValue &&
            !string.IsNullOrWhiteSpace(schedule.ApprovalPayloadHash))
        {
            var boundUtc = await _db.AccountingScheduleApprovalBindings.AsNoTracking()
                .Where(x => x.CompanyId == schedule.CompanyId && x.ApprovalRequestId == schedule.ApprovalRequestId)
                .Select(x => (DateTime?)x.BoundUtc).SingleOrDefaultAsync(cancellationToken) ?? schedule.UpdatedUtc;
            approval = new(schedule.ApprovalRequest.Id, schedule.ApprovalRequest.Status.ToStorageValue(),
                schedule.ApprovalVersionNumber.Value, schedule.ApprovalPayloadHash, boundUtc,
                schedule.ApprovalRequest.DecisionSummary);
        }
        var occurrences = schedule.Occurrences.OrderByDescending(x => x.OccurrenceDate).Select(x =>
            new AccountingScheduleOccurrenceDto(x.Id, x.OccurrenceDate, x.PostingDate, x.ScheduledAmount,
                x.ReleasedAmount, x.ReversedAmount, x.Currency, x.Status, x.LedgerEntryId,
                x.ReversalLedgerEntryId, x.ReversalDueDate, x.AttemptCount, x.FailureCode, x.FailureSummary,
                x.Version, x.UpdatedUtc, x.Exceptions.OrderByDescending(y => y.CreatedUtc).Select(y =>
                    new AccountingScheduleExceptionDto(y.Id, y.ReasonCode, y.Explanation,
                        y.SafeNextAction, y.Status, y.CreatedUtc, y.ResolvedUtc)).ToArray())).ToArray();
        var reconciliation = Reconcile(schedule, occurrences);
        return new(schedule.Id, schedule.CompanyId, schedule.Code, schedule.Name, schedule.ScheduleType,
            schedule.Cadence, schedule.AmountBasis, schedule.ProrationRule, schedule.StartDate,
            schedule.EndDate, schedule.OccurrenceDay, schedule.TimeZoneId, schedule.VoucherSeriesCode,
            schedule.Currency, schedule.ReversalRule, EffectiveStatus(schedule), schedule.NextOccurrenceDate,
            schedule.CurrentVersionNumber, schedule.CurrentVersionHash, schedule.Version, schedule.CreatedByUserId,
            schedule.UpdatedByUserId, schedule.CreatedUtc, schedule.UpdatedUtc, version, approval,
            occurrences, reconciliation, AllowedActions(schedule));
    }

    private static AccountingScheduleReconciliationDto Reconcile(AccountingSchedule schedule,
        IReadOnlyList<AccountingScheduleOccurrenceDto> occurrences)
    {
        var dates = AccountingScheduleCalculator.PlannedDates(schedule);
        decimal original;
        if (schedule.CurrentVersion is null) original = 0m;
        else if (dates.Count > 0) original = dates.Sum(date =>
            AccountingScheduleCalculator.Calculate(schedule, schedule.CurrentVersion, date).DebitTotal);
        else original = occurrences.Sum(x => x.ScheduledAmount);
        var released = occurrences.Sum(x => x.ReleasedAmount); var reversed = occurrences.Sum(x => x.ReversedAmount);
        var remaining = dates.Count > 0 ? Math.Max(0m, original - released) : (decimal?)null;
        var exceptionAmount = occurrences.Where(x => x.Status is AccountingScheduleOccurrenceStatuses.Blocked or AccountingScheduleOccurrenceStatuses.Failed)
            .Sum(x => x.ScheduledAmount);
        return new(original, released, reversed, remaining, exceptionAmount, schedule.Currency,
            dates.Count, occurrences.Count(x => x.Status is AccountingScheduleOccurrenceStatuses.Posted or AccountingScheduleOccurrenceStatuses.Reversed),
            occurrences.Count(x => x.Status == AccountingScheduleOccurrenceStatuses.Reversed),
            occurrences.Count(x => x.Status is AccountingScheduleOccurrenceStatuses.Blocked or AccountingScheduleOccurrenceStatuses.Failed),
            remaining is null || decimal.Round(original - released - remaining.Value, 2) == 0m);
    }

    private static IReadOnlyList<string> AllowedActions(AccountingSchedule schedule)
    {
        var actions = new List<string>();
        if (schedule.Status != AccountingScheduleStatuses.Ended) actions.Add("edit");
        if (schedule.Status is AccountingScheduleStatuses.Draft or AccountingScheduleStatuses.AwaitingApproval)
            actions.Add("preview");
        if (schedule.Status == AccountingScheduleStatuses.Draft || schedule.Status == AccountingScheduleStatuses.AwaitingApproval &&
            schedule.ApprovalRequest?.Status is null or ApprovalRequestStatus.Rejected or ApprovalRequestStatus.Cancelled or ApprovalRequestStatus.Expired)
            actions.Add("submit");
        if (schedule.Status == AccountingScheduleStatuses.AwaitingApproval && schedule.ApprovalRequest?.Status == ApprovalRequestStatus.Pending)
            actions.AddRange(["approve", "reject"]);
        if (HasCurrentApproval(schedule) && schedule.Status is AccountingScheduleStatuses.AwaitingApproval or AccountingScheduleStatuses.Paused) actions.Add("activate");
        if (schedule.Status == AccountingScheduleStatuses.Active) actions.AddRange(["pause", "end"]);
        if (schedule.Status == AccountingScheduleStatuses.Paused) actions.AddRange(["resume", "end"]);
        return actions.Distinct().ToArray();
    }

    private async Task<AccountingScheduleVersion> BuildVersionAsync(AccountingSchedule schedule,
        int versionNumber, AccountingScheduleInput input, string payloadHash, Guid actorUserId,
        DateTime now, CancellationToken cancellationToken)
    {
        var accountIds = input.Lines.Select(x => x.FinanceAccountId).Distinct().ToArray();
        var accounts = await _db.FinanceAccounts.AsNoTracking().Where(x => x.CompanyId == schedule.CompanyId && accountIds.Contains(x.Id))
            .Select(x => x.Id).ToListAsync(cancellationToken);
        if (accounts.Count != accountIds.Length) throw Error(AccountingScheduleReasonCodes.InvalidTemplate,
            "One or more schedule accounts could not be found for this company.");
        var memberIds = input.Lines.SelectMany(x => x.DimensionMemberIds ?? []).Distinct().ToArray();
        var members = await _db.AccountingDimensionMembers.AsNoTracking().Where(x => x.CompanyId == schedule.CompanyId && memberIds.Contains(x.Id))
            .Select(x => x.Id).ToListAsync(cancellationToken);
        if (members.Count != memberIds.Length) throw Error(AccountingScheduleReasonCodes.InvalidTemplate,
            "One or more schedule dimensions could not be found for this company.");
        var documentIds = (input.EvidenceDocumentIds ?? []).Distinct().ToArray();
        var documents = await _db.CompanyKnowledgeDocuments.AsNoTracking().Where(x => x.CompanyId == schedule.CompanyId && documentIds.Contains(x.Id))
            .ToListAsync(cancellationToken);
        if (documents.Count != documentIds.Length) throw Error(AccountingScheduleReasonCodes.InvalidTemplate,
            "One or more evidence documents could not be found for this company.");
        var version = new AccountingScheduleVersion(Guid.NewGuid(), schedule.CompanyId, schedule.Id,
            versionNumber, payloadHash, input.Description, input.StartDate, actorUserId, now);
        var sequence = 0;
        foreach (var inputLine in input.Lines)
        {
            var line = new AccountingScheduleLine(Guid.NewGuid(), schedule.CompanyId, version.Id, ++sequence,
                inputLine.FinanceAccountId, inputLine.DebitAmount, inputLine.CreditAmount, inputLine.Description);
            foreach (var memberId in (inputLine.DimensionMemberIds ?? []).Distinct())
                line.DimensionAssignments.Add(new AccountingScheduleLineDimension(Guid.NewGuid(), schedule.CompanyId, line.Id, memberId));
            version.Lines.Add(line);
        }
        foreach (var document in documents)
        {
            var contentHash = Metadata(document, "checksum_sha256") ?? throw Error(
                AccountingScheduleReasonCodes.InvalidTemplate, $"Evidence document '{document.Title}' has no verified content hash.");
            version.EvidenceLinks.Add(new AccountingScheduleEvidenceLink(Guid.NewGuid(), schedule.CompanyId,
                version.Id, document.Id, document.Title, contentHash, now));
        }
        return version;
    }

    private static void ValidateInput(AccountingScheduleInput input)
    {
        if (input.Lines is null || input.Lines.Count < 2) throw Error(AccountingScheduleReasonCodes.InvalidTemplate,
            "An accounting schedule requires at least two lines.");
        var debit = input.Lines.Sum(x => x.DebitAmount); var credit = input.Lines.Sum(x => x.CreditAmount);
        if (debit <= 0m || decimal.Round(debit - credit, 4) != 0m) throw Error(
            AccountingScheduleReasonCodes.InvalidTemplate, "The accounting schedule template must balance.");
        if (input.ScheduleType is AccountingScheduleTypes.DateAllocation or AccountingScheduleTypes.Prepayment &&
            input.AmountBasis != AccountingScheduleAmountBases.TotalSchedule)
            throw Error(AccountingScheduleReasonCodes.InvalidTemplate,
                "Date allocations and prepayments must use the total_schedule amount basis.");
    }

    private static string EffectiveStatus(AccountingSchedule schedule) => schedule.Status == AccountingScheduleStatuses.AwaitingApproval && schedule.ApprovalRequest is not null
        ? schedule.ApprovalRequest.Status switch
        {
            ApprovalRequestStatus.Approved => "approved",
            ApprovalRequestStatus.Rejected => "rejected",
            ApprovalRequestStatus.Cancelled or ApprovalRequestStatus.Expired => "approval_expired",
            _ => schedule.Status
        } : schedule.Status;
    internal static bool HasCurrentApproval(AccountingSchedule schedule) => schedule.ApprovalRequest is { Status: ApprovalRequestStatus.Approved } &&
        schedule.ApprovalRequest.TargetEntityType == ApprovalTargetEntityType.AccountingSchedule.ToStorageValue() &&
        schedule.ApprovalRequest.TargetEntityId == schedule.Id && schedule.ApprovalVersionNumber == schedule.CurrentVersionNumber &&
        string.Equals(schedule.ApprovalPayloadHash, schedule.CurrentVersionHash, StringComparison.OrdinalIgnoreCase);
    private static void EnsureCurrentApproval(AccountingSchedule schedule)
    {
        if (!HasCurrentApproval(schedule)) throw Error(AccountingScheduleReasonCodes.ApprovalStale,
            "The current accounting schedule version does not have final approval.");
    }
    private static void EnsureVersion(AccountingSchedule schedule, long expected)
    {
        if (schedule.Version != expected) throw Error(AccountingScheduleReasonCodes.VersionConflict,
            $"This schedule is now version {schedule.Version}. Reload it before continuing.", true, schedule.Version);
    }
    private async Task<AccountingScheduleOperation?> FindOperationAsync(Guid companyId, string idempotencyKey,
        CancellationToken cancellationToken) => await _db.AccountingScheduleOperations.AsNoTracking()
        .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.IdempotencyKey == idempotencyKey.Trim(), cancellationToken);
    private async Task<AccountingScheduleDto> ReplayAsync(AccountingScheduleOperation operation, string payloadHash,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(operation.PayloadHash, payloadHash, StringComparison.OrdinalIgnoreCase))
            throw Error(AccountingScheduleReasonCodes.IdempotencyConflict,
                "This request identity was already used for different accounting schedule content.", true);
        return await GetAsync(new(operation.CompanyId, operation.ScheduleId), cancellationToken);
    }
    private async Task WriteStateAuditAsync(Guid companyId, Guid actorId, AccountingSchedule schedule,
        string action, string? correlationId, DateTime now, CancellationToken cancellationToken) =>
        await WriteAuditAsync(companyId, actorId, AuditEventActions.AccountingScheduleStateChanged, schedule.Id,
            $"Accounting schedule was {action}.", correlationId, now, cancellationToken);
    private async Task WriteAuditAsync(Guid companyId, Guid actorId, string action, Guid scheduleId,
        string summary, string? correlationId, DateTime now, CancellationToken cancellationToken,
        Guid? approvalRequestId = null) => await _audit.WriteAsync(new(companyId, AuditActorTypes.User,
        actorId, action, AuditTargetTypes.AccountingSchedule, scheduleId.ToString("D"),
        AuditEventOutcomes.Succeeded, summary, ["accounting_schedule"],
        new Dictionary<string, string?> { ["approvalRequestId"] = approvalRequestId?.ToString("D") },
        correlationId, now), cancellationToken);
    private static Guid DeterministicOccurrenceId(Guid scheduleId, DateOnly date, int version)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{scheduleId:N}|{date:O}|{version}"));
        return new Guid(bytes.AsSpan(0, 16));
    }
    private static string HashInput(AccountingScheduleInput input) => HashText(JsonSerializer.Serialize(new
    {
        input.Code, input.Name, input.ScheduleType, input.Cadence, input.AmountBasis, input.ProrationRule,
        input.StartDate, input.EndDate, input.OccurrenceDay, input.TimeZoneId, input.VoucherSeriesCode,
        input.Currency, input.ReversalRule, input.Description,
        Lines = input.Lines.Select(x => new { x.FinanceAccountId, x.DebitAmount, x.CreditAmount, x.Description,
            DimensionMemberIds = (x.DimensionMemberIds ?? []).OrderBy(y => y) }),
        Evidence = (input.EvidenceDocumentIds ?? []).OrderBy(x => x)
    }));
    private static string HashText(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string? Metadata(CompanyKnowledgeDocument document, string key) => document.Metadata.TryGetValue(key, out var node)
        ? node is JsonValue value && value.TryGetValue<string>(out var text) ? text : node?.ToString() : null;
    private static void ValidateCommand(Guid companyId, Guid actorId, string idempotencyKey)
    { if (companyId == Guid.Empty || actorId == Guid.Empty) throw Error(AccountingScheduleReasonCodes.NotFound, "The accounting schedule was not found."); if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Trim().Length > 200) throw Error(AccountingScheduleReasonCodes.IdempotencyConflict, "A stable request identity is required."); }
    private DateTime Now() => _clock.GetUtcNow().UtcDateTime;
    private static AccountingScheduleException Error(string code, string message, bool conflict = false, long? current = null) => new(code, message, conflict, current);
}
