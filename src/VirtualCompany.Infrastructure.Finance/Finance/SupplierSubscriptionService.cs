using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class SupplierSubscriptionService : ISupplierSubscriptionService
{
    private const int AutomaticThreshold = 85;
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IAuditEventWriter _audit;
    private readonly ILogger<SupplierSubscriptionService> _logger;

    public SupplierSubscriptionService(
        VirtualCompanyDbContext dbContext,
        IAuditEventWriter audit,
        ILogger<SupplierSubscriptionService> logger)
    {
        _dbContext = dbContext;
        _audit = audit;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SupplierSubscriptionSummaryDto>> GetAsync(GetSupplierSubscriptionsQuery query, CancellationToken cancellationToken)
    {
        var subscriptions = _dbContext.SupplierSubscriptions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId)
            .Include(x => x.Counterparty)
            .Include(x => x.BillMatches)
                .ThenInclude(x => x.Bill)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            var status = NormalizeToken(query.Status);
            subscriptions = subscriptions.Where(x => x.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            subscriptions = subscriptions.Where(x => x.Name.Contains(search) || x.Counterparty.Name.Contains(search));
        }

        var rows = await subscriptions
            .OrderBy(x => x.NextExpectedBillDateUtc)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return rows.Select(MapSummary).ToList();
    }

    public async Task<SupplierSubscriptionDetailDto?> GetAsync(GetSupplierSubscriptionQuery query, CancellationToken cancellationToken)
    {
        var subscription = await LoadSubscriptionForReadAsync(query.CompanyId, query.SubscriptionId, cancellationToken);
        if (subscription is null)
        {
            return null;
        }

        var sourceEvidence = await LoadSourceEvidenceAsync(query.CompanyId, query.SubscriptionId, cancellationToken);
        return MapDetail(subscription, sourceEvidence);
    }

    public async Task<SupplierBillSubscriptionContextDto> GetBillContextAsync(GetSupplierBillSubscriptionContextQuery query, CancellationToken cancellationToken)
    {
        var bill = await LoadBillForReadAsync(query.CompanyId, query.BillId, cancellationToken);
        if (bill is null)
        {
            return new SupplierBillSubscriptionContextDto(query.BillId, false, null, null, [], "not_found", "The supplier bill was not found for this company.");
        }

        return await BuildBillContextAsync(bill, cancellationToken);
    }

    public async Task<SupplierSubscriptionDetailDto> CreateAsync(CreateSupplierSubscriptionCommand command, CancellationToken cancellationToken)
    {
        await EnsureCounterpartyAsync(command.CompanyId, command.CounterpartyId, cancellationToken);
        await EnsureContractDocumentAsync(command.CompanyId, command.ContractDocumentId, cancellationToken);

        var subscription = new SupplierSubscription(
            Guid.NewGuid(),
            command.CompanyId,
            command.CounterpartyId,
            command.Name,
            command.Currency,
            command.ExpectedAmount,
            command.Cadence,
            command.BillingDay,
            command.StartDateUtc,
            command.NextExpectedBillDateUtc,
            command.AmountTolerance,
            command.DateToleranceDays,
            command.EndDateUtc,
            command.ContractReference,
            command.Description,
            command.NoticePeriodDays,
            command.AutoRenews,
            command.ContractDocumentId,
            SupplierSubscriptionStatuses.Draft);

        _dbContext.SupplierSubscriptions.Add(subscription);
        await WriteAuditAsync(command.CompanyId, command.ActorUserId, "supplier_subscription.created", subscription.Id, AuditEventOutcomes.Succeeded, "Supplier subscription agreement was created.", command.ActorDisplayName, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return (await GetAsync(new GetSupplierSubscriptionQuery(command.CompanyId, subscription.Id), cancellationToken))!;
    }

    public async Task<SupplierSubscriptionDetailDto> UpdateAsync(UpdateSupplierSubscriptionCommand command, CancellationToken cancellationToken)
    {
        var subscription = await LoadSubscriptionForWriteAsync(command.CompanyId, command.SubscriptionId, cancellationToken);
        await EnsureCounterpartyAsync(command.CompanyId, command.CounterpartyId, cancellationToken);
        await EnsureContractDocumentAsync(command.CompanyId, command.ContractDocumentId, cancellationToken);

        subscription.UpdateTerms(
            command.Name,
            command.Currency,
            command.ExpectedAmount,
            command.Cadence,
            command.BillingDay,
            command.StartDateUtc,
            command.NextExpectedBillDateUtc,
            command.AmountTolerance,
            command.DateToleranceDays,
            command.EndDateUtc,
            command.ContractReference,
            command.Description,
            command.NoticePeriodDays,
            command.AutoRenews,
            command.ContractDocumentId);

        await WriteAuditAsync(command.CompanyId, command.ActorUserId, "supplier_subscription.updated", subscription.Id, AuditEventOutcomes.Succeeded, "Supplier subscription terms were updated.", command.ActorDisplayName, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return (await GetAsync(new GetSupplierSubscriptionQuery(command.CompanyId, subscription.Id), cancellationToken))!;
    }

    public async Task<SupplierSubscriptionDetailDto> ChangeStatusAsync(ChangeSupplierSubscriptionStatusCommand command, CancellationToken cancellationToken)
    {
        var subscription = await LoadSubscriptionForWriteAsync(command.CompanyId, command.SubscriptionId, cancellationToken);
        var action = NormalizeToken(command.Action);
        switch (action)
        {
            case "activate": subscription.Activate(); break;
            case "pause": subscription.Pause(); break;
            case "resume": subscription.Resume(); break;
            case "cancel": subscription.Cancel(); break;
            default: throw new ArgumentOutOfRangeException(nameof(command.Action), command.Action, "Unsupported supplier subscription lifecycle action.");
        }

        var lifecycleState = FormatLifecycle(action);
        await WriteAuditAsync(command.CompanyId, command.ActorUserId, $"supplier_subscription.{lifecycleState}", subscription.Id, AuditEventOutcomes.Succeeded, $"Supplier subscription was {lifecycleState}.", command.ActorDisplayName, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return (await GetAsync(new GetSupplierSubscriptionQuery(command.CompanyId, subscription.Id), cancellationToken))!;
    }

    public async Task<SupplierBillSubscriptionContextDto> EvaluateBillAsync(EvaluateSupplierSubscriptionBillCommand command, CancellationToken cancellationToken)
    {
        var bill = await LoadBillForWriteAsync(command.CompanyId, command.BillId, cancellationToken);
        var existing = await _dbContext.SupplierSubscriptionBillMatches
            .IgnoreQueryFilters()
            .Include(x => x.Subscription).ThenInclude(x => x.Counterparty)
            .Include(x => x.Bill)
            .Where(x => x.CompanyId == command.CompanyId && x.BillId == command.BillId && x.Status != SupplierSubscriptionMatchStatuses.Rejected)
            .ToListAsync(cancellationToken);

        if (existing.Count > 0)
        {
            _logger.LogInformation("Subscription bill evaluation reused {MatchCount} existing match records for company {CompanyId} and bill {BillId}.", existing.Count, command.CompanyId, command.BillId);
            return await BuildBillContextAsync(bill, cancellationToken);
        }

        if (!IsMatchableBill(bill))
        {
            return new SupplierBillSubscriptionContextDto(bill.Id, false, null, null, [], "not_matchable", "This bill is not eligible for subscription matching.");
        }

        var subscriptions = await _dbContext.SupplierSubscriptions
            .IgnoreQueryFilters()
            .Include(x => x.Counterparty)
            .Where(x => x.CompanyId == command.CompanyId &&
                        x.CounterpartyId == bill.CounterpartyId &&
                        x.Status == SupplierSubscriptionStatuses.Active &&
                        x.Currency == bill.Currency)
            .ToListAsync(cancellationToken);

        var candidates = subscriptions
            .Select(subscription => Score(subscription, bill))
            .Where(candidate => candidate.IsUseful)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Subscription.NextExpectedBillDateUtc)
            .ToList();

        if (candidates.Count == 1 && candidates[0].CanAutoConfirm)
        {
            var candidate = candidates[0];
            var match = CreateMatch(candidate, bill, SupplierSubscriptionMatchStatuses.Confirmed, SupplierSubscriptionMatchMethods.Automatic);
            match.Confirm(command.ActorUserId, SupplierSubscriptionMatchMethods.Automatic);
            _dbContext.SupplierSubscriptionBillMatches.Add(match);
            candidate.Subscription.AdvanceAfterConfirmedBill(candidate.Subscription.NextExpectedBillDateUtc);
            await WriteAuditAsync(command.CompanyId, command.ActorUserId, "supplier_subscription.match.automatic_confirmed", candidate.Subscription.Id, AuditEventOutcomes.Succeeded, match.EvidenceSummary, command.ActorDisplayName, cancellationToken, bill.Id, match.Id);
        }
        else if (candidates.Count > 0)
        {
            foreach (var candidate in candidates)
            {
                var status = candidate.CanAutoConfirm ? SupplierSubscriptionMatchStatuses.Suggested : SupplierSubscriptionMatchStatuses.Exception;
                var match = CreateMatch(candidate, bill, status, SupplierSubscriptionMatchMethods.Automatic);
                _dbContext.SupplierSubscriptionBillMatches.Add(match);
                await WriteAuditAsync(command.CompanyId, command.ActorUserId, $"supplier_subscription.match.{status}", candidate.Subscription.Id, AuditEventOutcomes.Pending, match.EvidenceSummary, command.ActorDisplayName, cancellationToken, bill.Id, match.Id);
            }
        }

        _logger.LogInformation("Subscription bill evaluation completed for company {CompanyId}, bill {BillId}. Candidate count: {CandidateCount}.", command.CompanyId, command.BillId, candidates.Count);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return await BuildBillContextAsync(bill, cancellationToken);
    }

    public async Task<SupplierBillSubscriptionContextDto> DecideMatchAsync(DecideSupplierSubscriptionMatchCommand command, CancellationToken cancellationToken)
    {
        var match = await _dbContext.SupplierSubscriptionBillMatches
            .IgnoreQueryFilters()
            .Include(x => x.Subscription).ThenInclude(x => x.Counterparty)
            .Include(x => x.Bill)
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.MatchId, cancellationToken)
            ?? throw new InvalidOperationException("The subscription match was not found for this company.");

        if (command.Confirm)
        {
            if (match.Status != SupplierSubscriptionMatchStatuses.Confirmed)
            {
                var competing = await _dbContext.SupplierSubscriptionBillMatches
                    .IgnoreQueryFilters()
                    .Where(x => x.CompanyId == command.CompanyId && x.BillId == match.BillId && x.Id != match.Id && x.Status != SupplierSubscriptionMatchStatuses.Rejected)
                    .ToListAsync(cancellationToken);
                foreach (var other in competing)
                {
                    other.Reject(command.ActorUserId);
                }

                match.Confirm(command.ActorUserId, SupplierSubscriptionMatchMethods.Manual);
                match.Subscription.AdvanceAfterConfirmedBill(match.ExpectedBillDateUtc);
            }

            await WriteAuditAsync(command.CompanyId, command.ActorUserId, "supplier_subscription.match.manually_confirmed", match.SubscriptionId, AuditEventOutcomes.Succeeded, match.EvidenceSummary, command.ActorDisplayName, cancellationToken, match.BillId, match.Id);
        }
        else
        {
            match.Reject(command.ActorUserId);
            await WriteAuditAsync(command.CompanyId, command.ActorUserId, "supplier_subscription.match.rejected", match.SubscriptionId, AuditEventOutcomes.Rejected, "Suggested subscription match was rejected.", command.ActorDisplayName, cancellationToken, match.BillId, match.Id);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return await BuildBillContextAsync(match.Bill, cancellationToken);
    }
    public async Task<SupplierBillSubscriptionContextDto> LinkReceiptEvidenceAsync(LinkSupplierSubscriptionReceiptEvidenceCommand command, CancellationToken cancellationToken)
    {
        var subscription = await _dbContext.SupplierSubscriptions
            .IgnoreQueryFilters()
            .Include(x => x.Counterparty)
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.SubscriptionId, cancellationToken)
            ?? throw new InvalidOperationException("The supplier subscription was not found for this company.");

        var bill = await LoadBillForWriteAsync(command.CompanyId, command.BillId, cancellationToken);
        if (bill.CounterpartyId != subscription.CounterpartyId)
        {
            throw new InvalidOperationException("The receipt evidence belongs to a different supplier than this subscription.");
        }

        if (!string.Equals(bill.Currency, subscription.Currency, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The receipt evidence uses a different currency than this subscription.");
        }

        if (!IsMatchableBill(bill))
        {
            throw new InvalidOperationException("The receipt evidence bill is not eligible for subscription matching.");
        }

        var existing = await _dbContext.SupplierSubscriptionBillMatches
            .IgnoreQueryFilters()
            .Include(x => x.Bill)
            .Where(x => x.CompanyId == command.CompanyId && x.SubscriptionId == command.SubscriptionId && x.BillId == command.BillId && x.Status != SupplierSubscriptionMatchStatuses.Rejected)
            .OrderByDescending(x => x.Status == SupplierSubscriptionMatchStatuses.Confirmed)
            .ThenByDescending(x => x.UpdatedUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            return await BuildBillContextAsync(existing.Bill, cancellationToken);
        }

        var expectedDate = subscription.NextExpectedBillDateUtc;
        var periodStart = ResolvePeriodStart(subscription, expectedDate);
        var evidence = string.IsNullOrWhiteSpace(command.EvidenceSummary)
            ? $"Receipt evidence from supplier bill {bill.BillNumber}. Expected {subscription.ExpectedAmount:0.00} {subscription.Currency}, actual {bill.Amount:0.00} {bill.Currency}."
            : command.EvidenceSummary.Trim();
        if (evidence.Length > 600)
        {
            evidence = evidence[..600];
        }

        var match = new SupplierSubscriptionBillMatch(
            Guid.NewGuid(),
            command.CompanyId,
            subscription.Id,
            bill.Id,
            periodStart,
            expectedDate,
            expectedDate,
            subscription.ExpectedAmount,
            bill.Amount,
            SupplierSubscriptionMatchStatuses.Suggested,
            SupplierSubscriptionMatchMethods.ReceiptEvidence,
            70,
            evidence);
        _dbContext.SupplierSubscriptionBillMatches.Add(match);
        await WriteAuditAsync(command.CompanyId, command.ActorUserId, "supplier_subscription.receipt_evidence.linked", subscription.Id, AuditEventOutcomes.Pending, evidence, command.ActorDisplayName, cancellationToken, bill.Id, match.Id);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return await BuildBillContextAsync(bill, cancellationToken);
    }

    private async Task<SupplierSubscription> LoadSubscriptionForWriteAsync(Guid companyId, Guid subscriptionId, CancellationToken cancellationToken) =>
        await _dbContext.SupplierSubscriptions
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == subscriptionId, cancellationToken)
        ?? throw new InvalidOperationException("The supplier subscription was not found for this company.");

    private async Task<SupplierSubscription?> LoadSubscriptionForReadAsync(Guid companyId, Guid subscriptionId, CancellationToken cancellationToken) =>
        await _dbContext.SupplierSubscriptions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(x => x.Counterparty)
            .Include(x => x.BillMatches.OrderByDescending(match => match.ExpectedBillDateUtc))
                .ThenInclude(x => x.Bill)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == subscriptionId, cancellationToken);

    private async Task<SupplierSubscriptionSourceEvidenceDto?> LoadSourceEvidenceAsync(Guid companyId, Guid subscriptionId, CancellationToken cancellationToken) =>
        await _dbContext.SupplierSubscriptionIntakeProposals
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.AcceptedSubscriptionId == subscriptionId)
            .OrderByDescending(x => x.DecidedUtc ?? x.UpdatedUtc)
            .Select(x => new SupplierSubscriptionSourceEvidenceDto(
                x.Id,
                x.Status,
                x.SourceEmailMessageSnapshot.Subject,
                x.SourceEmailAttachmentSnapshot != null ? x.SourceEmailAttachmentSnapshot.FileName : null,
                x.EvidenceSummary,
                x.DecisionReason,
                x.DecidedByUserId,
                x.DecidedUtc,
                x.CreatedUtc))
            .FirstOrDefaultAsync(cancellationToken);
    private async Task<FinanceBill> LoadBillForWriteAsync(Guid companyId, Guid billId, CancellationToken cancellationToken) =>
        await _dbContext.FinanceBills
            .IgnoreQueryFilters()
            .Include(x => x.Counterparty)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == billId, cancellationToken)
        ?? throw new InvalidOperationException("The supplier bill was not found for this company.");

    private async Task<FinanceBill?> LoadBillForReadAsync(Guid companyId, Guid billId, CancellationToken cancellationToken) =>
        await _dbContext.FinanceBills
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(x => x.Counterparty)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == billId, cancellationToken);

    private async Task EnsureCounterpartyAsync(Guid companyId, Guid counterpartyId, CancellationToken cancellationToken)
    {
        var exists = await _dbContext.FinanceCounterparties
            .IgnoreQueryFilters()
            .AnyAsync(x => x.CompanyId == companyId && x.Id == counterpartyId && x.CounterpartyType == "supplier", cancellationToken);
        if (!exists)
        {
            throw new InvalidOperationException("The supplier was not found for this company.");
        }
    }

    private async Task EnsureContractDocumentAsync(Guid companyId, Guid? documentId, CancellationToken cancellationToken)
    {
        if (!documentId.HasValue)
        {
            return;
        }

        var exists = await _dbContext.CompanyKnowledgeDocuments
            .IgnoreQueryFilters()
            .AnyAsync(x => x.CompanyId == companyId && x.Id == documentId.Value, cancellationToken);
        if (!exists)
        {
            throw new InvalidOperationException("The contract document was not found for this company.");
        }
    }

    private async Task<SupplierBillSubscriptionContextDto> BuildBillContextAsync(FinanceBill bill, CancellationToken cancellationToken)
    {
        var matches = await _dbContext.SupplierSubscriptionBillMatches
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(x => x.Subscription).ThenInclude(x => x.Counterparty)
            .Include(x => x.Bill)
            .Where(x => x.CompanyId == bill.CompanyId && x.BillId == bill.Id && x.Status != SupplierSubscriptionMatchStatuses.Rejected)
            .OrderByDescending(x => x.Status == SupplierSubscriptionMatchStatuses.Confirmed)
            .ThenByDescending(x => x.ConfidenceScore)
            .ToListAsync(cancellationToken);

        var confirmed = matches.FirstOrDefault(x => x.Status == SupplierSubscriptionMatchStatuses.Confirmed);
        if (confirmed is not null)
        {
            return new SupplierBillSubscriptionContextDto(bill.Id, true, MapSummary(confirmed.Subscription), MapMatch(confirmed), [], "confirmed", "This bill is linked to a supplier subscription.");
        }

        var suggestions = matches.Select(MapMatch).ToList();
        return suggestions.Count == 0
            ? new SupplierBillSubscriptionContextDto(bill.Id, false, null, null, [], "none", "No supplier subscription context was found for this bill.")
            : new SupplierBillSubscriptionContextDto(bill.Id, true, MapSummary(matches[0].Subscription), null, suggestions, "needs_review", "This bill has subscription match suggestions that need review.");
    }

    private static bool IsMatchableBill(FinanceBill bill) =>
        !string.Equals(bill.PostingStatus, FinanceDocumentPostingStatuses.Cancelled, StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(bill.DocumentKind, FinanceDocumentKinds.SupplierCreditNote, StringComparison.OrdinalIgnoreCase) &&
        bill.Amount > 0m;

    private static MatchCandidate Score(SupplierSubscription subscription, FinanceBill bill)
    {
        var amountVariance = Math.Abs(decimal.Round(bill.Amount - subscription.ExpectedAmount, 2, MidpointRounding.AwayFromZero));
        var amountWithin = amountVariance <= subscription.AmountTolerance;
        var dateDelta = Math.Abs((bill.ReceivedUtc.Date - subscription.NextExpectedBillDateUtc.Date).Days);
        var dateWithin = dateDelta <= subscription.DateToleranceDays;
        var score = 60 + (amountWithin ? 20 : 0) + (dateWithin ? 20 : 0);
        var evidence = $"Supplier and currency match. Expected {subscription.ExpectedAmount:0.00} {subscription.Currency}, actual {bill.Amount:0.00} {bill.Currency}, expected bill date {subscription.NextExpectedBillDateUtc:yyyy-MM-dd}.";
        var useful = amountWithin || dateDelta <= Math.Max(subscription.DateToleranceDays * 2, 14);
        return new MatchCandidate(subscription, Math.Min(score, 100), amountWithin && dateWithin && score >= AutomaticThreshold, useful, evidence);
    }

    private static SupplierSubscriptionBillMatch CreateMatch(MatchCandidate candidate, FinanceBill bill, string status, string method)
    {
        var periodEnd = candidate.Subscription.NextExpectedBillDateUtc;
        var periodStart = ResolvePeriodStart(candidate.Subscription, periodEnd);
        return new SupplierSubscriptionBillMatch(
            Guid.NewGuid(),
            candidate.Subscription.CompanyId,
            candidate.Subscription.Id,
            bill.Id,
            periodStart,
            periodEnd,
            candidate.Subscription.NextExpectedBillDateUtc,
            candidate.Subscription.ExpectedAmount,
            bill.Amount,
            status,
            method,
            candidate.Score,
            candidate.Evidence);
    }

    private static DateTime ResolvePeriodStart(SupplierSubscription subscription, DateTime periodEnd)
    {
        var months = SupplierSubscriptionCadences.Months(subscription.Cadence);
        var anchor = new DateTime(periodEnd.Year, periodEnd.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-months);
        var day = Math.Min(subscription.BillingDay, DateTime.DaysInMonth(anchor.Year, anchor.Month));
        var candidate = new DateTime(anchor.Year, anchor.Month, day, 0, 0, 0, DateTimeKind.Utc);
        return candidate < subscription.StartDateUtc ? subscription.StartDateUtc : candidate;
    }

    private static SupplierSubscriptionSummaryDto MapSummary(SupplierSubscription subscription)
    {
        var activeMatches = subscription.BillMatches?.Where(x => x.Status != SupplierSubscriptionMatchStatuses.Rejected).ToList() ?? [];
        var confirmed = activeMatches.Where(x => x.Status == SupplierSubscriptionMatchStatuses.Confirmed).OrderByDescending(x => x.ExpectedBillDateUtc).FirstOrDefault();
        var reviewCount = activeMatches.Count(x => x.Status is SupplierSubscriptionMatchStatuses.Suggested or SupplierSubscriptionMatchStatuses.Exception);
        var (health, message) = ResolveHealth(subscription, reviewCount);
        return new SupplierSubscriptionSummaryDto(
            subscription.Id,
            subscription.CounterpartyId,
            subscription.Counterparty?.Name ?? "Unknown supplier",
            subscription.Name,
            subscription.Currency,
            subscription.ExpectedAmount,
            subscription.Cadence,
            subscription.Status,
            health,
            message,
            subscription.NextExpectedBillDateUtc,
            subscription.EndDateUtc,
            confirmed?.Bill?.ReceivedUtc,
            activeMatches.Count(x => x.Status == SupplierSubscriptionMatchStatuses.Confirmed),
            reviewCount);
    }

    private static SupplierSubscriptionDetailDto MapDetail(SupplierSubscription subscription, SupplierSubscriptionSourceEvidenceDto? sourceEvidence = null) =>
        new(
            subscription.Id,
            subscription.CounterpartyId,
            subscription.Counterparty?.Name ?? "Unknown supplier",
            subscription.Name,
            subscription.ContractReference,
            subscription.Description,
            subscription.Currency,
            subscription.ExpectedAmount,
            subscription.AmountTolerance,
            subscription.Cadence,
            subscription.BillingDay,
            subscription.StartDateUtc,
            subscription.EndDateUtc,
            subscription.NextExpectedBillDateUtc,
            subscription.DateToleranceDays,
            subscription.NoticePeriodDays,
            subscription.AutoRenews,
            subscription.Status,
            ResolveHealth(subscription, subscription.BillMatches.Count(x => x.Status is SupplierSubscriptionMatchStatuses.Suggested or SupplierSubscriptionMatchStatuses.Exception)).Health,
            ResolveHealth(subscription, subscription.BillMatches.Count(x => x.Status is SupplierSubscriptionMatchStatuses.Suggested or SupplierSubscriptionMatchStatuses.Exception)).Message,
            subscription.ContractDocumentId,
            subscription.CreatedUtc,
            subscription.UpdatedUtc,
            sourceEvidence,
            subscription.BillMatches.OrderByDescending(x => x.ExpectedBillDateUtc).Select(MapMatch).ToList());

    private static SupplierSubscriptionMatchDto MapMatch(SupplierSubscriptionBillMatch match) =>
        new(
            match.Id,
            match.SubscriptionId,
            match.BillId,
            match.Bill?.BillNumber ?? string.Empty,
            match.PeriodStartUtc,
            match.PeriodEndUtc,
            match.ExpectedBillDateUtc,
            match.ExpectedAmount,
            match.ActualAmount,
            match.AmountVariance,
            match.Bill?.Currency ?? match.Subscription.Currency,
            match.Status,
            match.MatchMethod,
            match.ConfidenceScore,
            match.EvidenceSummary,
            match.DecidedByUserId,
            match.DecidedUtc,
            match.CreatedUtc);

    private static (string Health, string Message) ResolveHealth(SupplierSubscription subscription, int reviewCount)
    {
        if (reviewCount > 0)
        {
            return ("needs_review", "Review suggested subscription bill matches.");
        }

        if (subscription.Status != SupplierSubscriptionStatuses.Active)
        {
            return (subscription.Status, FormatStatusMessage(subscription.Status));
        }

        var today = DateTime.UtcNow.Date;
        if (subscription.NextExpectedBillDateUtc.Date < today.AddDays(-subscription.DateToleranceDays))
        {
            return ("missing_bill", "Expected bill is overdue and has not been matched.");
        }

        if (subscription.NextExpectedBillDateUtc.Date <= today.AddDays(subscription.DateToleranceDays))
        {
            return ("due", "Expected bill is due soon.");
        }

        return ("upcoming", "Next bill is expected in the future.");
    }

    private static string FormatStatusMessage(string status) => status switch
    {
        SupplierSubscriptionStatuses.Draft => "Draft subscription is not matched automatically.",
        SupplierSubscriptionStatuses.Paused => "Subscription is paused.",
        SupplierSubscriptionStatuses.Cancelled => "Subscription is cancelled.",
        SupplierSubscriptionStatuses.Expired => "Subscription has expired.",
        _ => "Subscription is active."
    };

    private static string NormalizeToken(string value) => value.Trim().Replace('-', '_').Replace(' ', '_').ToLowerInvariant();
    private static string FormatLifecycle(string action) => action switch
    {
        "activate" => "activated",
        "pause" => "paused",
        "resume" => "resumed",
        "cancel" => "cancelled",
        _ => action
    };

    private async Task WriteAuditAsync(Guid companyId, Guid? actorUserId, string action, Guid subscriptionId, string outcome, string rationale, string actorDisplayName, CancellationToken cancellationToken, Guid? billId = null, Guid? matchId = null)
    {
        await _audit.WriteAsync(
            new AuditEventWriteRequest(
                companyId,
                AuditActorTypes.User,
                actorUserId,
                action,
                "supplier_subscription",
                subscriptionId.ToString("D"),
                outcome,
                rationale,
                DataSources: ["supplier_subscription", "finance_bill"],
                Metadata: new Dictionary<string, string?>
                {
                    ["actorDisplayName"] = actorDisplayName,
                    ["subscriptionId"] = subscriptionId.ToString("D"),
                    ["billId"] = billId?.ToString("D"),
                    ["matchId"] = matchId?.ToString("D")
                },
                OccurredUtc: DateTime.UtcNow),
            cancellationToken);
    }

    private sealed record MatchCandidate(SupplierSubscription Subscription, int Score, bool CanAutoConfirm, bool IsUseful, string Evidence);
}

