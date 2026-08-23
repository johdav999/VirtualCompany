using System.Data;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class AccountingProviderSwitchService : IAccountingProviderSwitchService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IAuditEventWriter _auditWriter;
    private readonly TimeProvider _timeProvider;
    private readonly IAccountingProviderSwitchStagingService? _stagingService;

    public AccountingProviderSwitchService(
        VirtualCompanyDbContext dbContext,
        IAuditEventWriter auditWriter,
        TimeProvider timeProvider,
        IAccountingProviderSwitchStagingService? stagingService = null)
    {
        _dbContext = dbContext;
        _auditWriter = auditWriter;
        _timeProvider = timeProvider;
        _stagingService = stagingService;
    }

    public async Task<AccountingProviderSwitchDto> CreateAsync(
        CreateAccountingProviderSwitchCommand command,
        CancellationToken cancellationToken)
    {
        ValidateCommandContext(command.CompanyId, command.ActorUserId, command.CorrelationId);
        var plan = await ValidatePlanAsync(
            command.CompanyId,
            command.SourceKind,
            command.SourceProviderKey,
            command.TargetKind,
            command.TargetProviderKey,
            command.EffectiveFiscalPeriodId,
            command.MigrationStrategy,
            command.ResponsibleUserId,
            command.ResponsibleAgentId,
            cancellationToken);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (await HasActiveSwitchAsync(command.CompanyId, cancellationToken))
            throw Conflict(AccountingProviderSwitchReasonCodes.DuplicateActiveSwitch,
                "This company already has an active accounting-system switch. Complete or cancel it before creating another one.");

        var now = UtcNow();
        var providerSwitch = new AccountingProviderSwitch(
            Guid.NewGuid(), command.CompanyId, plan.Source, plan.Target, plan.FiscalPeriod.Id,
            plan.Strategy, command.Reason, command.ResponsibleUserId, command.ResponsibleAgentId,
            command.ActorUserId, command.CorrelationId, now);
        _dbContext.AccountingProviderSwitches.Add(providerSwitch);
        await WriteAuditAsync(
            providerSwitch,
            command.ActorUserId,
            AuditEventActions.AccountingProviderSwitchCreated,
            AuditEventOutcomes.Succeeded,
            "An accounting-system switch plan was created. The source system remains authoritative.",
            beforeStatus: null,
            afterStatus: providerSwitch.Status,
            reasonCode: null,
            command.CorrelationId,
            now,
            cancellationToken);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsActiveSwitchUniquenessViolation(exception))
        {
            _dbContext.ChangeTracker.Clear();
            throw Conflict(AccountingProviderSwitchReasonCodes.DuplicateActiveSwitch,
                "This company already has an active accounting-system switch. Complete or cancel it before creating another one.");
        }

        return await GetAsync(new(command.CompanyId, providerSwitch.Id), cancellationToken);
    }

    public async Task<AccountingProviderSwitchDto> GetAsync(
        GetAccountingProviderSwitchQuery query,
        CancellationToken cancellationToken)
    {
        ValidateCompany(query.CompanyId);
        var providerSwitch = await Switches(query.CompanyId, tracking: false)
            .SingleOrDefaultAsync(x => x.Id == query.SwitchId, cancellationToken)
            ?? throw NotFound();
        return ToDto(providerSwitch);
    }

    public async Task<IReadOnlyList<AccountingProviderSwitchDto>> ListAsync(
        ListAccountingProviderSwitchesQuery query,
        CancellationToken cancellationToken)
    {
        ValidateCompany(query.CompanyId);
        var status = string.IsNullOrWhiteSpace(query.Status)
            ? null
            : NormalizeStatus(query.Status);
        var switches = Switches(query.CompanyId, tracking: false);
        if (status is not null) switches = switches.Where(x => x.Status == status);
        return (await switches
                .OrderByDescending(x => x.UpdatedUtc)
                .Take(Math.Clamp(query.Limit, 1, 200))
                .ToListAsync(cancellationToken))
            .Select(ToDto)
            .ToArray();
    }

    public async Task<AccountingProviderSwitchDto> UpdatePlanAsync(
        UpdateAccountingProviderSwitchPlanCommand command,
        CancellationToken cancellationToken)
    {
        ValidateCommandContext(command.CompanyId, command.ActorUserId, command.CorrelationId);
        var providerSwitch = await FindTrackedAsync(command.CompanyId, command.SwitchId, cancellationToken);
        await EnsureVersionAsync(providerSwitch, command.ExpectedVersion, command.ActorUserId, command.CorrelationId, cancellationToken);
        if (!providerSwitch.CanUpdatePlan)
        {
            await RejectAsync(providerSwitch, command.ActorUserId, command.CorrelationId,
                AccountingProviderSwitchReasonCodes.PlanLocked,
                "Only a draft accounting-system switch plan can be edited.", cancellationToken);
            throw Conflict(AccountingProviderSwitchReasonCodes.PlanLocked,
                "Only a draft accounting-system switch plan can be edited.");
        }

        var plan = await ValidatePlanAsync(
            command.CompanyId,
            command.SourceKind,
            command.SourceProviderKey,
            command.TargetKind,
            command.TargetProviderKey,
            command.EffectiveFiscalPeriodId,
            command.MigrationStrategy,
            command.ResponsibleUserId,
            command.ResponsibleAgentId,
            cancellationToken);
        var before = DescribePlan(providerSwitch);
        var now = UtcNow();
        providerSwitch.UpdatePlan(
            plan.Source, plan.Target, plan.FiscalPeriod.Id, plan.Strategy, command.Reason,
            command.ResponsibleUserId, command.ResponsibleAgentId, command.ActorUserId,
            command.CorrelationId, now);
        await WriteAuditAsync(providerSwitch, command.ActorUserId,
            AuditEventActions.AccountingProviderSwitchPlanUpdated, AuditEventOutcomes.Succeeded,
            "The draft accounting-system switch plan was updated. Accounting authority remains unchanged.",
            beforeStatus: before, afterStatus: DescribePlan(providerSwitch), reasonCode: null,
            command.CorrelationId, now, cancellationToken);
        await SaveAsync(providerSwitch, command.ActorUserId, command.CorrelationId, cancellationToken);
        return await GetAsync(new(command.CompanyId, command.SwitchId), cancellationToken);
    }

    public async Task<AccountingProviderSwitchDto> CancelAsync(
        CancelAccountingProviderSwitchCommand command,
        CancellationToken cancellationToken)
    {
        ValidateCommandContext(command.CompanyId, command.ActorUserId, command.CorrelationId);
        var providerSwitch = await FindTrackedAsync(command.CompanyId, command.SwitchId, cancellationToken);
        await EnsureVersionAsync(providerSwitch, command.ExpectedVersion, command.ActorUserId, command.CorrelationId, cancellationToken);
        if (!providerSwitch.CanCancel)
        {
            await RejectAsync(providerSwitch, command.ActorUserId, command.CorrelationId,
                AccountingProviderSwitchReasonCodes.CancellationUnavailable,
                "This switch can no longer be cancelled because target activation has begun.", cancellationToken);
            throw Conflict(AccountingProviderSwitchReasonCodes.CancellationUnavailable,
                "This switch can no longer be cancelled because target activation has begun.");
        }

        var before = providerSwitch.Status;
        var now = UtcNow();
        providerSwitch.Cancel(command.Reason, command.ActorUserId, command.CorrelationId, now);
        await WriteAuditAsync(providerSwitch, command.ActorUserId,
            AuditEventActions.AccountingProviderSwitchCancelled, AuditEventOutcomes.Succeeded,
            "The accounting-system switch was cancelled. The source system remains authoritative.",
            before, providerSwitch.Status, null, command.CorrelationId, now, cancellationToken);
        await SaveAsync(providerSwitch, command.ActorUserId, command.CorrelationId, cancellationToken);
        return await GetAsync(new(command.CompanyId, command.SwitchId), cancellationToken);
    }

    public async Task<AccountingProviderSwitchAllowedActionsDto> GetAllowedActionsAsync(
        GetAccountingProviderSwitchAllowedActionsQuery query,
        CancellationToken cancellationToken)
    {
        ValidateCompany(query.CompanyId);
        var providerSwitch = await Switches(query.CompanyId, tracking: false)
            .SingleOrDefaultAsync(x => x.Id == query.SwitchId, cancellationToken)
            ?? throw NotFound();
        var transitions = AccountingProviderSwitchStatuses.AllowedTransitions(
            providerSwitch.Status, providerSwitch.BlockedFromStatus).ToList();
        AccountingProviderSwitchCompletenessDto? completeness = null;
        if (providerSwitch.Status == AccountingProviderSwitchStatuses.ReadyForPlanning && _stagingService is not null)
        {
            completeness = await _stagingService.GetCompletenessAsync(
                new GetAccountingProviderSwitchCompletenessQuery(query.CompanyId, query.SwitchId), cancellationToken);
            if (!completeness.IsComplete)
                transitions.Remove(AccountingProviderSwitchStatuses.PlanAwaitingApproval);
        }
        var ready = providerSwitch.Status != AccountingProviderSwitchStatuses.Blocked &&
                    !providerSwitch.IsTerminal && transitions.Count > 0;
        var explanation = providerSwitch.Status switch
        {
            AccountingProviderSwitchStatuses.Draft =>
                "The draft plan is valid and can move to read-only assessment. The source system remains authoritative.",
            AccountingProviderSwitchStatuses.ReadyForPlanning when completeness is { IsComplete: false } =>
                completeness.Explanation,
            AccountingProviderSwitchStatuses.Blocked =>
                providerSwitch.FailureSummary ?? "The switch is blocked and needs an operator recovery action.",
            AccountingProviderSwitchStatuses.Cancelled =>
                "The switch was cancelled and the source system remains authoritative.",
            AccountingProviderSwitchStatuses.Completed =>
                "The accounting-system switch is complete.",
            _ => "The available actions reflect the current persisted workflow state."
        };
        return new AccountingProviderSwitchAllowedActionsDto(
            providerSwitch.Id, providerSwitch.Version, providerSwitch.Status, providerSwitch.IsTerminal,
            providerSwitch.CanUpdatePlan, providerSwitch.CanCancel, ready, transitions, explanation,
            providerSwitch.FailureCode, providerSwitch.FailureSummary);
    }

    public async Task<AccountingProviderSwitchDto> TransitionAsync(
        TransitionAccountingProviderSwitchCommand command,
        CancellationToken cancellationToken)
    {
        ValidateCommandContext(command.CompanyId, command.ActorUserId, command.CorrelationId);
        var providerSwitch = await FindTrackedAsync(command.CompanyId, command.SwitchId, cancellationToken);
        await EnsureVersionAsync(providerSwitch, command.ExpectedVersion, command.ActorUserId, command.CorrelationId, cancellationToken);
        var before = providerSwitch.Status;
        var now = UtcNow();
        if (providerSwitch.Status == AccountingProviderSwitchStatuses.ReadyForPlanning &&
            string.Equals(command.NextStatus, AccountingProviderSwitchStatuses.PlanAwaitingApproval,
                StringComparison.OrdinalIgnoreCase) && _stagingService is not null)
        {
            var completeness = await _stagingService.GetCompletenessAsync(
                new GetAccountingProviderSwitchCompletenessQuery(command.CompanyId, command.SwitchId), cancellationToken);
            if (!completeness.IsComplete)
            {
                await RejectAsync(providerSwitch, command.ActorUserId, command.CorrelationId,
                    AccountingProviderSwitchReasonCodes.StagingIncomplete, completeness.Explanation, cancellationToken);
                throw Conflict(AccountingProviderSwitchReasonCodes.StagingIncomplete, completeness.Explanation);
            }
        }
        try
        {
            providerSwitch.TransitionTo(command.NextStatus, command.ActorUserId, command.CorrelationId, now);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            await RejectAsync(providerSwitch, command.ActorUserId, command.CorrelationId,
                AccountingProviderSwitchReasonCodes.IllegalTransition, exception.Message, cancellationToken);
            throw Conflict(AccountingProviderSwitchReasonCodes.IllegalTransition, exception.Message);
        }

        await WriteAuditAsync(providerSwitch, command.ActorUserId,
            AuditEventActions.AccountingProviderSwitchStatusChanged, AuditEventOutcomes.Succeeded,
            "The accounting-system switch advanced to its next controlled workflow state.",
            before, providerSwitch.Status, null, command.CorrelationId, now, cancellationToken);
        await SaveAsync(providerSwitch, command.ActorUserId, command.CorrelationId, cancellationToken);
        return await GetAsync(new(command.CompanyId, command.SwitchId), cancellationToken);
    }

    public async Task<AccountingProviderSwitchDto> BlockAsync(
        BlockAccountingProviderSwitchCommand command,
        CancellationToken cancellationToken)
    {
        ValidateCommandContext(command.CompanyId, command.ActorUserId, command.CorrelationId);
        var providerSwitch = await FindTrackedAsync(command.CompanyId, command.SwitchId, cancellationToken);
        await EnsureVersionAsync(providerSwitch, command.ExpectedVersion, command.ActorUserId, command.CorrelationId, cancellationToken);
        var before = providerSwitch.Status;
        var now = UtcNow();
        try
        {
            providerSwitch.Block(command.FailureCode, command.FailureSummary, command.ActorUserId, command.CorrelationId, now);
        }
        catch (InvalidOperationException exception)
        {
            await RejectAsync(providerSwitch, command.ActorUserId, command.CorrelationId,
                AccountingProviderSwitchReasonCodes.IllegalTransition, exception.Message, cancellationToken);
            throw Conflict(AccountingProviderSwitchReasonCodes.IllegalTransition, exception.Message);
        }

        await WriteAuditAsync(providerSwitch, command.ActorUserId,
            AuditEventActions.AccountingProviderSwitchBlocked, AuditEventOutcomes.Blocked,
            "The accounting-system switch was blocked with a safe operator-visible reason.",
            before, providerSwitch.Status, command.FailureCode, command.CorrelationId, now, cancellationToken);
        await SaveAsync(providerSwitch, command.ActorUserId, command.CorrelationId, cancellationToken);
        return await GetAsync(new(command.CompanyId, command.SwitchId), cancellationToken);
    }

    private async Task<ValidatedPlan> ValidatePlanAsync(
        Guid companyId,
        string sourceKind,
        string? sourceProviderKey,
        string targetKind,
        string? targetProviderKey,
        Guid effectiveFiscalPeriodId,
        string strategy,
        Guid responsibleUserId,
        Guid? responsibleAgentId,
        CancellationToken cancellationToken)
    {
        ValidateCompany(companyId);
        AccountingProviderEndpoint source;
        AccountingProviderEndpoint target;
        try
        {
            source = new AccountingProviderEndpoint(sourceKind, sourceProviderKey);
            target = new AccountingProviderEndpoint(targetKind, targetProviderKey);
        }
        catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException)
        {
            throw new AccountingAuthorityException(AccountingProviderSwitchReasonCodes.InvalidEndpoint, exception.Message);
        }
        if (source.IsSameAs(target))
            throw new AccountingAuthorityException(AccountingProviderSwitchReasonCodes.SameEndpoint,
                "Source and target accounting systems must be different.");

        string normalizedStrategy;
        try { normalizedStrategy = AccountingProviderSwitchStrategies.Normalize(strategy); }
        catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException)
        {
            throw new AccountingAuthorityException(AccountingProviderSwitchReasonCodes.InvalidStrategy, exception.Message);
        }

        var fiscalPeriod = await _dbContext.FiscalPeriods.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == effectiveFiscalPeriodId, cancellationToken)
            ?? throw new AccountingAuthorityException(AccountingProviderSwitchReasonCodes.FiscalPeriodNotFound,
                "Select an existing accounting period for this company.");
        var effectiveFrom = DateOnly.FromDateTime(fiscalPeriod.StartUtc);
        if (fiscalPeriod.StartUtc.TimeOfDay != TimeSpan.Zero || fiscalPeriod.EndUtc.TimeOfDay != TimeSpan.Zero ||
            effectiveFrom.Day != 1 || DateOnly.FromDateTime(fiscalPeriod.EndUtc) != effectiveFrom.AddMonths(1))
            throw new AccountingAuthorityException(AccountingProviderSwitchReasonCodes.MonthlyBoundaryRequired,
                "The switch must take effect at the start of an existing monthly accounting period.");
        if (effectiveFrom <= DateOnly.FromDateTime(UtcNow()))
            throw new AccountingAuthorityException(AccountingProviderSwitchReasonCodes.FutureBoundaryRequired,
                "Choose a future monthly accounting period for the switch.");

        var authority = await _dbContext.AccountingAuthorityPeriods.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.EffectiveFrom <= effectiveFrom &&
                        (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= effectiveFrom))
            .OrderByDescending(x => x.EffectiveFrom)
            .FirstOrDefaultAsync(cancellationToken);
        if (!SourceMatchesAuthority(source, authority))
            throw new AccountingAuthorityException(AccountingProviderSwitchReasonCodes.SourceAuthorityMismatch,
                "The selected source does not match the accounting system that remains authoritative at this boundary.");

        var responsibleUserExists = responsibleUserId != Guid.Empty &&
            await _dbContext.CompanyMemberships.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                x.CompanyId == companyId && x.UserId == responsibleUserId &&
                x.Status == CompanyMembershipStatus.Active, cancellationToken);
        if (!responsibleUserExists)
            throw new AccountingAuthorityException(AccountingProviderSwitchReasonCodes.ResponsibleUserInvalid,
                "Choose an active company member to own the accounting-system switch.");

        if (responsibleAgentId.HasValue)
        {
            if (responsibleAgentId == Guid.Empty || !await _dbContext.Agents.IgnoreQueryFilters().AsNoTracking()
                    .AnyAsync(x => x.CompanyId == companyId && x.Id == responsibleAgentId.Value, cancellationToken))
                throw new AccountingAuthorityException(AccountingProviderSwitchReasonCodes.ResponsibleAgentInvalid,
                    "The responsible agent was not found in this company.");
        }

        return new ValidatedPlan(source, target, fiscalPeriod, normalizedStrategy);
    }

    private async Task EnsureVersionAsync(
        AccountingProviderSwitch providerSwitch,
        long expectedVersion,
        Guid actorUserId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (providerSwitch.Version == expectedVersion) return;
        await RejectAsync(providerSwitch, actorUserId, correlationId,
            AccountingProviderSwitchReasonCodes.ConcurrencyConflict,
            "The accounting-system switch changed while this request was being reviewed.", cancellationToken);
        throw Conflict(AccountingProviderSwitchReasonCodes.ConcurrencyConflict,
            "The accounting-system switch changed while this request was being reviewed.");
    }

    private async Task SaveAsync(
        AccountingProviderSwitch providerSwitch,
        Guid actorUserId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        try { await _dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException)
        {
            _dbContext.ChangeTracker.Clear();
            var current = await FindTrackedAsync(providerSwitch.CompanyId, providerSwitch.Id, cancellationToken);
            await RejectAsync(current, actorUserId, correlationId,
                AccountingProviderSwitchReasonCodes.ConcurrencyConflict,
                "The accounting-system switch changed while this request was being applied.", cancellationToken);
            throw Conflict(AccountingProviderSwitchReasonCodes.ConcurrencyConflict,
                "The accounting-system switch changed while this request was being applied.");
        }
    }

    private async Task RejectAsync(
        AccountingProviderSwitch providerSwitch,
        Guid actorUserId,
        string correlationId,
        string reasonCode,
        string explanation,
        CancellationToken cancellationToken)
    {
        await WriteAuditAsync(providerSwitch, actorUserId,
            AuditEventActions.AccountingProviderSwitchMutationRejected, AuditEventOutcomes.Rejected,
            explanation, providerSwitch.Status, providerSwitch.Status, reasonCode,
            correlationId, UtcNow(), cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private Task WriteAuditAsync(
        AccountingProviderSwitch providerSwitch,
        Guid actorUserId,
        string action,
        string outcome,
        string summary,
        string? beforeStatus,
        string? afterStatus,
        string? reasonCode,
        string correlationId,
        DateTime occurredUtc,
        CancellationToken cancellationToken) =>
        _auditWriter.WriteAsync(new AuditEventWriteRequest(
            providerSwitch.CompanyId,
            AuditActorTypes.User,
            actorUserId,
            action,
            AuditTargetTypes.AccountingProviderSwitch,
            providerSwitch.Id.ToString("D"),
            outcome,
            summary,
            ["accounting_provider_switch", "accounting_authority", "fiscal_period"],
            new Dictionary<string, string?>
            {
                ["source"] = EndpointDescription(providerSwitch.Source),
                ["target"] = EndpointDescription(providerSwitch.Target),
                ["effectiveFiscalPeriodId"] = providerSwitch.EffectiveFiscalPeriodId.ToString("D"),
                ["migrationStrategy"] = providerSwitch.MigrationStrategy,
                ["actorUserId"] = actorUserId.ToString("D"),
                ["beforeState"] = beforeStatus,
                ["afterState"] = afterStatus,
                ["reasonCode"] = reasonCode
            },
            correlationId,
            occurredUtc), cancellationToken);

    private IQueryable<AccountingProviderSwitch> Switches(Guid companyId, bool tracking)
    {
        var query = _dbContext.AccountingProviderSwitches.IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .Include(x => x.EffectiveFiscalPeriod);
        return tracking ? query : query.AsNoTracking();
    }

    private async Task<AccountingProviderSwitch> FindTrackedAsync(
        Guid companyId,
        Guid switchId,
        CancellationToken cancellationToken) =>
        await Switches(companyId, tracking: true)
            .SingleOrDefaultAsync(x => x.Id == switchId, cancellationToken)
        ?? throw NotFound();

    private Task<bool> HasActiveSwitchAsync(Guid companyId, CancellationToken cancellationToken) =>
        _dbContext.AccountingProviderSwitches.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.CompanyId == companyId &&
                x.Status != AccountingProviderSwitchStatuses.Completed &&
                x.Status != AccountingProviderSwitchStatuses.Cancelled, cancellationToken);

    private static bool SourceMatchesAuthority(
        AccountingProviderEndpoint source,
        AccountingAuthorityPeriod? authority) =>
        authority is not null &&
        ((source.Kind == AccountingProviderEndpointKinds.Internal &&
          authority.Authority == AccountingAuthorityValues.InternalLedger) ||
         (source.Kind == AccountingProviderEndpointKinds.External &&
          authority.Authority == AccountingAuthorityValues.ExternalProvider &&
          string.Equals(source.ProviderKey, authority.ProviderKey, StringComparison.OrdinalIgnoreCase)));

    private static AccountingProviderSwitchDto ToDto(AccountingProviderSwitch providerSwitch)
    {
        var start = DateOnly.FromDateTime(providerSwitch.EffectiveFiscalPeriod.StartUtc);
        return new AccountingProviderSwitchDto(
            providerSwitch.Id,
            providerSwitch.CompanyId,
            ToEndpointDto(providerSwitch.Source),
            ToEndpointDto(providerSwitch.Target),
            Direction(providerSwitch.Source, providerSwitch.Target),
            providerSwitch.EffectiveFiscalPeriodId,
            start,
            DateOnly.FromDateTime(providerSwitch.EffectiveFiscalPeriod.EndUtc).AddDays(-1),
            providerSwitch.MigrationStrategy,
            StrategyLabel(providerSwitch.MigrationStrategy),
            providerSwitch.Reason,
            providerSwitch.ResponsibleUserId,
            providerSwitch.ResponsibleAgentId,
            providerSwitch.Status,
            StatusLabel(providerSwitch.Status),
            providerSwitch.BlockedFromStatus,
            providerSwitch.FailureCode,
            providerSwitch.FailureSummary,
            providerSwitch.CreatedByUserId,
            providerSwitch.UpdatedByUserId,
            providerSwitch.CancelledByUserId,
            providerSwitch.CancellationReason,
            providerSwitch.CorrelationId,
            providerSwitch.CreatedUtc,
            providerSwitch.UpdatedUtc,
            providerSwitch.StatusChangedUtc,
            providerSwitch.BlockedUtc,
            providerSwitch.CancelledUtc,
            providerSwitch.CompletedUtc,
            providerSwitch.Version);
    }

    private static AccountingProviderSwitchEndpointDto ToEndpointDto(AccountingProviderEndpoint endpoint) =>
        new(endpoint.Kind, endpoint.ProviderKey,
            endpoint.Kind == AccountingProviderEndpointKinds.Internal
                ? "Virtual Company"
                : endpoint.ProviderKey == FinanceIntegrationProviderKeys.Fortnox
                    ? "Fortnox"
                    : endpoint.ProviderKey!);

    private static string Direction(AccountingProviderEndpoint source, AccountingProviderEndpoint target) =>
        source.Kind == AccountingProviderEndpointKinds.Internal ? "outbound" :
        target.Kind == AccountingProviderEndpointKinds.Internal ? "inbound" : "provider_to_provider";

    private static string StrategyLabel(string strategy) => strategy switch
    {
        AccountingProviderSwitchStrategies.OpeningBalancesAndOpenItems => "Opening balances and open items",
        AccountingProviderSwitchStrategies.CurrentFiscalYear => "Current fiscal year",
        AccountingProviderSwitchStrategies.FullHistory => "Full history",
        _ => "Migration plan"
    };

    private static string StatusLabel(string status) => status switch
    {
        AccountingProviderSwitchStatuses.Draft => "Draft",
        AccountingProviderSwitchStatuses.Assessing => "Assessing source and target",
        AccountingProviderSwitchStatuses.ReadyForPlanning => "Ready for planning",
        AccountingProviderSwitchStatuses.PlanAwaitingApproval => "Plan awaiting approval",
        AccountingProviderSwitchStatuses.PreparingTarget => "Preparing target",
        AccountingProviderSwitchStatuses.RehearsalPassed => "Rehearsal passed",
        AccountingProviderSwitchStatuses.Scheduled => "Scheduled",
        AccountingProviderSwitchStatuses.SourceFrozen => "Source posting frozen",
        AccountingProviderSwitchStatuses.Reconciling => "Reconciling",
        AccountingProviderSwitchStatuses.ActivationAwaitingApproval => "Activation awaiting approval",
        AccountingProviderSwitchStatuses.TargetAuthoritative => "Target is authoritative",
        AccountingProviderSwitchStatuses.Monitoring => "Monitoring",
        AccountingProviderSwitchStatuses.Completed => "Completed",
        AccountingProviderSwitchStatuses.Blocked => "Blocked",
        AccountingProviderSwitchStatuses.Cancelled => "Cancelled",
        AccountingProviderSwitchStatuses.Recovery => "Recovery in progress",
        _ => "Needs review"
    };

    private static string DescribePlan(AccountingProviderSwitch providerSwitch) =>
        $"{EndpointDescription(providerSwitch.Source)}->{EndpointDescription(providerSwitch.Target)}|" +
        $"{providerSwitch.EffectiveFiscalPeriodId:D}|{providerSwitch.MigrationStrategy}|v{providerSwitch.Version}";

    private static string EndpointDescription(AccountingProviderEndpoint endpoint) =>
        endpoint.Kind == AccountingProviderEndpointKinds.Internal
            ? AccountingProviderEndpointKinds.Internal
            : $"{AccountingProviderEndpointKinds.External}:{endpoint.ProviderKey}";

    private static bool IsActiveSwitchUniquenessViolation(DbUpdateException exception)
    {
        var detail = exception.ToString();
        return detail.Contains("UX_accounting_provider_switches_company_active", StringComparison.OrdinalIgnoreCase) ||
               detail.Contains("accounting_provider_switches.company_id", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeStatus(string status)
    {
        try { return AccountingProviderSwitchStatuses.Normalize(status); }
        catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException)
        {
            throw new AccountingAuthorityException(AccountingProviderSwitchReasonCodes.IllegalTransition, exception.Message);
        }
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    private static AccountingAuthorityException NotFound() =>
        new(AccountingProviderSwitchReasonCodes.NotFound,
            "The accounting-system switch was not found for this company.");

    private static AccountingAuthorityException Conflict(string reasonCode, string message) =>
        new(reasonCode, message, isConflict: true);

    private static void ValidateCompany(Guid companyId)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(companyId));
    }

    private static void ValidateCommandContext(Guid companyId, Guid actorUserId, string correlationId)
    {
        ValidateCompany(companyId);
        if (actorUserId == Guid.Empty) throw new ArgumentException("ActorUserId is required.", nameof(actorUserId));
        if (string.IsNullOrWhiteSpace(correlationId)) throw new ArgumentException("CorrelationId is required.", nameof(correlationId));
    }

    private sealed record ValidatedPlan(
        AccountingProviderEndpoint Source,
        AccountingProviderEndpoint Target,
        FiscalPeriod FiscalPeriod,
        string Strategy);
}
