using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class SupplierSubscriptionIntakeProposalService : ISupplierSubscriptionIntakeProposalService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ISupplierSubscriptionService _subscriptionService;
    private readonly IAuditEventWriter _audit;
    private readonly ILogger<SupplierSubscriptionIntakeProposalService> _logger;

    public SupplierSubscriptionIntakeProposalService(
        VirtualCompanyDbContext dbContext,
        ISupplierSubscriptionService subscriptionService,
        IAuditEventWriter audit,
        ILogger<SupplierSubscriptionIntakeProposalService> logger)
    {
        _dbContext = dbContext;
        _subscriptionService = subscriptionService;
        _audit = audit;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SupplierSubscriptionIntakeProposalSummaryDto>> GetAsync(GetSupplierSubscriptionIntakeProposalsQuery query, CancellationToken cancellationToken)
    {
        var proposals = _dbContext.SupplierSubscriptionIntakeProposals
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            var status = NormalizeToken(query.Status);
            proposals = proposals.Where(x => x.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            proposals = proposals.Where(x =>
                (x.SupplierName != null && x.SupplierName.Contains(search)) ||
                (x.AgreementName != null && x.AgreementName.Contains(search)) ||
                (x.ContractReference != null && x.ContractReference.Contains(search)) ||
                x.EvidenceSummary.Contains(search));
        }

        var rows = await proposals
            .OrderByDescending(x => x.Status == SupplierSubscriptionIntakeProposalStatuses.NeedsReview)
            .ThenByDescending(x => x.CreatedUtc)
            .Take(200)
            .ToListAsync(cancellationToken);

        return rows.Select(MapSummary).ToList();
    }

    public async Task<SupplierSubscriptionIntakeProposalDetailDto?> GetAsync(GetSupplierSubscriptionIntakeProposalQuery query, CancellationToken cancellationToken)
    {
        var proposal = await LoadProposalForReadAsync(query.CompanyId, query.ProposalId, cancellationToken);
        return proposal is null ? null : MapDetail(proposal);
    }

    public async Task<SupplierSubscriptionIntakeProposalDetailDto> RecordAsync(RecordSupplierSubscriptionIntakeProposalCommand command, CancellationToken cancellationToken)
    {
        await EnsureSourceAsync(command.CompanyId, command.SourceEmailMessageSnapshotId, command.SourceEmailAttachmentSnapshotId, command.SourceDocumentId, cancellationToken);
        if (command.Terms.CounterpartyId is Guid counterpartyId)
        {
            await EnsureSupplierAsync(command.CompanyId, counterpartyId, cancellationToken);
        }

        var fingerprint = NormalizeFingerprint(command.SourceFingerprint);
        var existing = await _dbContext.SupplierSubscriptionIntakeProposals
            .IgnoreQueryFilters()
            .Include(x => x.SourceEmailMessageSnapshot)
            .Include(x => x.SourceEmailAttachmentSnapshot)
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.SourceFingerprint == fingerprint, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation("Subscription intake proposal reused existing source fingerprint. CompanyId: {CompanyId}. ProposalId: {ProposalId}.", command.CompanyId, existing.Id);
            await WriteAuditAsync(command.CompanyId, command.ActorUserId, "supplier_subscription.proposal.duplicate_suppressed", existing.Id, "skipped", "Duplicate subscription proposal source fingerprint was suppressed.", command.ActorDisplayName, cancellationToken);
            return MapDetail(existing);
        }

        var proposal = new SupplierSubscriptionIntakeProposal(
            Guid.NewGuid(),
            command.CompanyId,
            command.SourceEmailMessageSnapshotId,
            command.SourceEmailAttachmentSnapshotId,
            command.SourceDocumentId,
            fingerprint,
            command.Classification,
            command.Status,
            command.ConfidenceScore,
            command.EvidenceSummary,
            command.SupplierName,
            command.SupplierOrgNumber,
            command.Terms.CounterpartyId,
            command.Terms.Name,
            command.Terms.Currency,
            command.Terms.ExpectedAmount,
            command.Terms.Cadence,
            command.Terms.BillingDay,
            command.Terms.StartDateUtc,
            command.Terms.EndDateUtc,
            command.Terms.NextExpectedBillDateUtc,
            command.Terms.AmountTolerance,
            command.Terms.DateToleranceDays,
            command.Terms.NoticePeriodDays,
            command.Terms.AutoRenews,
            command.Terms.ContractReference,
            command.Terms.Description,
            command.SafeFailureSummary);

        _dbContext.SupplierSubscriptionIntakeProposals.Add(proposal);
        await WriteAuditAsync(command.CompanyId, command.ActorUserId, "supplier_subscription.proposal.detected", proposal.Id, AuditEventOutcomes.Pending, command.EvidenceSummary, command.ActorDisplayName, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return (await GetAsync(new GetSupplierSubscriptionIntakeProposalQuery(command.CompanyId, proposal.Id), cancellationToken))!;
    }

    public async Task<SupplierSubscriptionDetailDto> AcceptAsync(AcceptSupplierSubscriptionIntakeProposalCommand command, CancellationToken cancellationToken)
    {
        var proposal = await LoadProposalForWriteAsync(command.CompanyId, command.ProposalId, cancellationToken);
        if (!proposal.CanAccept)
        {
            throw new InvalidOperationException("This subscription proposal cannot be accepted in its current state.");
        }

        var terms = command.Terms;
        var counterpartyId = terms.CounterpartyId ?? proposal.MatchedCounterpartyId ?? throw new InvalidOperationException("Choose a supplier before accepting the proposal.");
        var name = terms.Name ?? proposal.AgreementName ?? proposal.SupplierName ?? "Supplier subscription";
        var currency = terms.Currency ?? proposal.Currency ?? throw new InvalidOperationException("Currency is required before accepting the proposal.");
        var expectedAmount = terms.ExpectedAmount ?? proposal.ExpectedAmount ?? throw new InvalidOperationException("Expected amount is required before accepting the proposal.");
        var cadence = terms.Cadence ?? proposal.Cadence ?? throw new InvalidOperationException("Cadence is required before accepting the proposal.");
        var billingDay = terms.BillingDay ?? proposal.BillingDay ?? throw new InvalidOperationException("Billing day is required before accepting the proposal.");
        var startDateUtc = terms.StartDateUtc ?? proposal.StartDateUtc ?? DateTime.UtcNow.Date;
        var nextExpectedBillDateUtc = terms.NextExpectedBillDateUtc ?? proposal.NextExpectedBillDateUtc ?? startDateUtc;

        proposal.UpdateReviewTerms(
            counterpartyId,
            name,
            currency,
            expectedAmount,
            cadence,
            billingDay,
            startDateUtc,
            terms.EndDateUtc ?? proposal.EndDateUtc,
            nextExpectedBillDateUtc,
            terms.AmountTolerance ?? proposal.AmountTolerance ?? 0m,
            terms.DateToleranceDays ?? proposal.DateToleranceDays ?? 5,
            terms.NoticePeriodDays ?? proposal.NoticePeriodDays ?? 30,
            terms.AutoRenews ?? proposal.AutoRenews ?? true,
            terms.ContractReference ?? proposal.ContractReference,
            terms.Description ?? proposal.Description);

        var subscription = await _subscriptionService.CreateAsync(
            new CreateSupplierSubscriptionCommand(
                command.CompanyId,
                counterpartyId,
                name,
                currency,
                expectedAmount,
                cadence,
                billingDay,
                startDateUtc,
                nextExpectedBillDateUtc,
                terms.AmountTolerance ?? proposal.AmountTolerance ?? 0m,
                terms.DateToleranceDays ?? proposal.DateToleranceDays ?? 5,
                terms.EndDateUtc ?? proposal.EndDateUtc,
                terms.ContractReference ?? proposal.ContractReference,
                terms.Description ?? proposal.Description,
                terms.NoticePeriodDays ?? proposal.NoticePeriodDays ?? 30,
                terms.AutoRenews ?? proposal.AutoRenews ?? true,
                terms.ContractDocumentId ?? proposal.SourceDocumentId,
                command.ActorUserId,
                command.ActorDisplayName),
            cancellationToken);

        proposal.Accept(subscription.Id, command.ActorUserId, command.DecisionReason);
        await WriteAuditAsync(command.CompanyId, command.ActorUserId, "supplier_subscription.proposal.accepted", proposal.Id, AuditEventOutcomes.Succeeded, "Subscription proposal was accepted into a draft agreement.", command.ActorDisplayName, cancellationToken, subscription.Id);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return subscription;
    }

    public async Task<SupplierSubscriptionIntakeProposalDetailDto> RejectAsync(RejectSupplierSubscriptionIntakeProposalCommand command, CancellationToken cancellationToken)
    {
        var proposal = await LoadProposalForWriteAsync(command.CompanyId, command.ProposalId, cancellationToken);
        proposal.Reject(command.ActorUserId, command.Reason);
        await WriteAuditAsync(command.CompanyId, command.ActorUserId, "supplier_subscription.proposal.rejected", proposal.Id, AuditEventOutcomes.Rejected, command.Reason, command.ActorDisplayName, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return (await GetAsync(new GetSupplierSubscriptionIntakeProposalQuery(command.CompanyId, proposal.Id), cancellationToken))!;
    }

    public async Task<SupplierSubscriptionIntakeProposalDetailDto> RetryAsync(RetrySupplierSubscriptionIntakeProposalCommand command, CancellationToken cancellationToken)
    {
        var proposal = await LoadProposalForWriteAsync(command.CompanyId, command.ProposalId, cancellationToken);
        if (proposal.Status is SupplierSubscriptionIntakeProposalStatuses.Accepted or SupplierSubscriptionIntakeProposalStatuses.Rejected)
        {
            throw new InvalidOperationException("A decided subscription proposal cannot be retried.");
        }

        proposal.MarkNeedsReview("Laura should retry subscription term extraction for this source.", proposal.ConfidenceScore);
        await WriteAuditAsync(command.CompanyId, command.ActorUserId, "supplier_subscription.proposal.retry_requested", proposal.Id, AuditEventOutcomes.Requested, "Subscription proposal retry was requested.", command.ActorDisplayName, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return (await GetAsync(new GetSupplierSubscriptionIntakeProposalQuery(command.CompanyId, proposal.Id), cancellationToken))!;
    }

    private async Task<SupplierSubscriptionIntakeProposal?> LoadProposalForReadAsync(Guid companyId, Guid proposalId, CancellationToken cancellationToken) =>
        await _dbContext.SupplierSubscriptionIntakeProposals
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(x => x.SourceEmailMessageSnapshot)
            .Include(x => x.SourceEmailAttachmentSnapshot)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == proposalId, cancellationToken);

    private async Task<SupplierSubscriptionIntakeProposal> LoadProposalForWriteAsync(Guid companyId, Guid proposalId, CancellationToken cancellationToken) =>
        await _dbContext.SupplierSubscriptionIntakeProposals
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == proposalId, cancellationToken)
        ?? throw new InvalidOperationException("The subscription proposal was not found for this company.");

    private async Task EnsureSourceAsync(Guid companyId, Guid messageSnapshotId, Guid? attachmentSnapshotId, Guid? documentId, CancellationToken cancellationToken)
    {
        var messageExists = await _dbContext.EmailMessageSnapshots
            .IgnoreQueryFilters()
            .AnyAsync(x => x.CompanyId == companyId && x.Id == messageSnapshotId, cancellationToken);
        if (!messageExists)
        {
            throw new InvalidOperationException("The source email was not found for this company.");
        }

        if (attachmentSnapshotId is Guid attachmentId)
        {
            var attachmentExists = await _dbContext.EmailAttachmentSnapshots
                .IgnoreQueryFilters()
                .AnyAsync(x => x.CompanyId == companyId && x.Id == attachmentId && x.EmailMessageSnapshotId == messageSnapshotId, cancellationToken);
            if (!attachmentExists)
            {
                throw new InvalidOperationException("The source attachment was not found for this company.");
            }
        }

        if (documentId is Guid sourceDocumentId)
        {
            var documentExists = await _dbContext.CompanyKnowledgeDocuments
                .IgnoreQueryFilters()
                .AnyAsync(x => x.CompanyId == companyId && x.Id == sourceDocumentId, cancellationToken);
            if (!documentExists)
            {
                throw new InvalidOperationException("The source document was not found for this company.");
            }
        }
    }

    private async Task EnsureSupplierAsync(Guid companyId, Guid counterpartyId, CancellationToken cancellationToken)
    {
        var supplierExists = await _dbContext.FinanceCounterparties
            .IgnoreQueryFilters()
            .AnyAsync(x => x.CompanyId == companyId && x.Id == counterpartyId && x.CounterpartyType == "supplier", cancellationToken);
        if (!supplierExists)
        {
            throw new InvalidOperationException("The supplier was not found for this company.");
        }
    }

    private static SupplierSubscriptionIntakeProposalSummaryDto MapSummary(SupplierSubscriptionIntakeProposal proposal) =>
        new(
            proposal.Id,
            proposal.Status,
            proposal.Classification,
            proposal.SupplierName ?? "Unknown supplier",
            proposal.AgreementName ?? proposal.ContractReference ?? "Subscription proposal",
            proposal.Currency,
            proposal.ExpectedAmount,
            proposal.Cadence,
            proposal.ConfidenceScore,
            proposal.EvidenceSummary,
            proposal.AcceptedSubscriptionId,
            proposal.CreatedUtc,
            proposal.UpdatedUtc);

    private static SupplierSubscriptionIntakeProposalDetailDto MapDetail(SupplierSubscriptionIntakeProposal proposal) =>
        new(
            proposal.Id,
            proposal.Status,
            proposal.Classification,
            proposal.SourceEmailMessageSnapshotId,
            proposal.SourceEmailAttachmentSnapshotId,
            proposal.SourceDocumentId,
            proposal.SourceFingerprint,
            proposal.SourceEmailMessageSnapshot?.Subject,
            proposal.SourceEmailAttachmentSnapshot?.FileName,
            proposal.SupplierName ?? "Unknown supplier",
            proposal.SupplierOrgNumber,
            new SupplierSubscriptionProposalTermsDto(
                proposal.MatchedCounterpartyId,
                proposal.AgreementName,
                proposal.Currency,
                proposal.ExpectedAmount,
                proposal.Cadence,
                proposal.BillingDay,
                proposal.StartDateUtc,
                proposal.NextExpectedBillDateUtc,
                proposal.AmountTolerance,
                proposal.DateToleranceDays,
                proposal.EndDateUtc,
                proposal.ContractReference,
                proposal.Description,
                proposal.NoticePeriodDays,
                proposal.AutoRenews,
                proposal.SourceDocumentId),
            proposal.ConfidenceScore,
            proposal.EvidenceSummary,
            proposal.SafeFailureSummary,
            proposal.AcceptedSubscriptionId,
            proposal.DecidedByUserId,
            proposal.DecisionReason,
            proposal.DecidedUtc,
            proposal.CreatedUtc,
            proposal.UpdatedUtc);

    private async Task WriteAuditAsync(Guid companyId, Guid? actorUserId, string action, Guid proposalId, string outcome, string rationale, string actorDisplayName, CancellationToken cancellationToken, Guid? subscriptionId = null)
    {
        await _audit.WriteAsync(
            new AuditEventWriteRequest(
                companyId,
                AuditActorTypes.User,
                actorUserId,
                action,
                "supplier_subscription_proposal",
                proposalId.ToString("D"),
                outcome,
                rationale,
                DataSources: ["supplier_subscription_proposal", "mailbox_snapshot"],
                Metadata: new Dictionary<string, string?>
                {
                    ["actorDisplayName"] = actorDisplayName,
                    ["proposalId"] = proposalId.ToString("D"),
                    ["subscriptionId"] = subscriptionId?.ToString("D")
                },
                OccurredUtc: DateTime.UtcNow),
            cancellationToken);
    }

    private static string NormalizeToken(string value) => value.Trim().Replace('-', '_').Replace(' ', '_').ToLowerInvariant();
    private static string NormalizeFingerprint(string value) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Source fingerprint is required.", nameof(value)) : value.Trim().ToLowerInvariant();
}
