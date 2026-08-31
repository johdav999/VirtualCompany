using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class AccountingDimensionPostingPolicy : IAccountingDimensionPostingPolicy
{
    private readonly VirtualCompanyDbContext _db;

    public AccountingDimensionPostingPolicy(VirtualCompanyDbContext db) => _db = db;

    public async Task<AccountingDimensionPostingDecision> EvaluateAsync(
        ProposedAccountingEntry entry,
        CancellationToken cancellationToken)
    {
        if (entry.CompanyId == Guid.Empty || entry.Lines is null)
            return new([], new Dictionary<int, IReadOnlyList<ResolvedAccountingDimensionAssignment>>());

        var types = await _db.AccountingDimensionTypes.AsNoTracking()
            .Where(x => x.CompanyId == entry.CompanyId)
            .Include(x => x.Members)
            .ToListAsync(cancellationToken);
        var policies = await _db.AccountingDimensionAccountPolicies.AsNoTracking()
            .Where(x => x.CompanyId == entry.CompanyId && x.EffectiveFrom <= entry.PostingDate &&
                (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= entry.PostingDate))
            .ToListAsync(cancellationToken);
        var rules = await _db.AccountingDimensionCombinationRules.AsNoTracking()
            .Where(x => x.CompanyId == entry.CompanyId && x.EffectiveFrom <= entry.PostingDate &&
                (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= entry.PostingDate))
            .Include(x => x.LeftMember).Include(x => x.RightMember)
            .ToListAsync(cancellationToken);
        var mappings = await _db.AccountingDimensionExternalMappings.AsNoTracking()
            .Where(x => x.CompanyId == entry.CompanyId && x.EffectiveFrom <= entry.PostingDate &&
                (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= entry.PostingDate))
            .ToListAsync(cancellationToken);

        var typeById = types.ToDictionary(x => x.Id);
        var typeByCode = types.ToDictionary(x => NormalizeCode(x.Code), StringComparer.OrdinalIgnoreCase);
        var memberById = types.SelectMany(x => x.Members).ToDictionary(x => x.Id);
        var provider = ResolveProvider(entry);
        var issues = new List<AccountingPostingIssue>();
        var assignments = new Dictionary<int, IReadOnlyList<ResolvedAccountingDimensionAssignment>>();

        for (var index = 0; index < entry.Lines.Count; index++)
        {
            var line = entry.Lines[index];
            var resolved = new Dictionary<Guid, AccountingDimensionMember>();

            foreach (var memberId in line.DimensionMemberIds ?? [])
            {
                if (!memberById.TryGetValue(memberId, out var member))
                {
                    issues.Add(new(AccountingDimensionReasonCodes.Invalid,
                        "A selected accounting dimension member could not be found in this company.", memberId));
                    continue;
                }
                AddMember(resolved, member, issues, line.FinanceAccountId);
            }

            if (line.CostCenterId is Guid costCenterId)
            {
                if (memberById.TryGetValue(costCenterId, out var member) &&
                    typeById.GetValueOrDefault(member.DimensionTypeId)?.Code == AccountingDimensionCodes.CostCenter)
                    AddMember(resolved, member, issues, line.FinanceAccountId);
                else
                    issues.Add(new(AccountingDimensionReasonCodes.MappingConflict,
                        "The legacy cost-center value is not mapped to one governed cost-center member.", costCenterId));
            }

            foreach (var fact in line.DimensionFacts ?? new Dictionary<string, string>())
            {
                if (!typeByCode.TryGetValue(NormalizeCode(fact.Key), out var type)) continue;
                var direct = type.Members.Where(x => string.Equals(x.Code, fact.Value.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
                AccountingDimensionMember? member = direct.Length == 1 ? direct[0] : null;
                if (member is null)
                {
                    var external = mappings.Where(x => x.DimensionTypeId == type.Id &&
                        string.Equals(x.ProviderKey, provider, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(x.ExternalDimensionType, NormalizeCode(fact.Key), StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(x.ExternalValue, fact.Value.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
                    if (external.Length == 1) memberById.TryGetValue(external[0].DimensionMemberId, out member);
                }
                if (member is null)
                {
                    issues.Add(new(AccountingDimensionReasonCodes.MappingConflict,
                        $"The {type.Name} value '{fact.Value}' does not resolve to one governed member.", line.FinanceAccountId));
                    continue;
                }
                AddMember(resolved, member, issues, line.FinanceAccountId);
            }

            foreach (var member in resolved.Values)
            {
                var type = typeById[member.DimensionTypeId];
                if (!IsActive(type.Status, type.EffectiveFrom, type.EffectiveTo, entry.PostingDate) ||
                    !IsActive(member.Status, member.EffectiveFrom, member.EffectiveTo, entry.PostingDate))
                    issues.Add(new(AccountingDimensionReasonCodes.Inactive,
                        $"{type.Name} member {member.Code} is not active on the posting date.", member.Id));
            }

            foreach (var policy in policies.Where(x => x.FinanceAccountId == line.FinanceAccountId))
            {
                var hasAssignment = resolved.ContainsKey(policy.DimensionTypeId);
                var typeName = typeById.GetValueOrDefault(policy.DimensionTypeId)?.Name ?? "The required dimension";
                if (policy.Requirement == VirtualCompany.Domain.Entities.AccountingDimensionRequirementValues.Required && !hasAssignment)
                    issues.Add(new(AccountingDimensionReasonCodes.Required,
                        $"{typeName} is required for this account.", line.FinanceAccountId));
                else if (policy.Requirement == VirtualCompany.Domain.Entities.AccountingDimensionRequirementValues.Prohibited && hasAssignment)
                    issues.Add(new(AccountingDimensionReasonCodes.Prohibited,
                        $"{typeName} is not allowed for this account.", line.FinanceAccountId));
            }

            ValidateCombinations(resolved.Values.ToArray(), rules, issues, line.FinanceAccountId);
            assignments[index] = resolved.Values.OrderBy(x => typeById[x.DimensionTypeId].Code)
                .Select(member =>
                {
                    var type = typeById[member.DimensionTypeId];
                    return new ResolvedAccountingDimensionAssignment(type.Id, type.Code, type.Name, member.Id,
                        member.Code, member.Name, BuildPath(member, memberById));
                }).ToArray();
        }

        return new(issues, assignments);
    }

    private static void AddMember(IDictionary<Guid, AccountingDimensionMember> resolved,
        AccountingDimensionMember member, ICollection<AccountingPostingIssue> issues, Guid accountId)
    {
        if (resolved.TryGetValue(member.DimensionTypeId, out var existing) && existing.Id != member.Id)
            issues.Add(new(AccountingDimensionReasonCodes.Invalid,
                "A journal line cannot contain multiple members of the same accounting dimension.", accountId));
        else
            resolved[member.DimensionTypeId] = member;
    }

    private static void ValidateCombinations(IReadOnlyList<AccountingDimensionMember> members,
        IReadOnlyList<AccountingDimensionCombinationRule> rules, ICollection<AccountingPostingIssue> issues, Guid accountId)
    {
        for (var left = 0; left < members.Count; left++)
        for (var right = left + 1; right < members.Count; right++)
        {
            var a = members[left]; var b = members[right];
            var pairRules = rules.Where(rule =>
                rule.LeftMember.DimensionTypeId == a.DimensionTypeId && rule.RightMember.DimensionTypeId == b.DimensionTypeId ||
                rule.LeftMember.DimensionTypeId == b.DimensionTypeId && rule.RightMember.DimensionTypeId == a.DimensionTypeId).ToArray();
            if (pairRules.Length == 0) continue;
            var exact = pairRules.Where(rule =>
                rule.LeftMemberId == a.Id && rule.RightMemberId == b.Id ||
                rule.LeftMemberId == b.Id && rule.RightMemberId == a.Id).ToArray();
            if (exact.Any(rule => !rule.IsAllowed) || pairRules.Any(rule => rule.IsAllowed) && !exact.Any(rule => rule.IsAllowed))
                issues.Add(new(AccountingDimensionReasonCodes.CombinationInvalid,
                    $"The dimension combination {a.Code} + {b.Code} is not allowed.", accountId));
        }
    }

    private static string BuildPath(AccountingDimensionMember member, IReadOnlyDictionary<Guid, AccountingDimensionMember> members)
    {
        var path = new Stack<string>(); var current = member; var visited = new HashSet<Guid>();
        while (visited.Add(current.Id))
        {
            path.Push(current.Code);
            if (!current.ParentMemberId.HasValue || !members.TryGetValue(current.ParentMemberId.Value, out current!)) break;
        }
        return string.Join(" / ", path);
    }

    private static bool IsActive(string status, DateOnly from, DateOnly? to, DateOnly date) =>
        status == AccountingDimensionStatusValues.Active && from <= date && (!to.HasValue || to.Value >= date);

    private static string ResolveProvider(ProposedAccountingEntry entry)
    {
        if (entry.PolicyFacts?.TryGetValue("dimensionProvider", out var provider) == true && !string.IsNullOrWhiteSpace(provider))
            return NormalizeCode(provider);
        return entry.SourceType.Contains("fortnox", StringComparison.OrdinalIgnoreCase) ? "fortnox" : "internal";
    }

    private static string NormalizeCode(string value) => value.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_') switch
    {
        "costcenter" or "cost_centre" or "costcentre" => AccountingDimensionCodes.CostCenter,
        var normalized => normalized
    };
}
