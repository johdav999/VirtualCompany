using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Observability;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class CompanyGoalService : ICompanyGoalCommandService, ICompanyGoalQueryService
{
    private readonly VirtualCompanyDbContext _db;
    private readonly ICompanyMembershipContextResolver _memberships;
    private readonly IAuditEventWriter _audit;
    private readonly ICorrelationContextAccessor _correlation;

    public CompanyGoalService(VirtualCompanyDbContext db, ICompanyMembershipContextResolver memberships,
        IAuditEventWriter audit, ICorrelationContextAccessor correlation)
    { _db = db; _memberships = memberships; _audit = audit; _correlation = correlation; }

    public async Task<CompanyGoalDto> CreateAsync(Guid companyId, CreateCompanyGoalCommand command, CancellationToken ct)
    {
        var member = await RequireManagerAsync(companyId, ct); ArgumentNullException.ThrowIfNull(command);
        var priority = ParsePriority(command.Priority); await ValidateOwnersAsync(companyId, command.OwnerUserId, command.OwnerAgentId, ct);
        CompanyGoal goal;
        try { goal = new CompanyGoal(Guid.NewGuid(), companyId, command.Name, command.Outcome, priority, command.StartUtc, command.TargetUtc, command.MetricKey, command.MetricUnit, command.BaselineValue, command.TargetValue, command.OwnerUserId, command.OwnerAgentId, command.Constraints); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { throw Validation("goal", ex.Message); }
        _db.CompanyGoals.Add(goal); await WriteAuditAsync(member, goal, "company.goal.created", "succeeded", "Company goal created.", command.CorrelationId, ct); await SaveAsync(ct); return Map(goal);
    }

    public async Task<CompanyGoalDto> UpdateAsync(Guid companyId, Guid goalId, UpdateCompanyGoalCommand command, CancellationToken ct)
    {
        var member = await RequireManagerAsync(companyId, ct); var goal = await FindAsync(companyId, goalId, ct); EnsureVersion(goal.Version, command.ExpectedVersion);
        await ValidateOwnersAsync(companyId, command.OwnerUserId, command.OwnerAgentId, ct);
        try { goal.Update(command.Name, command.Outcome, ParsePriority(command.Priority), command.StartUtc, command.TargetUtc, command.MetricKey, command.MetricUnit, command.BaselineValue, command.TargetValue, command.OwnerUserId, command.OwnerAgentId, command.Constraints); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { throw Validation("goal", ex.Message); }
        await WriteAuditAsync(member, goal, "company.goal.updated", "succeeded", "Company goal updated.", command.CorrelationId, ct); await SaveAsync(ct); return Map(goal);
    }

    public Task<CompanyGoalDto> ActivateAsync(Guid companyId, Guid goalId, int expectedVersion, string? correlationId, CancellationToken ct) => TransitionAsync(companyId, goalId, expectedVersion, correlationId, "company.goal.activated", x => x.Activate(), ct);
    public Task<CompanyGoalDto> PauseAsync(Guid companyId, Guid goalId, int expectedVersion, string? correlationId, CancellationToken ct) => TransitionAsync(companyId, goalId, expectedVersion, correlationId, "company.goal.paused", x => x.Pause(), ct);
    public Task<CompanyGoalDto> CompleteAsync(Guid companyId, Guid goalId, int expectedVersion, string? correlationId, CancellationToken ct) => TransitionAsync(companyId, goalId, expectedVersion, correlationId, "company.goal.completed", x => x.Complete(), ct);
    public Task<CompanyGoalDto> CancelAsync(Guid companyId, Guid goalId, int expectedVersion, string? correlationId, CancellationToken ct) => TransitionAsync(companyId, goalId, expectedVersion, correlationId, "company.goal.cancelled", x => x.Cancel(), ct);

    public async Task<CompanyGoalDto> GetAsync(Guid companyId, Guid goalId, CancellationToken ct) { await RequireMemberAsync(companyId, ct); return Map(await FindAsync(companyId, goalId, ct)); }
    public async Task<IReadOnlyList<CompanyGoalDto>> ListAsync(Guid companyId, string? status, CancellationToken ct)
    {
        await RequireMemberAsync(companyId, ct); var query = _db.CompanyGoals.AsNoTracking().Where(x => x.CompanyId == companyId);
        if (!string.IsNullOrWhiteSpace(status)) { CompanyGoalStatus parsed; try { parsed = CompanyGoalStatusValues.Parse(status); } catch { throw Validation("status", "Unsupported company goal status."); } query = query.Where(x => x.Status == parsed); }
        return (await query.OrderByDescending(x => x.Priority).ThenBy(x => x.TargetUtc).ToListAsync(ct)).Select(Map).ToList();
    }

    private async Task<CompanyGoalDto> TransitionAsync(Guid companyId, Guid goalId, int expectedVersion, string? correlationId, string action, Action<CompanyGoal> transition, CancellationToken ct)
    {
        var member = await RequireManagerAsync(companyId, ct); var goal = await FindAsync(companyId, goalId, ct); EnsureVersion(goal.Version, expectedVersion);
        try { transition(goal); } catch (InvalidOperationException ex) { throw Validation("status", ex.Message); }
        await WriteAuditAsync(member, goal, action, "succeeded", "Company goal status changed.", correlationId, ct); await SaveAsync(ct); return Map(goal);
    }

    private async Task ValidateOwnersAsync(Guid companyId, Guid? userId, Guid? agentId, CancellationToken ct)
    {
        if (agentId.HasValue && !await _db.Agents.AnyAsync(x => x.CompanyId == companyId && x.Id == agentId && x.Status == AgentStatus.Active, ct)) throw Validation("ownerAgentId", "The goal owner must be an active agent in this company.");
        if (userId.HasValue && !await _db.CompanyMemberships.AnyAsync(x => x.CompanyId == companyId && x.UserId == userId && x.Status == CompanyMembershipStatus.Active, ct)) throw Validation("ownerUserId", "The goal owner must be an active member of this company.");
    }
    private async Task<CompanyGoal> FindAsync(Guid companyId, Guid goalId, CancellationToken ct) => await _db.CompanyGoals.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == goalId, ct) ?? throw new KeyNotFoundException("Company goal not found.");
    private async Task SaveAsync(CancellationToken ct) { try { await _db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { throw new CompanyOperatingConcurrencyException("The company goal changed. Refresh and try again."); } }
    private static void EnsureVersion(int actual, int expected) { if (expected > 0 && actual != expected) throw new CompanyOperatingConcurrencyException("The company goal changed. Refresh and try again."); }
    private static CompanyGoalPriority ParsePriority(string value) { try { return CompanyGoalPriorityValues.Parse(value); } catch { throw Validation("priority", "Priority must be low, normal, high, or critical."); } }
    private async Task<ResolvedCompanyMembershipContext> RequireMemberAsync(Guid companyId, CancellationToken ct) => await _memberships.ResolveAsync(companyId, ct) ?? throw new UnauthorizedAccessException("Active company membership is required.");
    private async Task<ResolvedCompanyMembershipContext> RequireManagerAsync(Guid companyId, CancellationToken ct) { var m = await RequireMemberAsync(companyId, ct); if (m.MembershipRole is not (CompanyMembershipRole.Owner or CompanyMembershipRole.Admin or CompanyMembershipRole.Manager)) throw new UnauthorizedAccessException("Company manager access is required."); return m; }
    private async Task WriteAuditAsync(ResolvedCompanyMembershipContext member, CompanyGoal goal, string action, string outcome, string summary, string? correlationId, CancellationToken ct) => await _audit.WriteAsync(new AuditEventWriteRequest(goal.CompanyId, AuditActorTypes.User, member.UserId, action, "company_goal", goal.Id.ToString("N"), outcome, summary, Metadata: new Dictionary<string, string?> { ["status"] = goal.Status.ToStorageValue(), ["version"] = goal.Version.ToString() }, CorrelationId: string.IsNullOrWhiteSpace(correlationId) ? _correlation.CorrelationId : correlationId), ct);
    private static CompanyOperatingValidationException Validation(string key, string message) => new(new Dictionary<string, string[]> { [key] = [message] });
    private static CompanyGoalDto Map(CompanyGoal x) => new(x.Id, x.CompanyId, x.Name, x.Outcome, x.Status.ToStorageValue(), x.Priority.ToStorageValue(), x.MetricKey, x.MetricUnit, x.BaselineValue, x.TargetValue, x.StartUtc, x.TargetUtc, x.OwnerUserId, x.OwnerAgentId, x.Constraints, x.Version, x.CreatedUtc, x.UpdatedUtc, x.CompletedUtc);
}
