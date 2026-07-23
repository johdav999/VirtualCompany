using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.CustomerMemory;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.CustomerMemory;

public sealed class CustomerMemoryService : ICustomerMemoryService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public CustomerMemoryService(VirtualCompanyDbContext dbContext, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    public async Task<CustomerMemoryContext?> GetContextAsync(Guid companyId, Guid contactId, CancellationToken cancellationToken)
    {
        EnsureIds(companyId, contactId);

        var contact = await LoadContactAsync(companyId, contactId, cancellationToken);
        if (contact is null)
        {
            return null;
        }

        var persisted = await _dbContext.CustomerMemoryProfiles.IgnoreQueryFilters()
            .AsNoTracking()
            .Include(x => x.Preferences)
            .Include(x => x.PriceSignals)
            .Include(x => x.IndustrySignals)
            .Where(x => x.CompanyId == companyId && x.ContactId == contactId)
            .SingleOrDefaultAsync(cancellationToken);

        var aggregate = await BuildAggregateAsync(companyId, contact, cancellationToken);
        if (persisted is null)
        {
            return aggregate;
        }

        return aggregate with
        {
            AiSummary = FirstUseful(persisted.AiSummary, aggregate.AiSummary),
            RelationshipMemory = FirstUseful(persisted.RelationshipMemory, aggregate.RelationshipMemory),
            LastOutreachSummary = FirstUseful(persisted.LastOutreachSummary, aggregate.LastOutreachSummary),
            EngagementScore = persisted.EngagementScore ?? aggregate.EngagementScore,
            Preferences = persisted.Preferences.Count > 0
                ? persisted.Preferences.OrderByDescending(x => x.ObservedUtc).Select(x => new CustomerMemorySignal(x.PreferenceKey, x.PreferenceValue, x.Confidence ?? 0.7m, x.ObservedUtc, x.SourceSummary ?? "Saved customer preference.")).ToList()
                : aggregate.Preferences,
            PriceSensitivityIndicators = persisted.PriceSignals.Count > 0
                ? persisted.PriceSignals.OrderByDescending(x => x.ObservedUtc).Select(x => new CustomerMemorySignal(x.SignalKey, x.SignalValue, x.Confidence ?? 0.7m, x.ObservedUtc, x.SourceSummary ?? "Saved price signal.")).ToList()
                : aggregate.PriceSensitivityIndicators,
            IndustrySignals = persisted.IndustrySignals.Count > 0
                ? persisted.IndustrySignals.OrderByDescending(x => x.ObservedUtc).Select(x => new CustomerMemorySignal(x.SignalKey, x.SignalValue, x.Confidence ?? 0.7m, x.ObservedUtc, x.SourceSummary ?? "Saved industry signal.")).ToList()
                : aggregate.IndustrySignals,
            RefreshedUtc = persisted.UpdatedUtc
        };
    }

    public async Task<CustomerMemoryContext?> RefreshProfileAsync(Guid companyId, Guid contactId, CancellationToken cancellationToken)
    {
        EnsureIds(companyId, contactId);

        var contact = await LoadContactAsync(companyId, contactId, cancellationToken);
        if (contact is null)
        {
            return null;
        }

        var context = await BuildAggregateAsync(companyId, contact, cancellationToken);
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var profile = await _dbContext.CustomerMemoryProfiles.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.ContactId == contactId, cancellationToken);

        if (profile is null)
        {
            profile = new CustomerMemoryProfile(
                Guid.NewGuid(),
                companyId,
                contactId,
                context.AiSummary,
                context.RelationshipMemory,
                context.LastOutreachSummary,
                context.EngagementScore,
                BuildProfileMetadata(context),
                now,
                now);
            _dbContext.CustomerMemoryProfiles.Add(profile);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        else
        {
            profile.Refresh(
                context.AiSummary,
                context.RelationshipMemory,
                context.LastOutreachSummary,
                context.EngagementScore,
                BuildProfileMetadata(context),
                now);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        await ReplaceProfileDetailsAsync(profile, context, now, cancellationToken);
        return context with { RefreshedUtc = now };
    }

    public async Task<OfferEligibilityResult> EvaluateOfferEligibilityAsync(Guid companyId, Guid contactId, string offerKey, TimeSpan lookbackWindow, CancellationToken cancellationToken)
    {
        EnsureIds(companyId, contactId);
        var normalizedOfferKey = NormalizeOfferKey(offerKey);
        var lookbackStartUtc = _timeProvider.GetUtcNow().UtcDateTime.Subtract(lookbackWindow);

        var campaignExposures = await _dbContext.SalesSequenceExecutionSteps.IgnoreQueryFilters()
            .AsNoTracking()
            .Include(x => x.SequenceExecution).ThenInclude(x => x.SalesCampaign)
            .Where(x => x.CompanyId == companyId &&
                x.ContactId == contactId &&
                x.SentUtc >= lookbackStartUtc &&
                x.SequenceExecution.SalesCampaign.Name.ToLower() == normalizedOfferKey)
            .Select(x => new OfferExposureMemory(
                normalizedOfferKey,
                x.SalesCampaignId,
                null,
                x.SentUtc!.Value,
                "campaign",
                $"Campaign email sent for {x.SequenceExecution.SalesCampaign.Name}."))
            .ToListAsync(cancellationToken);

        var dealExposures = await _dbContext.Deals.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                x.PrimaryContactId == contactId &&
                !x.IsDeleted &&
                x.CreatedUtc >= lookbackStartUtc &&
                x.Title.ToLower() == normalizedOfferKey)
            .Select(x => new OfferExposureMemory(
                normalizedOfferKey,
                null,
                x.Id,
                x.CreatedUtc,
                "deal",
                $"Deal history already includes {x.Title}."))
            .ToListAsync(cancellationToken);

        var exposures = campaignExposures.Concat(dealExposures).OrderByDescending(x => x.OccurredUtc).ToList();
        return exposures.Count == 0
            ? OfferEligibilityResult.Allowed(normalizedOfferKey, lookbackStartUtc)
            : OfferEligibilityResult.Blocked(normalizedOfferKey, lookbackStartUtc, exposures);
    }

    private async Task<Contact?> LoadContactAsync(Guid companyId, Guid contactId, CancellationToken cancellationToken) =>
        await _dbContext.Contacts.IgnoreQueryFilters()
            .AsNoTracking()
            .Include(x => x.CustomerCompany)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == contactId && !x.IsDeleted, cancellationToken);

    private async Task<CustomerMemoryContext> BuildAggregateAsync(Guid companyId, Contact contact, CancellationToken cancellationToken)
    {
        var conversations = await LoadConversationMemoryAsync(companyId, contact, cancellationToken);
        var deals = await LoadDealMemoryAsync(companyId, contact.Id, cancellationToken);
        var offerExposures = await LoadOfferExposureHistoryAsync(companyId, contact.Id, cancellationToken);
        var preferences = BuildPreferences(conversations);
        var priceSignals = BuildPriceSignals(conversations, deals);
        var industrySignals = BuildIndustrySignals(contact);
        var lastOutreach = offerExposures.OrderByDescending(x => x.OccurredUtc).FirstOrDefault()?.Summary;
        var engagementScore = CalculateEngagementScore(conversations, deals, offerExposures);

        return new CustomerMemoryContext(
            companyId,
            contact.Id,
            contact.FullName,
            contact.Email,
            contact.CustomerCompany?.Name,
            contact.CustomerCompany?.Industry,
            BuildAiSummary(contact, conversations, deals, priceSignals, industrySignals),
            BuildRelationshipMemory(contact, conversations, deals),
            lastOutreach,
            engagementScore,
            conversations,
            deals,
            preferences,
            priceSignals,
            industrySignals,
            offerExposures,
            _timeProvider.GetUtcNow().UtcDateTime);
    }

    private async Task<IReadOnlyList<CustomerConversationMemory>> LoadConversationMemoryAsync(Guid companyId, Contact contact, CancellationToken cancellationToken)
    {
        var emailLinks = await _dbContext.SalesEmailLinks.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.ContactId == contact.Id && !x.IsDeleted)
            .OrderByDescending(x => x.CreatedUtc)
            .Take(10)
            .Select(x => new CustomerConversationMemory(
                null,
                x.Rationale ?? x.DetectedIntent ?? x.ProductOrServiceInterest ?? "Sales email linked to this contact.",
                x.CreatedUtc,
                "sales_email"))
            .ToListAsync(cancellationToken);

        var bodyMatches = await _dbContext.Messages.IgnoreQueryFilters()
            .AsNoTracking()
            .Include(x => x.Conversation)
            .Where(x => x.CompanyId == companyId && (x.Body.Contains(contact.Email) || x.Body.Contains(contact.FullName)))
            .OrderByDescending(x => x.CreatedUtc)
            .Take(10)
            .Select(x => new CustomerConversationMemory(
                x.ConversationId,
                x.Conversation.Subject ?? Trim(x.Body, 220),
                x.CreatedUtc,
                x.Conversation.ChannelType))
            .ToListAsync(cancellationToken);

        return emailLinks.Concat(bodyMatches)
            .OrderByDescending(x => x.OccurredUtc)
            .Take(12)
            .ToList();
    }

    private async Task<IReadOnlyList<CustomerDealMemory>> LoadDealMemoryAsync(Guid companyId, Guid contactId, CancellationToken cancellationToken) =>
        await _dbContext.Deals.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.PrimaryContactId == contactId && !x.IsDeleted)
            .OrderByDescending(x => x.UpdatedUtc)
            .Take(10)
            .Select(x => new CustomerDealMemory(
                x.Id,
                x.Title,
                x.Status,
                x.Amount,
                x.Currency,
                x.Status == SalesStatuses.Won || x.Status == SalesStatuses.Lost ? x.UpdatedUtc : null,
                $"{x.Title} is {x.Status} at {x.Amount} {x.Currency}."))
            .ToListAsync(cancellationToken);

    private async Task<IReadOnlyList<OfferExposureMemory>> LoadOfferExposureHistoryAsync(Guid companyId, Guid contactId, CancellationToken cancellationToken) =>
        await _dbContext.SalesSequenceExecutionSteps.IgnoreQueryFilters()
            .AsNoTracking()
            .Include(x => x.SequenceExecution).ThenInclude(x => x.SalesCampaign)
            .Where(x => x.CompanyId == companyId && x.ContactId == contactId && x.SentUtc.HasValue)
            .OrderByDescending(x => x.SentUtc)
            .Take(20)
            .Select(x => new OfferExposureMemory(
                x.SequenceExecution.SalesCampaign.Name.ToLower(),
                x.SalesCampaignId,
                null,
                x.SentUtc!.Value,
                "campaign",
                $"Campaign email sent for {x.SequenceExecution.SalesCampaign.Name}."))
            .ToListAsync(cancellationToken);

    private async Task ReplaceProfileDetailsAsync(CustomerMemoryProfile profile, CustomerMemoryContext context, DateTime now, CancellationToken cancellationToken)
    {
        _dbContext.CustomerMemoryProfileConversations.RemoveRange(_dbContext.CustomerMemoryProfileConversations.IgnoreQueryFilters().Where(x => x.CompanyId == profile.CompanyId && x.CustomerMemoryProfileId == profile.Id));
        _dbContext.CustomerMemoryProfileDeals.RemoveRange(_dbContext.CustomerMemoryProfileDeals.IgnoreQueryFilters().Where(x => x.CompanyId == profile.CompanyId && x.CustomerMemoryProfileId == profile.Id));
        _dbContext.CustomerMemoryProfilePreferences.RemoveRange(_dbContext.CustomerMemoryProfilePreferences.IgnoreQueryFilters().Where(x => x.CompanyId == profile.CompanyId && x.CustomerMemoryProfileId == profile.Id));
        _dbContext.CustomerMemoryProfilePriceSignals.RemoveRange(_dbContext.CustomerMemoryProfilePriceSignals.IgnoreQueryFilters().Where(x => x.CompanyId == profile.CompanyId && x.CustomerMemoryProfileId == profile.Id));
        _dbContext.CustomerMemoryProfileIndustrySignals.RemoveRange(_dbContext.CustomerMemoryProfileIndustrySignals.IgnoreQueryFilters().Where(x => x.CompanyId == profile.CompanyId && x.CustomerMemoryProfileId == profile.Id));

        foreach (var conversation in context.PastConversations.Where(x => x.ConversationId.HasValue))
        {
            _dbContext.CustomerMemoryProfileConversations.Add(new CustomerMemoryProfileConversation(Guid.NewGuid(), profile.CompanyId, profile.Id, conversation.ConversationId!.Value, conversation.Summary, conversation.OccurredUtc, 0.7m, createdUtc: now));
        }

        foreach (var deal in context.PreviousDeals)
        {
            _dbContext.CustomerMemoryProfileDeals.Add(new CustomerMemoryProfileDeal(Guid.NewGuid(), profile.CompanyId, profile.Id, deal.DealId, "primary", deal.Status, deal.ClosedUtc, deal.Summary, createdUtc: now));
        }

        foreach (var preference in context.Preferences)
        {
            _dbContext.CustomerMemoryProfilePreferences.Add(new CustomerMemoryProfilePreference(Guid.NewGuid(), profile.CompanyId, profile.Id, preference.Key, preference.Value, preference.SourceSummary, preference.Confidence, preference.ObservedUtc, now));
        }

        foreach (var signal in context.PriceSensitivityIndicators)
        {
            _dbContext.CustomerMemoryProfilePriceSignals.Add(new CustomerMemoryProfilePriceSignal(Guid.NewGuid(), profile.CompanyId, profile.Id, signal.Key, signal.Value, signal.Confidence, signal.ObservedUtc, signal.SourceSummary, now));
        }

        foreach (var signal in context.IndustrySignals)
        {
            _dbContext.CustomerMemoryProfileIndustrySignals.Add(new CustomerMemoryProfileIndustrySignal(Guid.NewGuid(), profile.CompanyId, profile.Id, signal.Key, signal.Value, signal.Confidence, signal.ObservedUtc, signal.SourceSummary, now));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static IReadOnlyList<CustomerMemorySignal> BuildPreferences(IReadOnlyList<CustomerConversationMemory> conversations) =>
        conversations.Where(x => x.Summary.Contains("prefers", StringComparison.OrdinalIgnoreCase))
            .Take(5)
            .Select(x => new CustomerMemorySignal("communication", Trim(x.Summary, 180), 0.6m, x.OccurredUtc, x.SourceType))
            .ToList();

    private static IReadOnlyList<CustomerMemorySignal> BuildPriceSignals(IReadOnlyList<CustomerConversationMemory> conversations, IReadOnlyList<CustomerDealMemory> deals)
    {
        var signals = conversations.Where(x => x.Summary.Contains("price", StringComparison.OrdinalIgnoreCase) || x.Summary.Contains("budget", StringComparison.OrdinalIgnoreCase))
            .Select(x => new CustomerMemorySignal("price_discussion", Trim(x.Summary, 180), 0.65m, x.OccurredUtc, x.SourceType))
            .ToList();

        if (deals.Any(x => string.Equals(x.Status, SalesStatuses.Lost, StringComparison.OrdinalIgnoreCase)))
        {
            signals.Add(new CustomerMemorySignal("lost_deal", "Prior lost deal may indicate price or timing sensitivity.", 0.45m, deals.Max(x => x.ClosedUtc ?? DateTime.MinValue), "deal_history"));
        }

        return signals.Take(5).ToList();
    }

    private static IReadOnlyList<CustomerMemorySignal> BuildIndustrySignals(Contact contact) =>
        string.IsNullOrWhiteSpace(contact.CustomerCompany?.Industry)
            ? []
            : [new CustomerMemorySignal("industry", contact.CustomerCompany.Industry, 0.9m, contact.UpdatedUtc, "customer company profile")];

    private static decimal CalculateEngagementScore(IReadOnlyList<CustomerConversationMemory> conversations, IReadOnlyList<CustomerDealMemory> deals, IReadOnlyList<OfferExposureMemory> offerExposures)
    {
        var score = 20m + Math.Min(30m, conversations.Count * 5m) + Math.Min(30m, deals.Count * 8m) + Math.Min(20m, offerExposures.Count * 2m);
        if (deals.Any(x => string.Equals(x.Status, SalesStatuses.Won, StringComparison.OrdinalIgnoreCase)))
        {
            score += 10m;
        }

        return Math.Clamp(decimal.Round(score, 2), 0m, 100m);
    }

    private static string BuildAiSummary(Contact contact, IReadOnlyList<CustomerConversationMemory> conversations, IReadOnlyList<CustomerDealMemory> deals, IReadOnlyList<CustomerMemorySignal> priceSignals, IReadOnlyList<CustomerMemorySignal> industrySignals)
    {
        var parts = new List<string> { $"{contact.FullName} is a sales contact" };
        if (!string.IsNullOrWhiteSpace(contact.CustomerCompany?.Name)) parts.Add($"at {contact.CustomerCompany.Name}");
        if (industrySignals.Count > 0) parts.Add($"in {industrySignals[0].Value}");
        parts.Add($"with {conversations.Count} recent interaction(s) and {deals.Count} deal record(s)");
        if (priceSignals.Count > 0) parts.Add("price sensitivity has been observed");
        return string.Join(" ", parts) + ".";
    }

    private static string BuildRelationshipMemory(Contact contact, IReadOnlyList<CustomerConversationMemory> conversations, IReadOnlyList<CustomerDealMemory> deals) =>
        conversations.Count == 0 && deals.Count == 0
            ? $"{contact.FullName} has no prior recorded sales history yet."
            : $"Relationship history includes {conversations.Count} conversation item(s) and {deals.Count} previous deal(s).";

    private static Dictionary<string, JsonNode?> BuildProfileMetadata(CustomerMemoryContext context) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["conversationCount"] = JsonValue.Create(context.PastConversations.Count),
            ["dealCount"] = JsonValue.Create(context.PreviousDeals.Count),
            ["offerExposureCount"] = JsonValue.Create(context.OfferExposureHistory.Count),
            ["refreshedUtc"] = JsonValue.Create(context.RefreshedUtc)
        };

    private static string NormalizeOfferKey(string value) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Offer key is required.", nameof(value)) : value.Trim().ToLowerInvariant();

    private static string FirstUseful(string? first, string fallback) =>
        string.IsNullOrWhiteSpace(first) ? fallback : first;

    private static string Trim(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private static void EnsureIds(Guid companyId, Guid contactId)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(companyId));
        if (contactId == Guid.Empty) throw new ArgumentException("ContactId is required.", nameof(contactId));
    }
}