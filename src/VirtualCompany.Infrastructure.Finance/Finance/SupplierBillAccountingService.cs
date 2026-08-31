using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class SupplierBillAccountingService : ISupplierBillAccountingService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly SupplierBillAccountingPolicy _policy;
    private readonly IAccountingPostingService _postingService;
    private readonly IAuditEventWriter _audit;
    private readonly TimeProvider _timeProvider;
    private readonly IAccountingPolicyPackResolver _packResolver;
    private readonly IExchangeRateService? _exchangeRates;

    public SupplierBillAccountingService(
        VirtualCompanyDbContext dbContext,
        SupplierBillAccountingPolicy policy,
        IAccountingPostingService postingService,
        IAuditEventWriter audit,
        TimeProvider timeProvider,
        IAccountingPolicyPackResolver packResolver,
        IExchangeRateService? exchangeRates = null)
    {
        _dbContext = dbContext;
        _policy = policy;
        _postingService = postingService;
        _audit = audit;
        _timeProvider = timeProvider;
        _packResolver = packResolver;
        _exchangeRates = exchangeRates;
    }

    public Task<SupplierBillAccountingPreviewDto> PreviewAsync(
        PreviewSupplierBillAccountingQuery query, CancellationToken cancellationToken) =>
        _policy.PreviewAsync(query, cancellationToken);

    public async Task<SupplierBillAccountingReferenceDataDto> GetReferenceDataAsync(
        GetSupplierBillAccountingQuery query, CancellationToken cancellationToken)
    {
        var bill = await _dbContext.FinanceBills.IgnoreQueryFilters().AsNoTracking()
            .Include(x => x.Counterparty)
            .SingleOrDefaultAsync(x => x.CompanyId == query.CompanyId && x.Id == query.BillId, cancellationToken)
            ?? throw Error(SupplierBillAccountingReasonCodes.BillNotFound, "The supplier bill could not be found.");
        var configuration = await _dbContext.AccountingConfigurations.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == query.CompanyId, cancellationToken)
            ?? throw Error(SupplierBillAccountingReasonCodes.ConfigurationUnavailable, "Complete accounting setup before preparing this bill.");
        var pack = _packResolver.Resolve(configuration.PolicyPackKey, configuration.PolicyPackVersion);
        var billDate = DateOnly.FromDateTime(bill.ReceivedUtc);
        var periods = await _dbContext.FiscalPeriods.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && !x.IsClosed && !x.IsReportingLocked)
            .OrderBy(x => x.StartUtc)
            .Select(x => new SupplierBillAccountingPeriodOptionDto(x.Id, x.Name,
                DateOnly.FromDateTime(x.StartUtc), DateOnly.FromDateTime(x.EndUtc).AddDays(-1)))
            .ToListAsync(cancellationToken);
        var series = await _dbContext.VoucherSeries.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.IsActive).OrderBy(x => x.Code)
            .Select(x => new SupplierBillAccountingVoucherSeriesOptionDto(x.Code, x.DisplayName))
            .ToListAsync(cancellationToken);
        var taxRules = pack.Definition.TaxRules.Where(x => x.EffectiveFrom <= billDate)
            .Select(x => new SupplierBillAccountingTaxRuleOptionDto(x.Key, x.DisplayName, x.Rate,
                x.AmountMethod, ResolveTaxTreatment(x), x.EffectiveFrom)).ToArray();
        var costAccounts = await _dbContext.FinanceAccounts.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.IsPostingEnabled &&
                (x.AccountClass == FinanceAccountClassValues.Expense || x.AccountClass == FinanceAccountClassValues.Asset) &&
                x.ControlAccountRole == null && (x.EffectiveFrom == null || x.EffectiveFrom <= billDate) &&
                (x.EffectiveTo == null || x.EffectiveTo >= billDate))
            .OrderBy(x => x.Code)
            .Select(x => new SupplierBillAccountingAccountOptionDto(x.Id, x.Code, x.Name, x.AccountClass!))
            .ToListAsync(cancellationToken);

        var enrichment = await _dbContext.SupplierInvoiceEnrichmentActions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.BillId == query.BillId)
            .OrderByDescending(x => x.UpdatedUtc).FirstOrDefaultAsync(cancellationToken);
        var coding = enrichment?.SuggestionPayload?["coding"] as JsonObject;
        var suggestedCode = ReadString(coding, "ledgerAccount") ?? bill.Counterparty.DefaultAccountMapping;
        var suggested = costAccounts.FirstOrDefault(x => string.Equals(x.Code, suggestedCode?.Trim(), StringComparison.OrdinalIgnoreCase));
        var suggestedEvidence = suggested is null ? null : ReadString(coding, "basis") ?? "Supplier account mapping";
        var defaultRule = taxRules.FirstOrDefault(x => x.TaxTreatment == SupplierBillTaxTreatmentValues.Exempt) ?? taxRules.FirstOrDefault();
        var defaultPeriod = periods.FirstOrDefault(x => billDate >= x.StartDate && billDate <= x.EndDate);
        return new(bill.Id, bill.Currency, configuration.BaseCurrency, Math.Abs(bill.Amount), taxRules,
            periods, series, costAccounts, defaultRule?.Key, defaultPeriod?.Id,
            series.FirstOrDefault(x => x.Code == "G")?.Code ?? series.FirstOrDefault()?.Code,
            suggested?.Id, suggestedEvidence);
    }

    public async Task<SupplierBillAccountingSubmissionResult> SubmitAsync(
        SubmitSupplierBillAccountingCommand command, CancellationToken cancellationToken)
    {
        ValidateActor(command.CompanyId, command.BillId, command.ActorUserId);
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
            throw Error(SupplierBillAccountingReasonCodes.RequiredFieldMissing, "An idempotency key is required.");
        var plan = await _policy.BuildPlanAsync(
            new(command.CompanyId, command.BillId, command.Input, command.ActorUserId), cancellationToken);
        if (!plan.IsReady)
        {
            var first = plan.Issues.First(x => x.IsBlocking);
            if (first.ReasonCode == SupplierBillAccountingReasonCodes.TaxRuleUnsupported)
                await RecordBlockedTaxDecisionAsync(command.CompanyId, command.BillId, command.ActorUserId,
                    command.CorrelationId, plan, first, cancellationToken);
            throw Error(first.ReasonCode, first.Explanation);
        }

        var profile = await _dbContext.SupplierBillAccountingProfiles.IgnoreQueryFilters()
            .Include(x => x.Lines).Include(x => x.ApprovalRequest)
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.BillId == command.BillId, cancellationToken);
        if (profile?.Status == SupplierBillAccountingStatuses.Posted)
            throw Error(SupplierBillAccountingReasonCodes.AlreadyPosted, "This bill is already posted in Virtual Company.", true);
        if (profile is not null && command.ExpectedVersion.HasValue && profile.Version != command.ExpectedVersion.Value)
            throw Error(SupplierBillAccountingReasonCodes.VersionConflict, "The bill accounting details changed. Reload them before continuing.", true);
        var factsUnchanged = profile is not null && string.Equals(profile.PayloadHash, plan.PayloadHash, StringComparison.OrdinalIgnoreCase);
        if (factsUnchanged && profile!.ApprovalRequestId.HasValue)
            return new(await MapStateAsync(profile, cancellationToken), profile.ApprovalRequestId.Value, true);

        var retainedCurrency = await RetainCurrencyFactsAsync(plan, command.ActorUserId,
            command.CorrelationId, cancellationToken);

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (profile is null)
        {
            profile = new SupplierBillAccountingProfile(Guid.NewGuid(), command.CompanyId, command.BillId,
                plan.FiscalPeriodId, plan.VoucherSeriesCode, plan.Bill.Currency, plan.Configuration!.BaseCurrency,
                plan.ExchangeRate, plan.NetAmount, plan.RecoverableTaxAmount, plan.NonRecoverableTaxAmount,
                plan.GrossAmount, plan.CostBaseAmount, plan.RecoverableTaxBaseAmount, plan.GrossBaseAmount,
                plan.RoundingBaseAmount, plan.PayableAccountId, plan.TaxTreatment,
                plan.Configuration.PolicyPackKey, plan.Configuration.PolicyPackVersion,
                plan.PolicyPack!.DefinitionHash, plan.SourceDocumentHash, plan.ExistingProfile?.OriginalBillId,
                command.ActorUserId, now);
            _dbContext.SupplierBillAccountingProfiles.Add(profile);
        }
        else if (!factsUnchanged)
        {
            if (profile.ApprovalRequest is { Status: ApprovalRequestStatus.Pending })
                profile.ApprovalRequest.MarkCancelled("The supplier bill accounting facts changed and require a new approval.");
            _dbContext.SupplierBillAccountingLines.RemoveRange(profile.Lines);
            profile.Lines.Clear();
            profile.ReplaceFacts(plan.FiscalPeriodId, plan.VoucherSeriesCode, plan.Bill.Currency,
                plan.Configuration!.BaseCurrency, plan.ExchangeRate, plan.NetAmount, plan.RecoverableTaxAmount,
                plan.NonRecoverableTaxAmount, plan.GrossAmount, plan.CostBaseAmount,
                plan.RecoverableTaxBaseAmount, plan.GrossBaseAmount, plan.RoundingBaseAmount,
                plan.PayableAccountId, plan.TaxTreatment, plan.Configuration.PolicyPackKey,
                plan.Configuration.PolicyPackVersion, plan.PolicyPack!.DefinitionHash,
                plan.SourceDocumentHash, profile.OriginalBillId, command.ActorUserId, now);
        }

        if (!factsUnchanged)
        {
            profile.SetPayloadHash(plan.PayloadHash);
            foreach (var line in plan.Lines)
                profile.Lines.Add(new SupplierBillAccountingLine(Guid.NewGuid(), command.CompanyId, profile.Id,
                    line.Sequence, line.Description, line.CostAccountId, line.AccountClassification,
                    line.TaxRuleKey, line.TaxMethod, line.TaxTreatment, line.TaxRate, line.NetAmount,
                    line.TaxAmount, line.RecoverableTaxAmount, line.NonRecoverableTaxAmount, line.GrossAmount,
                    line.CostBaseAmount, line.RecoverableTaxBaseAmount, line.RecoverableTaxAccountId,
                    SerializeTaxFacts(line, plan, command.ActorUserId)));
        }
        profile.BindCurrencyFacts(retainedCurrency.ConversionId, plan.ExchangeRateDate,
            plan.ExchangeRatePurpose, plan.ExchangeRateIdentity, retainedCurrency.RoundingResidual,
            retainedCurrency.Provenance, command.ActorUserId, now);

        var approval = ApprovalRequest.CreateForTarget(Guid.NewGuid(), command.CompanyId,
            ApprovalTargetEntityType.SupplierBillAccounting, profile.Id, AuditActorTypes.User,
            command.ActorUserId, "supplier_bill_accounting_posting",
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["sourceVersion"] = JsonValue.Create(profile.Version.ToString(CultureInfo.InvariantCulture)),
                ["payloadHash"] = JsonValue.Create(profile.PayloadHash),
                ["sourceDocumentHash"] = JsonValue.Create(profile.SourceDocumentHash),
                ["billId"] = JsonValue.Create(command.BillId.ToString("N")),
                ["grossBaseAmount"] = JsonValue.Create(profile.GrossBaseAmount),
                ["exchangeRateIdentity"] = JsonValue.Create(profile.ExchangeRateIdentity),
                ["exchangeRateConversionId"] = JsonValue.Create(profile.ExchangeRateConversionId?.ToString("N")),
                ["idempotencyKey"] = JsonValue.Create(command.IdempotencyKey.Trim())
            }, null, null, [new ApprovalStepDefinition(1, ApprovalStepApproverType.Role, "finance_approver")]);
        _dbContext.ApprovalRequests.Add(approval);
        profile.BindApproval(approval.Id, command.ActorUserId, now);
        await _audit.WriteAsync(new AuditEventWriteRequest(command.CompanyId, AuditActorTypes.User,
            command.ActorUserId, AuditEventActions.AccountingSupplierBillApprovalRequested,
            AuditTargetTypes.SupplierBillAccounting, profile.Id.ToString("N"), AuditEventOutcomes.Succeeded,
            "Approval was requested for the exact supplier bill, source document, account, and tax facts.",
            ["finance_bill", "source_document", "accounting_configuration", "accounting_policy_pack"],
            new Dictionary<string, string?>
            {
                ["billId"] = command.BillId.ToString("N"),
                ["sourceVersion"] = profile.Version.ToString(CultureInfo.InvariantCulture),
                ["payloadHash"] = profile.PayloadHash,
                ["sourceDocumentHash"] = profile.SourceDocumentHash,
                ["exchangeRateIdentity"] = profile.ExchangeRateIdentity,
                ["exchangeRateConversionId"] = profile.ExchangeRateConversionId?.ToString("N")
            }, command.CorrelationId, now), cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return new(await MapStateAsync(profile, cancellationToken), approval.Id, false);
    }

    public async Task<SupplierBillAccountingPostingResult> PostAsync(
        PostSupplierBillAccountingCommand command, CancellationToken cancellationToken)
    {
        ValidateActor(command.CompanyId, command.BillId, command.ActorUserId);
        var profile = await LoadProfileAsync(command.CompanyId, command.BillId, cancellationToken);
        if (profile.Version != command.ExpectedVersion)
            throw Error(SupplierBillAccountingReasonCodes.VersionConflict, "The approved supplier bill accounting version is no longer current.", true);
        if (profile.ApprovalRequest is null)
            throw Error(SupplierBillAccountingReasonCodes.ApprovalRequired, "Submit the supplier bill accounting entry for approval first.");
        if (profile.ApprovalRequest.Status == ApprovalRequestStatus.Pending)
            throw Error(SupplierBillAccountingReasonCodes.ApprovalPending, "This supplier bill accounting entry is waiting for approval.");
        if (profile.ApprovalRequest.Status != ApprovalRequestStatus.Approved || !ApprovalMatches(profile))
            throw Error(SupplierBillAccountingReasonCodes.ApprovalStale, "The accounting approval is stale or no longer approved.", true);

        var currentPlan = await _policy.BuildPlanAsync(new(command.CompanyId, command.BillId,
            ToInput(profile), command.ActorUserId), cancellationToken);
        var staleIssue = currentPlan.Issues.FirstOrDefault(x => x.ReasonCode != SupplierBillAccountingReasonCodes.AlreadyPosted);
        if (staleIssue is not null || !string.Equals(currentPlan.PayloadHash, profile.PayloadHash, StringComparison.OrdinalIgnoreCase))
            throw Error(SupplierBillAccountingReasonCodes.ApprovalStale,
                staleIssue?.Explanation ?? "The supplier bill or its source document changed after approval.", true);

        var proposed = await BuildProposedAsync(profile, command.IdempotencyKey, command.ActorUserId, cancellationToken);
        var posted = await _postingService.PostAsync(new(proposed, command.CorrelationId), cancellationToken);
        var refreshed = await LoadProfileAsync(command.CompanyId, command.BillId, cancellationToken);
        return new(await MapStateAsync(refreshed, cancellationToken), posted.Journal, posted.IsIdempotentReplay);
    }

    public async Task<SupplierBillAccountingStateDto> GetAsync(
        GetSupplierBillAccountingQuery query, CancellationToken cancellationToken)
    {
        var bill = await _dbContext.FinanceBills.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == query.CompanyId && x.Id == query.BillId, cancellationToken)
            ?? throw Error(SupplierBillAccountingReasonCodes.BillNotFound, "The supplier bill could not be found.");
        var profile = await _dbContext.SupplierBillAccountingProfiles.IgnoreQueryFilters().AsNoTracking()
            .Include(x => x.Lines).Include(x => x.ApprovalRequest)
            .SingleOrDefaultAsync(x => x.CompanyId == query.CompanyId && x.BillId == query.BillId, cancellationToken);
        return profile is null ? EmptyState(bill) : await MapStateAsync(profile, cancellationToken);
    }

    public async Task<SupplierBillAccountingStateDto> CreateCreditNoteAsync(
        CreateNativeSupplierCreditNoteCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.CreditNoteNumber) || string.IsNullOrWhiteSpace(command.Reason))
            throw Error(SupplierBillAccountingReasonCodes.CreditNoteInvalid, "A credit-note number and correction reason are required.");
        var original = await _dbContext.FinanceBills.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.OriginalBillId, cancellationToken)
            ?? throw Error(SupplierBillAccountingReasonCodes.BillNotFound, "The original supplier bill could not be found.");
        var originalProfile = await _dbContext.SupplierBillAccountingProfiles.IgnoreQueryFilters().AsNoTracking()
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.BillId == original.Id && x.LedgerEntryId != null, cancellationToken)
            ?? throw Error(SupplierBillAccountingReasonCodes.CreditNoteInvalid, "Post the original supplier bill before creating its native credit note.");
        var existing = await _dbContext.FinanceBills.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.BillNumber == command.CreditNoteNumber.Trim(), cancellationToken);
        if (existing is not null)
        {
            var existingProfile = await _dbContext.SupplierBillAccountingProfiles.IgnoreQueryFilters().AsNoTracking()
                .Include(x => x.Lines).Include(x => x.ApprovalRequest)
                .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.BillId == existing.Id && x.OriginalBillId == original.Id, cancellationToken);
            if (existingProfile is not null) return await MapStateAsync(existingProfile, cancellationToken);
            throw Error(SupplierBillAccountingReasonCodes.DuplicateBill, "Another supplier bill already uses this credit-note number.", true);
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var credit = new FinanceBill(Guid.NewGuid(), command.CompanyId, original.CounterpartyId,
                command.CreditNoteNumber.Trim(), command.BillDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                command.DueDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), -originalProfile.GrossAmount,
                original.Currency, "approved", original.DocumentId, now, now,
                documentKind: FinanceDocumentKinds.SupplierCreditNote);
            _dbContext.FinanceBills.Add(credit);
            await _dbContext.SaveChangesAsync(cancellationToken);

            var originalInput = ToInput(originalProfile);
            var creditInput = new SupplierBillAccountingInput(command.Accounting.FiscalPeriodId,
                command.Accounting.VoucherSeriesCode, command.Accounting.ExchangeRate,
                originalInput.Lines);
            var plan = await _policy.BuildPlanAsync(new(command.CompanyId, credit.Id, creditInput, command.ActorUserId), cancellationToken);
            if (!plan.IsReady)
            {
                var first = plan.Issues.First(x => x.IsBlocking);
                throw Error(first.ReasonCode, first.Explanation);
            }
            var profile = new SupplierBillAccountingProfile(Guid.NewGuid(), command.CompanyId, credit.Id,
                plan.FiscalPeriodId, plan.VoucherSeriesCode, credit.Currency, plan.Configuration!.BaseCurrency,
                plan.ExchangeRate, plan.NetAmount, plan.RecoverableTaxAmount, plan.NonRecoverableTaxAmount,
                plan.GrossAmount, plan.CostBaseAmount, plan.RecoverableTaxBaseAmount, plan.GrossBaseAmount,
                plan.RoundingBaseAmount, plan.PayableAccountId, plan.TaxTreatment,
                plan.Configuration.PolicyPackKey, plan.Configuration.PolicyPackVersion,
                plan.PolicyPack!.DefinitionHash, plan.SourceDocumentHash, original.Id, command.ActorUserId, now);
            profile.SetPayloadHash(plan.PayloadHash);
            foreach (var line in plan.Lines)
                profile.Lines.Add(new SupplierBillAccountingLine(Guid.NewGuid(), command.CompanyId, profile.Id,
                    line.Sequence, line.Description, line.CostAccountId, line.AccountClassification,
                    line.TaxRuleKey, line.TaxMethod, line.TaxTreatment, line.TaxRate, line.NetAmount,
                    line.TaxAmount, line.RecoverableTaxAmount, line.NonRecoverableTaxAmount, line.GrossAmount,
                    line.CostBaseAmount, line.RecoverableTaxBaseAmount, line.RecoverableTaxAccountId,
                    SerializeTaxFacts(line, plan, command.ActorUserId)));
            _dbContext.SupplierBillAccountingProfiles.Add(profile);
            await _dbContext.SaveChangesAsync(cancellationToken);

            var linkedPlan = await _policy.BuildPlanAsync(new(command.CompanyId, credit.Id, creditInput, command.ActorUserId), cancellationToken);
            profile.SetPayloadHash(linkedPlan.PayloadHash);
            await _dbContext.SaveChangesAsync(cancellationToken);
            var submitted = await SubmitAsync(new(command.CompanyId, credit.Id, creditInput, profile.Version,
                command.IdempotencyKey, command.ActorUserId, command.CorrelationId), cancellationToken);
            await _audit.WriteAsync(new AuditEventWriteRequest(command.CompanyId, AuditActorTypes.User,
                command.ActorUserId, AuditEventActions.AccountingSupplierCreditNoteCreated,
                AuditTargetTypes.SupplierBillAccounting, profile.Id.ToString("N"), AuditEventOutcomes.Succeeded,
                "A supplier credit note was created and linked to the original posted supplier bill.",
                ["finance_bill", "accounting_journal"], new Dictionary<string, string?>
                {
                    ["originalBillId"] = original.Id.ToString("N"),
                    ["creditBillId"] = credit.Id.ToString("N"),
                    ["originalLedgerEntryId"] = originalProfile.LedgerEntryId?.ToString("N"),
                    ["reason"] = command.Reason.Trim()
                }, command.CorrelationId, now), cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return submitted.State;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<SupplierBillPayablesReconciliationDto> ReconcileAsync(
        GetSupplierBillPayablesReconciliationQuery query, CancellationToken cancellationToken)
    {
        var configuration = await _dbContext.AccountingConfigurations.IgnoreQueryFilters().AsNoTracking()
            .Include(x => x.AccountRoles).SingleOrDefaultAsync(x => x.CompanyId == query.CompanyId, cancellationToken)
            ?? throw Error(SupplierBillAccountingReasonCodes.ConfigurationUnavailable, "Accounting is not configured.");
        var payableId = configuration.AccountRoles.SingleOrDefault(x => x.RoleKey == "accounts_payable")?.FinanceAccountId
            ?? throw Error(SupplierBillAccountingReasonCodes.AccountRoleMissing, "The accounts payable role is not configured.");
        var profiles = await _dbContext.SupplierBillAccountingProfiles.IgnoreQueryFilters().AsNoTracking()
            .Include(x => x.Bill).Where(x => x.CompanyId == query.CompanyId && x.Status == SupplierBillAccountingStatuses.Posted)
            .Where(x => !query.ThroughDate.HasValue || x.Bill.ReceivedUtc < query.ThroughDate.Value.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc))
            .ToListAsync(cancellationToken);
        var postedDocuments = profiles.Sum(x => x.Bill.DocumentKind == FinanceDocumentKinds.SupplierCreditNote
            ? -x.GrossBaseAmount : x.GrossBaseAmount);
        var ledger = await _dbContext.LedgerEntryLines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.FinanceAccountId == payableId &&
                x.LedgerEntry.SourceType == "supplier_bill" && x.LedgerEntry.Status == LedgerEntryStatuses.Posted)
            .Where(x => !query.ThroughDate.HasValue || x.LedgerEntry.PostingDate <= query.ThroughDate.Value)
            .SumAsync(x => x.CreditAmount - x.DebitAmount, cancellationToken);
        var profileByBill = profiles.ToDictionary(x => x.BillId);
        var allocations = await _dbContext.PaymentAllocations.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.BillId != null &&
                x.SettlementStatus != PaymentAllocationSettlementStatuses.Reversed &&
                profileByBill.Keys.Contains(x.BillId.Value))
            .ToListAsync(cancellationToken);
        var allocatedBase = allocations.Sum(x => x.AllocatedFunctionalAmount ??
            decimal.Round((x.AllocatedAmount + x.WriteOffAmount) * profileByBill[x.BillId!.Value].ExchangeRate,
                configuration.RoundingPrecision, MidpointRounding.ToEven));
        var difference = decimal.Round(postedDocuments - ledger, configuration.RoundingPrecision, MidpointRounding.ToEven);
        var breakdown = profiles.GroupBy(x => x.DocumentCurrency, StringComparer.OrdinalIgnoreCase).Select(group =>
        {
            var ids = group.Select(x => x.BillId).ToHashSet();
            var documentPosted = group.Sum(x => x.Bill.DocumentKind == FinanceDocumentKinds.SupplierCreditNote ? -x.GrossAmount : x.GrossAmount);
            var functionalPosted = group.Sum(x => x.Bill.DocumentKind == FinanceDocumentKinds.SupplierCreditNote ? -x.GrossBaseAmount : x.GrossBaseAmount);
            var documentAllocated = allocations.Where(x => x.BillId.HasValue && ids.Contains(x.BillId.Value))
                .Sum(x => x.AllocatedAmount + x.WriteOffAmount);
            var functionalAllocated = allocations.Where(x => x.BillId.HasValue && ids.Contains(x.BillId.Value))
                .Sum(x => x.AllocatedFunctionalAmount ??
                    decimal.Round((x.AllocatedAmount + x.WriteOffAmount) * profileByBill[x.BillId!.Value].ExchangeRate,
                        configuration.RoundingPrecision, MidpointRounding.ToEven));
            return new DocumentCurrencyOpenItemControlDto(group.Key, documentPosted, documentAllocated,
                documentPosted - documentAllocated, functionalPosted, functionalAllocated,
                functionalPosted - functionalAllocated, configuration.BaseCurrency);
        }).OrderBy(x => x.DocumentCurrency, StringComparer.Ordinal).ToArray();
        return new(query.CompanyId, configuration.BaseCurrency, postedDocuments, ledger, allocatedBase,
            postedDocuments - allocatedBase, difference, difference == 0m, _timeProvider.GetUtcNow().UtcDateTime,
            breakdown);
    }

    private async Task<RetainedDocumentCurrencyFacts> RetainCurrencyFactsAsync(
        SupplierBillAccountingPlan plan, Guid actorUserId, string? correlationId,
        CancellationToken cancellationToken)
    {
        if (string.Equals(plan.Bill.Currency, plan.Configuration!.BaseCurrency, StringComparison.OrdinalIgnoreCase))
            return new(null, 0m, DocumentCurrencyFacts.BaseCurrencyIdentity);

        if (_exchangeRates is null)
            throw Error(SupplierBillAccountingReasonCodes.CurrencyConversionMissing,
                "The authoritative exchange-rate service is unavailable.");
        var conversion = await _exchangeRates.ConvertAsync(new(plan.Bill.CompanyId, actorUserId,
            plan.GrossAmount, plan.Bill.Currency, plan.Configuration.BaseCurrency,
            plan.ExchangeRateDate, plan.ExchangeRatePurpose,
            $"supplier-bill:{plan.Bill.Id:N}:{plan.PayloadHash}:gross", correlationId), cancellationToken);
        if (conversion.EffectiveRate != plan.ExchangeRate || conversion.RoundedAmount != plan.GrossBaseAmount ||
            !string.Equals(DocumentCurrencyFacts.RateIdentity(conversion), plan.ExchangeRateIdentity,
                StringComparison.OrdinalIgnoreCase))
            throw Error(SupplierBillAccountingReasonCodes.VersionConflict,
                "The selected exchange rate changed while the supplier bill was being prepared. Refresh the preview before submitting.", true);
        return new(conversion.Id, conversion.RoundingResidual, DocumentCurrencyFacts.AuthoritativeRate);
    }

    private async Task<ProposedAccountingEntry> BuildProposedAsync(
        SupplierBillAccountingProfile profile, string idempotencyKey, Guid actorUserId, CancellationToken cancellationToken)
    {
        if (!profile.HasAuthoritativeCurrencyFacts)
            throw Error(SupplierBillAccountingReasonCodes.CurrencyConversionMissing,
                "This supplier bill has no authoritative currency conversion evidence. Prepare and approve a new accounting version before posting.", true);
        var bill = await _dbContext.FinanceBills.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x => x.CompanyId == profile.CompanyId && x.Id == profile.BillId, cancellationToken);
        var isCredit = bill.DocumentKind == FinanceDocumentKinds.SupplierCreditNote;
        var lines = new List<ProposedAccountingLine>
        {
            new(profile.PayableAccountId, isCredit ? profile.GrossBaseAmount : 0m,
                isCredit ? 0m : profile.GrossBaseAmount, profile.BaseCurrency,
                $"{bill.BillNumber} · accounts payable",
                DocumentDebitAmount: isCredit ? profile.GrossAmount : 0m,
                DocumentCreditAmount: isCredit ? 0m : profile.GrossAmount,
                DocumentCurrency: profile.DocumentCurrency, ExchangeRate: profile.ExchangeRate,
                ExchangeRateDate: profile.ExchangeRateDate, ExchangeRateConversionId: profile.ExchangeRateConversionId,
                ExchangeRateIdentity: profile.ExchangeRateIdentity,
                ConversionRoundingResidual: profile.ConversionRoundingResidual)
        };
        var ordered = profile.Lines.OrderBy(x => x.Sequence).ToArray();
        foreach (var line in ordered)
        {
            var cost = line.CostBaseAmount + (line.Sequence == ordered[^1].Sequence ? profile.RoundingBaseAmount : 0m);
            var retainedTaxFacts = ParseTaxFacts(line.TaxFactsJson);
            retainedTaxFacts["documentCurrency"] = profile.DocumentCurrency;
            lines.Add(new(line.CostAccountId, isCredit ? 0m : cost, isCredit ? cost : 0m,
                profile.BaseCurrency, line.Description, TaxFacts: retainedTaxFacts,
                DocumentDebitAmount: isCredit ? 0m : line.NetAmount + line.NonRecoverableTaxAmount,
                DocumentCreditAmount: isCredit ? line.NetAmount + line.NonRecoverableTaxAmount : 0m,
                DocumentCurrency: profile.DocumentCurrency, ExchangeRate: profile.ExchangeRate,
                ExchangeRateDate: profile.ExchangeRateDate, ExchangeRateConversionId: profile.ExchangeRateConversionId,
                ExchangeRateIdentity: profile.ExchangeRateIdentity));
            if (line.RecoverableTaxBaseAmount > 0m && line.RecoverableTaxAccountId.HasValue)
                lines.Add(new(line.RecoverableTaxAccountId.Value,
                    isCredit ? 0m : line.RecoverableTaxBaseAmount,
                    isCredit ? line.RecoverableTaxBaseAmount : 0m,
                    profile.BaseCurrency, $"Tax · {line.Description}", TaxFacts: retainedTaxFacts,
                    DocumentDebitAmount: isCredit ? 0m : line.RecoverableTaxAmount,
                    DocumentCreditAmount: isCredit ? line.RecoverableTaxAmount : 0m,
                    DocumentCurrency: profile.DocumentCurrency, ExchangeRate: profile.ExchangeRate,
                    ExchangeRateDate: profile.ExchangeRateDate, ExchangeRateConversionId: profile.ExchangeRateConversionId,
                    ExchangeRateIdentity: profile.ExchangeRateIdentity));
        }

        var evidence = new List<ProposedAccountingEvidence>();
        var documentId = bill.DocumentId ?? await _dbContext.SupplierInvoiceSourceDocumentAttachments.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == profile.CompanyId && x.BillId == profile.BillId && x.DocumentId != null)
            .OrderByDescending(x => x.AttachedUtc).Select(x => x.DocumentId).FirstOrDefaultAsync(cancellationToken);
        if (documentId.HasValue && !string.IsNullOrWhiteSpace(profile.SourceDocumentHash))
        {
            var title = await _dbContext.CompanyKnowledgeDocuments.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == profile.CompanyId && x.Id == documentId.Value)
                .Select(x => x.Title).SingleOrDefaultAsync(cancellationToken) ?? $"Supplier bill {bill.BillNumber}";
            evidence.Add(new(documentId.Value, profile.SourceDocumentHash, title));
        }
        Guid? originalLedgerEntryId = null;
        if (profile.OriginalBillId.HasValue)
            originalLedgerEntryId = await _dbContext.SupplierBillAccountingProfiles.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == profile.CompanyId && x.BillId == profile.OriginalBillId.Value)
                .Select(x => x.LedgerEntryId).SingleAsync(cancellationToken);

        var policyFacts = new Dictionary<string, string>
        {
            ["documentKind"] = bill.DocumentKind,
            ["documentCurrency"] = profile.DocumentCurrency,
            ["exchangeRate"] = profile.ExchangeRate.ToString(CultureInfo.InvariantCulture),
            ["netAmount"] = profile.NetAmount.ToString(CultureInfo.InvariantCulture),
            ["recoverableTaxAmount"] = profile.RecoverableTaxAmount.ToString(CultureInfo.InvariantCulture),
            ["nonRecoverableTaxAmount"] = profile.NonRecoverableTaxAmount.ToString(CultureInfo.InvariantCulture),
            ["grossAmount"] = profile.GrossAmount.ToString(CultureInfo.InvariantCulture),
            ["currencyProvenance"] = profile.CurrencyProvenance,
            ["policyDefinitionHash"] = profile.PolicyDefinitionHash
        };
        AddOptionalFact(policyFacts, "exchangeRateDate",
            profile.ExchangeRateDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        AddOptionalFact(policyFacts, "exchangeRateIdentity", profile.ExchangeRateIdentity);
        AddOptionalFact(policyFacts, "exchangeRateConversionId", profile.ExchangeRateConversionId?.ToString("N"));
        AddOptionalFact(policyFacts, "conversionRoundingResidual",
            profile.ConversionRoundingResidual?.ToString(CultureInfo.InvariantCulture));
        AddOptionalFact(policyFacts, "sourceDocumentHash", profile.SourceDocumentHash);

        return new(profile.CompanyId, profile.FiscalPeriodId, profile.VoucherSeriesCode,
            DateOnly.FromDateTime(bill.ReceivedUtc), DateOnly.FromDateTime(bill.ReceivedUtc),
            LedgerPostingTypeValues.SourceDocument, $"{(isCredit ? "Supplier credit note" : "Supplier bill")} {bill.BillNumber}",
            "supplier_bill", bill.Id.ToString("N"), profile.Version.ToString(CultureInfo.InvariantCulture),
            idempotencyKey.Trim(), lines, actorUserId, profile.ApprovalRequestId, true,
            policyFacts, isCredit ? "credit" : "post", profile.PayloadHash, evidence, originalLedgerEntryId,
            isCredit ? $"Credit note {bill.BillNumber} corrects supplier bill {profile.OriginalBillId:N}." : null);
    }

    private static void AddOptionalFact(IDictionary<string, string> facts, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) facts[key] = value;
    }

    private async Task<SupplierBillAccountingProfile> LoadProfileAsync(
        Guid companyId, Guid billId, CancellationToken cancellationToken) =>
        await _dbContext.SupplierBillAccountingProfiles.IgnoreQueryFilters()
            .Include(x => x.Lines).Include(x => x.ApprovalRequest)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.BillId == billId, cancellationToken)
        ?? throw Error(SupplierBillAccountingReasonCodes.RequiredFieldMissing, "Prepare the supplier bill accounting entry first.");

    private async Task<SupplierBillAccountingStateDto> MapStateAsync(
        SupplierBillAccountingProfile profile, CancellationToken cancellationToken)
    {
        var bill = await _dbContext.FinanceBills.IgnoreQueryFilters().AsNoTracking()
            .Include(x => x.Counterparty).SingleAsync(x => x.CompanyId == profile.CompanyId && x.Id == profile.BillId, cancellationToken);
        var accountIds = profile.Lines.Select(x => x.CostAccountId)
            .Concat(profile.Lines.Where(x => x.RecoverableTaxAccountId.HasValue).Select(x => x.RecoverableTaxAccountId!.Value))
            .Append(profile.PayableAccountId).Distinct().ToArray();
        var accounts = await _dbContext.FinanceAccounts.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == profile.CompanyId && accountIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var journalLines = BuildStateJournalLines(profile, bill, accounts);
        var duplicates = await FindCurrentDuplicatesAsync(bill, cancellationToken);
        string? voucher = null;
        if (profile.LedgerEntryId.HasValue)
            voucher = await _dbContext.LedgerEntries.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == profile.CompanyId && x.Id == profile.LedgerEntryId.Value)
                .Select(x => x.EntryNumber).SingleOrDefaultAsync(cancellationToken);
        var approval = profile.ApprovalRequest is null ? null : new SupplierBillAccountingApprovalDto(
            profile.ApprovalRequest.Id, profile.ApprovalRequest.Status.ToStorageValue(), profile.Version,
            profile.PayloadHash, profile.ApprovalRequest.CreatedUtc, profile.ApprovalRequest.DecidedUtc);
        var status = profile.Status == SupplierBillAccountingStatuses.AwaitingApproval && profile.ApprovalRequest?.Status == ApprovalRequestStatus.Approved
            ? SupplierBillAccountingStatuses.ReadyToPost : profile.Status;
        return new(profile.BillId, profile.Id, status, StatusLabel(status), true,
            status is SupplierBillAccountingStatuses.NotReady or SupplierBillAccountingStatuses.Blocked,
            status == SupplierBillAccountingStatuses.ReadyToPost,
            status == SupplierBillAccountingStatuses.Posted,
            profile.Version, profile.NetAmount, profile.RecoverableTaxAmount, profile.NonRecoverableTaxAmount,
            profile.GrossAmount, profile.DocumentCurrency, profile.ExchangeRate, profile.GrossBaseAmount,
            profile.BaseCurrency, profile.TaxTreatment, profile.PolicyPackKey, profile.PolicyPackVersion,
            profile.SourceDocumentHash, profile.LedgerEntryId, voucher, profile.OriginalBillId,
            profile.BlockingReasonCode, profile.BlockingReason, approval, journalLines, duplicates,
            profile.BlockingReason is null ? [] : [new(profile.BlockingReasonCode ?? "blocked", profile.BlockingReason)],
            profile.ExchangeRateDate, profile.ExchangeRateConversionId, profile.ExchangeRateIdentity,
            profile.ConversionRoundingResidual, profile.CurrencyProvenance);
    }

    private static IReadOnlyList<SupplierBillAccountingJournalLineDto> BuildStateJournalLines(
        SupplierBillAccountingProfile profile, FinanceBill bill, IReadOnlyDictionary<Guid, FinanceAccount> accounts)
    {
        var isCredit = bill.DocumentKind == FinanceDocumentKinds.SupplierCreditNote;
        var result = new List<SupplierBillAccountingJournalLineDto>();
        if (!accounts.TryGetValue(profile.PayableAccountId, out var payable)) return result;
        result.Add(new(payable.Id, "accounts_payable", payable.Code, payable.Name,
            isCredit ? profile.GrossBaseAmount : 0m, isCredit ? 0m : profile.GrossBaseAmount,
            profile.BaseCurrency, "Accounts payable", DocumentDebitAmount: isCredit ? profile.GrossAmount : 0m,
            DocumentCreditAmount: isCredit ? 0m : profile.GrossAmount, DocumentCurrency: profile.DocumentCurrency));
        var ordered = profile.Lines.OrderBy(x => x.Sequence).ToArray();
        foreach (var line in ordered)
        {
            if (!accounts.TryGetValue(line.CostAccountId, out var costAccount)) continue;
            var taxFacts = ParseTaxFacts(line.TaxFactsJson);
            var cost = line.CostBaseAmount + (line.Sequence == ordered[^1].Sequence ? profile.RoundingBaseAmount : 0m);
            result.Add(new(costAccount.Id, line.AccountClassification, costAccount.Code, costAccount.Name,
                isCredit ? 0m : cost, isCredit ? cost : 0m, profile.BaseCurrency,
                line.Description, line.TaxRuleKey, line.TaxTreatment,
                taxFacts.GetValueOrDefault("taxRuleVersion"), ParseList(taxFacts.GetValueOrDefault("vatBoxes")),
                taxFacts.GetValueOrDefault("evidenceClassification"),
                isCredit ? 0m : line.NetAmount + line.NonRecoverableTaxAmount,
                isCredit ? line.NetAmount + line.NonRecoverableTaxAmount : 0m, profile.DocumentCurrency));
            if (line.RecoverableTaxBaseAmount > 0m && line.RecoverableTaxAccountId.HasValue &&
                accounts.TryGetValue(line.RecoverableTaxAccountId.Value, out var taxAccount))
                result.Add(new(taxAccount.Id, taxFacts.GetValueOrDefault("recoverableAccountRole") ?? "tax_recoverable", taxAccount.Code, taxAccount.Name,
                    isCredit ? 0m : line.RecoverableTaxBaseAmount,
                    isCredit ? line.RecoverableTaxBaseAmount : 0m, profile.BaseCurrency,
                    "Recoverable tax", line.TaxRuleKey, line.TaxTreatment,
                    taxFacts.GetValueOrDefault("taxRuleVersion"), ParseList(taxFacts.GetValueOrDefault("vatBoxes")),
                    taxFacts.GetValueOrDefault("evidenceClassification"), isCredit ? 0m : line.RecoverableTaxAmount,
                    isCredit ? line.RecoverableTaxAmount : 0m, profile.DocumentCurrency));
        }
        return result;
    }

    private async Task<IReadOnlyList<SupplierBillDuplicateEvidenceDto>> FindCurrentDuplicatesAsync(
        FinanceBill bill, CancellationToken cancellationToken)
    {
        var start = bill.ReceivedUtc.Date;
        var rows = await _dbContext.FinanceBills.IgnoreQueryFilters().AsNoTracking().Include(x => x.Counterparty)
            .Where(x => x.CompanyId == bill.CompanyId && x.Id != bill.Id && x.CounterpartyId == bill.CounterpartyId &&
                x.BillNumber == bill.BillNumber && x.Amount == bill.Amount && x.Currency == bill.Currency &&
                x.ReceivedUtc >= start && x.ReceivedUtc < start.AddDays(1)).ToListAsync(cancellationToken);
        var evidence = rows.Select(x => new SupplierBillDuplicateEvidenceDto(x.Id, x.BillNumber, x.Counterparty.Name,
            DateOnly.FromDateTime(x.ReceivedUtc), x.Amount, x.Currency,
            ["Supplier", "Bill number", "Amount", "Currency", "Bill date"])).ToList();
        var intakeChecks = await _dbContext.BillDuplicateChecks.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == bill.CompanyId && x.IsDuplicate && x.InvoiceNumber == bill.BillNumber &&
                x.TotalAmount == bill.Amount && x.Currency == bill.Currency)
            .OrderByDescending(x => x.CheckedUtc)
            .Take(10)
            .ToListAsync(cancellationToken);
        evidence.AddRange(intakeChecks.Select(check => new SupplierBillDuplicateEvidenceDto(
            check.GetMatchedBillIds().FirstOrDefault() is var matchedId && matchedId != Guid.Empty ? matchedId : check.Id,
            check.InvoiceNumber ?? bill.BillNumber,
            check.SupplierName ?? bill.Counterparty?.Name ?? "Supplier",
            DateOnly.FromDateTime(bill.ReceivedUtc),
            check.TotalAmount ?? bill.Amount,
            check.Currency ?? bill.Currency,
            ["Persisted intake duplicate check", check.CriteriaSummary])));
        return evidence.GroupBy(x => new { x.MatchedBillId, x.BillNumber }).Select(x => x.First()).ToArray();
    }

    private static SupplierBillAccountingInput ToInput(SupplierBillAccountingProfile profile) => new(
        profile.FiscalPeriodId, profile.VoucherSeriesCode, CanonicalInputDecimal(profile.ExchangeRate),
        profile.Lines.OrderBy(x => x.Sequence).Select(x => new SupplierBillAccountingLineInput(
            x.Description,
            CanonicalInputDecimal(x.TaxMethod == CustomerInvoiceTaxMethodValues.Inclusive
                ? x.GrossAmount
                : x.NetAmount),
            x.CostAccountId,
            x.TaxRuleKey,
            ReadTaxFact(x.TaxFactsJson, "lineClassification"),
            ReadTaxFact(x.TaxFactsJson, "counterpartyJurisdiction"),
            ReadTaxFact(x.TaxFactsJson, "counterpartyVatStatus"),
            DeserializeEvidence(x.TaxFactsJson))).ToArray());

    // SQL Server preserves the declared decimal column scale on materialization (for example,
    // 1 becomes 1.00000000). The approval hash is JSON-based, so rebuild the submitted input
    // with a canonical scale before revalidating it; otherwise numerically identical approved
    // facts can produce a different JSON hash after a database round trip.
    private static decimal CanonicalInputDecimal(decimal value) => decimal.Parse(
        value.ToString("0.############################", CultureInfo.InvariantCulture),
        NumberStyles.Number,
        CultureInfo.InvariantCulture);

    private static SupplierBillAccountingStateDto EmptyState(FinanceBill bill) => new(
        bill.Id, null, SupplierBillAccountingStatuses.NotReady, "Not ready", true, IsApproved(bill.Status),
        false, false, null, null, null, null, null, bill.Currency, null, null, null, null, null, null,
        null, null, null, null, null, null, null, [], [], []);

    private static bool ApprovalMatches(SupplierBillAccountingProfile profile)
    {
        var approval = profile.ApprovalRequest!;
        var version = approval.ThresholdContext.TryGetValue("sourceVersion", out var versionNode) ? versionNode?.ToString() : null;
        var hash = approval.ThresholdContext.TryGetValue("payloadHash", out var hashNode) ? hashNode?.ToString() : null;
        var documentHash = approval.ThresholdContext.TryGetValue("sourceDocumentHash", out var documentNode) ? documentNode?.ToString() : null;
        var rateIdentity = approval.ThresholdContext.TryGetValue("exchangeRateIdentity", out var rateNode) ? rateNode?.ToString() : null;
        var conversionId = approval.ThresholdContext.TryGetValue("exchangeRateConversionId", out var conversionNode) ? conversionNode?.ToString() : null;
        return approval.TargetEntityType == ApprovalTargetEntityType.SupplierBillAccounting.ToStorageValue() &&
            approval.TargetEntityId == profile.Id && version == profile.Version.ToString(CultureInfo.InvariantCulture) &&
            string.Equals(hash, profile.PayloadHash, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(documentHash, profile.SourceDocumentHash, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(rateIdentity, profile.ExchangeRateIdentity, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(conversionId, profile.ExchangeRateConversionId?.ToString("N"), StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveTaxTreatment(AccountingTaxRuleDefinition rule) =>
        rule.Rate.GetValueOrDefault() == 0m || rule.AmountMethod == CustomerInvoiceTaxMethodValues.Exempt
            ? SupplierBillTaxTreatmentValues.Exempt
            : string.IsNullOrWhiteSpace(rule.RecoverableAccountRoleKey)
                ? SupplierBillTaxTreatmentValues.NonRecoverable
                : SupplierBillTaxTreatmentValues.Recoverable;
    private static string? ReadString(JsonObject? node, string key) =>
        node is not null && node.TryGetPropertyValue(key, out var value) ? value?.ToString() : null;
    private static bool IsApproved(string status) => status.Trim().ToLowerInvariant() is "approved" or "paid" or "booked";
    private static string StatusLabel(string status) => status switch
    {
        SupplierBillAccountingStatuses.NotReady => "Not ready",
        SupplierBillAccountingStatuses.AwaitingApproval => "Waiting for approval",
        SupplierBillAccountingStatuses.ReadyToPost => "Ready to post",
        SupplierBillAccountingStatuses.Posted => "Posted in Virtual Company",
        SupplierBillAccountingStatuses.Reversed => "Reversed",
        SupplierBillAccountingStatuses.Blocked => "Needs review",
        _ => "Unknown"
    };
    private static void ValidateActor(Guid companyId, Guid billId, Guid actorId)
    {
        if (companyId == Guid.Empty || billId == Guid.Empty || actorId == Guid.Empty)
            throw Error(SupplierBillAccountingReasonCodes.BillNotFound, "The supplier bill could not be found.");
    }
    private static string SerializeTaxFacts(SupplierBillAccountingLinePlan line, SupplierBillAccountingPlan plan, Guid actorUserId) =>
        JsonSerializer.Serialize(new Dictionary<string, string>
    {
        ["schemaVersion"] = "2.0",
        ["specificationKey"] = ResolveSpecificationKey(plan.PolicyPack),
        ["policyPackKey"] = plan.Configuration?.PolicyPackKey ?? "unknown",
        ["policyPackVersion"] = plan.Configuration?.PolicyPackVersion ?? "unknown",
        ["policyDefinitionHash"] = plan.PolicyPack?.DefinitionHash ?? "unknown",
        ["taxRuleKey"] = line.TaxRuleKey, ["taxRuleVersion"] = line.TaxRuleVersion,
        ["accountingDate"] = DateOnly.FromDateTime(plan.Bill.ReceivedUtc).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ["direction"] = AccountingTaxDirectionValues.Purchase,
        ["documentType"] = plan.Bill.DocumentKind == FinanceDocumentKinds.SupplierCreditNote ? "supplier_credit_note" : "supplier_invoice",
        ["lineClassification"] = line.LineClassification,
        ["counterpartyJurisdiction"] = line.CounterpartyJurisdiction,
        ["counterpartyVatStatus"] = line.CounterpartyVatStatus,
        ["inputAmount"] = line.InputAmount.ToString(CultureInfo.InvariantCulture),
        ["documentCurrency"] = plan.Bill.Currency,
        ["taxMethod"] = line.TaxMethod, ["taxTreatment"] = line.TaxTreatment,
        ["taxRate"] = line.TaxRate.ToString(CultureInfo.InvariantCulture),
        ["taxableBasis"] = line.NetAmount.ToString(CultureInfo.InvariantCulture),
        ["taxAmount"] = line.TaxAmount.ToString(CultureInfo.InvariantCulture),
        ["grossAmount"] = line.GrossAmount.ToString(CultureInfo.InvariantCulture),
        ["recoverableTaxAmount"] = line.RecoverableTaxAmount.ToString(CultureInfo.InvariantCulture),
        ["liabilityAccountRole"] = line.LiabilityAccountRoleKey ?? "none",
        ["recoverableAccountRole"] = line.RecoverableAccountRoleKey ?? "none",
        ["recoverability"] = line.Recoverability,
        ["vatBoxes"] = line.VatBoxMappings is { Count: > 0 } ? string.Join(",", line.VatBoxMappings) : "none",
        ["evidenceClassification"] = line.EvidenceClassification,
        ["evidence"] = JsonSerializer.Serialize(line.SuppliedEvidence ?? []),
        ["evidenceAttestedByUserId"] = actorUserId.ToString("N"),
        ["roundingPrecision"] = (plan.Configuration?.RoundingPrecision ?? 2).ToString(CultureInfo.InvariantCulture),
        ["roundingMode"] = plan.Configuration?.RoundingMode ?? AccountingRoundingModeValues.MidpointToEven
    });
    private static string ResolveSpecificationKey(IAccountingPolicyPack? pack) =>
        pack?.Definition.PolicyMetadata?.GetValueOrDefault("tax_specification") ?? "none";
    private static string? ReadTaxFact(string json, string key) =>
        ParseTaxFacts(json).GetValueOrDefault(key) is { Length: > 0 } value && value != "unknown" ? value : null;
    private static IReadOnlyList<AccountingTaxEvidenceInput> DeserializeEvidence(string json)
    {
        var raw = ParseTaxFacts(json).GetValueOrDefault("evidence");
        return string.IsNullOrWhiteSpace(raw)
            ? []
            : JsonSerializer.Deserialize<IReadOnlyList<AccountingTaxEvidenceInput>>(raw) ?? [];
    }
    private async Task RecordBlockedTaxDecisionAsync(Guid companyId, Guid billId, Guid actorUserId,
        string? correlationId, SupplierBillAccountingPlan plan, SupplierBillAccountingIssueDto issue,
        CancellationToken cancellationToken)
    {
        await _audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.User, actorUserId,
            AuditEventActions.AccountingTaxDecisionBlocked, AuditTargetTypes.SupplierBillAccounting,
            billId.ToString("N"), AuditEventOutcomes.Blocked,
            "Supplier bill accounting was blocked by the tax policy.",
            ["finance_bill", "accounting_policy_pack"], new Dictionary<string, string?>
            {
                ["documentId"] = billId.ToString("N"), ["direction"] = AccountingTaxDirectionValues.Purchase,
                ["reasonCode"] = issue.PolicyReasonCode ?? issue.ReasonCode, ["policyPackKey"] = plan.Configuration?.PolicyPackKey,
                ["policyPackVersion"] = plan.Configuration?.PolicyPackVersion
            }, correlationId, _timeProvider.GetUtcNow().UtcDateTime), cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
    private static Dictionary<string, string> ParseTaxFacts(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
    private static IReadOnlyList<string> ParseList(string? value) =>
        string.IsNullOrWhiteSpace(value) || value == "none"
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    private static SupplierBillAccountingException Error(string code, string message, bool conflict = false) => new(code, message, conflict);

    private sealed record RetainedDocumentCurrencyFacts(Guid? ConversionId, decimal RoundingResidual, string Provenance);
}
