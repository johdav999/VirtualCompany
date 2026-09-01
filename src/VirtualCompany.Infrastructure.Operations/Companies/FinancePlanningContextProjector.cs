using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class FinancePlanningContextProjector : IFinancePlanningContextProjector
{
    private readonly IAgentEffectiveAuthorityResolver _authorityResolver;
    private readonly IFinanceAgentAuthorizationService _actorAuthorization;
    private readonly ICompanyToolRegistry _registry;
    private readonly IFinancePlanningEntityResolver _entityResolver;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly TimeProvider _timeProvider;

    public FinancePlanningContextProjector(
        IAgentEffectiveAuthorityResolver authorityResolver,
        IFinanceAgentAuthorizationService actorAuthorization,
        ICompanyToolRegistry registry,
        IFinancePlanningEntityResolver entityResolver,
        ICurrentUserAccessor currentUser,
        TimeProvider timeProvider)
    {
        _authorityResolver = authorityResolver;
        _actorAuthorization = actorAuthorization;
        _registry = registry;
        _entityResolver = entityResolver;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
    }

    public async Task<FinancePlanningContextBundle> ProjectAsync(
        FinancePlanningContextProjectionRequest request,
        AgentEffectiveAuthorityDto authority,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request, authority);
        var tools = new List<FinanceProjectedToolManifest>();
        var policyVersions = new List<string>();

        foreach (var item in authority.Tools
                     .Where(item => item.IsUsable && string.Equals(item.Scope, "finance", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(item => item.ToolName, StringComparer.OrdinalIgnoreCase))
        {
            if (!ToolActionTypeValues.TryParse(item.ActionType, out var action) ||
                !_registry.TryGetToolDefinition(item.ToolName, out var definition) ||
                !_registry.TryGetTool(item.ToolName, out var registration) ||
                definition.SelectionMetadata is null ||
                !string.Equals(item.ToolVersion, definition.Version, StringComparison.Ordinal) ||
                definition.ActionType != action ||
                !registration.Supports(action, item.Scope))
            {
                continue;
            }

            var actor = await _actorAuthorization.AuthorizeAsync(new FinanceAgentAuthorizationRequest(
                request.CompanyId,
                request.AgentId,
                Guid.NewGuid(),
                item.ToolName,
                action,
                item.Scope,
                null,
                request.CorrelationId,
                _currentUser.UserId), cancellationToken);
            policyVersions.Add($"{item.ToolName}:{actor.PolicyVersion}:{actor.Outcome}");
            if (!actor.IsAllowed)
            {
                continue;
            }

            var metadata = definition.SelectionMetadata;
            tools.Add(new FinanceProjectedToolManifest(
                definition.ToolName,
                definition.Version,
                metadata.ActionClass,
                item.Scope,
                SafeText(metadata.SafePurpose, 300),
                metadata.TargetEntityTypes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                SafeText(metadata.SideEffectSummary, 300),
                metadata.RequiredEvidenceTypes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                Math.Clamp(metadata.MaximumEvidenceAgeSeconds, 1, 31_536_000),
                metadata.ConfirmationBehavior,
                ResolveApprovalBehavior(metadata.ApprovalBehavior, item),
                SafeText(metadata.ResultSemantics, 300),
                metadata.NaturalLanguageExamples.Take(3).Select(value => SafeText(value, 200)).ToArray(),
                Rank(request.UserRequest, metadata),
                RedactSchema(definition.InputSchema),
                item.State));
        }

        var references = FinancePlanningReferenceParser.Extract(request.UserRequest, request.ExplicitReferences);
        var supportedTargets = tools.SelectMany(tool => tool.TargetEntityTypes).ToHashSet(StringComparer.Ordinal);
        var relevantReferences = references.Where(reference => supportedTargets.Contains(reference.Type)).ToArray();
        var evidence = new List<FinancePlanningEvidenceReference>();
        var unresolved = new List<FinancePlanningReference>();
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        foreach (var reference in relevantReferences)
        {
            var resolution = await _entityResolver.ResolveAsync(new FinanceEntityResolutionRequest(
                request.CompanyId,
                reference.Type,
                reference.Value,
                Math.Min(request.MaximumEvidenceRecords, 10)), cancellationToken);
            if (resolution.State != FinanceEntityResolutionStates.Resolved || resolution.Candidates.Count != 1)
            {
                unresolved.Add(reference);
                continue;
            }

            var candidate = resolution.Candidates[0];
            var maximumAge = tools.Where(tool => tool.TargetEntityTypes.Contains(reference.Type, StringComparer.Ordinal))
                .Select(tool => tool.MaximumEvidenceAgeSeconds)
                .DefaultIfEmpty(300)
                .Min();
            evidence.Add(new FinancePlanningEvidenceReference(
                candidate.SourceId,
                candidate.SourceVersion,
                candidate.EntityType,
                candidate.EntityId,
                SafeEvidenceLabel(candidate.EntityType),
                candidate.UpdatedUtc,
                candidate.UpdatedUtc >= now.AddSeconds(-maximumAge)));
        }

        evidence = evidence.GroupBy(item => item.SourceId, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
        if (evidence.Count > request.MaximumEvidenceRecords)
        {
            evidence = evidence.Take(request.MaximumEvidenceRecords).ToList();
            unresolved.AddRange(relevantReferences.Skip(request.MaximumEvidenceRecords));
        }

        tools = tools.OrderByDescending(tool => tool.RankingScore)
            .ThenBy(tool => tool.ToolName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        evidence = evidence.OrderBy(item => item.SourceId, StringComparer.Ordinal).ToList();
        unresolved = unresolved.Distinct().OrderBy(item => item.Type, StringComparer.Ordinal)
            .ThenBy(item => item.Value, StringComparer.OrdinalIgnoreCase).ToList();

        var state = unresolved.Count == 0
            ? FinancePlanningResolutionStates.Ready
            : FinancePlanningResolutionStates.NeedsClarification;
        var reason = unresolved.Count == 0
            ? "finance_planning_context_ready"
            : "finance_planning_target_ambiguous_or_unavailable";
        var explanation = unresolved.Count == 0
            ? "Permitted Finance tools and accessible target evidence were projected for planning."
            : "One or more Finance references are ambiguous or unavailable within the current access boundary.";
        var hash = ComputeHash(request, authority, tools, evidence, unresolved, policyVersions, state);

        return new FinancePlanningContextBundle(
            FinancePlanningContextVersions.V1,
            hash,
            request.CompanyId,
            request.AgentId,
            state,
            reason,
            explanation,
            authority.AuthorityVersion,
            authority.AuthorityHash,
            tools,
            evidence,
            unresolved,
            now);
    }

    public async Task<FinancePlanningContextFreshnessResult> CheckFreshnessAsync(
        FinancePlanningContextProjectionRequest request,
        string expectedHash,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(expectedHash))
        {
            throw new ArgumentException("An expected planning-context hash is required.", nameof(expectedHash));
        }

        var authority = await _authorityResolver.ResolveAsync(request.CompanyId, request.AgentId, cancellationToken);
        var current = await ProjectAsync(request, authority, cancellationToken);
        var isCurrent = CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expectedHash.Trim().ToLowerInvariant()),
            Encoding.ASCII.GetBytes(current.Hash));
        return new FinancePlanningContextFreshnessResult(
            isCurrent,
            expectedHash.Trim().ToLowerInvariant(),
            current.Hash,
            isCurrent ? "finance_planning_context_current" : "finance_planning_context_stale");
    }

    private static string ComputeHash(
        FinancePlanningContextProjectionRequest request,
        AgentEffectiveAuthorityDto authority,
        IReadOnlyList<FinanceProjectedToolManifest> tools,
        IReadOnlyList<FinancePlanningEvidenceReference> evidence,
        IReadOnlyList<FinancePlanningReference> unresolved,
        IReadOnlyList<string> policyVersions,
        string state)
    {
        var value = JsonSerializer.Serialize(new
        {
            version = FinancePlanningContextVersions.V1,
            request.CompanyId,
            request.AgentId,
            authority.AuthorityVersion,
            authority.AuthorityHash,
            policyVersions = policyVersions.Order(StringComparer.Ordinal).ToArray(),
            state,
            tools = tools.OrderBy(tool => tool.ToolName, StringComparer.Ordinal).Select(tool => new
            {
                tool.ToolName,
                tool.ToolVersion,
                tool.ActionClass,
                tool.Scope,
                tool.SafePurpose,
                tool.TargetEntityTypes,
                tool.SideEffectSummary,
                tool.RequiredEvidenceTypes,
                tool.MaximumEvidenceAgeSeconds,
                tool.ConfirmationBehavior,
                tool.ApprovalBehavior,
                tool.ResultSemantics,
                tool.NaturalLanguageExamples,
                inputSchema = Canonicalize(tool.InputSchema)
            }),
            evidence = evidence.Select(item => new
            {
                item.SourceId,
                item.SourceVersion,
                item.EntityType,
                item.EntityId,
                item.IsFresh
            }),
            unresolved
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private static JsonObject RedactSchema(JsonObject schema)
    {
        // Selection receives the authoritative public input contract only. Output payloads, risk internals,
        // provider details, policy requirements, and implementation metadata are deliberately not projected.
        return Canonicalize(schema).AsObject();
    }

    private static JsonNode Canonicalize(JsonNode node) => node switch
    {
        JsonObject obj => new JsonObject(obj.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => KeyValuePair.Create(pair.Key, pair.Value is null ? null : Canonicalize(pair.Value))).ToArray()),
        JsonArray array => new JsonArray(array.Select(item => item is null ? null : Canonicalize(item)).ToArray()),
        _ => node.DeepClone()
    };

    private static int Rank(string request, ToolSelectionMetadata metadata)
    {
        var text = request.ToLowerInvariant();
        var score = metadata.RankingIntents.Count(intent =>
            intent.Length > 2 && text.Contains(intent.ToLowerInvariant(), StringComparison.Ordinal)) * 20;
        if (metadata.NaturalLanguageExamples.Any(example =>
                example.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Count(word => word.Length > 3 && text.Contains(word.ToLowerInvariant(), StringComparison.Ordinal)) >= 2))
        {
            score += 10;
        }
        return Math.Min(score, 100);
    }

    private static string ResolveApprovalBehavior(string declared, EffectiveAgentToolAuthorityDto authority) =>
        authority.State == AgentCapabilityStates.ApprovalRequired ||
        string.Equals(authority.ApprovalBehavior, "required", StringComparison.OrdinalIgnoreCase)
            ? "required"
            : declared;

    private static string SafeText(string value, int maximum)
    {
        var text = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text.Length <= maximum ? text : text[..maximum];
    }

    private static string SafeEvidenceLabel(string entityType) => entityType switch
    {
        FinancePlanningReferenceTypes.Invoice => "Accessible invoice match",
        FinancePlanningReferenceTypes.Bill => "Accessible supplier bill match",
        FinancePlanningReferenceTypes.Customer => "Accessible customer match",
        FinancePlanningReferenceTypes.Supplier => "Accessible supplier match",
        FinancePlanningReferenceTypes.FiscalPeriod => "Accessible fiscal period match",
        FinancePlanningReferenceTypes.Migration => "Accessible accounting migration match",
        FinancePlanningReferenceTypes.Account => "Accessible account match",
        FinancePlanningReferenceTypes.Journal => "Accessible journal match",
        FinancePlanningReferenceTypes.VoucherSeries => "Accessible voucher-series match",
        FinancePlanningReferenceTypes.ReportDefinition => "Accessible report-definition match",
        FinancePlanningReferenceTypes.ReportLine => "Accessible report-line match",
        _ => "Accessible Finance record match"
    };

    private static void ValidateRequest(
        FinancePlanningContextProjectionRequest request,
        AgentEffectiveAuthorityDto authority)
    {
        if (request.CompanyId == Guid.Empty || request.AgentId == Guid.Empty ||
            authority.CompanyId != request.CompanyId || authority.AgentId != request.AgentId)
        {
            throw new ArgumentException("The projection and authority must identify the same company and agent.");
        }
        if (string.IsNullOrWhiteSpace(request.UserRequest) || request.UserRequest.Length > 8_000)
        {
            throw new ArgumentException("A bounded Finance request is required.");
        }
        if (request.MaximumEvidenceRecords is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(request.MaximumEvidenceRecords));
        }
    }
}

internal static partial class FinancePlanningReferenceParser
{
    private const int MaximumReferenceLength = 128;
    private static readonly IReadOnlyDictionary<string, Regex> Patterns = new Dictionary<string, Regex>(StringComparer.Ordinal)
    {
        [FinancePlanningReferenceTypes.Invoice] = ReferencePattern("invoice"),
        [FinancePlanningReferenceTypes.Bill] = ReferencePattern("bill"),
        [FinancePlanningReferenceTypes.Customer] = NamedReferencePattern("customer"),
        [FinancePlanningReferenceTypes.Supplier] = NamedReferencePattern("supplier"),
        [FinancePlanningReferenceTypes.FiscalPeriod] = NamedReferencePattern("period"),
        [FinancePlanningReferenceTypes.Migration] = ReferencePattern("migration"),
        [FinancePlanningReferenceTypes.Account] = ReferencePattern("account"),
        [FinancePlanningReferenceTypes.Journal] = ReferencePattern("journal"),
        [FinancePlanningReferenceTypes.VoucherSeries] = ReferencePattern("voucher series"),
        [FinancePlanningReferenceTypes.ReportDefinition] = NamedReferencePattern("report definition"),
        [FinancePlanningReferenceTypes.ReportLine] = NamedReferencePattern("report line")
    };

    public static IReadOnlyList<FinancePlanningReference> Extract(
        string userRequest,
        IReadOnlyList<FinancePlanningReference>? explicitReferences)
    {
        var values = new List<FinancePlanningReference>();
        if (explicitReferences is not null)
        {
            foreach (var reference in explicitReferences)
            {
                Add(values, reference.Type, reference.Value);
            }
        }

        foreach (var (type, pattern) in Patterns)
        {
            foreach (Match match in pattern.Matches(userRequest))
            {
                var value = match.Groups["quoted"].Success
                    ? match.Groups["quoted"].Value
                    : match.Groups["value"].Value;
                Add(values, type, value);
            }
        }

        return values.Distinct().Take(20).ToArray();
    }

    private static void Add(List<FinancePlanningReference> values, string type, string value)
    {
        var normalizedType = type?.Trim().ToLowerInvariant();
        var normalizedValue = value?.Trim().TrimEnd('.', ',', ';', ':');
        if (normalizedType is null || !FinancePlanningReferenceTypes.All.Contains(normalizedType) ||
            string.IsNullOrWhiteSpace(normalizedValue) || normalizedValue.Length > MaximumReferenceLength)
        {
            return;
        }
        values.Add(new FinancePlanningReference(normalizedType, normalizedValue));
    }

    private static Regex ReferencePattern(string label) => new(
        $"\\b{Regex.Escape(label)}\\s+(?:number\\s+|#\\s*)?(?:\"(?<quoted>[^\"]{{1,128}})\"|(?<value>[A-Za-z0-9][A-Za-z0-9._/-]{{0,127}}))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static Regex NamedReferencePattern(string label) => new(
        $"\\b{Regex.Escape(label)}\\s+(?:named\\s+)?(?:\"(?<quoted>[^\"]{{1,128}})\"|(?<value>[A-Za-z0-9][A-Za-z0-9 .&'/-]{{0,127}}?))(?=$|[,.!?;]|\\s+(?:for|from|with|during|and|then|that|who|which|while)\\b)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
}
