using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class CustomerInvoiceAccountingService : ICustomerInvoiceAccountingService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly CustomerInvoiceAccountingPolicy _policy;
    private readonly IAccountingPostingService _postingService;
    private readonly IAccountingJournalReadService _journalReadService;
    private readonly IAuditEventWriter _audit;
    private readonly TimeProvider _timeProvider;
    private readonly IAccountingPolicyPackResolver _packResolver;

    public CustomerInvoiceAccountingService(
        VirtualCompanyDbContext dbContext,
        CustomerInvoiceAccountingPolicy policy,
        IAccountingPostingService postingService,
        IAccountingJournalReadService journalReadService,
        IAuditEventWriter audit,
        TimeProvider timeProvider,
        IAccountingPolicyPackResolver packResolver)
    {
        _dbContext = dbContext;
        _policy = policy;
        _postingService = postingService;
        _journalReadService = journalReadService;
        _audit = audit;
        _timeProvider = timeProvider;
        _packResolver = packResolver;
    }

    public Task<CustomerInvoiceAccountingPreviewDto> PreviewAsync(
        PreviewCustomerInvoiceAccountingQuery query, CancellationToken cancellationToken) =>
        _policy.PreviewAsync(query, cancellationToken);

    public async Task<CustomerInvoiceAccountingReferenceDataDto> GetReferenceDataAsync(
        GetCustomerInvoiceAccountingQuery query, CancellationToken cancellationToken)
    {
        var invoice = await _dbContext.FinanceInvoices.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == query.CompanyId && x.Id == query.InvoiceId, cancellationToken)
            ?? throw Error(CustomerInvoiceAccountingReasonCodes.InvoiceNotFound, "The customer invoice could not be found.");
        var configuration = await _dbContext.AccountingConfigurations.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == query.CompanyId, cancellationToken)
            ?? throw Error(CustomerInvoiceAccountingReasonCodes.ConfigurationUnavailable, "Complete accounting setup before preparing this invoice.");
        var pack = _packResolver.Resolve(configuration.PolicyPackKey, configuration.PolicyPackVersion);
        var issueDate = DateOnly.FromDateTime(invoice.IssuedUtc);
        var periods = await _dbContext.FiscalPeriods.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && !x.IsClosed && !x.IsReportingLocked)
            .OrderBy(x => x.StartUtc)
            .Select(x => new CustomerInvoiceAccountingPeriodOptionDto(x.Id, x.Name,
                DateOnly.FromDateTime(x.StartUtc), DateOnly.FromDateTime(x.EndUtc).AddDays(-1)))
            .ToListAsync(cancellationToken);
        var series = await _dbContext.VoucherSeries.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.IsActive).OrderBy(x => x.Code)
            .Select(x => new CustomerInvoiceAccountingVoucherSeriesOptionDto(x.Code, x.DisplayName))
            .ToListAsync(cancellationToken);
        var rules = pack.Definition.TaxRules.Where(x => x.EffectiveFrom <= issueDate)
            .Select(x => new CustomerInvoiceAccountingTaxRuleOptionDto(x.Key, x.DisplayName, x.Rate, x.AmountMethod, x.EffectiveFrom))
            .ToArray();
        var defaultRule = rules.FirstOrDefault(x => x.AmountMethod == CustomerInvoiceTaxMethodValues.Exempt) ?? rules.FirstOrDefault();
        var defaultPeriod = periods.FirstOrDefault(x => issueDate >= x.StartDate && issueDate <= x.EndDate);
        return new(invoice.Id, invoice.Currency, configuration.BaseCurrency, Math.Abs(invoice.Amount), rules, periods,
            series, defaultRule?.Key, defaultPeriod?.Id, series.FirstOrDefault(x => x.Code == "G")?.Code ?? series.FirstOrDefault()?.Code);
    }

    public async Task<CustomerInvoiceAccountingSubmissionResult> SubmitAsync(
        SubmitCustomerInvoiceAccountingCommand command, CancellationToken cancellationToken)
    {
        ValidateActor(command.CompanyId, command.InvoiceId, command.ActorUserId);
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
            throw Error(CustomerInvoiceAccountingReasonCodes.RequiredFieldMissing, "An idempotency key is required.");

        var plan = await _policy.BuildPlanAsync(
            new(command.CompanyId, command.InvoiceId, command.Input, command.ActorUserId), cancellationToken);
        if (!plan.IsReady)
        {
            var first = plan.Issues.First(x => x.IsBlocking);
            throw Error(first.ReasonCode, first.Explanation);
        }

        var profile = await _dbContext.CustomerInvoiceAccountingProfiles.IgnoreQueryFilters()
            .Include(x => x.Lines).Include(x => x.ApprovalRequest)
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.InvoiceId == command.InvoiceId, cancellationToken);
        if (profile?.Status == CustomerInvoiceAccountingStatuses.Posted)
            throw Error(CustomerInvoiceAccountingReasonCodes.AlreadyPosted, "This invoice is already posted to the native ledger.", true);
        if (profile is not null && command.ExpectedVersion.HasValue && profile.Version != command.ExpectedVersion.Value)
            throw Error(CustomerInvoiceAccountingReasonCodes.VersionConflict, "The invoice accounting details changed. Reload them before continuing.", true);
        var factsUnchanged = profile is not null &&
            string.Equals(profile.PayloadHash, plan.PayloadHash, StringComparison.OrdinalIgnoreCase);
        if (factsUnchanged && profile!.ApprovalRequestId.HasValue)
            return new(await MapStateAsync(profile, cancellationToken), profile.ApprovalRequestId.Value, true);

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (profile is null)
        {
            profile = new CustomerInvoiceAccountingProfile(Guid.NewGuid(), command.CompanyId, command.InvoiceId,
                plan.FiscalPeriodId, plan.VoucherSeriesCode, plan.Invoice.Currency, plan.Configuration!.BaseCurrency,
                plan.ExchangeRate, plan.NetAmount, plan.TaxAmount, plan.GrossAmount, plan.NetBaseAmount,
                plan.TaxBaseAmount, plan.GrossBaseAmount, plan.RoundingBaseAmount, plan.ReceivableAccountId,
                plan.RevenueAccountId, plan.TaxMethod, plan.Configuration.PolicyPackKey,
                plan.Configuration.PolicyPackVersion, plan.PolicyPack!.DefinitionHash,
                plan.ExistingProfile?.OriginalInvoiceId, command.ActorUserId, now);
            _dbContext.CustomerInvoiceAccountingProfiles.Add(profile);
        }
        else if (!factsUnchanged)
        {
            if (profile.ApprovalRequest is { Status: ApprovalRequestStatus.Pending })
                profile.ApprovalRequest.MarkCancelled("The invoice accounting facts changed and require a new approval.");
            _dbContext.CustomerInvoiceAccountingLines.RemoveRange(profile.Lines);
            profile.Lines.Clear();
            profile.ReplaceFacts(plan.FiscalPeriodId, plan.VoucherSeriesCode, plan.Invoice.Currency,
                plan.Configuration!.BaseCurrency, plan.ExchangeRate, plan.NetAmount, plan.TaxAmount, plan.GrossAmount,
                plan.NetBaseAmount, plan.TaxBaseAmount, plan.GrossBaseAmount, plan.RoundingBaseAmount,
                plan.ReceivableAccountId, plan.RevenueAccountId, plan.TaxMethod, plan.Configuration.PolicyPackKey,
                plan.Configuration.PolicyPackVersion, plan.PolicyPack!.DefinitionHash,
                profile.OriginalInvoiceId, command.ActorUserId, now);
        }

        if (!factsUnchanged)
        {
            profile.SetPayloadHash(plan.PayloadHash);
            foreach (var line in plan.Lines)
                profile.Lines.Add(new CustomerInvoiceAccountingLine(Guid.NewGuid(), command.CompanyId, profile.Id,
                    line.Sequence, line.Description, line.TaxRuleKey, line.TaxMethod, line.TaxRate,
                    line.NetAmount, line.TaxAmount, line.GrossAmount, line.NetBaseAmount, line.TaxBaseAmount,
                    line.TaxPayableAccountId));
        }

        var approval = ApprovalRequest.CreateForTarget(Guid.NewGuid(), command.CompanyId,
            ApprovalTargetEntityType.CustomerInvoiceAccounting, profile.Id, AuditActorTypes.User,
            command.ActorUserId, "customer_invoice_accounting_posting",
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["sourceVersion"] = JsonValue.Create(profile.Version.ToString(CultureInfo.InvariantCulture)),
                ["payloadHash"] = JsonValue.Create(profile.PayloadHash),
                ["invoiceId"] = JsonValue.Create(command.InvoiceId.ToString("N")),
                ["grossBaseAmount"] = JsonValue.Create(profile.GrossBaseAmount),
                ["idempotencyKey"] = JsonValue.Create(command.IdempotencyKey.Trim())
            }, null, null,
            [new ApprovalStepDefinition(1, ApprovalStepApproverType.Role, "finance_approver")]);
        _dbContext.ApprovalRequests.Add(approval);
        profile.BindApproval(approval.Id, command.ActorUserId, now);
        await _audit.WriteAsync(new AuditEventWriteRequest(command.CompanyId, AuditActorTypes.User,
            command.ActorUserId, AuditEventActions.AccountingCustomerInvoiceApprovalRequested,
            AuditTargetTypes.CustomerInvoiceAccounting, profile.Id.ToString("N"), AuditEventOutcomes.Succeeded,
            "Approval was requested for the exact customer invoice accounting facts.",
            ["finance_invoice", "accounting_configuration", "accounting_policy_pack"],
            new Dictionary<string, string?>
            {
                ["invoiceId"] = command.InvoiceId.ToString("N"),
                ["sourceVersion"] = profile.Version.ToString(CultureInfo.InvariantCulture),
                ["payloadHash"] = profile.PayloadHash
            }, command.CorrelationId, now), cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return new(await MapStateAsync(profile, cancellationToken), approval.Id, false);
    }

    public async Task<CustomerInvoiceAccountingPostingResult> PostAsync(
        PostCustomerInvoiceAccountingCommand command, CancellationToken cancellationToken)
    {
        ValidateActor(command.CompanyId, command.InvoiceId, command.ActorUserId);
        var profile = await LoadProfileAsync(command.CompanyId, command.InvoiceId, cancellationToken);
        if (profile.Version != command.ExpectedVersion)
            throw Error(CustomerInvoiceAccountingReasonCodes.VersionConflict, "The approved invoice accounting version is no longer current.", true);
        if (profile.ApprovalRequest is null)
            throw Error(CustomerInvoiceAccountingReasonCodes.ApprovalRequired, "Submit the invoice accounting entry for approval first.");
        if (profile.ApprovalRequest.Status == ApprovalRequestStatus.Pending)
            throw Error(CustomerInvoiceAccountingReasonCodes.ApprovalPending, "This invoice accounting entry is waiting for approval.");
        if (profile.ApprovalRequest.Status != ApprovalRequestStatus.Approved || !ApprovalMatches(profile))
            throw Error(CustomerInvoiceAccountingReasonCodes.ApprovalStale, "The accounting approval is stale or no longer approved.", true);

        var proposed = await BuildProposedAsync(profile, command.IdempotencyKey, command.ActorUserId, cancellationToken);
        var posted = await _postingService.PostAsync(new(proposed, command.CorrelationId), cancellationToken);
        var refreshed = await LoadProfileAsync(command.CompanyId, command.InvoiceId, cancellationToken);
        return new(await MapStateAsync(refreshed, cancellationToken), posted.Journal, posted.IsIdempotentReplay);
    }

    public async Task<CustomerInvoiceAccountingStateDto> GetAsync(
        GetCustomerInvoiceAccountingQuery query, CancellationToken cancellationToken)
    {
        var invoice = await _dbContext.FinanceInvoices.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == query.CompanyId && x.Id == query.InvoiceId, cancellationToken)
            ?? throw Error(CustomerInvoiceAccountingReasonCodes.InvoiceNotFound, "The customer invoice could not be found.");
        var profile = await _dbContext.CustomerInvoiceAccountingProfiles.IgnoreQueryFilters().AsNoTracking()
            .Include(x => x.Lines).Include(x => x.ApprovalRequest)
            .SingleOrDefaultAsync(x => x.CompanyId == query.CompanyId && x.InvoiceId == query.InvoiceId, cancellationToken);
        return profile is null ? EmptyState(invoice) : await MapStateAsync(profile, cancellationToken);
    }

    public async Task<CustomerInvoiceAccountingStateDto> CreateCreditNoteAsync(
        CreateCustomerCreditNoteCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.CreditNoteNumber) || string.IsNullOrWhiteSpace(command.Reason))
            throw Error(CustomerInvoiceAccountingReasonCodes.CreditNoteInvalid, "A credit-note number and correction reason are required.");
        var original = await _dbContext.FinanceInvoices.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.OriginalInvoiceId, cancellationToken)
            ?? throw Error(CustomerInvoiceAccountingReasonCodes.InvoiceNotFound, "The original customer invoice could not be found.");
        var originalProfile = await _dbContext.CustomerInvoiceAccountingProfiles.IgnoreQueryFilters().AsNoTracking()
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.InvoiceId == original.Id && x.LedgerEntryId != null, cancellationToken)
            ?? throw Error(CustomerInvoiceAccountingReasonCodes.CreditNoteInvalid, "Post the original customer invoice before creating its credit note.");
        var existing = await _dbContext.FinanceInvoices.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.InvoiceNumber == command.CreditNoteNumber.Trim(), cancellationToken);
        if (existing is not null)
        {
            var existingProfile = await _dbContext.CustomerInvoiceAccountingProfiles.IgnoreQueryFilters().AsNoTracking()
                .Include(x => x.Lines).Include(x => x.ApprovalRequest)
                .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.InvoiceId == existing.Id && x.OriginalInvoiceId == original.Id, cancellationToken);
            if (existingProfile is not null) return await MapStateAsync(existingProfile, cancellationToken);
            throw Error(CustomerInvoiceAccountingReasonCodes.DuplicateDocumentNumber, "Another invoice already uses this credit-note number.", true);
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var gross = originalProfile.GrossAmount;
            if (gross <= 0m) throw Error(CustomerInvoiceAccountingReasonCodes.AmountMismatch, "Credit-note lines must have a positive total.");
            var credit = new FinanceInvoice(Guid.NewGuid(), command.CompanyId, original.CounterpartyId,
                command.CreditNoteNumber.Trim(), command.IssueDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                command.DueDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), -gross, original.Currency, "approved",
                original.DocumentId, now, now, documentKind: FinanceDocumentKinds.CreditNote);
            _dbContext.FinanceInvoices.Add(credit);
            await _dbContext.SaveChangesAsync(cancellationToken);

            var plan = await _policy.BuildPlanAsync(new(command.CompanyId, credit.Id, command.Accounting, command.ActorUserId), cancellationToken);
            if (!plan.IsReady)
            {
                var first = plan.Issues.First(x => x.IsBlocking);
                throw Error(first.ReasonCode, first.Explanation);
            }
            var profile = new CustomerInvoiceAccountingProfile(Guid.NewGuid(), command.CompanyId, credit.Id,
                plan.FiscalPeriodId, plan.VoucherSeriesCode, credit.Currency, plan.Configuration!.BaseCurrency,
                plan.ExchangeRate, plan.NetAmount, plan.TaxAmount, plan.GrossAmount, plan.NetBaseAmount,
                plan.TaxBaseAmount, plan.GrossBaseAmount, plan.RoundingBaseAmount, plan.ReceivableAccountId,
                plan.RevenueAccountId, plan.TaxMethod, plan.Configuration.PolicyPackKey,
                plan.Configuration.PolicyPackVersion, plan.PolicyPack!.DefinitionHash, original.Id,
                command.ActorUserId, now);
            profile.SetPayloadHash(plan.PayloadHash);
            foreach (var line in plan.Lines)
                profile.Lines.Add(new CustomerInvoiceAccountingLine(Guid.NewGuid(), command.CompanyId, profile.Id,
                    line.Sequence, line.Description, line.TaxRuleKey, line.TaxMethod, line.TaxRate,
                    line.NetAmount, line.TaxAmount, line.GrossAmount, line.NetBaseAmount, line.TaxBaseAmount,
                    line.TaxPayableAccountId));
            _dbContext.CustomerInvoiceAccountingProfiles.Add(profile);
            await _dbContext.SaveChangesAsync(cancellationToken);

            // Rebuild once the original-invoice link exists so the approval hash binds that correction relationship.
            var linkedPlan = await _policy.BuildPlanAsync(
                new(command.CompanyId, credit.Id, command.Accounting, command.ActorUserId), cancellationToken);
            if (!linkedPlan.IsReady)
            {
                var first = linkedPlan.Issues.First(x => x.IsBlocking);
                throw Error(first.ReasonCode, first.Explanation);
            }
            profile.SetPayloadHash(linkedPlan.PayloadHash);
            await _dbContext.SaveChangesAsync(cancellationToken);

            var submitted = await SubmitAsync(new(command.CompanyId, credit.Id, command.Accounting, profile.Version,
                command.IdempotencyKey, command.ActorUserId, command.CorrelationId), cancellationToken);
            await _audit.WriteAsync(new AuditEventWriteRequest(command.CompanyId, AuditActorTypes.User,
                command.ActorUserId, AuditEventActions.AccountingCustomerCreditNoteCreated,
                AuditTargetTypes.CustomerInvoiceAccounting, profile.Id.ToString("N"), AuditEventOutcomes.Succeeded,
                "A customer credit note was created and linked to the original posted invoice.",
                ["finance_invoice", "accounting_journal"], new Dictionary<string, string?>
                {
                    ["originalInvoiceId"] = original.Id.ToString("N"), ["creditInvoiceId"] = credit.Id.ToString("N"),
                    ["originalLedgerEntryId"] = originalProfile.LedgerEntryId?.ToString("N"), ["reason"] = command.Reason.Trim()
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

    public async Task<CustomerInvoiceReceivableReconciliationDto> ReconcileAsync(
        GetCustomerInvoiceReceivableReconciliationQuery query, CancellationToken cancellationToken)
    {
        var configuration = await _dbContext.AccountingConfigurations.IgnoreQueryFilters().AsNoTracking()
            .Include(x => x.AccountRoles).SingleOrDefaultAsync(x => x.CompanyId == query.CompanyId, cancellationToken)
            ?? throw Error(CustomerInvoiceAccountingReasonCodes.ConfigurationUnavailable, "Accounting is not configured.");
        var receivableId = configuration.AccountRoles.SingleOrDefault(x => x.RoleKey == "accounts_receivable")?.FinanceAccountId
            ?? throw Error(CustomerInvoiceAccountingReasonCodes.AccountRoleMissing, "The accounts receivable role is not configured.");
        var profiles = await _dbContext.CustomerInvoiceAccountingProfiles.IgnoreQueryFilters().AsNoTracking()
            .Include(x => x.Invoice).Where(x => x.CompanyId == query.CompanyId && x.Status == CustomerInvoiceAccountingStatuses.Posted)
            .Where(x => !query.ThroughDate.HasValue || x.Invoice.IssuedUtc < query.ThroughDate.Value.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc))
            .ToListAsync(cancellationToken);
        var postedDocuments = profiles.Sum(x => x.Invoice.DocumentKind == FinanceDocumentKinds.CreditNote ? -x.GrossBaseAmount : x.GrossBaseAmount);
        var ledger = await _dbContext.LedgerEntryLines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.FinanceAccountId == receivableId &&
                x.LedgerEntry.SourceType == "customer_invoice" && x.LedgerEntry.Status == LedgerEntryStatuses.Posted)
            .Where(x => !query.ThroughDate.HasValue || x.LedgerEntry.PostingDate <= query.ThroughDate.Value)
            .SumAsync(x => x.DebitAmount - x.CreditAmount, cancellationToken);
        var profileByInvoice = profiles.ToDictionary(x => x.InvoiceId);
        var allocations = await _dbContext.PaymentAllocations.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.InvoiceId != null && profileByInvoice.Keys.Contains(x.InvoiceId.Value))
            .ToListAsync(cancellationToken);
        var allocatedBase = allocations.Sum(x => x.AllocatedAmount * profileByInvoice[x.InvoiceId!.Value].ExchangeRate);
        var difference = decimal.Round(postedDocuments - ledger, configuration.RoundingPrecision, MidpointRounding.ToEven);
        return new(query.CompanyId, configuration.BaseCurrency, postedDocuments, ledger, allocatedBase,
            postedDocuments - allocatedBase, difference, difference == 0m, _timeProvider.GetUtcNow().UtcDateTime);
    }

    private async Task<ProposedAccountingEntry> BuildProposedAsync(
        CustomerInvoiceAccountingProfile profile, string idempotencyKey, Guid actorUserId, CancellationToken cancellationToken)
    {
        var invoice = await _dbContext.FinanceInvoices.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x => x.CompanyId == profile.CompanyId && x.Id == profile.InvoiceId, cancellationToken);
        var isCredit = invoice.DocumentKind == FinanceDocumentKinds.CreditNote;
        var lines = new List<ProposedAccountingLine>
        {
            new(profile.ReceivableAccountId, isCredit ? 0m : profile.GrossBaseAmount,
                isCredit ? profile.GrossBaseAmount : 0m, profile.BaseCurrency,
                $"{invoice.InvoiceNumber} · accounts receivable")
        };
        var ordered = profile.Lines.OrderBy(x => x.Sequence).ToArray();
        foreach (var line in ordered)
        {
            var revenue = line.NetBaseAmount + (line.Sequence == ordered[^1].Sequence ? profile.RoundingBaseAmount : 0m);
            lines.Add(new(profile.RevenueAccountId, isCredit ? revenue : 0m, isCredit ? 0m : revenue,
                profile.BaseCurrency, line.Description, TaxFacts: new Dictionary<string, string>
                {
                    ["taxRuleKey"] = line.TaxRuleKey, ["taxMethod"] = line.TaxMethod,
                    ["taxRate"] = line.TaxRate.ToString(CultureInfo.InvariantCulture),
                    ["documentCurrency"] = profile.DocumentCurrency
                }));
            if (line.TaxBaseAmount > 0m && line.TaxPayableAccountId.HasValue)
                lines.Add(new(line.TaxPayableAccountId.Value, isCredit ? line.TaxBaseAmount : 0m,
                    isCredit ? 0m : line.TaxBaseAmount, profile.BaseCurrency, $"Tax · {line.Description}",
                    TaxFacts: new Dictionary<string, string>
                    {
                        ["taxRuleKey"] = line.TaxRuleKey, ["taxMethod"] = line.TaxMethod,
                        ["taxRate"] = line.TaxRate.ToString(CultureInfo.InvariantCulture),
                        ["documentTaxAmount"] = line.TaxAmount.ToString(CultureInfo.InvariantCulture)
                    }));
        }

        var evidence = new List<ProposedAccountingEvidence>();
        if (invoice.DocumentId.HasValue)
        {
            var document = await _dbContext.CompanyKnowledgeDocuments.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(x => x.CompanyId == profile.CompanyId && x.Id == invoice.DocumentId.Value, cancellationToken);
            var checksum = document?.Metadata.TryGetValue("checksum_sha256", out var checksumNode) == true ? checksumNode?.ToString() : null;
            if (document is not null && !string.IsNullOrWhiteSpace(checksum)) evidence.Add(new(document.Id, checksum, document.Title));
        }
        Guid? originalLedgerEntryId = null;
        if (profile.OriginalInvoiceId.HasValue)
            originalLedgerEntryId = await _dbContext.CustomerInvoiceAccountingProfiles.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == profile.CompanyId && x.InvoiceId == profile.OriginalInvoiceId.Value)
                .Select(x => x.LedgerEntryId).SingleAsync(cancellationToken);

        return new(profile.CompanyId, profile.FiscalPeriodId, profile.VoucherSeriesCode,
            DateOnly.FromDateTime(invoice.IssuedUtc), DateOnly.FromDateTime(invoice.IssuedUtc),
            LedgerPostingTypeValues.SourceDocument, $"{(isCredit ? "Credit note" : "Customer invoice")} {invoice.InvoiceNumber}",
            "customer_invoice", invoice.Id.ToString("N"), profile.Version.ToString(CultureInfo.InvariantCulture),
            idempotencyKey.Trim(), lines, actorUserId, profile.ApprovalRequestId, true,
            new Dictionary<string, string>
            {
                ["documentKind"] = invoice.DocumentKind, ["documentCurrency"] = profile.DocumentCurrency,
                ["exchangeRate"] = profile.ExchangeRate.ToString(CultureInfo.InvariantCulture),
                ["netAmount"] = profile.NetAmount.ToString(CultureInfo.InvariantCulture),
                ["taxAmount"] = profile.TaxAmount.ToString(CultureInfo.InvariantCulture),
                ["grossAmount"] = profile.GrossAmount.ToString(CultureInfo.InvariantCulture),
                ["policyDefinitionHash"] = profile.PolicyDefinitionHash
            }, isCredit ? "credit" : "post", profile.PayloadHash, evidence,
            originalLedgerEntryId, isCredit ? $"Credit note {invoice.InvoiceNumber} corrects invoice {profile.OriginalInvoiceId:N}." : null);
    }

    private async Task<CustomerInvoiceAccountingProfile> LoadProfileAsync(Guid companyId, Guid invoiceId, CancellationToken cancellationToken) =>
        await _dbContext.CustomerInvoiceAccountingProfiles.IgnoreQueryFilters()
            .Include(x => x.Lines).Include(x => x.ApprovalRequest)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.InvoiceId == invoiceId, cancellationToken)
        ?? throw Error(CustomerInvoiceAccountingReasonCodes.RequiredFieldMissing, "Prepare the invoice accounting entry first.");

    private async Task<CustomerInvoiceAccountingStateDto> MapStateAsync(CustomerInvoiceAccountingProfile profile, CancellationToken cancellationToken)
    {
        var invoice = await _dbContext.FinanceInvoices.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x => x.CompanyId == profile.CompanyId && x.Id == profile.InvoiceId, cancellationToken);
        var accounts = await _dbContext.FinanceAccounts.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == profile.CompanyId &&
                (x.Id == profile.ReceivableAccountId || x.Id == profile.RevenueAccountId || profile.Lines.Select(l => l.TaxPayableAccountId).Contains(x.Id)))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var journalLines = BuildStateJournalLines(profile, invoice, accounts);
        string? voucher = null;
        if (profile.LedgerEntryId.HasValue)
            voucher = await _dbContext.LedgerEntries.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == profile.CompanyId && x.Id == profile.LedgerEntryId.Value)
                .Select(x => x.EntryNumber).SingleOrDefaultAsync(cancellationToken);
        var approval = profile.ApprovalRequest is null ? null : new CustomerInvoiceAccountingApprovalDto(
            profile.ApprovalRequest.Id, profile.ApprovalRequest.Status.ToStorageValue(), profile.Version,
            profile.PayloadHash, profile.ApprovalRequest.CreatedUtc, profile.ApprovalRequest.DecidedUtc);
        var status = profile.Status == CustomerInvoiceAccountingStatuses.AwaitingApproval && profile.ApprovalRequest?.Status == ApprovalRequestStatus.Approved
            ? CustomerInvoiceAccountingStatuses.ReadyToPost : profile.Status;
        return new(profile.InvoiceId, profile.Id, status, StatusLabel(status), true,
            status is CustomerInvoiceAccountingStatuses.NotReady or CustomerInvoiceAccountingStatuses.Blocked,
            status == CustomerInvoiceAccountingStatuses.ReadyToPost, status == CustomerInvoiceAccountingStatuses.Posted,
            profile.Version, profile.NetAmount, profile.TaxAmount, profile.GrossAmount, profile.DocumentCurrency,
            profile.ExchangeRate, profile.GrossBaseAmount, profile.BaseCurrency, profile.TaxMethod,
            profile.PolicyPackKey, profile.PolicyPackVersion, profile.LedgerEntryId, voucher,
            profile.OriginalInvoiceId, profile.BlockingReasonCode, profile.BlockingReason, approval,
            journalLines, profile.BlockingReason is null ? [] : [new(profile.BlockingReasonCode ?? "blocked", profile.BlockingReason)]);
    }

    private static IReadOnlyList<CustomerInvoiceAccountingJournalLineDto> BuildStateJournalLines(
        CustomerInvoiceAccountingProfile profile, FinanceInvoice invoice, IReadOnlyDictionary<Guid, FinanceAccount> accounts)
    {
        var isCredit = invoice.DocumentKind == FinanceDocumentKinds.CreditNote;
        var result = new List<CustomerInvoiceAccountingJournalLineDto>();
        if (!accounts.TryGetValue(profile.ReceivableAccountId, out var receivable) || !accounts.TryGetValue(profile.RevenueAccountId, out var revenue)) return result;
        result.Add(new(receivable.Id, "accounts_receivable", receivable.Code, receivable.Name,
            isCredit ? 0m : profile.GrossBaseAmount, isCredit ? profile.GrossBaseAmount : 0m,
            profile.BaseCurrency, "Accounts receivable"));
        var ordered = profile.Lines.OrderBy(x => x.Sequence).ToArray();
        foreach (var line in ordered)
        {
            var revenueAmount = line.NetBaseAmount + (line.Sequence == ordered[^1].Sequence ? profile.RoundingBaseAmount : 0m);
            result.Add(new(revenue.Id, "revenue", revenue.Code, revenue.Name,
                isCredit ? revenueAmount : 0m, isCredit ? 0m : revenueAmount,
                profile.BaseCurrency, line.Description, line.TaxRuleKey));
            if (line.TaxBaseAmount > 0m && line.TaxPayableAccountId.HasValue && accounts.TryGetValue(line.TaxPayableAccountId.Value, out var tax))
                result.Add(new(tax.Id, "tax_payable", tax.Code, tax.Name,
                    isCredit ? line.TaxBaseAmount : 0m, isCredit ? 0m : line.TaxBaseAmount,
                    profile.BaseCurrency, "Tax payable", line.TaxRuleKey));
        }
        return result;
    }

    private static CustomerInvoiceAccountingStateDto EmptyState(FinanceInvoice invoice) => new(
        invoice.Id, null, CustomerInvoiceAccountingStatuses.NotReady, "Not ready", true, IsApproved(invoice.Status),
        false, false, null, null, null, null, invoice.Currency, null, null, null, null, null, null,
        null, null, null, null, null, null, [], []);

    private static bool ApprovalMatches(CustomerInvoiceAccountingProfile profile)
    {
        var approval = profile.ApprovalRequest!;
        var version = approval.ThresholdContext.TryGetValue("sourceVersion", out var versionNode) ? versionNode?.ToString() : null;
        var hash = approval.ThresholdContext.TryGetValue("payloadHash", out var hashNode) ? hashNode?.ToString() : null;
        return approval.TargetEntityType == ApprovalTargetEntityType.CustomerInvoiceAccounting.ToStorageValue() &&
            approval.TargetEntityId == profile.Id && version == profile.Version.ToString(CultureInfo.InvariantCulture) &&
            string.Equals(hash, profile.PayloadHash, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsApproved(string status) => status.Trim().ToLowerInvariant() is "approved" or "paid";
    private static string StatusLabel(string status) => status switch
    {
        CustomerInvoiceAccountingStatuses.NotReady => "Not ready",
        CustomerInvoiceAccountingStatuses.AwaitingApproval => "Waiting for approval",
        CustomerInvoiceAccountingStatuses.ReadyToPost => "Ready to post",
        CustomerInvoiceAccountingStatuses.Posted => "Posted",
        CustomerInvoiceAccountingStatuses.Reversed => "Reversed",
        CustomerInvoiceAccountingStatuses.Blocked => "Needs review",
        _ => "Unknown"
    };
    private static void ValidateActor(Guid companyId, Guid invoiceId, Guid actorId)
    {
        if (companyId == Guid.Empty || invoiceId == Guid.Empty || actorId == Guid.Empty)
            throw Error(CustomerInvoiceAccountingReasonCodes.InvoiceNotFound, "The customer invoice could not be found.");
    }
    private static CustomerInvoiceAccountingException Error(string code, string message, bool conflict = false) => new(code, message, conflict);
}
