using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class CurrencyRevaluationService : ICurrencyRevaluationService
{
    private const string SourceType = "currency_revaluation_run";
    private readonly VirtualCompanyDbContext _db;
    private readonly IExchangeRateService _rates;
    private readonly IAccountingPostingService _posting;
    private readonly IAuditEventWriter _audit;
    private readonly TimeProvider _time;
    private readonly CurrencyRevaluationTelemetry _telemetry;

    public CurrencyRevaluationService(VirtualCompanyDbContext db, IExchangeRateService rates,
        IAccountingPostingService posting, IAuditEventWriter audit, TimeProvider time,
        CurrencyRevaluationTelemetry telemetry)
    {
        _db = db;
        _rates = rates;
        _posting = posting;
        _audit = audit;
        _time = time;
        _telemetry = telemetry;
    }

    public async Task<CurrencyRevaluationRunListDto> ListAsync(
        ListCurrencyRevaluationRunsQuery query, CancellationToken cancellationToken)
    {
        Require(query.CompanyId, nameof(query.CompanyId));
        var skip = Math.Max(0, query.Skip);
        var take = Math.Clamp(query.Take, 1, 100);
        var source = RunQuery(false).Where(x => x.CompanyId == query.CompanyId);
        if (query.FiscalPeriodId.HasValue) source = source.Where(x => x.FiscalPeriodId == query.FiscalPeriodId.Value);
        var total = await source.CountAsync(cancellationToken);
        var runs = await source.OrderByDescending(x => x.AsOfDate).ThenByDescending(x => x.RunNumber)
            .Skip(skip).Take(take).ToListAsync(cancellationToken);
        return new(runs.Select(Map).ToArray(), total, skip, take);
    }

    public async Task<CurrencyRevaluationRunDto> GetAsync(
        GetCurrencyRevaluationRunQuery query, CancellationToken cancellationToken) =>
        Map(await LoadAsync(query.CompanyId, query.RunId, false, cancellationToken));

    public async Task<CurrencyRevaluationRunDto> PreviewAsync(
        PreviewCurrencyRevaluationCommand command, CancellationToken cancellationToken)
    {
        ValidateIdentity(command.CompanyId, command.ActorUserId, command.IdempotencyKey);
        await EnsureActorAsync(command.CompanyId, command.ActorUserId, cancellationToken);
        var requestIdentity = command.IdempotencyKey.Trim();
        var replay = await RunQuery(false).SingleOrDefaultAsync(x =>
            x.CompanyId == command.CompanyId && x.RequestIdentity == requestIdentity, cancellationToken);
        if (replay is not null)
        {
            if (replay.FiscalPeriodId != command.FiscalPeriodId ||
                !string.Equals(replay.VoucherSeriesCode, command.VoucherSeriesCode.Trim(), StringComparison.OrdinalIgnoreCase))
                throw Error(CurrencyRevaluationReasonCodes.IdempotencyConflict,
                    "This request identity was already used for a different revaluation preview.", true);
            return Map(replay);
        }

        var period = await _db.FiscalPeriods.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.FiscalPeriodId, cancellationToken)
            ?? throw NotFound();
        if (period.IsClosed || period.IsReportingLocked)
            throw Error(CurrencyRevaluationReasonCodes.PeriodNotOpen,
                "Currency revaluation must be prepared and posted before the period is closed.");
        var configuration = await _db.AccountingConfigurations.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId, cancellationToken)
            ?? throw Error(AccountingPostingReasonCodes.ConfigurationMissing,
                "Complete accounting configuration before preparing currency revaluation.");
        var asOfDate = DateOnly.FromDateTime(period.EndUtc.AddTicks(-1));
        var runId = Guid.NewGuid();
        var now = Now();

        await using var transaction = _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken)
            : null;
        var runNumber = (await _db.CurrencyRevaluationRuns.IgnoreQueryFilters()
            .Where(x => x.CompanyId == command.CompanyId && x.FiscalPeriodId == period.Id)
            .Select(x => (int?)x.RunNumber).MaxAsync(cancellationToken) ?? 0) + 1;
        var run = new CurrencyRevaluationRun(runId, command.CompanyId, period.Id, runNumber,
            asOfDate, configuration.BaseCurrency, command.VoucherSeriesCode, requestIdentity,
            command.ActorUserId, now, command.Scheduled);
        _db.CurrencyRevaluationRuns.Add(run);

        var roles = await LoadRolesAsync(command.CompanyId, cancellationToken);
        if (!roles.TryGetValue(AccountingAccountRoleKeys.ExchangeGain, out var exchangeGainAccountId) ||
            !roles.TryGetValue(AccountingAccountRoleKeys.ExchangeLoss, out var exchangeLossAccountId))
        {
            run.Fail(CurrencyRevaluationReasonCodes.MissingGainLossAccounts,
                "Configure governed exchange gain and exchange loss account roles before revaluation.", now);
            await _db.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return Map(await LoadAsync(command.CompanyId, run.Id, false, cancellationToken));
        }

        var monetaryAccounts = await LoadMonetaryAccountsAsync(command.CompanyId, roles, cancellationToken);
        if (monetaryAccounts.Count == 0)
        {
            run.Fail(CurrencyRevaluationReasonCodes.MissingMonetaryAccounts,
                "No enabled foreign monetary accounts are configured for revaluation.", now);
            await _db.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return Map(await LoadAsync(command.CompanyId, run.Id, false, cancellationToken));
        }

        var groups = await BuildSourceGroupsAsync(command.CompanyId, asOfDate, configuration.BaseCurrency,
            monetaryAccounts, cancellationToken);
        foreach (var group in groups)
        {
            var itemId = Guid.NewGuid();
            var status = CurrencyRevaluationPopulationStatuses.Included;
            string? reviewReason = null;
            ExchangeRateConversionResult? conversion = null;
            ExchangeRateLookupResult? lookup = null;
            try
            {
                lookup = await _rates.LookupAsync(new ExchangeRateLookupQuery(command.CompanyId,
                    group.DocumentCurrency, configuration.BaseCurrency, asOfDate,
                    ExchangeRateLookupPurposes.PeriodEnd), cancellationToken);
                if (!lookup.IsReady)
                {
                    status = CurrencyRevaluationPopulationStatuses.NeedsReview;
                    reviewReason = lookup.Explanation;
                }
                else
                {
                    var rateIdentity = Hash(JsonSerializer.Serialize(lookup.Legs.Select(x => new
                    {
                        x.ObservationId, x.SourceKey, x.SourceSetVersion, x.Factor, x.EffectiveDate, x.EvidenceChecksum
                    })));
                    conversion = await _rates.ConvertAsync(new ConvertCurrencyCommand(command.CompanyId,
                        command.ActorUserId, group.DocumentBalance, group.DocumentCurrency,
                        configuration.BaseCurrency, asOfDate, ExchangeRateLookupPurposes.PeriodEnd,
                        $"revaluation:{command.CompanyId:N}:{period.Id:N}:{group.PopulationKey}:{rateIdentity}",
                        command.CorrelationId), cancellationToken);
                }
            }
            catch (ExchangeRateOperationException exception)
            {
                status = CurrencyRevaluationPopulationStatuses.NeedsReview;
                reviewReason = exception.SafeMessage;
            }

            var revalued = conversion?.RoundedAmount ?? group.CarryingFunctionalAmount;
            var adjustment = Round(revalued - group.CarryingFunctionalAmount,
                configuration.RoundingPrecision, configuration.RoundingMode);
            var item = new CurrencyRevaluationPopulationItem(itemId, command.CompanyId, run.Id,
                group.PopulationKey, group.MonetaryClass, group.AccountId, group.AccountCode,
                group.AccountName, group.NormalBalance, group.DocumentCurrency, configuration.BaseCurrency,
                group.DocumentBalance, group.CarryingFunctionalAmount, revalued, adjustment,
                conversion?.Id, conversion?.EffectiveRate, conversion?.RequestedDate, group.SourceChecksum,
                status, reviewReason);
            run.PopulationItems.Add(item);
            if (conversion is not null && lookup is not null)
            {
                var setIdentity = string.Join("|", lookup.Legs.Select(x => $"{x.SourceKey}@{x.SourceSetVersion}").Distinct());
                var observationIdentity = string.Join("|", lookup.Legs.Select(x => x.ObservationId.ToString("N")));
                var evidenceChecksum = Hash(string.Join("|", lookup.Legs.Select(x => x.EvidenceChecksum)));
                run.RateBindings.Add(new CurrencyRevaluationRateBinding(Guid.NewGuid(), command.CompanyId,
                    run.Id, item.Id, conversion.Id, group.DocumentCurrency, configuration.BaseCurrency,
                    conversion.EffectiveRate, conversion.RequestedDate, setIdentity, observationIdentity,
                    evidenceChecksum));
            }
        }

        BuildProposal(run, exchangeGainAccountId, exchangeLossAccountId, configuration, now);
        var alreadyPosted = await _db.CurrencyRevaluationRuns.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.CompanyId == command.CompanyId && x.FiscalPeriodId == period.Id &&
                (x.Status == CurrencyRevaluationRunStatuses.Posted || x.Status == CurrencyRevaluationRunStatuses.Reversed) &&
                x.PopulationChecksum == run.PopulationChecksum, cancellationToken);
        if (alreadyPosted)
            throw Error(CurrencyRevaluationReasonCodes.AlreadyPosted,
                "This exact period-end foreign-currency population has already been posted. Reverse the posted run before preparing a replacement.", true);
        foreach (var prior in await _db.CurrencyRevaluationRuns.IgnoreQueryFilters()
                     .Include(x => x.ApprovalRequest)
                     .Where(x => x.CompanyId == command.CompanyId && x.FiscalPeriodId == period.Id &&
                         x.Id != run.Id && (x.Status == CurrencyRevaluationRunStatuses.Draft ||
                         x.Status == CurrencyRevaluationRunStatuses.NeedsReview ||
                         x.Status == CurrencyRevaluationRunStatuses.AwaitingApproval))
                     .ToListAsync(cancellationToken))
        {
            if (prior.ApprovalRequest?.Status == ApprovalRequestStatus.Pending)
                prior.ApprovalRequest.MarkCancelled("Superseded by a regenerated currency revaluation proposal.");
            prior.Supersede(run.Id, now);
        }

        await AuditAsync(command.CompanyId, command.ActorUserId,
            AuditEventActions.AccountingCurrencyRevaluationPreviewed, run.Id,
            $"Prepared period-end currency revaluation run {run.RunNumber} with {run.PopulationCount} retained population item(s).",
            command.CorrelationId, run, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        _telemetry.Record("preview", run.Status, run.PopulationCount);
        return Map(await LoadAsync(command.CompanyId, run.Id, false, cancellationToken));
    }

    public async Task<CurrencyRevaluationRunDto> ReviewItemAsync(
        ReviewCurrencyRevaluationItemCommand command, CancellationToken cancellationToken)
    {
        await EnsureActorAsync(command.CompanyId, command.ActorUserId, cancellationToken);
        var run = await LoadAsync(command.CompanyId, command.RunId, true, cancellationToken);
        EnsureVersion(run, command.ExpectedVersion);
        if (!CurrencyRevaluationRunStatuses.IsMutable(run.Status))
            throw Error(CurrencyRevaluationReasonCodes.VersionConflict,
                "Posted, reversed, failed, or superseded revaluation runs cannot be reviewed.", true, run.Version);
        var item = run.PopulationItems.SingleOrDefault(x => x.Id == command.PopulationItemId)
            ?? throw NotFound();
        if (command.Action == CurrencyRevaluationReviewActions.Include && !item.ExchangeRateConversionId.HasValue)
            throw Error(CurrencyRevaluationReasonCodes.MissingRate,
                "This balance cannot be included until an authoritative period-end rate is available. Exclude it with evidence or regenerate after rates are corrected.");
        if (run.ApprovalRequest?.Status == ApprovalRequestStatus.Pending)
            run.ApprovalRequest.MarkCancelled("The revaluation population changed after approval was requested.");
        item.Review(command.Action, command.Reason);
        var roles = await LoadRolesAsync(command.CompanyId, cancellationToken);
        if (!roles.TryGetValue(AccountingAccountRoleKeys.ExchangeGain, out var gain) ||
            !roles.TryGetValue(AccountingAccountRoleKeys.ExchangeLoss, out var loss))
            throw Error(CurrencyRevaluationReasonCodes.MissingGainLossAccounts,
                "Configure governed exchange gain and exchange loss account roles before revaluation.");
        var configuration = await _db.AccountingConfigurations.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x => x.CompanyId == command.CompanyId, cancellationToken);
        _db.CurrencyRevaluationProposalLines.RemoveRange(run.ProposalLines);
        _db.CurrencyRevaluationReconciliations.RemoveRange(run.Reconciliations);
        run.ProposalLines.Clear();
        run.Reconciliations.Clear();
        var now = Now();
        BuildProposal(run, gain, loss, configuration, now);
        foreach (var line in run.ProposalLines) _db.Entry(line).State = EntityState.Added;
        foreach (var reconciliation in run.Reconciliations) _db.Entry(reconciliation).State = EntityState.Added;
        AddReview(run, new CurrencyRevaluationReview(Guid.NewGuid(), command.CompanyId, run.Id,
            item.Id, command.Action, command.Reason, command.ActorUserId, null,
            run.ProposalChecksum!, now));
        await AuditAsync(command.CompanyId, command.ActorUserId,
            AuditEventActions.AccountingCurrencyRevaluationReviewed, run.Id,
            "Reviewed a period-end revaluation population item and regenerated the exact proposal.",
            command.CorrelationId, run, cancellationToken);
        await SaveAsync(cancellationToken);
        _telemetry.Record("review", run.Status, 1);
        return Map(await LoadAsync(command.CompanyId, run.Id, false, cancellationToken));
    }

    public async Task<CurrencyRevaluationRunDto> SubmitAsync(
        SubmitCurrencyRevaluationCommand command, CancellationToken cancellationToken)
    {
        await EnsureActorAsync(command.CompanyId, command.ActorUserId, cancellationToken);
        var run = await LoadAsync(command.CompanyId, command.RunId, true, cancellationToken);
        EnsureVersion(run, command.ExpectedVersion);
        if (run.Status == CurrencyRevaluationRunStatuses.AwaitingApproval && run.ApprovalRequestId.HasValue)
            return Map(run);
        if (run.Status != CurrencyRevaluationRunStatuses.Draft || run.ReviewCount > 0 ||
            run.Reconciliations.Any(x => !x.IsReconciled))
            throw Error(CurrencyRevaluationReasonCodes.ReviewRequired,
                "Resolve every population and reconciliation issue before requesting approval.");
        await EnsureProposalCurrentAsync(run, cancellationToken);

        var approvalVersion = run.Version + 1;
        var approval = ApprovalRequest.CreateForTarget(Guid.NewGuid(), command.CompanyId,
            ApprovalTargetEntityType.CurrencyRevaluationRun, run.Id, AuditActorTypes.User,
            command.ActorUserId, "period_end_currency_revaluation",
            new Dictionary<string, JsonNode?>
            {
                ["sourceVersion"] = JsonValue.Create(approvalVersion.ToString(CultureInfo.InvariantCulture)),
                ["payloadHash"] = JsonValue.Create(run.ProposalChecksum),
                ["populationChecksum"] = JsonValue.Create(run.PopulationChecksum),
                ["rateSetChecksum"] = JsonValue.Create(run.RateSetChecksum),
                ["adjustmentAmount"] = JsonValue.Create(run.ProposedAdjustmentTotal),
                ["currency"] = JsonValue.Create(run.FunctionalCurrency)
            }, null, null, [new ApprovalStepDefinition(1, ApprovalStepApproverType.Role, "finance_approver")]);
        _db.ApprovalRequests.Add(approval);
        var now = Now();
        run.BindApproval(approval.Id, now);
        AddReview(run, new CurrencyRevaluationReview(Guid.NewGuid(), command.CompanyId, run.Id,
            null, CurrencyRevaluationReviewActions.Submit,
            "Submitted the exact retained population, rate set, and proposal for finance approval.",
            command.ActorUserId, approval.Id, run.ProposalChecksum!, now));
        await AuditAsync(command.CompanyId, command.ActorUserId,
            AuditEventActions.AccountingCurrencyRevaluationApprovalRequested, run.Id,
            "Requested finance approval for the exact current currency revaluation proposal.",
            command.CorrelationId, run, cancellationToken);
        await SaveAsync(cancellationToken);
        _telemetry.Record("submit", run.Status, run.PopulationCount);
        return Map(await LoadAsync(command.CompanyId, run.Id, false, cancellationToken));
    }

    public async Task<CurrencyRevaluationRunDto> PostAsync(
        PostCurrencyRevaluationCommand command, CancellationToken cancellationToken)
    {
        ValidateIdentity(command.CompanyId, command.ActorUserId, command.IdempotencyKey);
        await EnsureActorAsync(command.CompanyId, command.ActorUserId, cancellationToken);
        var run = await LoadAsync(command.CompanyId, command.RunId, true, cancellationToken);
        if (run.Status == CurrencyRevaluationRunStatuses.Posted) return Map(run);
        EnsureVersion(run, command.ExpectedVersion);
        EnsureApproved(run);
        await EnsureProposalCurrentAsync(run, cancellationToken);
        if (run.Reconciliations.Any(x => !x.IsReconciled))
            throw Error(CurrencyRevaluationReasonCodes.ReconciliationFailed,
                "Revaluation control totals do not reconcile to the retained proposal lines.");

        var now = Now();
        if (run.ProposalLines.Count == 0)
        {
            run.MarkCompletedWithoutPosting(command.ActorUserId, now);
        }
        else
        {
            var proposed = ToProposed(run, command.ActorUserId, command.IdempotencyKey);
            var preview = await _posting.PreviewAsync(new PreviewAccountingEntryCommand(proposed), cancellationToken);
            if (!preview.IsValid)
                throw Error(preview.Issues[0].ReasonCode, preview.Issues[0].Explanation);
            var posted = await _posting.PostAsync(new PostAccountingEntryCommand(proposed, command.CorrelationId), cancellationToken);
            run.MarkPosted(posted.Journal.Id, command.ActorUserId, posted.Journal.PostedAtUtc ?? now);
        }
        AddReview(run, new CurrencyRevaluationReview(Guid.NewGuid(), command.CompanyId, run.Id,
            null, CurrencyRevaluationReviewActions.Post,
            run.LedgerEntryId.HasValue ? "Posted the approved revaluation through the native accounting boundary." : "Completed the approved zero-adjustment revaluation without creating an empty journal.",
            command.ActorUserId, run.ApprovalRequestId, run.ProposalChecksum!, now));
        await AuditAsync(command.CompanyId, command.ActorUserId,
            AuditEventActions.AccountingCurrencyRevaluationPosted, run.Id,
            run.LedgerEntryId.HasValue ? "Posted the approved period-end currency revaluation." : "Completed the approved period-end revaluation with no journal because the adjustment was zero.",
            command.CorrelationId, run, cancellationToken);
        await SaveAsync(cancellationToken);
        _telemetry.Record("post", run.Status, run.PopulationCount);
        return Map(await LoadAsync(command.CompanyId, run.Id, false, cancellationToken));
    }

    public async Task<CurrencyRevaluationRunDto> ReverseAsync(
        ReverseCurrencyRevaluationCommand command, CancellationToken cancellationToken)
    {
        ValidateIdentity(command.CompanyId, command.ActorUserId, command.IdempotencyKey);
        await EnsureActorAsync(command.CompanyId, command.ActorUserId, cancellationToken);
        var run = await LoadAsync(command.CompanyId, command.RunId, true, cancellationToken);
        if (run.Status == CurrencyRevaluationRunStatuses.Reversed) return Map(run);
        EnsureVersion(run, command.ExpectedVersion);
        if (run.Status != CurrencyRevaluationRunStatuses.Posted)
            throw Error(CurrencyRevaluationReasonCodes.AlreadyPosted,
                "Only a posted revaluation can be reversed.");
        if (!run.LedgerEntryId.HasValue)
        {
            return Map(run);
        }
        var nextPeriod = await _db.FiscalPeriods.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == command.CompanyId && x.StartUtc >= run.FiscalPeriod.EndUtc)
            .OrderBy(x => x.StartUtc).FirstOrDefaultAsync(cancellationToken)
            ?? throw Error(CurrencyRevaluationReasonCodes.NextPeriodMissing,
                "Create the next fiscal period before reversing this period-end revaluation.");
        if (nextPeriod.IsClosed || nextPeriod.IsReportingLocked)
            throw Error(CurrencyRevaluationReasonCodes.PeriodNotOpen,
                "The next fiscal period is closed and cannot receive the automatic revaluation reversal.");

        var existing = await _db.LedgerEntries.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == command.CompanyId && x.OriginalLedgerEntryId == run.LedgerEntryId &&
                x.PostingType == LedgerPostingTypeValues.Reversal)
            .OrderBy(x => x.PostedAtUtc).FirstOrDefaultAsync(cancellationToken);
        var now = Now();
        Guid reversalId;
        if (existing is not null)
        {
            reversalId = existing.Id;
        }
        else
        {
            var postingDate = DateOnly.FromDateTime(nextPeriod.StartUtc);
            var reversed = await _posting.ReverseAsync(new ReverseAccountingEntryCommand(command.CompanyId,
                run.LedgerEntryId.Value, nextPeriod.Id, run.VoucherSeriesCode, postingDate,
                $"Automatic reversal of period-end currency revaluation run {run.RunNumber}.",
                run.Version.ToString(CultureInfo.InvariantCulture), command.IdempotencyKey,
                command.ActorUserId, null, command.CorrelationId), cancellationToken);
            reversalId = reversed.Journal.Id;
        }
        run.MarkReversed(reversalId, command.ActorUserId, now);
        AddReview(run, new CurrencyRevaluationReview(Guid.NewGuid(), command.CompanyId, run.Id,
            null, CurrencyRevaluationReviewActions.Reverse,
            "Reversed the posted revaluation exactly once in the next open fiscal period.",
            command.ActorUserId, null, run.ProposalChecksum!, now));
        await AuditAsync(command.CompanyId, command.ActorUserId,
            AuditEventActions.AccountingCurrencyRevaluationReversed, run.Id,
            "Reversed the period-end currency revaluation in the next fiscal period.",
            command.CorrelationId, run, cancellationToken);
        await SaveAsync(cancellationToken);
        _telemetry.Record("reverse", run.Status, run.PopulationCount);
        return Map(await LoadAsync(command.CompanyId, run.Id, false, cancellationToken));
    }

    public async Task<IReadOnlyList<CurrencyRevaluationAccountPolicyDto>> ListAccountPoliciesAsync(
        Guid companyId, CancellationToken cancellationToken)
    {
        Require(companyId, nameof(companyId));
        return await _db.CurrencyRevaluationAccountPolicies.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId).OrderBy(x => x.FinanceAccount.Code)
            .Select(x => new CurrencyRevaluationAccountPolicyDto(x.Id, x.FinanceAccountId,
                x.FinanceAccount.Code, x.FinanceAccount.Name, x.MonetaryClass, x.IsEnabled,
                x.Version, x.UpdatedUtc)).ToListAsync(cancellationToken);
    }

    public async Task<CurrencyRevaluationAccountPolicyDto> ConfigureAccountAsync(
        ConfigureCurrencyRevaluationAccountCommand command, CancellationToken cancellationToken)
    {
        await EnsureActorAsync(command.CompanyId, command.ActorUserId, cancellationToken);
        var account = await _db.FinanceAccounts.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.FinanceAccountId,
                cancellationToken) ?? throw NotFound();
        var policy = await _db.CurrencyRevaluationAccountPolicies.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId &&
                x.FinanceAccountId == command.FinanceAccountId, cancellationToken);
        var now = Now();
        if (policy is null)
        {
            if (command.ExpectedVersion is > 0) throw Conflict("The monetary account policy no longer exists.");
            policy = new CurrencyRevaluationAccountPolicy(Guid.NewGuid(), command.CompanyId,
                command.FinanceAccountId, command.MonetaryClass, command.IsEnabled,
                command.ActorUserId, now);
            _db.CurrencyRevaluationAccountPolicies.Add(policy);
        }
        else
        {
            if (!command.ExpectedVersion.HasValue) throw Conflict("An expected version is required when changing a monetary account policy.");
            try { policy.Update(command.MonetaryClass, command.IsEnabled, command.ActorUserId, command.ExpectedVersion.Value, now); }
            catch (InvalidOperationException) { throw Conflict("The monetary account policy changed. Reload before retrying.", policy.Version); }
        }
        await AuditConfigurationAsync(command.CompanyId, command.ActorUserId, policy.Id,
            $"Configured account {account.Code} for period-end currency revaluation.", command.CorrelationId,
            cancellationToken);
        await SaveAsync(cancellationToken);
        return new(policy.Id, account.Id, account.Code, account.Name, policy.MonetaryClass,
            policy.IsEnabled, policy.Version, policy.UpdatedUtc);
    }

    public async Task<CurrencyRevaluationScheduleDto?> GetScheduleAsync(Guid companyId,
        CancellationToken cancellationToken)
    {
        Require(companyId, nameof(companyId));
        var schedule = await _db.CurrencyRevaluationSchedules.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId, cancellationToken);
        return schedule is null ? null : Map(schedule);
    }

    public async Task<CurrencyRevaluationScheduleDto> ConfigureScheduleAsync(
        ConfigureCurrencyRevaluationScheduleCommand command, CancellationToken cancellationToken)
    {
        await EnsureActorAsync(command.CompanyId, command.ActorUserId, cancellationToken);
        var schedule = await _db.CurrencyRevaluationSchedules.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId, cancellationToken);
        var now = Now();
        if (schedule is null)
        {
            if (command.ExpectedVersion is > 0) throw Conflict("The revaluation schedule no longer exists.");
            schedule = new CurrencyRevaluationSchedule(Guid.NewGuid(), command.CompanyId,
                command.IsEnabled, command.DaysBeforePeriodEnd, command.AutomaticReversal,
                command.VoucherSeriesCode, command.ActorUserId, now);
            _db.CurrencyRevaluationSchedules.Add(schedule);
        }
        else
        {
            if (!command.ExpectedVersion.HasValue) throw Conflict("An expected version is required when changing the revaluation schedule.");
            try { schedule.Update(command.IsEnabled, command.DaysBeforePeriodEnd, command.AutomaticReversal,
                command.VoucherSeriesCode, command.ActorUserId, command.ExpectedVersion.Value, now); }
            catch (InvalidOperationException) { throw Conflict("The revaluation schedule changed. Reload before retrying.", schedule.Version); }
        }
        await AuditConfigurationAsync(command.CompanyId, command.ActorUserId, schedule.Id,
            "Updated the governed period-end currency revaluation schedule.", command.CorrelationId,
            cancellationToken);
        await SaveAsync(cancellationToken);
        return Map(schedule);
    }

    public async Task<int> RunScheduledAsync(CancellationToken cancellationToken)
    {
        var now = Now();
        var today = DateOnly.FromDateTime(now);
        var schedules = await _db.CurrencyRevaluationSchedules.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.IsEnabled).OrderBy(x => x.CompanyId).Take(100).ToListAsync(cancellationToken);
        var completed = 0;
        foreach (var schedule in schedules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var period = await _db.FiscalPeriods.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == schedule.CompanyId && !x.IsClosed && !x.IsReportingLocked)
                .OrderBy(x => x.EndUtc).FirstOrDefaultAsync(cancellationToken);
            if (period is not null)
            {
                var asOf = DateOnly.FromDateTime(period.EndUtc.AddTicks(-1));
                if (asOf.DayNumber - today.DayNumber <= schedule.DaysBeforePeriodEnd)
                {
                    var hasCurrent = await _db.CurrencyRevaluationRuns.IgnoreQueryFilters().AsNoTracking()
                        .AnyAsync(x => x.CompanyId == schedule.CompanyId && x.FiscalPeriodId == period.Id &&
                            x.Status != CurrencyRevaluationRunStatuses.Superseded &&
                            x.Status != CurrencyRevaluationRunStatuses.Failed, cancellationToken);
                    if (!hasCurrent)
                    {
                        await PreviewAsync(new PreviewCurrencyRevaluationCommand(schedule.CompanyId, period.Id,
                            schedule.VoucherSeriesCode,
                            $"scheduled-revaluation:{schedule.CompanyId:N}:{period.Id:N}:{asOf:yyyyMMdd}",
                            schedule.UpdatedByUserId, "scheduled-currency-revaluation", true), cancellationToken);
                        completed++;
                    }
                }
            }

            if (schedule.AutomaticReversal)
            {
                var dueRuns = await _db.CurrencyRevaluationRuns.IgnoreQueryFilters().AsNoTracking()
                    .Include(x => x.FiscalPeriod)
                    .Where(x => x.CompanyId == schedule.CompanyId && x.Status == CurrencyRevaluationRunStatuses.Posted &&
                        x.LedgerEntryId != null && x.FiscalPeriod.EndUtc <= now)
                    .OrderBy(x => x.FiscalPeriod.EndUtc).Take(20).ToListAsync(cancellationToken);
                foreach (var due in dueRuns)
                {
                    await ReverseAsync(new ReverseCurrencyRevaluationCommand(schedule.CompanyId, due.Id,
                        due.Version, $"scheduled-revaluation-reversal:{due.Id:N}", schedule.UpdatedByUserId,
                        "scheduled-currency-revaluation-reversal"), cancellationToken);
                    completed++;
                }
            }

            var tracked = await _db.CurrencyRevaluationSchedules.IgnoreQueryFilters()
                .SingleAsync(x => x.CompanyId == schedule.CompanyId, cancellationToken);
            tracked.MarkEvaluated(now);
            await SaveAsync(cancellationToken);
        }
        return completed;
    }

    private IQueryable<CurrencyRevaluationRun> RunQuery(bool tracking)
    {
        var query = _db.CurrencyRevaluationRuns.IgnoreQueryFilters();
        if (!tracking) query = query.AsNoTracking();
        return query.Include(x => x.FiscalPeriod).Include(x => x.ApprovalRequest)
            .Include(x => x.PopulationItems).Include(x => x.RateBindings)
            .Include(x => x.ProposalLines).ThenInclude(x => x.FinanceAccount)
            .Include(x => x.Reviews).Include(x => x.Reconciliations);
    }

    private async Task<CurrencyRevaluationRun> LoadAsync(Guid companyId, Guid runId, bool tracking,
        CancellationToken cancellationToken)
    {
        Require(companyId, nameof(companyId)); Require(runId, nameof(runId));
        return await RunQuery(tracking).SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == runId,
            cancellationToken) ?? throw NotFound();
    }

    private async Task<Dictionary<string, Guid>> LoadRolesAsync(Guid companyId,
        CancellationToken cancellationToken) => await _db.AccountingConfigurationAccountRoles
        .IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId)
        .ToDictionaryAsync(x => x.RoleKey, x => x.FinanceAccountId, StringComparer.OrdinalIgnoreCase,
            cancellationToken);

    private async Task<Dictionary<Guid, MonetaryAccount>> LoadMonetaryAccountsAsync(Guid companyId,
        IReadOnlyDictionary<string, Guid> roles, CancellationToken cancellationToken)
    {
        var classes = new Dictionary<Guid, string>();
        AddRole(AccountingAccountRoleKeys.Cash, CurrencyRevaluationMonetaryClasses.Cash);
        AddRole(AccountingAccountRoleKeys.Bank, CurrencyRevaluationMonetaryClasses.Cash);
        AddRole(AccountingAccountRoleKeys.AccountsReceivable, CurrencyRevaluationMonetaryClasses.Receivable);
        AddRole(AccountingAccountRoleKeys.AccountsPayable, CurrencyRevaluationMonetaryClasses.Payable);
        var policies = await _db.CurrencyRevaluationAccountPolicies.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId).ToListAsync(cancellationToken);
        foreach (var policy in policies)
        {
            if (policy.IsEnabled) classes[policy.FinanceAccountId] = policy.MonetaryClass;
            else classes.Remove(policy.FinanceAccountId);
        }
        if (classes.Count == 0) return [];
        var ids = classes.Keys.ToArray();
        var accounts = await _db.FinanceAccounts.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && ids.Contains(x.Id) && x.IsPostingEnabled)
            .ToListAsync(cancellationToken);
        return accounts.ToDictionary(x => x.Id, x => new MonetaryAccount(x.Id, x.Code, x.Name,
            x.NormalBalance ?? throw Error(CurrencyRevaluationReasonCodes.MissingMonetaryAccounts,
                $"Monetary account {x.Code} is missing its normal-balance classification."),
            classes[x.Id], x.Currency, x.OpeningBalance));

        void AddRole(string role, string classification)
        {
            if (roles.TryGetValue(role, out var id)) classes[id] = classification;
        }
    }

    private async Task<IReadOnlyList<SourceGroup>> BuildSourceGroupsAsync(Guid companyId, DateOnly asOfDate,
        string baseCurrency, IReadOnlyDictionary<Guid, MonetaryAccount> monetaryAccounts,
        CancellationToken cancellationToken)
    {
        var accountIds = monetaryAccounts.Keys.ToArray();
        var rows = await _db.LedgerEntryLines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && accountIds.Contains(x.FinanceAccountId) &&
                x.LedgerEntry.Status == LedgerEntryStatuses.Posted && x.LedgerEntry.PostingDate.HasValue &&
                x.LedgerEntry.PostingDate.Value <= asOfDate && x.DocumentCurrency != baseCurrency)
            .Select(x => new SourceLine(x.Id, x.FinanceAccountId, x.DocumentCurrency,
                x.DocumentDebitAmount, x.DocumentCreditAmount, x.DebitAmount, x.CreditAmount))
            .ToListAsync(cancellationToken);
        var groups = new List<SourceGroup>();
        foreach (var grouping in rows.GroupBy(x => new { x.FinanceAccountId, x.DocumentCurrency })
                     .OrderBy(x => monetaryAccounts[x.Key.FinanceAccountId].Code)
                     .ThenBy(x => x.Key.DocumentCurrency))
        {
            var account = monetaryAccounts[grouping.Key.FinanceAccountId];
            var normalDebit = account.NormalBalance == FinanceNormalBalanceValues.Debit;
            var document = grouping.Sum(x => normalDebit
                ? x.DocumentDebitAmount - x.DocumentCreditAmount
                : x.DocumentCreditAmount - x.DocumentDebitAmount);
            var carrying = grouping.Sum(x => normalDebit
                ? x.DebitAmount - x.CreditAmount
                : x.CreditAmount - x.DebitAmount);
            if (document == 0m && carrying == 0m) continue;
            var populationKey = $"{account.Id:N}:{grouping.Key.DocumentCurrency}";
            var sourceChecksum = Hash(JsonSerializer.Serialize(grouping.OrderBy(x => x.Id).Select(x => new
            {
                x.Id, x.DocumentDebitAmount, x.DocumentCreditAmount, x.DebitAmount, x.CreditAmount
            })));
            groups.Add(new SourceGroup(populationKey, account.Id, account.Code, account.Name,
                account.NormalBalance, account.MonetaryClass, grouping.Key.DocumentCurrency,
                document, carrying, sourceChecksum));
        }
        return groups;
    }

    private static void BuildProposal(CurrencyRevaluationRun run, Guid exchangeGainAccountId,
        Guid exchangeLossAccountId, AccountingConfiguration configuration, DateTime now)
    {
        var sequence = 0;
        foreach (var item in run.PopulationItems
                     .Where(x => x.Status == CurrencyRevaluationPopulationStatuses.Included && x.AdjustmentAmount != 0m)
                     .OrderBy(x => x.AccountCode).ThenBy(x => x.DocumentCurrency))
        {
            var amount = Math.Abs(Round(item.AdjustmentAmount, configuration.RoundingPrecision,
                configuration.RoundingMode));
            if (amount == 0m) continue;
            var controlDebit = item.NormalBalance == FinanceNormalBalanceValues.Debit
                ? item.AdjustmentAmount > 0m : item.AdjustmentAmount < 0m;
            run.ProposalLines.Add(new CurrencyRevaluationProposalLine(Guid.NewGuid(), run.CompanyId,
                run.Id, ++sequence, item.FinanceAccountId, item.Id, "monetary_account",
                controlDebit ? amount : 0m, controlDebit ? 0m : amount, run.FunctionalCurrency,
                $"Period-end revaluation of {item.AccountCode} {item.DocumentCurrency}."));
            run.ProposalLines.Add(new CurrencyRevaluationProposalLine(Guid.NewGuid(), run.CompanyId,
                run.Id, ++sequence, controlDebit ? exchangeGainAccountId : exchangeLossAccountId,
                item.Id, controlDebit ? "unrealized_gain" : "unrealized_loss",
                controlDebit ? 0m : amount, controlDebit ? amount : 0m, run.FunctionalCurrency,
                $"Unrealized exchange {(controlDebit ? "gain" : "loss")} for {item.AccountCode} {item.DocumentCurrency}."));
        }

        foreach (var group in run.PopulationItems.GroupBy(x => x.MonetaryClass).OrderBy(x => x.Key))
        {
            var included = group.Where(x => x.Status == CurrencyRevaluationPopulationStatuses.Included).ToArray();
            var proposed = included.Sum(x => x.AdjustmentAmount);
            var controlLines = run.ProposalLines.Where(x => x.LineType == "monetary_account" &&
                x.PopulationItemId.HasValue && included.Select(i => i.Id).Contains(x.PopulationItemId.Value));
            var proposalLineAdjustment = controlLines.Sum(x =>
            {
                var item = included.Single(i => i.Id == x.PopulationItemId);
                return item.NormalBalance == FinanceNormalBalanceValues.Debit
                    ? x.DebitAmount - x.CreditAmount : x.CreditAmount - x.DebitAmount;
            });
            var difference = Round(proposed - proposalLineAdjustment, configuration.RoundingPrecision,
                configuration.RoundingMode);
            var checksum = Hash(JsonSerializer.Serialize(new
            {
                Type = group.Key, Count = included.Length,
                Carrying = included.Sum(x => x.CarryingFunctionalAmount),
                Revalued = included.Sum(x => x.RevaluedFunctionalAmount),
                Proposed = proposed, Lines = proposalLineAdjustment, Difference = difference
            }));
            run.Reconciliations.Add(new CurrencyRevaluationReconciliation(Guid.NewGuid(), run.CompanyId,
                run.Id, group.Key, included.Length, included.Sum(x => x.CarryingFunctionalAmount),
                included.Sum(x => x.RevaluedFunctionalAmount), proposed, proposalLineAdjustment,
                difference, run.FunctionalCurrency, checksum));
        }

        var populationChecksum = Hash(JsonSerializer.Serialize(run.PopulationItems.OrderBy(x => x.PopulationKey)
            .Select(x => new { x.PopulationKey, x.SourceChecksum, x.DocumentBalance, x.CarryingFunctionalAmount })));
        var rateSetChecksum = Hash(JsonSerializer.Serialize(run.RateBindings.OrderBy(x => x.PopulationItemId)
            .Select(x => new { x.PopulationItemId, x.ExchangeRateConversionId, x.EffectiveRate,
                x.RateDate, x.RateSetIdentity, x.ObservationIdentity, x.EvidenceChecksum })));
        var proposalChecksum = Hash(JsonSerializer.Serialize(run.ProposalLines.OrderBy(x => x.Sequence)
            .Select(x => new { x.Sequence, x.FinanceAccountId, x.PopulationItemId, x.LineType,
                x.DebitAmount, x.CreditAmount, x.Currency })));
        var includedItems = run.PopulationItems.Where(x => x.Status == CurrencyRevaluationPopulationStatuses.Included).ToArray();
        run.RecordProposal(populationChecksum, rateSetChecksum, proposalChecksum, run.PopulationItems.Count,
            includedItems.Length,
            run.PopulationItems.Count(x => x.Status == CurrencyRevaluationPopulationStatuses.Excluded),
            run.PopulationItems.Count(x => x.Status == CurrencyRevaluationPopulationStatuses.NeedsReview),
            includedItems.Sum(x => Math.Abs(x.DocumentBalance)),
            includedItems.Sum(x => x.CarryingFunctionalAmount),
            includedItems.Sum(x => x.RevaluedFunctionalAmount),
            includedItems.Sum(x => x.AdjustmentAmount), now);
    }

    private async Task EnsureProposalCurrentAsync(CurrencyRevaluationRun run,
        CancellationToken cancellationToken)
    {
        var roles = await LoadRolesAsync(run.CompanyId, cancellationToken);
        var monetary = await LoadMonetaryAccountsAsync(run.CompanyId, roles, cancellationToken);
        var current = await BuildSourceGroupsAsync(run.CompanyId, run.AsOfDate, run.FunctionalCurrency,
            monetary, cancellationToken);
        var checksum = Hash(JsonSerializer.Serialize(current.OrderBy(x => x.PopulationKey)
            .Select(x => new { x.PopulationKey, x.SourceChecksum, x.DocumentBalance, x.CarryingFunctionalAmount })));
        if (!string.Equals(checksum, run.PopulationChecksum, StringComparison.Ordinal))
            throw Error(CurrencyRevaluationReasonCodes.ProposalStale,
                "Posted monetary balances changed after this proposal was prepared. Regenerate the run and obtain a new approval.", true, run.Version);
    }

    private static ProposedAccountingEntry ToProposed(CurrencyRevaluationRun run, Guid actorUserId,
        string idempotencyKey) => new(run.CompanyId, run.FiscalPeriodId, run.VoucherSeriesCode,
        run.AsOfDate, run.AsOfDate, LedgerPostingTypeValues.CurrencyRevaluation,
        $"Period-end currency revaluation for {run.FiscalPeriod.Name}.", SourceType,
        run.Id.ToString("N"), run.Version.ToString(CultureInfo.InvariantCulture), idempotencyKey,
        run.ProposalLines.OrderBy(x => x.Sequence).Select(x => new ProposedAccountingLine(
            x.FinanceAccountId, x.DebitAmount, x.CreditAmount, run.FunctionalCurrency,
            x.Description, null, null, new Dictionary<string, string>
            {
                ["currencyRevaluationRunId"] = run.Id.ToString("N"),
                ["populationItemId"] = x.PopulationItemId?.ToString("N") ?? string.Empty,
                ["lineType"] = x.LineType
            }, x.DebitAmount, x.CreditAmount, run.FunctionalCurrency, 1m, run.AsOfDate,
            null, "functional-adjustment", 0m)).ToArray(), actorUserId, run.ApprovalRequestId,
        true, new Dictionary<string, string>
        {
            ["populationChecksum"] = run.PopulationChecksum!,
            ["rateSetChecksum"] = run.RateSetChecksum!,
            ["proposalChecksum"] = run.ProposalChecksum!,
            ["asOfDate"] = run.AsOfDate.ToString("O", CultureInfo.InvariantCulture)
        }, "post_currency_revaluation", run.ProposalChecksum);

    private static void EnsureApproved(CurrencyRevaluationRun run)
    {
        if (run.ApprovalRequest is null) throw Error(CurrencyRevaluationReasonCodes.ApprovalRequired,
            "Submit this revaluation for finance approval before posting.");
        if (run.ApprovalRequest.Status == ApprovalRequestStatus.Pending)
            throw Error(CurrencyRevaluationReasonCodes.ApprovalPending,
                "This revaluation is still waiting for finance approval.");
        if (run.ApprovalRequest.Status != ApprovalRequestStatus.Approved)
            throw Error(CurrencyRevaluationReasonCodes.ApprovalRejected,
                "This revaluation was not approved.");
        var version = ContextText(run.ApprovalRequest, "sourceVersion");
        var checksum = ContextText(run.ApprovalRequest, "payloadHash");
        if (version != run.Version.ToString(CultureInfo.InvariantCulture) ||
            !string.Equals(checksum, run.ProposalChecksum, StringComparison.OrdinalIgnoreCase))
            throw Error(CurrencyRevaluationReasonCodes.ApprovalStale,
                "The approval does not match the current revaluation version and proposal.");
    }

    private static string? ContextText(ApprovalRequest approval, string key) =>
        approval.ThresholdContext.TryGetValue(key, out var node) ? node?.ToString() : null;

    private async Task EnsureActorAsync(Guid companyId, Guid actorUserId,
        CancellationToken cancellationToken)
    {
        Require(companyId, nameof(companyId)); Require(actorUserId, nameof(actorUserId));
        if (!await _db.CompanyMemberships.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                x.CompanyId == companyId && x.UserId == actorUserId &&
                x.Status == CompanyMembershipStatus.Active, cancellationToken))
            throw new UnauthorizedAccessException("An active company member is required for currency revaluation.");
    }

    private async Task AuditAsync(Guid companyId, Guid actorId, string action, Guid runId,
        string summary, string? correlationId, CurrencyRevaluationRun run,
        CancellationToken cancellationToken) => await _audit.WriteAsync(new AuditEventWriteRequest(companyId,
        AuditActorTypes.User, actorId, action, AuditTargetTypes.CurrencyRevaluationRun,
        runId.ToString("N"), AuditEventOutcomes.Succeeded, summary,
        ["currency_revaluation_population", "exchange_rate_authority", "accounting_configuration"],
        new Dictionary<string, string?>
        {
            ["fiscalPeriodId"] = run.FiscalPeriodId.ToString("D"),
            ["populationChecksum"] = run.PopulationChecksum,
            ["rateSetChecksum"] = run.RateSetChecksum,
            ["proposalChecksum"] = run.ProposalChecksum,
            ["approvalRequestId"] = run.ApprovalRequestId?.ToString("D"),
            ["ledgerEntryId"] = run.LedgerEntryId?.ToString("D"),
            ["reversalLedgerEntryId"] = run.ReversalLedgerEntryId?.ToString("D")
        }, correlationId, Now()), cancellationToken);

    private async Task AuditConfigurationAsync(Guid companyId, Guid actorId, Guid targetId,
        string summary, string? correlationId, CancellationToken cancellationToken) =>
        await _audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.User, actorId,
            AuditEventActions.AccountingCurrencyRevaluationConfigured,
            AuditTargetTypes.CurrencyRevaluationRun, targetId.ToString("N"),
            AuditEventOutcomes.Succeeded, summary, ["accounting_configuration"],
            CorrelationId: correlationId, OccurredUtc: Now()), cancellationToken);

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try { await _db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException)
        {
            throw Error(CurrencyRevaluationReasonCodes.VersionConflict,
                "The revaluation changed after it was loaded. Refresh and try again.", true);
        }
    }

    private void AddReview(CurrencyRevaluationRun run, CurrencyRevaluationReview review)
    {
        run.Reviews.Add(review);
        _db.Entry(review).State = EntityState.Added;
    }

    private static CurrencyRevaluationRunDto Map(CurrencyRevaluationRun run)
    {
        var approval = run.ApprovalRequest is null ? null : new CurrencyRevaluationApprovalDto(
            run.ApprovalRequest.Id, run.ApprovalRequest.Status.ToStorageValue(),
            run.ApprovalRequest.DecisionSummary, run.ApprovalRequest.CreatedUtc,
            run.ApprovalRequest.DecidedUtc);
        var status = run.Status == CurrencyRevaluationRunStatuses.AwaitingApproval && run.ApprovalRequest is not null
            ? run.ApprovalRequest.Status switch
            {
                ApprovalRequestStatus.Approved => "approved",
                ApprovalRequestStatus.Rejected => "rejected",
                ApprovalRequestStatus.Cancelled or ApprovalRequestStatus.Expired => "approval_expired",
                _ => run.Status
            }
            : run.Status;
        return new CurrencyRevaluationRunDto(run.Id, run.CompanyId, run.FiscalPeriodId,
            run.FiscalPeriod.Name, run.RunNumber, run.AsOfDate, run.FunctionalCurrency,
            run.VoucherSeriesCode, status, run.FailureReasonCode, run.FailureSummary,
            run.PopulationChecksum, run.RateSetChecksum, run.ProposalChecksum,
            run.PopulationCount, run.IncludedCount, run.ExcludedCount, run.ReviewCount,
            run.DocumentBalanceTotal, run.CarryingFunctionalTotal, run.RevaluedFunctionalTotal,
            run.ProposedAdjustmentTotal, run.ApprovalRequestId, run.LedgerEntryId,
            run.ReversalLedgerEntryId, run.SupersededByRunId, run.IsScheduled, run.Version,
            run.CreatedUtc, run.UpdatedUtc, run.SubmittedUtc, run.PostedUtc, run.ReversedUtc,
            run.PopulationItems.OrderBy(x => x.AccountCode).ThenBy(x => x.DocumentCurrency)
                .Select(x => new CurrencyRevaluationPopulationItemDto(x.Id, x.PopulationKey,
                    x.MonetaryClass, x.FinanceAccountId, x.AccountCode, x.AccountName,
                    x.NormalBalance, x.DocumentCurrency, x.FunctionalCurrency, x.DocumentBalance,
                    x.CarryingFunctionalAmount, x.RevaluedFunctionalAmount, x.AdjustmentAmount,
                    x.ExchangeRateConversionId, x.PeriodEndRate, x.RateDate, x.SourceChecksum,
                    x.Status, x.ReviewReason)).ToArray(),
            run.RateBindings.OrderBy(x => x.DocumentCurrency)
                .Select(x => new CurrencyRevaluationRateBindingDto(x.Id, x.PopulationItemId,
                    x.ExchangeRateConversionId, x.DocumentCurrency, x.FunctionalCurrency,
                    x.EffectiveRate, x.RateDate, x.RateSetIdentity, x.ObservationIdentity,
                    x.EvidenceChecksum)).ToArray(),
            run.ProposalLines.OrderBy(x => x.Sequence).Select(x => new CurrencyRevaluationProposalLineDto(
                x.Id, x.Sequence, x.FinanceAccountId, x.PopulationItemId, x.FinanceAccount.Code,
                x.FinanceAccount.Name, x.LineType, x.DebitAmount, x.CreditAmount, x.Currency,
                x.Description)).ToArray(),
            run.Reviews.OrderBy(x => x.OccurredUtc).Select(x => new CurrencyRevaluationReviewDto(
                x.Id, x.PopulationItemId, x.Action, x.Reason, x.ActorUserId, x.ApprovalRequestId,
                x.EvidenceChecksum, x.OccurredUtc)).ToArray(),
            run.Reconciliations.OrderBy(x => x.ReconciliationType)
                .Select(x => new CurrencyRevaluationReconciliationDto(x.Id, x.ReconciliationType,
                    x.PopulationCount, x.CarryingAmount, x.RevaluedAmount, x.ProposedAdjustment,
                    x.ProposalLineAdjustment, x.Difference, x.Currency, x.Checksum,
                    x.IsReconciled)).ToArray(), approval);
    }

    private static CurrencyRevaluationScheduleDto Map(CurrencyRevaluationSchedule value) =>
        new(value.Id, value.CompanyId, value.IsEnabled, value.DaysBeforePeriodEnd,
            value.AutomaticReversal, value.VoucherSeriesCode, value.Version, value.UpdatedUtc,
            value.LastEvaluatedUtc);

    private DateTime Now() => _time.GetUtcNow().UtcDateTime;
    private static decimal Round(decimal value, int precision, string mode) => decimal.Round(value,
        precision, mode == AccountingRoundingModeValues.AwayFromZero
            ? MidpointRounding.AwayFromZero : MidpointRounding.ToEven);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static void Require(Guid value, string name)
    { if (value == Guid.Empty) throw new ArgumentException($"{name} is required.", name); }
    private static void ValidateIdentity(Guid companyId, Guid actorId, string identity)
    { Require(companyId, nameof(companyId)); Require(actorId, nameof(actorId)); if (string.IsNullOrWhiteSpace(identity) || identity.Trim().Length > 200) throw Error(CurrencyRevaluationReasonCodes.IdempotencyConflict, "A stable bounded request identity is required."); }
    private static void EnsureVersion(CurrencyRevaluationRun run, long expected)
    { if (run.Version != expected) throw Conflict("The revaluation changed after it was loaded.", run.Version); }
    private static CurrencyRevaluationException NotFound() => Error(CurrencyRevaluationReasonCodes.NotFound,
        "The currency revaluation record was not found.");
    private static CurrencyRevaluationException Conflict(string message, long? version = null) =>
        Error(CurrencyRevaluationReasonCodes.VersionConflict, message, true, version);
    private static CurrencyRevaluationException Error(string code, string message, bool conflict = false,
        long? version = null) => new(code, message, conflict, version);

    private sealed record MonetaryAccount(Guid Id, string Code, string Name, string NormalBalance,
        string MonetaryClass, string Currency, decimal OpeningBalance);
    private sealed record SourceLine(Guid Id, Guid FinanceAccountId, string DocumentCurrency,
        decimal DocumentDebitAmount, decimal DocumentCreditAmount, decimal DebitAmount,
        decimal CreditAmount);
    private sealed record SourceGroup(string PopulationKey, Guid AccountId, string AccountCode,
        string AccountName, string NormalBalance, string MonetaryClass, string DocumentCurrency,
        decimal DocumentBalance, decimal CarryingFunctionalAmount, string SourceChecksum);
}

public sealed class CurrencyRevaluationTelemetry
{
    private readonly Counter<long> _operations;
    private readonly Histogram<int> _population;
    public CurrencyRevaluationTelemetry(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create("VirtualCompany.Finance.CurrencyRevaluation");
        _operations = meter.CreateCounter<long>("finance.currency_revaluation.operations");
        _population = meter.CreateHistogram<int>("finance.currency_revaluation.population_items");
    }
    public void Record(string operation, string status, int populationCount)
    {
        _operations.Add(1, new KeyValuePair<string, object?>("operation", operation),
            new KeyValuePair<string, object?>("status", status));
        _population.Record(populationCount, new KeyValuePair<string, object?>("operation", operation));
    }
}
