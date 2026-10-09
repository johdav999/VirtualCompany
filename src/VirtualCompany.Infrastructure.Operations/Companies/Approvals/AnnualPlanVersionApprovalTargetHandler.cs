using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Application.Orchestration;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class AnnualPlanVersionApprovalTargetHandler : IApprovalTargetHandler
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IServiceProvider _serviceProvider;
    public AnnualPlanVersionApprovalTargetHandler(VirtualCompanyDbContext dbContext, IServiceProvider serviceProvider)
    {
        _dbContext = dbContext;
        _serviceProvider = serviceProvider;
    }

    public async Task<bool> ExistsAsync(Guid companyId, Guid targetEntityId, CancellationToken cancellationToken) => await _dbContext.Set<AnnualPlanVersion>().AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.Id == targetEntityId, cancellationToken);
    public async Task ValidateCreationAsync(Guid companyId, CreateApprovalRequestCommand command, Guid actorUserId, CancellationToken cancellationToken)
    {
        var annual = await _dbContext.Set<AnnualPlanVersion>().SingleAsync(x => x.CompanyId == companyId && x.Id == command.TargetEntityId, cancellationToken);
        if (annual.Status != AnnualPlanStates.Reviewed || annual.ApprovalId.HasValue || command.RequestedByActorType != "user" || command.RequestedByActorId != actorUserId || command.ApprovalType != "annual_plan_governance" || command.RequiredRole != "owner" || command.RequiredUserId.HasValue || command.Steps?.Count > 0 || command.ThresholdContext?.GetValueOrDefault("annualFingerprint")?.ToString() != annual.Fingerprint)
            throw new UnauthorizedAccessException("Annual approval must bind the owner's reviewed annual version and canonical owner review policy.");
    }

    public async Task BindCreatedAsync(ApprovalRequest approval, CancellationToken cancellationToken)
    {
        (await _dbContext.Set<AnnualPlanVersion>().SingleAsync(x => x.CompanyId == approval.CompanyId && x.Id == approval.TargetEntityId, cancellationToken)).BindApproval(approval.Id);
    }

    public async Task<object?> GetReviewMaterialAsync(ApprovalRequest approval, CancellationToken ct)
    {
        object? material = null;
        material = await _serviceProvider.GetRequiredService<VirtualCompany.Application.Orchestration.IAnnualPlanningService>().ApprovalMaterialAsync(approval.CompanyId, approval.TargetEntityId, ct);
        return material;
    }

    public async Task<ApprovalTargetReviewDetails> GetReviewDetailsAsync(ApprovalRequest approval, CancellationToken ct)
    {
        var comparisons = new List<ApprovalComparisonDto>();
        var evidence = new List<ApprovalEvidenceDto>();
        string? executionStatus = null;
        var annual = await _dbContext.Set<AnnualPlanVersion>().AsNoTracking().SingleAsync(x => x.CompanyId == approval.CompanyId && x.Id == approval.TargetEntityId, ct);
        comparisons.Add(new("Annual version", null, $"FY {annual.FiscalYear}, version {annual.Version}"));
        var proposedObjectives = await _dbContext.Set<AnnualObjective>().AsNoTracking().Where(x => x.CompanyId == approval.CompanyId && x.PlanId == annual.Id).OrderBy(x => x.GoalId).ToListAsync(ct);
        var beforeObjectives = annual.PreviousId.HasValue ? await _dbContext.Set<AnnualObjective>().AsNoTracking().Where(x => x.CompanyId == approval.CompanyId && x.PlanId == annual.PreviousId).ToListAsync(ct) : [];
        foreach (var objective in proposedObjectives)
        {
            var prior = beforeObjectives.SingleOrDefault(x => x.GoalId == objective.GoalId);
            comparisons.Add(new(objective.Name + " target", prior is null ? null : $"{prior.Target:0.####} {prior.Unit}", $"{objective.Target:0.####} {objective.Unit}"));
            comparisons.Add(new(objective.Name + " owner", prior?.OwnerName, objective.OwnerName));
        }

        var proposedAllocations = await _dbContext.Set<AnnualAllocation>().AsNoTracking().Where(x => x.CompanyId == approval.CompanyId && x.PlanId == annual.Id).OrderBy(x => x.Title).ToListAsync(ct);
        var beforeAllocations = annual.PreviousId.HasValue ? await _dbContext.Set<AnnualAllocation>().AsNoTracking().Where(x => x.CompanyId == approval.CompanyId && x.PlanId == annual.PreviousId).OrderBy(x => x.Title).ToListAsync(ct) : [];
        comparisons.Add(new("Expense allocations", annual.PreviousId.HasValue ? string.Join("; ", beforeAllocations.Select(x => $"{x.Title}: {x.Amount:0.##} {annual.Currency}")) : null, string.Join("; ", proposedAllocations.Select(x => $"{x.Title}: {x.Amount:0.##} {annual.Currency}"))));
        executionStatus = "Planning governance only; no execution authorized";
        evidence.Add(new("Open exact annual targets, budgets and version comparison", $"/dashboard/planning/year?companyId={annual.CompanyId}&year={annual.FiscalYear}&plan={annual.Id}"));
        return new(comparisons, evidence, executionStatus);
    }

    public Task<bool> CanReadAsync(ApprovalRequest approval, CancellationToken ct) => _serviceProvider.GetRequiredService<IAnnualPlanningService>().CanReadApprovalAsync(approval.CompanyId, approval.TargetEntityId, ct);
    public async Task<ApprovalTargetStateTransition?> ApplyDecisionAsync(ApprovalRequest approval, CancellationToken cancellationToken)
    {
        if (approval.Status == ApprovalRequestStatus.Pending)
            return null;
        await _serviceProvider.GetRequiredService<VirtualCompany.Application.Orchestration.IAnnualPlanningService>().ApplyDecisionAsync(approval.CompanyId, approval.TargetEntityId, approval.Id, approval.Status.ToStorageValue(), cancellationToken);
        return null;
    }
}
