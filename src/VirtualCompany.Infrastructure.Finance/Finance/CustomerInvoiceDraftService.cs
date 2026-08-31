using System.Globalization;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class CustomerInvoiceDraftService : ICustomerInvoiceDraftService
{
    private const int MaximumIssueAttempts = 5;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICustomerInvoiceDraftCalculationPolicy _calculationPolicy;
    private readonly ICustomerInvoiceDraftReadinessPolicy _readinessPolicy;
    private readonly IAuditEventWriter _auditWriter;
    private readonly CustomerInvoiceDraftTelemetry _telemetry;
    private readonly TimeProvider _timeProvider;
    private readonly IStatutoryDocumentPolicy? _statutoryDocumentPolicy;
    private readonly IAccountingPostingService? _postingService;
    private readonly IExchangeRateService? _exchangeRates;

    public CustomerInvoiceDraftService(VirtualCompanyDbContext dbContext,
        ICustomerInvoiceDraftCalculationPolicy calculationPolicy,
        ICustomerInvoiceDraftReadinessPolicy readinessPolicy, IAuditEventWriter auditWriter,
        CustomerInvoiceDraftTelemetry telemetry, TimeProvider timeProvider,
        IStatutoryDocumentPolicy? statutoryDocumentPolicy = null,
        IAccountingPostingService? postingService = null,
        IExchangeRateService? exchangeRates = null)
    {
        _dbContext = dbContext;
        _calculationPolicy = calculationPolicy;
        _readinessPolicy = readinessPolicy;
        _auditWriter = auditWriter;
        _telemetry = telemetry;
        _timeProvider = timeProvider;
        _statutoryDocumentPolicy = statutoryDocumentPolicy;
        _postingService = postingService;
        _exchangeRates = exchangeRates;
    }

    public async Task<CustomerInvoiceDraftDto> CreateAsync(CreateCustomerInvoiceDraftCommand command,
        CancellationToken cancellationToken)
    {
        ValidateCommand(command.CompanyId, command.ActorUserId, command.IdempotencyKey);
        ValidateInput(command.Draft);
        var requestHash = HashInput(command.Draft);
        var replay = await FindOperationAsync(command.CompanyId, command.IdempotencyKey, cancellationToken);
        if (replay is not null)
        {
            EnsureReplay(replay, requestHash);
            _telemetry.Record("create", replay: true);
            return await GetAsync(new(command.CompanyId, replay.DraftId), cancellationToken);
        }

        var material = await MaterializeAsync(command.CompanyId, command.Draft, cancellationToken);
        var now = UtcNow();
        var draft = new CustomerInvoiceDraft(Guid.NewGuid(), command.CompanyId, command.Draft.CustomerId,
            command.Draft.DocumentType, command.Draft.IssueDate, command.Draft.SupplyDate, command.Draft.DueDate,
            command.Draft.Currency, command.Draft.PaymentTermKind, command.Draft.PaymentTermDays,
            command.Draft.BuyerReference, command.Draft.SellerReference, command.Draft.Notes,
            command.Draft.DeliveryIntent, command.Draft.SourceKind, command.Draft.SourceReference,
            command.ActorUserId, now, command.Draft.OriginalInvoiceId);
        ApplyMaterial(draft, command.Draft, material, now);
        _dbContext.CustomerInvoiceDrafts.Add(draft);
        _dbContext.CustomerInvoiceDraftOperations.Add(new(Guid.NewGuid(), command.CompanyId, draft.Id,
            "create", command.IdempotencyKey, requestHash, draft.Version, null, now));
        await AuditAsync(command.CompanyId, command.ActorUserId,
            AuditEventActions.AccountingCustomerInvoiceDraftCreated, draft.Id,
            "A native customer invoice draft was created with an authoritative tax preview.",
            command.CorrelationId, now, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _telemetry.Record("create");
        if (material.Calculation.Blockers.Count > 0) _telemetry.RecordBlocked(material.Calculation.Blockers.Count);
        return await GetAsync(new(command.CompanyId, draft.Id), cancellationToken);
    }

    public async Task<CustomerInvoiceDraftDto> UpdateAsync(UpdateCustomerInvoiceDraftCommand command,
        CancellationToken cancellationToken)
    {
        ValidateCommand(command.CompanyId, command.ActorUserId, command.IdempotencyKey);
        ValidateInput(command.Draft);
        var requestHash = HashInput(command.Draft);
        var replay = await FindOperationAsync(command.CompanyId, command.IdempotencyKey, cancellationToken);
        if (replay is not null)
        {
            EnsureReplay(replay, requestHash);
            _telemetry.Record("update", replay: true);
            return await GetAsync(new(command.CompanyId, replay.DraftId), cancellationToken);
        }
        var material = await MaterializeAsync(command.CompanyId, command.Draft, cancellationToken);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var draft = await _dbContext.CustomerInvoiceDrafts.Include(x => x.ApprovalRequest)
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.DraftId, cancellationToken)
            ?? throw NotFound();
        EnsureVersion(draft, command.ExpectedVersion);
        EnsureEditable(draft);
        if (draft.OriginalInvoiceId != command.Draft.OriginalInvoiceId)
            throw new CustomerInvoiceDraftException(CustomerInvoiceDraftReasonCodes.NotEditable,
                "The original invoice link of a credit-note draft cannot be changed.", true, draft.Version);
        if (draft.ApprovalRequest is { Status: ApprovalRequestStatus.Pending })
            draft.ApprovalRequest.MarkCancelled("The invoice draft changed and requires a new approval.");
        await _dbContext.CustomerInvoiceDraftLines
            .Where(x => x.CompanyId == command.CompanyId && x.DraftId == draft.Id)
            .ExecuteDeleteAsync(cancellationToken);
        await _dbContext.CustomerInvoiceDraftEvidenceLinks
            .Where(x => x.CompanyId == command.CompanyId && x.DraftId == draft.Id)
            .ExecuteDeleteAsync(cancellationToken);
        var now = UtcNow();
        draft.ReplaceContent(command.Draft.CustomerId, command.Draft.DocumentType, command.Draft.IssueDate,
            command.Draft.SupplyDate, command.Draft.DueDate, command.Draft.Currency,
            command.Draft.PaymentTermKind, command.Draft.PaymentTermDays, command.Draft.BuyerReference,
            command.Draft.SellerReference, command.Draft.Notes, command.Draft.DeliveryIntent,
            command.Draft.SourceKind, command.Draft.SourceReference, command.ActorUserId, now);
        ApplyMaterial(draft, command.Draft, material, now);
        _dbContext.CustomerInvoiceDraftLines.AddRange(draft.Lines);
        _dbContext.CustomerInvoiceDraftEvidenceLinks.AddRange(draft.EvidenceLinks);
        _dbContext.CustomerInvoiceDraftOperations.Add(new(Guid.NewGuid(), command.CompanyId, draft.Id,
            "update", command.IdempotencyKey, requestHash, draft.Version, null, now));
        await AuditAsync(command.CompanyId, command.ActorUserId,
            AuditEventActions.AccountingCustomerInvoiceDraftUpdated, draft.Id,
            "The native invoice draft was updated and any earlier approval was invalidated.",
            command.CorrelationId, now, cancellationToken);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new CustomerInvoiceDraftException(CustomerInvoiceDraftReasonCodes.VersionConflict,
                "This invoice draft changed elsewhere. Reload the current version before editing.", true, draft.Version);
        }
        _telemetry.Record("update");
        if (material.Calculation.Blockers.Count > 0) _telemetry.RecordBlocked(material.Calculation.Blockers.Count);
        return await GetAsync(new(command.CompanyId, draft.Id), cancellationToken);
    }

    public async Task<CustomerInvoiceDraftDto> CopyAsync(CopyCustomerInvoiceDraftCommand command,
        CancellationToken cancellationToken)
    {
        ValidateCommand(command.CompanyId, command.ActorUserId, command.IdempotencyKey);
        var source = await LoadDraftAsync(command.CompanyId, command.DraftId, cancellationToken);
        EnsureVersion(source, command.ExpectedVersion);
        var input = ToInput(source) with
        {
            IssueDate = command.IssueDate,
            SupplyDate = command.IssueDate,
            DueDate = command.IssueDate.AddDays(source.PaymentTermDays),
            SourceKind = CustomerInvoiceDraftSourceKinds.Copy,
            SourceReference = source.Id.ToString("N")
        };
        var copy = await CreateAsync(new(command.CompanyId, input, command.IdempotencyKey,
            command.ActorUserId, command.CorrelationId), cancellationToken);
        _telemetry.Record("copy");
        return copy;
    }

    public async Task<CustomerInvoiceDraftDto> DiscardAsync(DiscardCustomerInvoiceDraftCommand command,
        CancellationToken cancellationToken)
    {
        ValidateCommand(command.CompanyId, command.ActorUserId, command.IdempotencyKey);
        var requestHash = HashText($"{command.DraftId:N}:{command.ExpectedVersion}:discard");
        var replay = await FindOperationAsync(command.CompanyId, command.IdempotencyKey, cancellationToken);
        if (replay is not null)
        {
            EnsureReplay(replay, requestHash);
            _telemetry.Record("discard", replay: true);
            return await GetAsync(new(command.CompanyId, replay.DraftId), cancellationToken);
        }
        var draft = await DraftQuery(true).SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.DraftId, cancellationToken)
            ?? throw NotFound();
        EnsureVersion(draft, command.ExpectedVersion);
        EnsureEditable(draft);
        if (draft.ApprovalRequest is { Status: ApprovalRequestStatus.Pending })
            draft.ApprovalRequest.MarkCancelled("The invoice draft was discarded.");
        var now = UtcNow();
        draft.Discard(command.ActorUserId, now);
        _dbContext.CustomerInvoiceDraftOperations.Add(new(Guid.NewGuid(), command.CompanyId, draft.Id,
            "discard", command.IdempotencyKey, requestHash, draft.Version, null, now));
        await AuditAsync(command.CompanyId, command.ActorUserId,
            AuditEventActions.AccountingCustomerInvoiceDraftDiscarded, draft.Id,
            "The native customer invoice draft was discarded.", command.CorrelationId, now, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _telemetry.Record("discard");
        return await GetAsync(new(command.CompanyId, draft.Id), cancellationToken);
    }

    public async Task<CustomerInvoiceDraftPreviewDto> PreviewAsync(PreviewCustomerInvoiceDraftQuery query,
        CancellationToken cancellationToken)
    {
        var draft = await LoadDraftAsync(query.CompanyId, query.DraftId, cancellationToken);
        EnsureVersion(draft, query.ExpectedVersion);
        _telemetry.Record("preview", replay: true);
        return new(await MapAsync(draft, cancellationToken), true);
    }

    public async Task<CustomerInvoiceDraftReadinessDto> GetReadinessAsync(
        GetCustomerInvoiceDraftReadinessQuery query, CancellationToken cancellationToken)
    {
        var draft = await LoadDraftAsync(query.CompanyId, query.DraftId, cancellationToken);
        EnsureVersion(draft, query.ExpectedVersion);
        return await _readinessPolicy.EvaluateAsync(query.CompanyId, draft, cancellationToken);
    }

    public async Task<CustomerInvoiceDraftSubmissionResult> SubmitAsync(
        SubmitCustomerInvoiceDraftForApprovalCommand command, CancellationToken cancellationToken)
    {
        ValidateCommand(command.CompanyId, command.ActorUserId, command.IdempotencyKey);
        var draft = await LoadDraftAsync(command.CompanyId, command.DraftId, cancellationToken);
        EnsureVersion(draft, command.ExpectedVersion);
        EnsureEditable(draft);
        var replay = await FindOperationAsync(command.CompanyId, command.IdempotencyKey, cancellationToken);
        if (replay is not null)
        {
            EnsureReplay(replay, draft.ResultHash);
            var replayDraft = await LoadDraftAsync(command.CompanyId, replay.DraftId, cancellationToken);
            var replayReadiness = await _readinessPolicy.EvaluateAsync(command.CompanyId, replayDraft, cancellationToken);
            _telemetry.Record("submit", replay: true);
            return new(await MapAsync(replayDraft, cancellationToken), replayReadiness,
                replay.ApprovalRequestId ?? throw new InvalidOperationException("Approval replay is incomplete."), true);
        }

        var readiness = await _readinessPolicy.EvaluateAsync(command.CompanyId, draft, cancellationToken);
        var submissionBlocker = readiness.Blockers.FirstOrDefault(issue => issue.ReasonCode is not
            CustomerInvoiceDraftReasonCodes.ApprovalRequired and not CustomerInvoiceDraftReasonCodes.ApprovalPending and not
            CustomerInvoiceDraftReasonCodes.ApprovalRejected and not CustomerInvoiceDraftReasonCodes.ApprovalStale);
        if (submissionBlocker is not null)
            throw new CustomerInvoiceDraftException(submissionBlocker.ReasonCode, submissionBlocker.Explanation);

        var approvalCurrencyFacts = await LookupCurrencyFactsAsync(command.CompanyId, draft, cancellationToken);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        draft = await DraftQuery(true).SingleAsync(x => x.CompanyId == command.CompanyId && x.Id == command.DraftId, cancellationToken);
        EnsureVersion(draft, command.ExpectedVersion);
        if (draft.ApprovalRequest is not null && ApprovalMatches(draft.ApprovalRequest, draft))
        {
            await transaction.CommitAsync(cancellationToken);
            var existingReadiness = await _readinessPolicy.EvaluateAsync(command.CompanyId, draft, cancellationToken);
            return new(await MapAsync(draft, cancellationToken), existingReadiness, draft.ApprovalRequest.Id, true);
        }
        if (draft.ApprovalRequest is { Status: ApprovalRequestStatus.Pending })
            draft.ApprovalRequest.MarkCancelled("Superseded by a new invoice draft approval request.");

        var approval = ApprovalRequest.CreateForTarget(Guid.NewGuid(), command.CompanyId,
            ApprovalTargetEntityType.CustomerInvoiceDraft, draft.Id, AuditActorTypes.User, command.ActorUserId,
            "customer_invoice_issue", new Dictionary<string, JsonNode?>
            {
                ["sourceVersion"] = JsonValue.Create(draft.Version.ToString(CultureInfo.InvariantCulture)),
                ["resultHash"] = JsonValue.Create(draft.ResultHash),
                ["grossTotal"] = JsonValue.Create(draft.GrossTotal),
                ["currency"] = JsonValue.Create(draft.Currency),
                ["functionalCurrency"] = JsonValue.Create(approvalCurrencyFacts.FunctionalCurrency),
                ["exchangeRate"] = JsonValue.Create(approvalCurrencyFacts.ExchangeRate),
                ["exchangeRateDate"] = JsonValue.Create(approvalCurrencyFacts.ExchangeRateDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                ["exchangeRatePurpose"] = JsonValue.Create(approvalCurrencyFacts.ExchangeRatePurpose),
                ["exchangeRateIdentity"] = JsonValue.Create(approvalCurrencyFacts.ExchangeRateIdentity),
                ["approvalThreshold"] = JsonValue.Create(readiness.ApprovalThreshold),
                ["policyPack"] = JsonValue.Create($"{draft.PolicyPackKey}@{draft.PolicyPackVersion}")
            }, null, null, [new ApprovalStepDefinition(1, ApprovalStepApproverType.Role, "finance_approver")]);
        _dbContext.ApprovalRequests.Add(approval);
        var now = UtcNow();
        draft.BindApproval(approval.Id, command.ActorUserId, now);
        _dbContext.CustomerInvoiceDraftOperations.Add(new(Guid.NewGuid(), command.CompanyId, draft.Id,
            "submit", command.IdempotencyKey, draft.ResultHash, draft.Version, approval.Id, now));
        await AuditAsync(command.CompanyId, command.ActorUserId,
            AuditEventActions.AccountingCustomerInvoiceDraftApprovalRequested, draft.Id,
            "Approval was requested for the exact saved invoice draft version and tax result.",
            command.CorrelationId, now, cancellationToken, approval.Id);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var submittedReadiness = await _readinessPolicy.EvaluateAsync(command.CompanyId, draft, cancellationToken);
        _telemetry.Record("submit");
        return new(await MapAsync(draft, cancellationToken), submittedReadiness, approval.Id, false);
    }

    public async Task<CustomerInvoiceDraftIssueResult> IssueAsync(IssueCustomerInvoiceDraftCommand command,
        CancellationToken cancellationToken)
    {
        ValidateCommand(command.CompanyId, command.ActorUserId, command.IdempotencyKey);
        if (command.SeriesId == Guid.Empty || command.FiscalPeriodId == Guid.Empty ||
            string.IsNullOrWhiteSpace(command.VoucherSeriesCode) || string.IsNullOrWhiteSpace(command.ExpectedResultHash))
            throw new CustomerInvoiceDraftException(CustomerInvoiceDraftReasonCodes.PostingBlocked,
                "The issue request must include the current invoice result, document series, and accounting period.");
        if (_postingService is null || _statutoryDocumentPolicy is null)
            throw new InvalidOperationException("Native customer invoice issuance has not been configured.");

        var requestHash = HashText(JsonSerializer.Serialize(new
        {
            command.DraftId, command.ExpectedVersion, command.ExpectedResultHash, command.SeriesId,
            command.FiscalPeriodId, command.AccountingDate, command.VoucherSeriesCode
        }, JsonOptions));
        for (var attempt = 1; attempt <= MaximumIssueAttempts; attempt++)
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            try
            {
                var operation = await FindOperationAsync(command.CompanyId, command.IdempotencyKey, cancellationToken);
                if (operation is not null)
                {
                    EnsureReplay(operation, requestHash);
                    var replay = await MapIssuedAsync(command.CompanyId, operation.DraftId, true, cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    _telemetry.Record("issue", replay: true);
                    return replay;
                }

                var draft = await DraftQuery(true).SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.DraftId, cancellationToken)
                    ?? throw NotFound();
                EnsureVersion(draft, command.ExpectedVersion);
                if (draft.Status == CustomerInvoiceDraftStatusValues.Issued)
                    throw new CustomerInvoiceDraftException(CustomerInvoiceDraftReasonCodes.AlreadyIssued,
                        "This invoice draft has already been issued.", true, draft.Version);
                if (!string.Equals(draft.ResultHash, command.ExpectedResultHash.Trim(), StringComparison.OrdinalIgnoreCase))
                    throw new CustomerInvoiceDraftException(CustomerInvoiceDraftReasonCodes.IssueHashConflict,
                        "The invoice calculation changed after it was reviewed. Reload the draft before issuing.", true, draft.Version);

                var readiness = await _readinessPolicy.EvaluateAsync(command.CompanyId, draft, cancellationToken);
                if (!readiness.IsAllowed)
                    throw new CustomerInvoiceDraftException(readiness.ReasonCode, readiness.Explanation, readiness.ReasonCode.Contains("stale", StringComparison.Ordinal));
                var profile = await _dbContext.CustomerBillingProfiles.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.CounterpartyId == draft.CustomerId, cancellationToken)
                    ?? throw new CustomerInvoiceDraftException(CustomerInvoiceDraftReasonCodes.CustomerProfileMissing,
                        "Complete the customer billing profile before invoice issue.");
                var statutory = await _dbContext.CompanyStatutoryProfiles.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId, cancellationToken)
                    ?? throw new CustomerInvoiceDraftException(CustomerInvoiceDraftReasonCodes.StatutoryProfileMissing,
                        "Complete the company statutory profile before invoice issue.");
                var configuration = await _dbContext.AccountingConfigurations
                    .Include(x => x.AccountRoles).ThenInclude(x => x.FinanceAccount)
                    .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId, cancellationToken)
                    ?? throw new CustomerInvoiceDraftException(CustomerInvoiceDraftReasonCodes.AccountingConfigurationMissing,
                        "Accounting configuration is unavailable.");
                var period = await _dbContext.FiscalPeriods.AsNoTracking().SingleOrDefaultAsync(x =>
                    x.CompanyId == command.CompanyId && x.Id == command.FiscalPeriodId, cancellationToken);
                if (period is null || period.IsClosed || period.IsReportingLocked ||
                    command.AccountingDate < DateOnly.FromDateTime(period.StartUtc) || command.AccountingDate >= DateOnly.FromDateTime(period.EndUtc))
                    throw new CustomerInvoiceDraftException(CustomerInvoiceDraftReasonCodes.AccountingPeriodUnavailable,
                        "The selected accounting period is unavailable for this posting date.");
                if (!await _dbContext.VoucherSeries.AsNoTracking().AnyAsync(x => x.CompanyId == command.CompanyId &&
                        x.Code == command.VoucherSeriesCode.Trim().ToUpperInvariant() && x.IsActive, cancellationToken))
                    throw new CustomerInvoiceDraftException(CustomerInvoiceDraftReasonCodes.PostingBlocked,
                        "The selected voucher series is unavailable.");
                var series = await _dbContext.StatutoryDocumentSeries.SingleOrDefaultAsync(x =>
                    x.CompanyId == command.CompanyId && x.Id == command.SeriesId, cancellationToken)
                    ?? throw new CustomerInvoiceDraftException(CustomerInvoiceDraftReasonCodes.SeriesUnavailable,
                        "The selected document series is unavailable.");
                if (!series.IsActive || !string.Equals(series.DocumentType, draft.DocumentType, StringComparison.OrdinalIgnoreCase) ||
                    draft.IssueDate < series.FiscalYearStart || draft.IssueDate > series.FiscalYearEnd)
                    throw new CustomerInvoiceDraftException(CustomerInvoiceDraftReasonCodes.SeriesUnavailable,
                        "The selected document series does not apply to this invoice.");
                if (!await AccountingSeriesPolicyEnforcement.IsStatutoryDocumentSeriesAllowedAsync(_dbContext,
                        command.CompanyId, series.Id, "customer_invoice", draft.DocumentType, draft.IssueDate,
                        profile.BillingCountryCode, configuration.PolicyPackKey, configuration.PolicyPackVersion,
                        cancellationToken))
                    throw new CustomerInvoiceDraftException(CustomerInvoiceDraftReasonCodes.SeriesUnavailable,
                        "The selected document series is not permitted by the active accounting series policy.");

                Guid? originalIssuedDocumentId = null;
                Guid? originalLedgerEntryId = null;
                if (draft.DocumentType == CustomerInvoiceDraftDocumentTypes.CreditNote)
                {
                    var original = await _dbContext.FinanceInvoices.AsNoTracking().SingleOrDefaultAsync(x =>
                        x.CompanyId == command.CompanyId && x.Id == draft.OriginalInvoiceId &&
                        x.CounterpartyId == draft.CustomerId && x.DocumentKind == FinanceDocumentKinds.Invoice,
                        cancellationToken) ?? throw new CustomerInvoiceDraftException(
                        CustomerInvoiceDraftReasonCodes.CustomerNotFound,
                        "The original customer invoice is unavailable for this credit note.");
                    originalIssuedDocumentId = await _dbContext.IssuedStatutoryDocuments.AsNoTracking()
                        .Where(x => x.CompanyId == command.CompanyId && x.SourceRecordId == original.Id &&
                            x.DocumentType == StatutoryDocumentTypes.CustomerInvoice)
                        .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(cancellationToken)
                        ?? throw new CustomerInvoiceDraftException(CustomerInvoiceDraftReasonCodes.UnsupportedTax,
                            "The original immutable issued invoice is unavailable for this credit note.");
                    originalLedgerEntryId = await _dbContext.CustomerInvoiceAccountingProfiles.AsNoTracking()
                        .Where(x => x.CompanyId == command.CompanyId && x.InvoiceId == original.Id && x.LedgerEntryId != null)
                        .Select(x => x.LedgerEntryId).SingleOrDefaultAsync(cancellationToken)
                        ?? throw new CustomerInvoiceDraftException(CustomerInvoiceDraftReasonCodes.PostingBlocked,
                            "The original posted invoice journal is unavailable for this credit note.");
                }
                var statutoryInput = BuildStatutoryInput(draft, profile, command.AccountingDate, originalIssuedDocumentId);
                var policy = await _statutoryDocumentPolicy.EvaluateAsync(new(command.CompanyId, statutoryInput), cancellationToken);
                if (!policy.IsAllowed)
                    throw new CustomerInvoiceDraftException(policy.Issues[0].ReasonCode, policy.Issues[0].Explanation);

                var currencyFacts = await RetainCurrencyFactsAsync(command, draft, configuration, cancellationToken);
                EnsureApprovedCurrencyFacts(draft.ApprovalRequest!, currencyFacts);

                var now = UtcNow();
                var number = series.Allocate(command.ActorUserId, now);
                var documentNumber = series.Format(number);
                var isCredit = draft.DocumentType == CustomerInvoiceDraftDocumentTypes.CreditNote;
                var invoice = new FinanceInvoice(Guid.NewGuid(), command.CompanyId, draft.CustomerId, documentNumber,
                    draft.IssueDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                    draft.DueDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                    isCredit ? -Math.Abs(draft.GrossTotal) : Math.Abs(draft.GrossTotal),
                    draft.Currency, "approved", documentKind: isCredit ? FinanceDocumentKinds.CreditNote : FinanceDocumentKinds.Invoice, createdUtc: now,
                    updatedUtc: now, authority: "native", sourceDraftId: draft.Id, sourceDraftVersion: draft.Version);
                var issued = BuildIssuedDocument(draft, profile, statutory, invoice.Id, documentNumber, series, number,
                    command.ActorUserId, now, originalIssuedDocumentId);
                var allocation = new StatutoryDocumentNumberAllocation(Guid.NewGuid(), command.CompanyId, series.Id,
                    series.FiscalYearKey, number, documentNumber, StatutoryDocumentAllocationStatuses.Issued, null,
                    $"native-customer-{(isCredit ? "credit-note" : "invoice")}:{draft.Id:N}", draft.Version, issued.Id, command.ActorUserId, now);
                _dbContext.FinanceInvoices.Add(invoice);
                _dbContext.IssuedStatutoryDocuments.Add(issued);
                _dbContext.StatutoryDocumentNumberAllocations.Add(allocation);

                var accounts = ResolveAccounts(configuration, draft);
                var accountingProfile = new CustomerInvoiceAccountingProfile(Guid.NewGuid(), command.CompanyId, invoice.Id,
                    command.FiscalPeriodId, command.VoucherSeriesCode, draft.Currency, configuration.BaseCurrency, currencyFacts.ExchangeRate,
                    draft.NetTotal, draft.TaxTotal, draft.GrossTotal, currencyFacts.NetBaseAmount, currencyFacts.TaxBaseAmount,
                    currencyFacts.GrossBaseAmount, currencyFacts.PostingRoundingAmount, accounts.Receivable.Id, accounts.Revenue.Id, "exclusive", draft.PolicyPackKey,
                    draft.PolicyPackVersion, draft.PolicyDefinitionHash, draft.OriginalInvoiceId, command.ActorUserId, now);
                accountingProfile.SetPayloadHash(draft.ResultHash);
                accountingProfile.BindCurrencyFacts(currencyFacts.ExchangeRateConversionId, currencyFacts.ExchangeRateDate,
                    currencyFacts.ExchangeRatePurpose, currencyFacts.ExchangeRateIdentity, currencyFacts.ConversionRoundingResidual,
                    currencyFacts.CurrencyProvenance, command.ActorUserId, now);
                accountingProfile.BindApproval(draft.ApprovalRequestId!.Value, command.ActorUserId, now);
                foreach (var line in draft.Lines.OrderBy(x => x.Sequence))
                    accountingProfile.Lines.Add(new CustomerInvoiceAccountingLine(Guid.NewGuid(), command.CompanyId,
                        accountingProfile.Id, line.Sequence, line.Description, line.TaxRuleKey, "exclusive", line.TaxRate,
                        line.NetAmount, line.TaxAmount, line.GrossAmount,
                        DocumentCurrencyFacts.Round(line.NetAmount * currencyFacts.ExchangeRate, configuration.RoundingPrecision, configuration.RoundingMode),
                        DocumentCurrencyFacts.Round(line.TaxAmount * currencyFacts.ExchangeRate, configuration.RoundingPrecision, configuration.RoundingMode),
                        line.TaxAmount == 0m ? null : accounts.Tax.Id, line.TaxEvidenceJson));
                _dbContext.CustomerInvoiceAccountingProfiles.Add(accountingProfile);
                await _dbContext.SaveChangesAsync(cancellationToken);

                var posting = await _postingService.PostAsync(new(BuildPostingEntry(command, draft, invoice, accounts,
                    originalLedgerEntryId, configuration, currencyFacts), command.CorrelationId), cancellationToken);
                draft.MarkIssued(invoice.Id, issued.Id, posting.Journal.Id, issued.SnapshotHash, command.ActorUserId, now);
                _dbContext.CustomerInvoiceDraftOperations.Add(new CustomerInvoiceDraftOperation(Guid.NewGuid(), command.CompanyId,
                    draft.Id, "issue", command.IdempotencyKey, requestHash, draft.Version, draft.ApprovalRequestId, now));
                await _auditWriter.WriteAsync(new AuditEventWriteRequest(command.CompanyId, AuditActorTypes.User,
                    command.ActorUserId, AuditEventActions.AccountingCustomerInvoiceDraftIssued,
                    AuditTargetTypes.CustomerInvoiceDraft, draft.Id.ToString("N"), AuditEventOutcomes.Succeeded,
                    "The approved native invoice was issued and posted as one accounting transaction.",
                    ["customer_invoice_draft", "statutory_document_series", "accounting_posting"],
                    new Dictionary<string, string?> { ["snapshotHash"] = issued.SnapshotHash,
                        ["documentNumber"] = documentNumber, ["seriesId"] = series.Id.ToString("N"),
                        ["journalId"] = posting.Journal.Id.ToString("N"), ["approvalId"] = draft.ApprovalRequestId?.ToString("N"),
                        ["documentCurrency"] = draft.Currency, ["functionalCurrency"] = configuration.BaseCurrency,
                        ["exchangeRateIdentity"] = currencyFacts.ExchangeRateIdentity,
                        ["exchangeRateConversionId"] = currencyFacts.ExchangeRateConversionId?.ToString("N") },
                    command.CorrelationId, now), cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                _telemetry.Record("issue");
                return new(invoice.Id, issued.Id, posting.Journal.Id, documentNumber, "not_queued", issued.SnapshotHash,
                    draft.NetTotal, draft.TaxTotal, draft.GrossTotal, draft.Currency, ["render", "deliver", "record_payment"], false);
            }
            catch (Exception exception) when (attempt < MaximumIssueAttempts && IsRetryableConcurrency(exception))
            {
                await transaction.RollbackAsync(cancellationToken);
                _dbContext.ChangeTracker.Clear();
                var existing = await FindOperationAsync(command.CompanyId, command.IdempotencyKey, cancellationToken);
                if (existing is not null)
                {
                    EnsureReplay(existing, requestHash);
                    var replay = await MapIssuedAsync(command.CompanyId, existing.DraftId, true, cancellationToken);
                    _telemetry.Record("issue", replay: true);
                    return replay;
                }
            }
            catch (Exception exception)
            {
                await transaction.RollbackAsync(cancellationToken);
                _dbContext.ChangeTracker.Clear();
                await AuditIssueFailureAsync(command, exception, cancellationToken);
                throw;
            }
        }
        throw new CustomerInvoiceDraftException(CustomerInvoiceDraftReasonCodes.PostingBlocked,
            "The invoice could not be issued safely after concurrent changes. Reload and try again.", true);
    }

    public async Task<CustomerInvoiceDraftDto> GetAsync(GetCustomerInvoiceDraftQuery query,
        CancellationToken cancellationToken) =>
        await MapAsync(await LoadDraftAsync(query.CompanyId, query.DraftId, cancellationToken), cancellationToken);

    public async Task<CustomerInvoiceDraftListResult> ListAsync(ListCustomerInvoiceDraftsQuery query,
        CancellationToken cancellationToken)
    {
        var skip = Math.Max(0, query.Skip);
        var take = Math.Clamp(query.Take, 1, 250);
        var source = DraftQuery(false).Where(x => x.CompanyId == query.CompanyId);
        if (!string.IsNullOrWhiteSpace(query.Status)) source = source.Where(x => x.Status == query.Status.Trim().ToLowerInvariant());
        if (query.CustomerId.HasValue) source = source.Where(x => x.CustomerId == query.CustomerId.Value);
        var total = await source.CountAsync(cancellationToken);
        var drafts = await source.OrderByDescending(x => x.UpdatedUtc).Skip(skip).Take(take).ToListAsync(cancellationToken);
        var items = new List<CustomerInvoiceDraftDto>(drafts.Count);
        foreach (var draft in drafts) items.Add(await MapAsync(draft, cancellationToken));
        return new(items, total, skip, take);
    }

    private IQueryable<CustomerInvoiceDraft> DraftQuery(bool tracking) =>
        (tracking ? _dbContext.CustomerInvoiceDrafts : _dbContext.CustomerInvoiceDrafts.AsNoTracking())
        .Include(x => x.Customer)
        .Include(x => x.Lines)
        .Include(x => x.EvidenceLinks).ThenInclude(x => x.Document)
        .Include(x => x.ApprovalRequest);

    private async Task<CustomerInvoiceDraft> LoadDraftAsync(Guid companyId, Guid draftId,
        CancellationToken cancellationToken) =>
        await DraftQuery(false).SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == draftId, cancellationToken)
        ?? throw NotFound();

    private async Task<CustomerInvoiceDraftIssueResult> MapIssuedAsync(Guid companyId, Guid draftId,
        bool isReplay, CancellationToken cancellationToken)
    {
        var draft = await _dbContext.CustomerInvoiceDrafts.AsNoTracking().SingleOrDefaultAsync(x =>
            x.CompanyId == companyId && x.Id == draftId, cancellationToken) ?? throw NotFound();
        if (!draft.IssuedInvoiceId.HasValue || !draft.IssuedStatutoryDocumentId.HasValue || !draft.IssuedLedgerEntryId.HasValue ||
            string.IsNullOrWhiteSpace(draft.IssuedSnapshotHash))
            throw new CustomerInvoiceDraftException(CustomerInvoiceDraftReasonCodes.PostingBlocked,
                "The issue replay does not contain a complete immutable invoice result.");
        var invoice = await _dbContext.FinanceInvoices.AsNoTracking().SingleOrDefaultAsync(x =>
            x.CompanyId == companyId && x.Id == draft.IssuedInvoiceId.Value, cancellationToken)
            ?? throw new CustomerInvoiceDraftException(CustomerInvoiceDraftReasonCodes.PostingBlocked,
                "The issued invoice is no longer available.");
        return new(invoice.Id, draft.IssuedStatutoryDocumentId.Value, draft.IssuedLedgerEntryId.Value,
            invoice.InvoiceNumber, "not_queued", draft.IssuedSnapshotHash, draft.NetTotal, draft.TaxTotal,
            draft.GrossTotal, draft.Currency, ["render", "deliver", "record_payment"], isReplay);
    }

    private static StatutoryDocumentInput BuildStatutoryInput(CustomerInvoiceDraft draft,
        CustomerBillingProfile customer, DateOnly accountingDate, Guid? originalIssuedDocumentId)
    {
        var statutoryType = draft.DocumentType == CustomerInvoiceDraftDocumentTypes.CreditNote
            ? StatutoryDocumentTypes.CustomerCredit : StatutoryDocumentTypes.CustomerInvoice;
        return new(statutoryType, StatutoryDocumentAuthorities.Native, draft.CustomerId,
            customer.LegalName, customer.BillingAddressLine1, customer.BillingPostalCode, customer.BillingCity,
            customer.BillingCountryCode, customer.VatIdentifier, draft.IssueDate, draft.SupplyDate, accountingDate,
            draft.DueDate, draft.Currency, $"{draft.PaymentTermKind}:{draft.PaymentTermDays}",
            draft.Notes ?? "Customer invoice", draft.NetTotal, draft.TaxTotal, draft.GrossTotal,
            draft.Lines.OrderBy(x => x.Sequence).Select(x => new StatutoryDocumentLineInput(x.Description,
                x.Quantity, x.UnitPrice, x.NetAmount, x.TaxRate, x.TaxAmount)).ToArray(),
            TaxFactsJson: JsonSerializer.Serialize(new
            {
                draft.PolicyPackKey, draft.PolicyPackVersion, draft.PolicyDefinitionHash, draft.ResultHash,
                lines = draft.Lines.OrderBy(x => x.Sequence).Select(x => new
                {
                    x.Sequence, x.TaxRuleKey, x.TaxRuleVersion, x.TaxClassification, x.TaxRate,
                    x.TaxAmount, x.VatBoxMappingsJson, x.TaxEvidenceJson
                })
            }), OriginalIssuedDocumentId: originalIssuedDocumentId,
            ApprovalIds: draft.ApprovalRequestId.HasValue ? [draft.ApprovalRequestId.Value] : [],
            SourceVersion: draft.Version);
    }

    private static IssuedStatutoryDocument BuildIssuedDocument(CustomerInvoiceDraft draft,
        CustomerBillingProfile customer, CompanyStatutoryProfile statutory, Guid invoiceId, string documentNumber,
        StatutoryDocumentSeries series, long number, Guid actorId, DateTime now,
        Guid? originalIssuedDocumentId)
    {
        var snapshot = JsonSerializer.Serialize(new
        {
            schemaVersion = "native-customer-invoice-issue-2026.1", documentNumber,
            originalInvoiceId = draft.OriginalInvoiceId,
            originalIssuedDocumentId,
            draft = new { draft.Id, draft.Version, draft.ResultHash, draft.DocumentType, draft.IssueDate,
                draft.SupplyDate, draft.DueDate, draft.Currency, draft.BuyerReference, draft.SellerReference,
                draft.Notes, draft.DeliveryIntent, draft.NetTotal, draft.DiscountTotal, draft.TaxTotal,
                draft.GrossTotal, draft.RoundingAmount },
            seller = new { statutory.LegalName, statutory.SwedishOrganisationNumber, statutory.VatRegistrationNumber,
                statutory.RegisteredAddressLine1, statutory.RegisteredAddressLine2, statutory.RegisteredPostalCode,
                statutory.RegisteredCity, statutory.RegisteredCountryCode },
            buyer = new { customer.LegalName, customer.VatIdentifier, customer.BillingAddressLine1,
                customer.BillingAddressLine2, customer.BillingPostalCode, customer.BillingCity, customer.BillingCountryCode,
                customer.InvoiceDeliveryEmail, customer.InvoiceDeliveryChannel, customer.PaymentMethod,
                customer.PaymentTermKind, customer.PaymentTermDays, customer.BuyerReference,
                customer.EInvoiceIdentifier, customer.EInvoiceIdentifierType },
            lines = draft.Lines.OrderBy(x => x.Sequence).Select(x => new { x.Sequence, x.Description, x.Quantity,
                x.Unit, x.UnitPrice, x.DiscountPercent, x.DiscountAmount, x.NetAmount, x.TaxRuleKey,
                x.TaxRuleVersion, x.TaxClassification, x.TaxRate, x.TaxAmount, x.GrossAmount,
                x.VatBoxMappingsJson, x.TaxEvidenceJson, x.DimensionFactsJson, x.SourceReference, x.OrderReference }),
            approvals = draft.ApprovalRequestId.HasValue ? new[] { draft.ApprovalRequestId.Value } : []
        }, JsonOptions);
        if (Encoding.UTF8.GetByteCount(snapshot) > 32768)
            throw new CustomerInvoiceDraftException(CustomerInvoiceDraftReasonCodes.PostingBlocked,
                "The immutable invoice snapshot exceeds the supported size.");
        var taxFacts = JsonSerializer.Serialize(draft.Lines.OrderBy(x => x.Sequence).Select(x => new
        {
            x.Sequence, x.TaxRuleKey, x.TaxRuleVersion, x.TaxClassification, x.TaxRate, x.TaxAmount,
            x.VatBoxMappingsJson, x.TaxEvidenceJson
        }), JsonOptions);
        var statutoryType = draft.DocumentType == CustomerInvoiceDraftDocumentTypes.CreditNote
            ? StatutoryDocumentTypes.CustomerCredit : StatutoryDocumentTypes.CustomerInvoice;
        var keyType = draft.DocumentType == CustomerInvoiceDraftDocumentTypes.CreditNote ? "credit-note" : "invoice";
        return new IssuedStatutoryDocument(Guid.NewGuid(), draft.CompanyId, statutoryType,
            StatutoryDocumentAuthorities.Native, documentNumber, invoiceId, draft.Version, series.Id,
            series.FiscalYearKey, number, statutory.Id, statutory.Version, draft.PolicyPackKey,
            draft.PolicyPackVersion, draft.PolicyDefinitionHash, snapshot, HashText(snapshot), taxFacts,
            JsonSerializer.Serialize(draft.ApprovalRequestId.HasValue ? new[] { draft.ApprovalRequestId.Value } : []),
            $"native-customer-{keyType}:{draft.Id:N}", originalIssuedDocumentId, actorId, now);
    }

    private static (FinanceAccount Receivable, FinanceAccount Revenue, FinanceAccount Tax) ResolveAccounts(
        AccountingConfiguration configuration, CustomerInvoiceDraft draft)
    {
        FinanceAccount Resolve(string key) => configuration.AccountRoles.FirstOrDefault(x =>
            string.Equals(x.RoleKey, key, StringComparison.OrdinalIgnoreCase))?.FinanceAccount
            ?? throw new CustomerInvoiceDraftException(CustomerInvoiceDraftReasonCodes.PostingBlocked,
                $"Accounting setup is missing the {key.Replace('_', ' ')} account.");
        return (Resolve("accounts_receivable"), Resolve("revenue"), Resolve("tax_payable"));
    }

    private async Task<ApprovalCurrencyFacts> LookupCurrencyFactsAsync(Guid companyId,
        CustomerInvoiceDraft draft, CancellationToken cancellationToken)
    {
        var configuration = await _dbContext.AccountingConfigurations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId, cancellationToken)
            ?? throw new CustomerInvoiceDraftException(CustomerInvoiceDraftReasonCodes.AccountingConfigurationMissing,
                "Accounting configuration is unavailable.");
        var purpose = ExchangeRateLookupPurposes.TransactionDate;
        if (string.Equals(draft.Currency, configuration.BaseCurrency, StringComparison.OrdinalIgnoreCase))
            return new(configuration.BaseCurrency, 1m, draft.IssueDate, purpose,
                DocumentCurrencyFacts.BaseIdentity(draft.Currency, draft.IssueDate));
        if (_exchangeRates is null)
            throw new CustomerInvoiceDraftException(CustomerInvoiceDraftReasonCodes.UnsupportedCurrency,
                "The authoritative exchange-rate service is unavailable for this document currency.");
        var lookup = await _exchangeRates.LookupAsync(new(companyId, draft.Currency, configuration.BaseCurrency,
            draft.IssueDate, purpose), cancellationToken);
        if (!lookup.IsReady || !lookup.EffectiveRate.HasValue)
            throw new CustomerInvoiceDraftException(CustomerInvoiceDraftReasonCodes.UnsupportedCurrency,
                $"The document currency cannot be issued because no authoritative historical rate is available: {lookup.Explanation}");
        return new(configuration.BaseCurrency, lookup.EffectiveRate.Value, draft.IssueDate, purpose,
            DocumentCurrencyFacts.RateIdentity(lookup));
    }

    private async Task<NativeInvoiceCurrencyFacts> RetainCurrencyFactsAsync(IssueCustomerInvoiceDraftCommand command,
        CustomerInvoiceDraft draft, AccountingConfiguration configuration, CancellationToken cancellationToken)
    {
        var approvalFacts = await LookupCurrencyFactsAsync(command.CompanyId, draft, cancellationToken);
        if (string.Equals(draft.Currency, configuration.BaseCurrency, StringComparison.OrdinalIgnoreCase))
            return BuildNativeCurrencyFacts(configuration, approvalFacts, null, 0m,
                DocumentCurrencyFacts.BaseCurrencyIdentity, draft);

        var conversion = await _exchangeRates!.ConvertAsync(new(command.CompanyId, command.ActorUserId,
            draft.GrossTotal, draft.Currency, configuration.BaseCurrency, draft.IssueDate,
            approvalFacts.ExchangeRatePurpose,
            $"native-customer-invoice:{draft.Id:N}:{draft.ResultHash}:gross", command.CorrelationId), cancellationToken);
        var identity = DocumentCurrencyFacts.RateIdentity(conversion);
        if (conversion.EffectiveRate != approvalFacts.ExchangeRate ||
            !string.Equals(identity, approvalFacts.ExchangeRateIdentity, StringComparison.OrdinalIgnoreCase))
            throw new CustomerInvoiceDraftException(CustomerInvoiceDraftReasonCodes.ApprovalStale,
                "The authoritative exchange-rate evidence changed after approval. Submit the invoice for approval again.", true, draft.Version);
        return BuildNativeCurrencyFacts(configuration, approvalFacts, conversion.Id, conversion.RoundingResidual,
            DocumentCurrencyFacts.AuthoritativeRate, draft, conversion.RoundedAmount);
    }

    private static NativeInvoiceCurrencyFacts BuildNativeCurrencyFacts(AccountingConfiguration configuration,
        ApprovalCurrencyFacts approvalFacts, Guid? conversionId, decimal conversionResidual, string provenance,
        CustomerInvoiceDraft draft, decimal? convertedGross = null)
    {
        var netBase = DocumentCurrencyFacts.Round(draft.NetTotal * approvalFacts.ExchangeRate,
            configuration.RoundingPrecision, configuration.RoundingMode);
        var taxBase = DocumentCurrencyFacts.Round(draft.TaxTotal * approvalFacts.ExchangeRate,
            configuration.RoundingPrecision, configuration.RoundingMode);
        var grossBase = convertedGross ?? DocumentCurrencyFacts.Round(draft.GrossTotal * approvalFacts.ExchangeRate,
            configuration.RoundingPrecision, configuration.RoundingMode);
        var postingRounding = DocumentCurrencyFacts.Round(grossBase - netBase - taxBase,
            configuration.RoundingPrecision, configuration.RoundingMode);
        return new(conversionId, approvalFacts.ExchangeRate, approvalFacts.ExchangeRateDate,
            approvalFacts.ExchangeRatePurpose, approvalFacts.ExchangeRateIdentity, conversionResidual, provenance,
            netBase, taxBase, grossBase, postingRounding);
    }

    private static void EnsureApprovedCurrencyFacts(ApprovalRequest approval, NativeInvoiceCurrencyFacts facts)
    {
        static string? Read(ApprovalRequest request, string key) =>
            request.ThresholdContext.TryGetValue(key, out var node) ? node?.ToString().Trim('"') : null;
        var approvedRate = decimal.TryParse(Read(approval, "exchangeRate"), NumberStyles.Number,
            CultureInfo.InvariantCulture, out var parsed) ? parsed : (decimal?)null;
        if (approvedRate != facts.ExchangeRate ||
            !string.Equals(Read(approval, "exchangeRateDate"), facts.ExchangeRateDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.Ordinal) ||
            !string.Equals(Read(approval, "exchangeRatePurpose"), facts.ExchangeRatePurpose, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Read(approval, "exchangeRateIdentity"), facts.ExchangeRateIdentity, StringComparison.OrdinalIgnoreCase))
            throw new CustomerInvoiceDraftException(CustomerInvoiceDraftReasonCodes.ApprovalStale,
                "The issued currency conversion does not match the approved exchange-rate evidence. Submit the invoice for approval again.", true);
    }

    private static ProposedAccountingEntry BuildPostingEntry(IssueCustomerInvoiceDraftCommand command,
        CustomerInvoiceDraft draft, FinanceInvoice invoice, (FinanceAccount Receivable, FinanceAccount Revenue,
        FinanceAccount Tax) accounts, Guid? originalLedgerEntryId, AccountingConfiguration configuration,
        NativeInvoiceCurrencyFacts currencyFacts)
    {
        var isCredit = draft.DocumentType == CustomerInvoiceDraftDocumentTypes.CreditNote;
        var lines = new List<ProposedAccountingLine>
        {
            new(accounts.Receivable.Id, isCredit ? 0m : currencyFacts.GrossBaseAmount,
                isCredit ? currencyFacts.GrossBaseAmount : 0m, configuration.BaseCurrency,
                $"{invoice.InvoiceNumber} · accounts receivable",
                DocumentDebitAmount: isCredit ? 0m : draft.GrossTotal,
                DocumentCreditAmount: isCredit ? draft.GrossTotal : 0m, DocumentCurrency: draft.Currency,
                ExchangeRate: currencyFacts.ExchangeRate, ExchangeRateDate: currencyFacts.ExchangeRateDate,
                ExchangeRateConversionId: currencyFacts.ExchangeRateConversionId,
                ExchangeRateIdentity: currencyFacts.ExchangeRateIdentity,
                ConversionRoundingResidual: currencyFacts.ConversionRoundingResidual)
        };
        var ordered = draft.Lines.OrderBy(x => x.Sequence).ToArray();
        foreach (var line in ordered)
        {
            var functionalNet = DocumentCurrencyFacts.Round(line.NetAmount * currencyFacts.ExchangeRate,
                configuration.RoundingPrecision, configuration.RoundingMode);
            if (line.Sequence == ordered[^1].Sequence) functionalNet += currencyFacts.PostingRoundingAmount;
            lines.Add(new(accounts.Revenue.Id, isCredit ? functionalNet : 0m,
                isCredit ? 0m : functionalNet, configuration.BaseCurrency, line.Description,
                TaxFacts: new Dictionary<string, string> { ["taxRuleKey"] = line.TaxRuleKey,
                    ["taxRuleVersion"] = line.TaxRuleVersion, ["taxClassification"] = line.TaxClassification },
                DimensionFacts: Deserialize<Dictionary<string, string>>(line.DimensionFactsJson),
                DocumentDebitAmount: isCredit ? line.NetAmount : 0m,
                DocumentCreditAmount: isCredit ? 0m : line.NetAmount, DocumentCurrency: draft.Currency,
                ExchangeRate: currencyFacts.ExchangeRate, ExchangeRateDate: currencyFacts.ExchangeRateDate,
                ExchangeRateConversionId: currencyFacts.ExchangeRateConversionId,
                ExchangeRateIdentity: currencyFacts.ExchangeRateIdentity,
                ConversionRoundingResidual: currencyFacts.ConversionRoundingResidual));
            if (line.TaxAmount > 0m)
            {
                var functionalTax = DocumentCurrencyFacts.Round(line.TaxAmount * currencyFacts.ExchangeRate,
                    configuration.RoundingPrecision, configuration.RoundingMode);
                lines.Add(new(accounts.Tax.Id, isCredit ? functionalTax : 0m,
                    isCredit ? 0m : functionalTax, configuration.BaseCurrency, "Output VAT",
                    TaxFacts: new Dictionary<string, string> { ["taxRuleKey"] = line.TaxRuleKey,
                        ["taxRuleVersion"] = line.TaxRuleVersion, ["vatBoxes"] = line.VatBoxMappingsJson },
                    DocumentDebitAmount: isCredit ? line.TaxAmount : 0m,
                    DocumentCreditAmount: isCredit ? 0m : line.TaxAmount, DocumentCurrency: draft.Currency,
                    ExchangeRate: currencyFacts.ExchangeRate, ExchangeRateDate: currencyFacts.ExchangeRateDate,
                    ExchangeRateConversionId: currencyFacts.ExchangeRateConversionId,
                    ExchangeRateIdentity: currencyFacts.ExchangeRateIdentity,
                    ConversionRoundingResidual: currencyFacts.ConversionRoundingResidual));
            }
        }
        return new(command.CompanyId, command.FiscalPeriodId, command.VoucherSeriesCode.Trim().ToUpperInvariant(),
            draft.IssueDate, command.AccountingDate, LedgerPostingTypeValues.SourceDocument,
            $"Issued customer {(isCredit ? "credit note" : "invoice")} {invoice.InvoiceNumber}", "customer_invoice_draft", draft.Id.ToString("D"),
            draft.Version.ToString(CultureInfo.InvariantCulture), command.IdempotencyKey, lines, command.ActorUserId,
            draft.ApprovalRequestId, true, new Dictionary<string, string> { ["draftResultHash"] = draft.ResultHash,
                ["invoiceNumber"] = invoice.InvoiceNumber, ["documentAuthority"] = "native",
                ["documentCurrency"] = draft.Currency, ["functionalCurrency"] = configuration.BaseCurrency,
                ["exchangeRateIdentity"] = currencyFacts.ExchangeRateIdentity,
                ["exchangeRateConversionId"] = currencyFacts.ExchangeRateConversionId?.ToString("N") ?? "identity" }, "issue",
            draft.ResultHash, draft.EvidenceLinks.Select(x => new ProposedAccountingEvidence(x.DocumentId,
                x.ContentHash, x.Title)).ToArray(),
            OriginalLedgerEntryId: originalLedgerEntryId,
            CorrectionReason: isCredit ? draft.Notes ?? "Customer invoice credit note" : null);
    }

    private async Task<CustomerInvoiceDraftDto> MapAsync(CustomerInvoiceDraft draft,
        CancellationToken cancellationToken)
    {
        if (!_dbContext.Entry(draft).Collection(x => x.Lines).IsLoaded)
            draft = await LoadDraftAsync(draft.CompanyId, draft.Id, cancellationToken);
        var lines = draft.Lines.OrderBy(x => x.Sequence).Select(x => new CustomerInvoiceDraftLineDto(
            x.Id, x.Sequence, x.Description, x.Quantity, x.Unit, x.UnitPrice, x.DiscountPercent,
            x.DiscountAmount, x.NetAmount, x.TaxRuleKey, x.TaxRuleVersion, x.TaxClassification,
            x.TaxRate, x.TaxAmount, x.GrossAmount, x.RevenueAccountRoleKey, x.TaxAccountRoleKey,
            Deserialize<List<string>>(x.VatBoxMappingsJson) ?? [],
            Deserialize<List<CustomerInvoiceDraftTaxEvidenceInput>>(x.TaxEvidenceJson) ?? [],
            Deserialize<Dictionary<string, string>>(x.DimensionFactsJson) ?? new Dictionary<string, string>(),
            x.SourceReference, x.OrderReference)).ToArray();
        var evidence = draft.EvidenceLinks.OrderBy(x => x.Title).Select(x =>
            new CustomerInvoiceDraftEvidenceDto(x.DocumentId, x.Title, x.ContentHash, x.Document.OriginalFileName)).ToArray();
        CustomerInvoiceDraftApprovalDto? approval = null;
        if (draft.ApprovalRequest is not null)
            approval = new(draft.ApprovalRequest.Id, draft.ApprovalRequest.Status.ToStorageValue(),
                draft.ApprovalRequest.DecisionSummary, draft.ApprovalDraftVersion ?? 0,
                draft.ApprovalResultHash ?? string.Empty, draft.ApprovalRequest.CreatedUtc,
                draft.ApprovalRequest.DecidedUtc, ApprovalMatches(draft.ApprovalRequest, draft));
        var status = draft.Status;
        if (status == CustomerInvoiceDraftStatusValues.AwaitingApproval && draft.ApprovalRequest is not null)
            status = draft.ApprovalRequest.Status switch
            {
                ApprovalRequestStatus.Approved => "approved",
                ApprovalRequestStatus.Rejected => "rejected",
                ApprovalRequestStatus.Cancelled or ApprovalRequestStatus.Expired => "approval_expired",
                _ => status
            };
        return new(draft.Id, draft.CompanyId, draft.CustomerId, draft.Customer.Name, status,
            draft.DocumentType, draft.IssueDate, draft.SupplyDate, draft.DueDate, draft.Currency,
            draft.PaymentTermKind, draft.PaymentTermDays, draft.BuyerReference, draft.SellerReference,
            draft.Notes, draft.DeliveryIntent, draft.SourceKind, draft.SourceReference, draft.Version,
            draft.InputHash, draft.ResultHash, draft.PolicyPackKey, draft.PolicyPackVersion,
            draft.PolicyDefinitionHash, draft.CreatedByUserId, draft.UpdatedByUserId, draft.CreatedUtc,
            draft.UpdatedUtc, draft.DiscardedUtc, new(draft.NetTotal, draft.DiscountTotal, draft.TaxTotal,
                draft.GrossTotal, draft.RoundingAmount, draft.RoundingPrecision, draft.RoundingMode),
            lines, evidence, CustomerInvoiceDraftReadinessPolicy.ParseIssues(draft.WarningsJson),
            CustomerInvoiceDraftReadinessPolicy.ParseIssues(draft.BlockersJson), approval, draft.OriginalInvoiceId);
    }

    private async Task<Material> MaterializeAsync(Guid companyId, CustomerInvoiceDraftInput input,
        CancellationToken cancellationToken)
    {
        var customer = await _dbContext.FinanceCounterparties.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == input.CustomerId && x.CounterpartyType == "customer", cancellationToken);
        if (customer is null) throw new CustomerInvoiceDraftException(CustomerInvoiceDraftReasonCodes.CustomerNotFound,
            "The selected customer could not be found.");
        if (customer.MergedIntoCounterpartyId.HasValue)
            throw new CustomerInvoiceDraftException(CustomerInvoiceDraftReasonCodes.CustomerMerged,
                "The selected customer was merged. Use the current customer record.");
        if (input.DocumentType == CustomerInvoiceDraftDocumentTypes.CreditNote)
        {
            var originalExists = input.OriginalInvoiceId.HasValue && await _dbContext.FinanceInvoices.AsNoTracking()
                .AnyAsync(x => x.CompanyId == companyId && x.Id == input.OriginalInvoiceId &&
                    x.CounterpartyId == input.CustomerId && x.DocumentKind == FinanceDocumentKinds.Invoice &&
                    x.Authority == StatutoryDocumentAuthorities.Native, cancellationToken);
            if (!originalExists)
                throw new CustomerInvoiceDraftException(CustomerInvoiceDraftReasonCodes.CustomerNotFound,
                    "The original native customer invoice is unavailable for this credit-note draft.");
        }
        var documentIds = input.EvidenceDocumentIds.Distinct().ToArray();
        var documents = await _dbContext.CompanyKnowledgeDocuments.AsNoTracking()
            .Where(x => x.CompanyId == companyId && documentIds.Contains(x.Id)).ToListAsync(cancellationToken);
        if (documents.Count != documentIds.Length)
            throw new CustomerInvoiceDraftException(CustomerInvoiceDraftReasonCodes.EvidenceNotFound,
                "One or more evidence documents could not be found.");
        var evidence = documents.Select(document => new Evidence(document.Id, document.Title,
            Metadata(document, "checksum_sha256") ?? throw new CustomerInvoiceDraftException(
                CustomerInvoiceDraftReasonCodes.InvalidEvidence,
                $"Evidence document '{document.Title}' does not have a verified content hash.")))
            .OrderBy(x => x.DocumentId).ToArray();
        var calculation = await _calculationPolicy.CalculateAsync(companyId, input, cancellationToken);
        var evidenceIdentity = string.Join('|', evidence.Select(x => $"{x.DocumentId:N}:{x.ContentHash}"));
        calculation = calculation with
        {
            InputHash = HashText($"{calculation.InputHash}|{evidenceIdentity}"),
            ResultHash = HashText($"{calculation.ResultHash}|{evidenceIdentity}")
        };
        return new(evidence, calculation);
    }

    private static void ApplyMaterial(CustomerInvoiceDraft draft, CustomerInvoiceDraftInput input,
        Material material, DateTime now)
    {
        draft.ApplyCalculation(material.Calculation.InputHash, material.Calculation.ResultHash,
            material.Calculation.PolicyPackKey, material.Calculation.PolicyPackVersion,
            material.Calculation.PolicyDefinitionHash, material.Calculation.RoundingPrecision,
            material.Calculation.RoundingMode, material.Calculation.NetTotal,
            material.Calculation.DiscountTotal, material.Calculation.TaxTotal,
            material.Calculation.GrossTotal, material.Calculation.RoundingAmount,
            JsonSerializer.Serialize(material.Calculation.Warnings, JsonOptions),
            JsonSerializer.Serialize(material.Calculation.Blockers, JsonOptions));
        var calculations = material.Calculation.Lines.ToDictionary(x => x.Sequence);
        foreach (var line in input.Lines.OrderBy(x => x.Sequence))
        {
            var calculated = calculations[line.Sequence];
            draft.Lines.Add(new CustomerInvoiceDraftLine(Guid.NewGuid(), draft.CompanyId, draft.Id,
                line.Sequence, line.Description, line.Quantity, line.Unit, line.UnitPrice,
                line.DiscountPercent, calculated.DiscountAmount, calculated.NetAmount, line.TaxRuleKey,
                calculated.TaxRuleVersion, line.TaxClassification, calculated.TaxRate,
                calculated.TaxAmount, calculated.GrossAmount, line.RevenueAccountRoleKey,
                calculated.TaxAccountRoleKey, JsonSerializer.Serialize(calculated.VatBoxMappings, JsonOptions),
                JsonSerializer.Serialize(line.TaxEvidence, JsonOptions),
                JsonSerializer.Serialize(Sorted(line.DimensionFacts), JsonOptions), line.SourceReference,
                line.OrderReference));
        }
        foreach (var evidence in material.Evidence)
            draft.EvidenceLinks.Add(new CustomerInvoiceDraftEvidenceLink(Guid.NewGuid(), draft.CompanyId,
                draft.Id, evidence.DocumentId, evidence.ContentHash, evidence.Title, now));
    }

    private static CustomerInvoiceDraftInput ToInput(CustomerInvoiceDraft draft) => new(draft.CustomerId,
        draft.DocumentType, draft.IssueDate, draft.SupplyDate, draft.DueDate, draft.Currency,
        draft.PaymentTermKind, draft.PaymentTermDays, draft.BuyerReference, draft.SellerReference,
        draft.Notes, draft.DeliveryIntent, draft.SourceKind, draft.SourceReference,
        draft.Lines.OrderBy(x => x.Sequence).Select(x => new CustomerInvoiceDraftLineInput(x.Sequence,
            x.Description, x.Quantity, x.Unit, x.UnitPrice, x.DiscountPercent, x.TaxRuleKey,
            x.TaxClassification, Deserialize<List<CustomerInvoiceDraftTaxEvidenceInput>>(x.TaxEvidenceJson) ?? [],
            Deserialize<Dictionary<string, string>>(x.DimensionFactsJson), x.RevenueAccountRoleKey,
            x.SourceReference, x.OrderReference)).ToArray(), draft.EvidenceLinks.Select(x => x.DocumentId).ToArray(),
        draft.OriginalInvoiceId);

    private async Task<CustomerInvoiceDraftOperation?> FindOperationAsync(Guid companyId, string idempotencyKey,
        CancellationToken cancellationToken) => await _dbContext.CustomerInvoiceDraftOperations.AsNoTracking()
        .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.IdempotencyKey == idempotencyKey.Trim(), cancellationToken);
    private static void EnsureReplay(CustomerInvoiceDraftOperation operation, string payloadHash)
    {
        if (!string.Equals(operation.PayloadHash, payloadHash, StringComparison.OrdinalIgnoreCase))
            throw new CustomerInvoiceDraftException(CustomerInvoiceDraftReasonCodes.IdempotencyConflict,
                "This request identity was already used with different invoice draft content.", true);
    }
    private static void EnsureVersion(CustomerInvoiceDraft draft, long expectedVersion)
    {
        if (draft.Version != expectedVersion)
            throw new CustomerInvoiceDraftException(CustomerInvoiceDraftReasonCodes.VersionConflict,
                $"This invoice draft is now version {draft.Version}. Reload it before continuing.", true, draft.Version);
    }
    private static void EnsureEditable(CustomerInvoiceDraft draft)
    {
        if (draft.Status == CustomerInvoiceDraftStatusValues.Discarded)
            throw new CustomerInvoiceDraftException(CustomerInvoiceDraftReasonCodes.NotEditable,
                "A discarded customer invoice draft cannot be changed.");
    }
    private static bool ApprovalMatches(ApprovalRequest approval, CustomerInvoiceDraft draft) =>
        draft.ApprovalDraftVersion == draft.Version && draft.ApprovalResultHash == draft.ResultHash &&
        approval.ThresholdContext.TryGetValue("sourceVersion", out var versionNode) &&
        long.TryParse(versionNode?.ToString().Trim('"'), CultureInfo.InvariantCulture, out var version) && version == draft.Version &&
        approval.ThresholdContext.TryGetValue("resultHash", out var hashNode) &&
        string.Equals(hashNode?.ToString().Trim('"'), draft.ResultHash, StringComparison.OrdinalIgnoreCase) &&
        approval.ThresholdContext.TryGetValue("exchangeRateIdentity", out var rateIdentityNode) &&
        !string.IsNullOrWhiteSpace(rateIdentityNode?.ToString().Trim('"'));

    private async Task AuditAsync(Guid companyId, Guid actorId, string action, Guid draftId, string summary,
        string? correlationId, DateTime occurredUtc, CancellationToken cancellationToken, Guid? approvalId = null) =>
        await _auditWriter.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.User, actorId,
            action, AuditTargetTypes.CustomerInvoiceDraft, draftId.ToString("N"), AuditEventOutcomes.Succeeded,
            summary, ["customer_invoice_draft"], new Dictionary<string, string?>
            {
                ["approvalRequestId"] = approvalId?.ToString("N")
            }, correlationId, occurredUtc), cancellationToken);

    private async Task AuditIssueFailureAsync(IssueCustomerInvoiceDraftCommand command, Exception exception,
        CancellationToken cancellationToken)
    {
        var reasonCode = exception is CustomerInvoiceDraftException draftException
            ? draftException.ReasonCode
            : CustomerInvoiceDraftReasonCodes.PostingBlocked;
        await _auditWriter.WriteAsync(new AuditEventWriteRequest(command.CompanyId, AuditActorTypes.User,
            command.ActorUserId, AuditEventActions.AccountingCustomerInvoiceDraftIssued,
            AuditTargetTypes.CustomerInvoiceDraft, command.DraftId.ToString("N"), AuditEventOutcomes.Failed,
            "Native invoice issue did not complete; no invoice or journal was committed.",
            ["customer_invoice_draft", "accounting_posting"], new Dictionary<string, string?>
            {
                ["reasonCode"] = reasonCode,
                ["expectedDraftVersion"] = command.ExpectedVersion.ToString(CultureInfo.InvariantCulture),
                ["seriesId"] = command.SeriesId == Guid.Empty ? null : command.SeriesId.ToString("N")
            }, command.CorrelationId, UtcNow()), cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static bool IsRetryableConcurrency(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbUpdateConcurrencyException) return true;
            if (current is SqlException sql && sql.Number is 1205 or 1222 or 2601 or 2627) return true;
        }
        return exception is DbUpdateException;
    }

    private static void ValidateCommand(Guid companyId, Guid actorId, string key)
    {
        if (companyId == Guid.Empty || actorId == Guid.Empty) throw NotFound();
        if (string.IsNullOrWhiteSpace(key) || key.Trim().Length > 200)
            throw new CustomerInvoiceDraftException(CustomerInvoiceDraftReasonCodes.IdempotencyConflict,
                "A stable request identity is required.");
    }
    private static void ValidateInput(CustomerInvoiceDraftInput input)
    {
        if (input.CustomerId == Guid.Empty) throw new CustomerInvoiceDraftException(CustomerInvoiceDraftReasonCodes.CustomerNotFound, "Select a customer.");
        if (input.Lines is null) throw new CustomerInvoiceDraftException(CustomerInvoiceDraftReasonCodes.LinesRequired, "Invoice lines are required.");
        if (input.EvidenceDocumentIds is null) throw new CustomerInvoiceDraftException(CustomerInvoiceDraftReasonCodes.InvalidEvidence, "Evidence document references are required.");
        foreach (var line in input.Lines)
        {
            if (line.TaxEvidence is null) throw new CustomerInvoiceDraftException(CustomerInvoiceDraftReasonCodes.UnsupportedTax, "Each invoice line requires an explicit tax evidence list.");
            if (line.DimensionFacts?.Any(x => string.IsNullOrWhiteSpace(x.Key) || x.Key.Length > 100 || x.Value.Length > 500) == true)
                throw new CustomerInvoiceDraftException(CustomerInvoiceDraftReasonCodes.CalculationBlocked, "Invoice line dimension facts are invalid.");
        }
    }
    private static string HashInput(CustomerInvoiceDraftInput input) => HashText(JsonSerializer.Serialize(new
    {
        input.CustomerId, input.DocumentType, input.IssueDate, input.SupplyDate, input.DueDate, input.Currency,
        input.PaymentTermKind, input.PaymentTermDays, input.BuyerReference, input.SellerReference, input.Notes,
        input.DeliveryIntent, input.SourceKind, input.SourceReference,
        Lines = input.Lines.OrderBy(x => x.Sequence).Select(x => new { x.Sequence, x.Description, Quantity = Number(x.Quantity),
            x.Unit, UnitPrice = Number(x.UnitPrice), DiscountPercent = Number(x.DiscountPercent), x.TaxRuleKey, x.TaxClassification,
            Evidence = x.TaxEvidence.OrderBy(y => y.Classification).ThenBy(y => y.SourceReference),
            Dimensions = Sorted(x.DimensionFacts), x.RevenueAccountRoleKey, x.SourceReference, x.OrderReference }),
        EvidenceDocuments = input.EvidenceDocumentIds.OrderBy(x => x)
    }, JsonOptions));
    private static SortedDictionary<string, string> Sorted(IReadOnlyDictionary<string, string>? facts) =>
        new((facts ?? new Dictionary<string, string>()).ToDictionary(x => x.Key, x => x.Value), StringComparer.Ordinal);
    private static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, JsonOptions);
    private static string? Metadata(CompanyKnowledgeDocument document, string key) =>
        document.Metadata.TryGetValue(key, out var node) ? node?.ToString().Trim('"') : null;
    private static string HashText(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string Number(decimal value) => value.ToString("G29", CultureInfo.InvariantCulture);
    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
    private static CustomerInvoiceDraftException NotFound() => new(CustomerInvoiceDraftReasonCodes.NotFound,
        "The customer invoice draft could not be found.");
    private sealed record Evidence(Guid DocumentId, string Title, string ContentHash);
    private sealed record Material(IReadOnlyList<Evidence> Evidence, CustomerInvoiceDraftCalculation Calculation);
    private sealed record ApprovalCurrencyFacts(string FunctionalCurrency, decimal ExchangeRate,
        DateOnly ExchangeRateDate, string ExchangeRatePurpose, string ExchangeRateIdentity);
    private sealed record NativeInvoiceCurrencyFacts(Guid? ExchangeRateConversionId, decimal ExchangeRate,
        DateOnly ExchangeRateDate, string ExchangeRatePurpose, string ExchangeRateIdentity,
        decimal ConversionRoundingResidual, string CurrencyProvenance, decimal NetBaseAmount,
        decimal TaxBaseAmount, decimal GrossBaseAmount, decimal PostingRoundingAmount);
}
