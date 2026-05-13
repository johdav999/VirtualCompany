using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class ConversionAnalyticsService : IConversionAnalyticsService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyContextAccessor? _companyContextAccessor;

    public ConversionAnalyticsService(VirtualCompanyDbContext dbContext, ICompanyContextAccessor? companyContextAccessor = null)
    {
        _dbContext = dbContext;
        _companyContextAccessor = companyContextAccessor;
    }

    public async Task<SalesMessagePerformanceDto> RecordMessagePerformanceEventAsync(RecordMessagePerformanceEventCommand command, CancellationToken cancellationToken)
    {
        EnsureTenant(command.CompanyId);
        EnsureId(command.ContactId, nameof(command.ContactId));
        if (string.IsNullOrWhiteSpace(command.MessageKey))
        {
            throw new ArgumentException("MessageKey is required.", nameof(command));
        }

        var context = await ResolveContextAsync(command, cancellationToken);
        var messageKey = command.MessageKey.Trim();
        var performance = await _dbContext.SalesMessagePerformances
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.MessageKey == messageKey, cancellationToken);

        if (performance is null)
        {
            performance = new SalesMessagePerformance(
                Guid.NewGuid(),
                command.CompanyId,
                messageKey,
                context.CampaignId,
                context.SequenceId,
                context.SequenceStepId,
                context.SequenceExecutionStepId,
                context.ContactId,
                context.Provider,
                context.ProviderMessageId,
                context.ProviderThreadId,
                context.InternetMessageId,
                context.VariantKey,
                context.StepOrder);
            _dbContext.SalesMessagePerformances.Add(performance);
        }
        else if (performance.CompanyId != command.CompanyId)
        {
            throw new InvalidOperationException("Message performance belongs to a different company.");
        }

        performance.MergeAttribution(
            context.CampaignId,
            context.SequenceId,
            context.SequenceStepId,
            context.SequenceExecutionStepId,
            context.DealId,
            context.Provider,
            context.ProviderMessageId,
            context.ProviderThreadId,
            context.InternetMessageId,
            context.VariantKey,
            context.StepOrder);
        performance.ApplyEvent(command.EventType, command.OccurredUtc);

        if (context.DealId.HasValue || command.ExpectedRevenueAmount.HasValue || command.ExpectedCloseUtc.HasValue || command.PipelineRiskScore.HasValue)
        {
            var revenue = await ResolveRevenueContextAsync(command.CompanyId, context.DealId, command, cancellationToken);
            performance.UpdateRevenueContext(revenue.Amount, revenue.Currency, revenue.ExpectedCloseUtc, revenue.RiskScore, revenue.RiskCalculatedUtc);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Map(performance);
    }

    public async Task<SalesMessagePerformanceDto?> RecordDealCreatedForContactAsync(Guid companyId, Guid contactId, Guid dealId, DateTime occurredUtc, CancellationToken cancellationToken)
    {
        EnsureTenant(companyId);
        EnsureId(contactId, nameof(contactId));
        EnsureId(dealId, nameof(dealId));

        var performance = await _dbContext.SalesMessagePerformances
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.ContactId == contactId)
            .OrderByDescending(x => x.RepliedAt ?? x.SentAt ?? x.CreatedUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (performance is null)
        {
            return null;
        }

        var revenue = await ResolveRevenueContextAsync(companyId, dealId, null, cancellationToken);
        performance.MergeAttribution(
            performance.CampaignId,
            performance.SequenceId,
            performance.SequenceStepId,
            performance.SequenceExecutionStepId,
            dealId,
            performance.Provider,
            performance.ProviderMessageId,
            performance.ProviderThreadId,
            performance.InternetMessageId,
            performance.VariantKey,
            performance.StepOrder);
        performance.ApplyEvent(ConversionAnalyticsEventType.DealCreated, occurredUtc);
        performance.UpdateRevenueContext(revenue.Amount, revenue.Currency, revenue.ExpectedCloseUtc, revenue.RiskScore, revenue.RiskCalculatedUtc);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Map(performance);
    }

    public async Task<CampaignPerformanceSummaryDto?> GetCampaignPerformanceAsync(Guid companyId, Guid campaignId, CancellationToken cancellationToken)
    {
        EnsureTenant(companyId);
        EnsureId(campaignId, nameof(campaignId));

        var rows = _dbContext.SalesMessagePerformances
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.CampaignId == campaignId);

        if (!await rows.AnyAsync(cancellationToken))
        {
            return null;
        }

        var counts = await BuildCountsAsync(rows, cancellationToken);
        var now = DateTime.UtcNow;
        var revenue = await rows
            .Where(x => x.ExpectedRevenueAmount.HasValue && x.ExpectedCloseAt.HasValue)
            .GroupBy(_ => 1)
            .Select(x => new
            {
                Currency = x.Select(row => row.ExpectedRevenueCurrency).FirstOrDefault(),
                Revenue30 = x.Sum(row => row.ExpectedCloseAt <= now.AddDays(30) ? row.ExpectedRevenueAmount ?? 0m : 0m),
                Revenue60 = x.Sum(row => row.ExpectedCloseAt <= now.AddDays(60) ? row.ExpectedRevenueAmount ?? 0m : 0m),
                Revenue90 = x.Sum(row => row.ExpectedCloseAt <= now.AddDays(90) ? row.ExpectedRevenueAmount ?? 0m : 0m)
            })
            .SingleOrDefaultAsync(cancellationToken);

        var risk = await rows
            .GroupBy(_ => 1)
            .Select(x => new RiskDistributionSummary(
                x.Count(row => !row.PipelineRiskScore.HasValue),
                x.Count(row => row.PipelineRiskScore.HasValue && row.PipelineRiskScore.Value < 0.34m),
                x.Count(row => row.PipelineRiskScore.HasValue && row.PipelineRiskScore.Value >= 0.34m && row.PipelineRiskScore.Value < 0.67m),
                x.Count(row => row.PipelineRiskScore.HasValue && row.PipelineRiskScore.Value >= 0.67m)))
            .SingleAsync(cancellationToken);

        return new CampaignPerformanceSummaryDto(
            companyId,
            campaignId,
            counts,
            BuildRates(counts),
            new RevenueWindowSummary(revenue?.Revenue30 ?? 0m, revenue?.Revenue60 ?? 0m, revenue?.Revenue90 ?? 0m, revenue?.Currency),
            risk);
    }

    public async Task<SequencePerformanceSummaryDto?> GetSequencePerformanceAsync(Guid companyId, Guid sequenceId, CancellationToken cancellationToken)
    {
        EnsureTenant(companyId);
        EnsureId(sequenceId, nameof(sequenceId));

        var rows = _dbContext.SalesMessagePerformances
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.SequenceId == sequenceId);
        if (!await rows.AnyAsync(cancellationToken))
        {
            return null;
        }

        var counts = await BuildCountsAsync(rows, cancellationToken);
        return new SequencePerformanceSummaryDto(companyId, sequenceId, counts, BuildRates(counts));
    }

    public async Task<StepPerformanceSummaryDto?> GetStepPerformanceAsync(Guid companyId, Guid sequenceStepId, CancellationToken cancellationToken)
    {
        EnsureTenant(companyId);
        EnsureId(sequenceStepId, nameof(sequenceStepId));

        var rows = _dbContext.SalesMessagePerformances
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.SequenceStepId == sequenceStepId);
        if (!await rows.AnyAsync(cancellationToken))
        {
            return null;
        }

        var counts = await BuildCountsAsync(rows, cancellationToken);
        return new StepPerformanceSummaryDto(companyId, sequenceStepId, counts, BuildRates(counts));
    }

    public async Task<IReadOnlyList<ContactPerformanceSummaryDto>> GetContactPerformanceAsync(Guid companyId, Guid contactId, CancellationToken cancellationToken)
    {
        EnsureTenant(companyId);
        EnsureId(contactId, nameof(contactId));

        return await _dbContext.SalesMessagePerformances
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.ContactId == contactId)
            .OrderByDescending(x => x.UpdatedUtc)
            .Select(x => new ContactPerformanceSummaryDto(
                x.Id,
                x.MessageKey,
                x.CampaignId,
                x.SequenceStepId,
                x.VariantKey,
                new PerformanceFunnelCounts(
                    x.SentAt.HasValue ? 1 : 0,
                    x.DeliveredAt.HasValue ? 1 : 0,
                    x.BouncedAt.HasValue ? 1 : 0,
                    x.OpenedAt.HasValue ? 1 : 0,
                    x.RepliedAt.HasValue ? 1 : 0,
                    x.DealCreatedAt.HasValue ? 1 : 0,
                    x.ConvertedAt.HasValue ? 1 : 0),
                x.UpdatedUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<VariantPerformanceSummaryDto>> GetVariantPerformanceAsync(Guid companyId, Guid? campaignId, Guid? sequenceId, Guid? sequenceStepId, CancellationToken cancellationToken)
    {
        EnsureTenant(companyId);

        var query = _dbContext.SalesMessagePerformances
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.VariantKey != null);

        if (campaignId.HasValue)
        {
            query = query.Where(x => x.CampaignId == campaignId.Value);
        }

        if (sequenceId.HasValue)
        {
            query = query.Where(x => x.SequenceId == sequenceId.Value);
        }

        if (sequenceStepId.HasValue)
        {
            query = query.Where(x => x.SequenceStepId == sequenceStepId.Value);
        }

        var grouped = await query
            .GroupBy(x => new { x.CampaignId, x.SequenceId, x.SequenceStepId, x.VariantKey })
            .Select(x => new
            {
                x.Key.CampaignId,
                x.Key.SequenceId,
                x.Key.SequenceStepId,
                VariantKey = x.Key.VariantKey!,
                Counts = new PerformanceFunnelCounts(
                    x.Count(row => row.SentAt.HasValue),
                    x.Count(row => row.DeliveredAt.HasValue),
                    x.Count(row => row.BouncedAt.HasValue),
                    x.Count(row => row.OpenedAt.HasValue),
                    x.Count(row => row.RepliedAt.HasValue),
                    x.Count(row => row.DealCreatedAt.HasValue),
                    x.Count(row => row.ConvertedAt.HasValue))
            })
            .OrderBy(x => x.SequenceStepId)
            .ThenBy(x => x.VariantKey)
            .ToListAsync(cancellationToken);

        return grouped
            .Select(x => new VariantPerformanceSummaryDto(x.CampaignId, x.SequenceId, x.SequenceStepId, x.VariantKey, x.Counts, BuildRates(x.Counts)))
            .ToList();
    }

    public async Task<SalesAnalyticsDashboardDto> GetDashboardAnalyticsAsync(Guid companyId, CancellationToken cancellationToken)
    {
        EnsureTenant(companyId);

        var rows = _dbContext.SalesMessagePerformances
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId);

        var counts = await rows.AnyAsync(cancellationToken)
            ? await BuildCountsAsync(rows, cancellationToken)
            : new PerformanceFunnelCounts(0, 0, 0, 0, 0, 0, 0);

        var campaigns = await rows
            .Where(x => x.CampaignId.HasValue)
            .GroupJoin(
                _dbContext.SalesCampaigns.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId),
                performance => performance.CampaignId,
                campaign => campaign.Id,
                (performance, campaignRows) => new { performance, campaignRows })
            .SelectMany(
                x => x.campaignRows.DefaultIfEmpty(),
                (x, campaign) => new { x.performance, campaign })
            .GroupBy(x => new
            {
                CampaignId = x.performance.CampaignId!.Value,
                CampaignName = x.campaign == null ? "Campaign" : x.campaign.Name,
                x.performance.SequenceId
            })
            .Select(x => new
            {
                x.Key.CampaignId,
                x.Key.CampaignName,
                x.Key.SequenceId,
                Counts = new PerformanceFunnelCounts(
                    x.Count(row => row.performance.SentAt.HasValue),
                    x.Count(row => row.performance.DeliveredAt.HasValue),
                    x.Count(row => row.performance.BouncedAt.HasValue),
                    x.Count(row => row.performance.OpenedAt.HasValue),
                    x.Count(row => row.performance.RepliedAt.HasValue),
                    x.Count(row => row.performance.DealCreatedAt.HasValue),
                    x.Count(row => row.performance.ConvertedAt.HasValue))
            })
            .OrderByDescending(x => x.Counts.Sent)
            .Take(10)
            .ToListAsync(cancellationToken);

        var variants = await GetVariantPerformanceAsync(companyId, null, null, null, cancellationToken);
        return new SalesAnalyticsDashboardDto(
            companyId,
            counts,
            BuildRates(counts),
            campaigns.Select(x => new CampaignPerformanceListItemDto(x.CampaignId, x.CampaignName, x.SequenceId, x.Counts, BuildRates(x.Counts))).ToList(),
            variants.Take(12).ToList());
    }

    private async Task<ResolvedMessageContext> ResolveContextAsync(RecordMessagePerformanceEventCommand command, CancellationToken cancellationToken)
    {
        var contactId = command.ContactId;
        var campaignId = command.CampaignId;
        var sequenceId = command.SequenceId;
        var sequenceStepId = command.SequenceStepId;
        var sequenceExecutionStepId = command.SequenceExecutionStepId;
        var provider = command.Provider;
        var providerMessageId = command.ProviderMessageId;
        var providerThreadId = command.ProviderThreadId;
        var internetMessageId = command.InternetMessageId;
        var stepOrder = command.StepOrder;
        var variantKey = command.VariantKey;

        if (sequenceExecutionStepId.HasValue)
        {
            var step = await _dbContext.SalesSequenceExecutionSteps
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(x => x.SalesSequenceStep)
                .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == sequenceExecutionStepId.Value, cancellationToken)
                ?? throw new InvalidOperationException("Sequence execution step does not belong to this company.");

            contactId = step.ContactId;
            campaignId ??= step.SalesCampaignId;
            sequenceStepId ??= step.SalesSequenceStepId;
            sequenceId ??= step.SalesSequenceStep.SalesSequenceId;
            provider ??= step.Provider;
            providerMessageId ??= step.ProviderMessageId;
            providerThreadId ??= step.ProviderThreadId;
            internetMessageId ??= step.InternetMessageId;
            stepOrder ??= step.StepOrder;
            variantKey ??= $"step:{step.SalesSequenceStepId:N}";
        }
        else if (sequenceStepId.HasValue && !sequenceId.HasValue)
        {
            sequenceId = await _dbContext.SalesSequenceSteps
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x => x.CompanyId == command.CompanyId && x.Id == sequenceStepId.Value)
                .Select(x => (Guid?)x.SalesSequenceId)
                .SingleOrDefaultAsync(cancellationToken);
            if (!sequenceId.HasValue)
            {
                throw new InvalidOperationException("Sequence step does not belong to this company.");
            }
        }

        return new ResolvedMessageContext(
            campaignId,
            sequenceId,
            sequenceStepId,
            sequenceExecutionStepId,
            contactId,
            command.DealId,
            provider,
            providerMessageId,
            providerThreadId,
            internetMessageId,
            variantKey,
            stepOrder);
    }

    private async Task<RevenueContext> ResolveRevenueContextAsync(Guid companyId, Guid? dealId, RecordMessagePerformanceEventCommand? command, CancellationToken cancellationToken)
    {
        var amount = command?.ExpectedRevenueAmount;
        var currency = command?.ExpectedRevenueCurrency;
        var expectedCloseUtc = command?.ExpectedCloseUtc;
        var riskScore = command?.PipelineRiskScore;
        var riskCalculatedUtc = command?.RiskCalculatedUtc;

        if (dealId.HasValue)
        {
            var deal = await _dbContext.Deals.IgnoreQueryFilters()
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == dealId.Value && !x.IsDeleted, cancellationToken);
            if (deal is null)
            {
                throw new InvalidOperationException("Deal does not belong to this company.");
            }

            amount ??= deal.Amount;
            currency ??= deal.Currency;
            expectedCloseUtc ??= deal.ExpectedCloseUtc;
        }

        return new RevenueContext(amount, currency, expectedCloseUtc, riskScore, riskCalculatedUtc);
    }

    private static async Task<PerformanceFunnelCounts> BuildCountsAsync(IQueryable<SalesMessagePerformance> rows, CancellationToken cancellationToken) =>
        await rows
            .GroupBy(_ => 1)
            .Select(x => new PerformanceFunnelCounts(
                x.Count(row => row.SentAt.HasValue),
                x.Count(row => row.DeliveredAt.HasValue),
                x.Count(row => row.BouncedAt.HasValue),
                x.Count(row => row.OpenedAt.HasValue),
                x.Count(row => row.RepliedAt.HasValue),
                x.Count(row => row.DealCreatedAt.HasValue),
                x.Count(row => row.ConvertedAt.HasValue)))
            .SingleAsync(cancellationToken);

    private static PerformanceFunnelRates BuildRates(PerformanceFunnelCounts counts) =>
        new(
            Rate(counts.Delivered, counts.Sent),
            Rate(counts.Opened, counts.Delivered),
            Rate(counts.Replied, counts.Sent),
            Rate(counts.Converted, counts.Sent));

    private static decimal Rate(int numerator, int denominator) =>
        denominator <= 0 ? 0m : Math.Round((decimal)numerator / denominator, 4, MidpointRounding.AwayFromZero);

    private void EnsureTenant(Guid companyId)
    {
        EnsureId(companyId, nameof(companyId));
        if (_companyContextAccessor?.CompanyId is Guid currentCompanyId && currentCompanyId != companyId)
        {
            throw new InvalidOperationException("The requested sales analytics data is outside the active company context.");
        }
    }

    private static void EnsureId(Guid id, string name) =>
        _ = id == Guid.Empty ? throw new ArgumentException($"{name} is required.", name) : id;

    private static SalesMessagePerformanceDto Map(SalesMessagePerformance performance) =>
        new(
            performance.Id,
            performance.CompanyId,
            performance.MessageKey,
            performance.CampaignId,
            performance.SequenceId,
            performance.SequenceStepId,
            performance.SequenceExecutionStepId,
            performance.ContactId,
            performance.DealId,
            performance.VariantKey,
            performance.SentAt,
            performance.DeliveredAt,
            performance.BouncedAt,
            performance.OpenedAt,
            performance.RepliedAt,
            performance.DealCreatedAt,
            performance.ConvertedAt,
            performance.ExpectedRevenueAmount,
            performance.ExpectedRevenueCurrency,
            performance.ExpectedCloseAt,
            performance.PipelineRiskScore);

    private sealed record ResolvedMessageContext(
        Guid? CampaignId,
        Guid? SequenceId,
        Guid? SequenceStepId,
        Guid? SequenceExecutionStepId,
        Guid ContactId,
        Guid? DealId,
        string? Provider,
        string? ProviderMessageId,
        string? ProviderThreadId,
        string? InternetMessageId,
        string? VariantKey,
        int? StepOrder);

    private sealed record RevenueContext(decimal? Amount, string? Currency, DateTime? ExpectedCloseUtc, decimal? RiskScore, DateTime? RiskCalculatedUtc);
}
