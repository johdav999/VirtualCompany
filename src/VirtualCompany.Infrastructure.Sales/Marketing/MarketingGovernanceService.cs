using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Marketing;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Sales;

public sealed partial class MarketingOperationsService
{
    private static readonly Regex PricePattern = new(
        @"(?:\b(?:SEK|USD|EUR|GBP)\b|[$€£])\s*\d|\d[\d\s,.]*\s*(?:SEK|USD|EUR|GBP)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex AbsoluteClaimPattern = new(
        @"\b(?:guaranteed|always|never|best|completely automated|risk[- ]free)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public Task<MarketingPlanProposalDto> PreparePlanProposalAsync(
        Guid companyId,
        CreateMarketingPlanRequest request,
        CancellationToken ct)
    {
        RequireCompany(companyId);
        ct.ThrowIfCancellationRequested();
        var objectiveIds = (request.ObjectiveIds ?? []).Distinct().Order().ToArray();
        var keyMaterial = JsonSerializer.Serialize(new
        {
            companyId,
            request.Name,
            request.Summary,
            StartsUtc = NormalizeUtc(request.StartsUtc),
            EndsUtc = NormalizeUtc(request.EndsUtc),
            request.PlannedBudget,
            Currency = request.BudgetCurrency.Trim().ToUpperInvariant(),
            ObjectiveIds = objectiveIds
        });
        var proposalKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(keyMaterial))).ToLowerInvariant();
        var missing = new List<string>();
        if (objectiveIds.Length == 0) missing.Add("No measurable objective is linked.");
        if (!request.PlannedBudget.HasValue) missing.Add("No planned budget has been provided.");
        var risks = request.EndsUtc <= request.StartsUtc
            ? new[] { "The proposed period does not end after it starts." }
            : Array.Empty<string>();

        return Task.FromResult(new MarketingPlanProposalDto(
            proposalKey,
            request.Name,
            request.Summary,
            NormalizeUtc(request.StartsUtc),
            NormalizeUtc(request.EndsUtc),
            request.PlannedBudget,
            request.BudgetCurrency.Trim().ToUpperInvariant(),
            objectiveIds,
            new[] { "Plan commitment creates internal records only and does not launch a campaign." },
            risks,
            missing));
    }

    public async Task<MarketingPlanDto> CommitPlanAsync(
        Guid companyId,
        Guid userId,
        CommitMarketingPlanRequest request,
        CancellationToken ct)
    {
        RequireCompany(companyId);
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            throw new ArgumentException("An idempotency key is required.", nameof(request));
        var normalizedKey = request.IdempotencyKey.Trim();
        var existing = await _db.MarketingPlans.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.IdempotencyKey == normalizedKey, ct);
        return existing is not null
            ? Map(existing)
            : await CreatePlanCoreAsync(companyId, userId, request.Plan, normalizedKey, ct);
    }

    public async Task<MarketingContentPreflightDto?> PreflightContentAsync(
        Guid companyId,
        Guid briefId,
        CancellationToken ct)
    {
        var briefExists = await _db.MarketingContentBriefs.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.CompanyId == companyId && x.Id == briefId, ct);
        if (!briefExists) return null;

        var variants = await _db.MarketingContentVariants.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.MarketingContentBriefId == briefId)
            .ToListAsync(ct);
        var issues = new List<MarketingContentPreflightIssueDto>();
        if (variants.Count == 0)
        {
            issues.Add(new("missing_variant", "blocking",
                "Add at least one content variant before requesting review."));
        }

        foreach (var variant in variants)
        {
            var references = variant.SourceReferences;
            if (string.IsNullOrWhiteSpace(references))
            {
                issues.Add(new("missing_sources", "blocking",
                    "This variant has no approved source references.", variant.Id));
            }
            if (PricePattern.IsMatch(variant.Body) &&
                !references.Contains("price", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new("unapproved_price", "blocking",
                    "A price is stated without a price source reference.", variant.Id));
            }
            if (AbsoluteClaimPattern.IsMatch(variant.Body) &&
                !references.Contains("claim", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new("unsupported_claim", "blocking",
                    "An absolute product claim is not linked to approved claim evidence.", variant.Id));
            }
            if (variant.GeneratedByAi)
            {
                issues.Add(new("ai_generated", "review",
                    "AI generated this variant. A person must review it before external use.", variant.Id));
            }
        }

        return new MarketingContentPreflightDto(
            briefId,
            issues.All(x => !string.Equals(x.Severity, "blocking", StringComparison.Ordinal)),
            issues);
    }

    public async Task<IReadOnlyList<MarketingQualificationDefinitionDto>> ListQualificationDefinitionsAsync(
        Guid companyId,
        CancellationToken ct) =>
        (await _db.MarketingQualificationDefinitions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.EffectiveFromUtc)
            .ToListAsync(ct))
        .Select(Map)
        .ToArray();

    public async Task<MarketingQualificationDefinitionDto> CreateQualificationDefinitionAsync(
        Guid companyId,
        Guid userId,
        CreateMarketingQualificationDefinitionRequest request,
        CancellationToken ct)
    {
        var definition = new MarketingQualificationDefinition(
            Guid.NewGuid(),
            companyId,
            request.Name,
            request.AudienceType,
            request.RequiredChannel,
            request.Threshold,
            request.FreshnessDays,
            request.RequiresCustomerCompany,
            request.EffectiveFromUtc,
            request.EffectiveToUtc,
            request.RulesJson,
            request.ExclusionsJson,
            userId);
        _db.MarketingQualificationDefinitions.Add(definition);
        await _db.SaveChangesAsync(ct);
        return Map(definition);
    }

    public async Task<MarketingQualificationDefinitionDto?> ActivateQualificationDefinitionAsync(
        Guid companyId,
        Guid definitionId,
        CancellationToken ct)
    {
        var definition = await _db.MarketingQualificationDefinitions.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == definitionId, ct);
        if (definition is null) return null;

        var active = await _db.MarketingQualificationDefinitions.IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId &&
                        x.AudienceType == definition.AudienceType &&
                        x.Status == MarketingStatuses.Active &&
                        x.Id != definition.Id)
            .ToListAsync(ct);
        foreach (var previous in active) previous.Retire();
        definition.Activate();
        await _db.SaveChangesAsync(ct);
        return Map(definition);
    }

    public async Task<IReadOnlyList<MarketingQualificationEvaluationDto>> ListQualificationEvaluationsAsync(
        Guid companyId,
        CancellationToken ct)
    {
        var rows = await _db.MarketingQualificationEvaluations.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.EvaluatedUtc)
            .Take(200)
            .ToListAsync(ct);
        var contactIds = rows.Select(x => x.ContactId).Distinct().ToArray();
        var names = await _db.Contacts.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && contactIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.FullName, ct);
        return rows.Select(x => Map(x, names.GetValueOrDefault(x.ContactId, "Unavailable contact"))).ToArray();
    }

    public async Task<MarketingQualificationEvaluationDto> EvaluateContactAsync(
        Guid companyId,
        EvaluateMarketingContactRequest request,
        CancellationToken ct)
    {
        RequireCompany(companyId);
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            throw new ArgumentException("An idempotency key is required.", nameof(request));
        var key = request.IdempotencyKey.Trim();
        var existing = await _db.MarketingQualificationEvaluations.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.IdempotencyKey == key, ct);
        if (existing is not null)
        {
            var existingName = await _db.Contacts.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.Id == existing.ContactId)
                .Select(x => x.FullName)
                .SingleOrDefaultAsync(ct) ?? "Unavailable contact";
            return Map(existing, existingName);
        }

        var definition = await _db.MarketingQualificationDefinitions.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId &&
                                       x.Id == request.DefinitionId &&
                                       x.Status == MarketingStatuses.Active, ct)
            ?? throw new InvalidOperationException("An active qualification definition is required.");
        var contact = await _db.Contacts.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == request.ContactId && !x.IsDeleted, ct)
            ?? throw new InvalidOperationException("The selected contact is not available to this company.");

        var now = DateTime.UtcNow;
        var permission = await _db.SalesContactPermissions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.ContactId == contact.Id &&
                        x.Channel == definition.RequiredChannel)
            .OrderByDescending(x => x.ObservedUtc)
            .FirstOrDefaultAsync(ct);
        var permissionFresh = permission is not null && permission.ObservedUtc >= now.AddDays(-definition.FreshnessDays);
        var permissionGranted = permission is not null &&
            permission.Status is "granted" or "consented" or "opted_in" or "allowed";
        var suppressed = await _db.SalesSuppressions.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.CompanyId == companyId && x.IsActive &&
                           (!x.ExpiresUtc.HasValue || x.ExpiresUtc > now) &&
                           ((x.ScopeType == "email" && x.ScopeValue == contact.Email) ||
                            (x.ScopeType == "contact" && x.ScopeValue == contact.Id.ToString().ToLower())), ct);

        var reasons = new List<string>();
        var evidence = new List<string> { $"contact:{contact.Id}" };
        decimal score = 0;
        if (string.Equals(contact.Status, "active", StringComparison.OrdinalIgnoreCase))
        {
            score += 30;
            reasons.Add("contact_active");
        }
        else reasons.Add("contact_inactive");

        if (permission is not null)
        {
            evidence.Add($"sales_contact_permission:{permission.Id}");
            if (permissionGranted && permissionFresh)
            {
                score += 40;
                reasons.Add("permission_current");
            }
            else reasons.Add(permissionFresh ? "permission_not_granted" : "permission_stale");
        }
        else reasons.Add("permission_missing");

        if (contact.CustomerCompanyId.HasValue)
        {
            score += definition.AudienceType == "b2b" ? 20 : 10;
            reasons.Add("customer_company_linked");
            evidence.Add($"customer_company:{contact.CustomerCompanyId}");
        }
        else if (definition.RequiresCustomerCompany) reasons.Add("customer_company_required");

        if (!string.IsNullOrWhiteSpace(contact.PreferredLanguage))
        {
            score += 10;
            reasons.Add("communication_language_known");
        }
        else reasons.Add("communication_language_missing");

        string status;
        if (suppressed)
        {
            status = MarketingQualificationStatuses.Excluded;
            reasons.Add("suppressed");
        }
        else if (!permissionGranted || !permissionFresh ||
                 (definition.RequiresCustomerCompany && !contact.CustomerCompanyId.HasValue))
        {
            status = MarketingQualificationStatuses.NotQualified;
        }
        else
        {
            status = score >= definition.Threshold
                ? MarketingQualificationStatuses.Qualified
                : MarketingQualificationStatuses.NotQualified;
            reasons.Add(status == MarketingQualificationStatuses.Qualified
                ? "threshold_met"
                : "threshold_not_met");
        }

        var observedUtc = permission?.ObservedUtc ?? contact.UpdatedUtc;
        var evaluation = new MarketingQualificationEvaluation(
            Guid.NewGuid(),
            companyId,
            definition.Id,
            definition.Version,
            contact.Id,
            Math.Min(score, 100),
            status,
            JsonSerializer.Serialize(reasons),
            JsonSerializer.Serialize(evidence),
            observedUtc,
            key);
        _db.MarketingQualificationEvaluations.Add(evaluation);
        await _db.SaveChangesAsync(ct);
        return Map(evaluation, contact.FullName);
    }

    public async Task<MarketingQualificationFeedbackDto> RecordQualificationFeedbackAsync(
        Guid companyId,
        Guid userId,
        Guid evaluationId,
        RecordMarketingQualificationFeedbackRequest request,
        CancellationToken ct)
    {
        var exists = await _db.MarketingQualificationEvaluations.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.CompanyId == companyId && x.Id == evaluationId, ct);
        if (!exists) throw new InvalidOperationException("The selected evaluation is not available to this company.");
        var feedback = new MarketingQualificationFeedback(
            Guid.NewGuid(), companyId, evaluationId, request.Decision, request.Reason,
            request.LeadId, request.DealId, userId);
        _db.MarketingQualificationFeedback.Add(feedback);
        await _db.SaveChangesAsync(ct);
        return new MarketingQualificationFeedbackDto(feedback.Id, feedback.MarketingQualificationEvaluationId,
            feedback.Decision, feedback.Reason, feedback.LinkedLeadId, feedback.LinkedDealId, feedback.CreatedUtc);
    }

    private static MarketingQualificationDefinitionDto Map(MarketingQualificationDefinition x) =>
        new(x.Id, x.Name, x.AudienceType, x.RequiredChannel, x.Threshold, x.FreshnessDays,
            x.RequiresCustomerCompany, x.EffectiveFromUtc, x.EffectiveToUtc, x.RulesJson,
            x.ExclusionsJson, x.Status, x.Version);

    private static MarketingQualificationEvaluationDto Map(MarketingQualificationEvaluation x, string contactName) =>
        new(x.Id, x.MarketingQualificationDefinitionId, x.DefinitionVersion, x.ContactId, contactName,
            x.Score, x.Status, DeserializeStrings(x.ReasonCodesJson), DeserializeStrings(x.EvidenceReferencesJson),
            x.EvidenceObservedUtc, x.EvaluatedUtc);

    private static IReadOnlyList<string> DeserializeStrings(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(json) ?? [];
        }
        catch (JsonException)
        {
            return ["Evidence could not be read."];
        }
    }
}
