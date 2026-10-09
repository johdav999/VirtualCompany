using Microsoft.AspNetCore.Authorization;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Companies;

// Shared read boundary for the board, its detail, and the retained task/agent summaries.
public sealed class CompanyWorkVisibility(ITodayWorkspaceLensResolver lenses,
    ICurrentUserAccessor user, IAuthorizationService authorization)
{
    public async Task<CompanyWorkScope> ResolveAsync(Guid companyId, CancellationToken token)
    {
        var resolved = await lenses.ResolveAsync(companyId, null, token);
        var executive = resolved.AvailableLenses.Any(x => x.Lens == TodayWorkspaceLenses.Company && x.IsExecutiveOversight);
        var allowed = resolved.AvailableLenses.Where(x => x.Lens != TodayWorkspaceLenses.Company)
            .Select(x => x.Lens == TodayWorkspaceLenses.Customers ? "support" : x.Lens).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (executive) allowed.UnionWith(["company", "finance", "sales", "marketing", "support"]);
        if (!(await authorization.AuthorizeAsync(user.Principal, companyId, CompanyPolicies.FinanceView)).Succeeded)
            allowed.Remove("finance");
        return new(resolved, allowed, executive);
    }

    internal async Task<CompanyWorkScope> ResolveRecipientAsync(Guid companyId, Guid userId, CancellationToken ct)
    {
        var r = await ((CompanyTodayWorkspaceLensResolver)lenses).ResolveRecipientAsync(companyId, userId, ct);
        var executive = r.AvailableLenses.Any(x => x.Lens == TodayWorkspaceLenses.Company && x.IsExecutiveOversight);
        var allowed = r.AvailableLenses.Where(x => x.Lens != TodayWorkspaceLenses.Company)
            .Select(x => x.Lens == TodayWorkspaceLenses.Customers ? "support" : x.Lens).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (executive) allowed.UnionWith(["company", "finance", "sales", "marketing", "support"]);
        if (!VirtualCompany.Shared.FinanceAccess.CanView(r.MembershipRole.ToStorageValue())) allowed.Remove("finance");
        return new(r, allowed, executive);
    }
}

public sealed record CompanyWorkScope(TodayWorkspaceLensResolution Resolution, HashSet<string> Areas, bool Executive)
{
    public bool Allows(string area) => Areas.Contains(area);
    public IQueryable<Agent> Agents(IQueryable<Agent> source)
    {
        var finance = Allows("finance"); var sales = Allows("sales"); var marketing = Allows("marketing");
        var support = Allows("support"); var company = Allows("company");
        return source.Where(x => x.CompanyId == Resolution.CompanyId &&
            (x.Department.ToLower() == "finance" ? finance : x.Department.ToLower() == "sales" ? sales :
             x.Department.ToLower() == "marketing" ? marketing : x.Department.ToLower() == "support" ? support : company));
    }
    public string Human(string area) => Resolution.AvailableLenses.FirstOrDefault(x =>
        (x.Lens == TodayWorkspaceLenses.Customers ? "support" : x.Lens) == area)?.ResponsiblePerson
        ?? (Executive ? Resolution.AvailableLenses.FirstOrDefault(x => x.IsExecutiveOversight)?.ResponsiblePerson : null)
        ?? "Human owner not recorded";

    public IQueryable<WorkTask> Tasks(IQueryable<WorkTask> source)
    {
        var finance = Allows("finance"); var sales = Allows("sales"); var marketing = Allows("marketing");
        var support = Allows("support"); var company = Allows("company"); var userId = Resolution.UserId;
        // Scope both assigned department and task type. A differently assigned agent cannot open Finance evidence.
        return source.Where(x =>
            (x.AssignedAgentId == null || x.AssignedAgent!.CompanyId == Resolution.CompanyId) &&
            (x.AssignedAgentId == null ||
                (x.AssignedAgent!.Department.ToLower() == "finance" ? finance :
                 x.AssignedAgent.Department.ToLower() == "sales" ? sales :
                 x.AssignedAgent.Department.ToLower() == "marketing" ? marketing :
                 x.AssignedAgent.Department.ToLower() == "support" ? support : company)) &&
            ((x.Type.ToLower().StartsWith("finance") || x.Type.ToLower().StartsWith("accounting") ||
              x.Type.ToLower().StartsWith("supplier") || x.Type.ToLower().StartsWith("invoice") || x.Type.ToLower().StartsWith("payment")) ? finance :
             x.Type.ToLower().StartsWith("support") ? support :
             x.Type.ToLower().StartsWith("marketing") ? marketing :
             (x.Type.ToLower().StartsWith("sales") || x.Type.ToLower().StartsWith("lead") || x.Type.ToLower().StartsWith("deal") || x.Type.ToLower().StartsWith("campaign")) ? sales :
             (company || x.AssignedAgentId != null || x.CreatedByActorType == "user" && x.CreatedByActorId == userId || x.DecisionOrigin != null && x.DecisionOrigin.OwnerUserId == userId)));
    }

    public static string Area(string? department, string? type = null)
    {
        var value = (type ?? "").ToLowerInvariant();
        if (value.StartsWith("finance") || value.StartsWith("accounting") || value.StartsWith("supplier") || value.StartsWith("invoice") || value.StartsWith("payment")) return "finance";
        if (value.StartsWith("support")) return "support";
        if (value.StartsWith("marketing")) return "marketing";
        if (value.StartsWith("sales") || value.StartsWith("lead") || value.StartsWith("deal") || value.StartsWith("campaign")) return "sales";
        return department?.ToLowerInvariant() switch { "finance" => "finance", "sales" => "sales", "marketing" => "marketing", "support" => "support", _ => "company" };
    }
}
