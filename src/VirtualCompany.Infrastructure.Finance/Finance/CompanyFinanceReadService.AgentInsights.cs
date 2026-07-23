using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Infrastructure.Finance;

public sealed partial class CompanyFinanceReadService
{
    public async Task<FinanceBillDetailDto?> GetBillDetailAsync(
        GetFinanceBillDetailQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        if (query.BillId == Guid.Empty)
        {
            throw new ArgumentException("Bill id is required.", nameof(query));
        }

        var row = await _dbContext.FinanceBills
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.Id == query.BillId)
            .Select(x => new FinanceBillRow(
                x.Id,
                x.CounterpartyId,
                x.Counterparty == null ? MissingCounterpartyName : x.Counterparty.Name,
                x.Counterparty == null ? null : x.Counterparty.DefaultAccountMapping,
                x.BillNumber,
                x.ReceivedUtc,
                x.DueUtc,
                x.Amount,
                x.Currency,
                x.Status,
                x.PostingStatus,
                x.SettlementStatus,
                x.DueStatus,
                x.DocumentKind,
                x.ProviderStatus,
                x.ProcessingStatus,
                x.DocumentId,
                EF.Property<string>(x, "SourceType"),
                EF.Property<string?>(x, "ProviderKey"),
                false))
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        var hasFortnoxReference = await HasFortnoxReferenceAsync(query.CompanyId, ["supplier_invoice", "bill"], row.Id, cancellationToken);
        await EnsureFinanceInitializedForRecordAsync(
            query.CompanyId,
            row.SourceType,
            row.ProviderKey,
            hasFortnoxReference,
            cancellationToken);

        var linkedDocuments = await LoadLinkedDocumentsAsync(query.CompanyId, [row.DocumentId], cancellationToken);
        var documentAccess = await BuildDocumentAccessAsync(query.CompanyId, row.DocumentId, linkedDocuments, cancellationToken);
        var billReviewStates = await LoadBillReviewStatesAsync(query.CompanyId, [row.Id], cancellationToken);
        var paymentContext = BuildPaymentContext(
            null,
            row.Id,
            new Dictionary<Guid, TransactionDocumentReviewState>(),
            billReviewStates);
        var relatedTransactions = await LoadBillRelatedTransactionsAsync(query.CompanyId, row.Id, cancellationToken);
        var paymentProposals = await LoadSupplierPaymentProposalsAsync(query.CompanyId, [row.Id], cancellationToken);
        var sourceDocumentAttachments = await LoadSupplierInvoiceSourceDocumentAttachmentsAsync(query.CompanyId, [row.Id], cancellationToken);
        var draftActions = await LoadSupplierInvoiceDraftActionsAsync(query.CompanyId, [row.Id], cancellationToken);
        var correctionActions = await LoadSupplierInvoiceCorrectionActionsAsync(query.CompanyId, [row.Id], cancellationToken);
        var enrichmentActions = await LoadSupplierInvoiceEnrichmentActionsAsync(query.CompanyId, [row.Id], cancellationToken);
        var enrichmentAction = enrichmentActions.GetValueOrDefault(row.Id);
        var paidExpenseAccount = PaidSupplierBillExpensePostingEligibility.ResolveExpenseAccountCode(
            enrichmentAction,
            row.CounterpartyDefaultAccountMapping);
        var isFullyPaid = paymentContext is not null &&
            paymentContext.TotalAmount > 0m &&
            paymentContext.PaidAmount >= paymentContext.TotalAmount &&
            paymentContext.RemainingAmount == 0m;
        var paidExpenseAvailability = PaidSupplierBillExpensePostingEligibility.Evaluate(
            row.DocumentKind,
            row.PostingStatus,
            row.SettlementStatus,
            row.Status,
            isFullyPaid,
            paidExpenseAccount,
            PaidSupplierBillExpensePostingEligibility.ExtractWarnings(enrichmentAction));

        return new FinanceBillDetailDto(
            row.Id,
            row.CounterpartyId,
            row.CounterpartyName,
            row.BillNumber,
            row.ReceivedUtc,
            row.DueUtc,
            row.Amount,
            row.Currency,
            row.Status,
            BuildActionPermissions(),
            documentAccess,
            await LoadEntityAgentInsightsAsync(query.CompanyId, "bill", row.Id, cancellationToken),
            row.PostingStatus,
            row.SettlementStatus,
            row.DueStatus,
            row.DocumentKind,
            row.ProviderStatus,
            row.ProcessingStatus,
            paymentContext,
            relatedTransactions,
            paymentProposals.GetValueOrDefault(row.Id),
            sourceDocumentAttachments.GetValueOrDefault(row.Id),
            draftActions.GetValueOrDefault(row.Id),
            correctionActions.GetValueOrDefault(row.Id),
            enrichmentAction,
            paidExpenseAvailability);
    }

    private async Task<IReadOnlyList<NormalizedFinanceInsightDto>> LoadEntityAgentInsightsAsync(
        Guid companyId,
        string entityType,
        Guid entityId,
        CancellationToken cancellationToken)
    {
        var entityTypeCandidates = FinanceInsightPresentation.BuildEntityTypeCandidates(entityType);
        var candidateIds = BuildEntityIdCandidates(entityId);

        var insights = await _dbContext.FinanceAgentInsights
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .ToListAsync(cancellationToken);

        return insights
            .Select(MapNormalizedInsight)
            .Where(x => MatchesEntityInsight(x, entityTypeCandidates, candidateIds))
            .OrderBy(x => string.Equals(x.Status, "resolved", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(x => GetInsightSeverityRank(x.Severity))
            .ThenByDescending(x => x.UpdatedAt)
            .ThenByDescending(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .ToArray();
    }

    private static bool MatchesEntityInsight(
        NormalizedFinanceInsightDto insight,
        IReadOnlySet<string> entityTypes,
        IReadOnlySet<string> candidateIds) =>
        MatchesEntityReference(insight.EntityReference, entityTypes, candidateIds) ||
        insight.AffectedEntities.Any(x => MatchesEntityReference(x, entityTypes, candidateIds));

    private static bool MatchesEntityReference(
        FinanceInsightEntityReferenceDto entity,
        IReadOnlySet<string> entityTypes,
        IReadOnlySet<string> candidateIds) =>
        entityTypes.Contains(entity.EntityType) &&
        candidateIds.Contains(entity.EntityId);

    private static IReadOnlySet<string> BuildEntityIdCandidates(Guid entityId)
    {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            entityId.ToString("D"),
            entityId.ToString("N")
        };

        return values;
    }

    private static int GetInsightSeverityRank(string severity) =>
        severity.Trim().ToLowerInvariant() switch
        {
            "critical" => 4,
            "high" => 3,
            "medium" => 2,
            "low" => 1,
            _ => 0
        };

    private sealed record FinancePaymentRow(
        Guid Id,
        Guid CompanyId,
        string PaymentType,
        decimal Amount,
        string Currency,
        DateTime PaymentDate,
        string Method,
        string Status,
        string CounterpartyReference,
        DateTime CreatedUtc,
        DateTime UpdatedUtc,
        IReadOnlyList<NormalizedFinanceInsightDto> AgentInsights);
}
