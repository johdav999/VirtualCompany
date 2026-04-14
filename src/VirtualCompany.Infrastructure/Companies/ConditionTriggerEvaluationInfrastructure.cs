using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Agents;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class EfConditionTriggerEvaluationRepository : IConditionTriggerEvaluationRepository
{
    private readonly VirtualCompanyDbContext _dbContext;

    public EfConditionTriggerEvaluationRepository(VirtualCompanyDbContext dbContext) =>
        _dbContext = dbContext;

    public async Task<ConditionTriggerEvaluation?> GetLatestAsync(
        Guid companyId,
        string conditionDefinitionId,
        Guid? workflowTriggerId,
        CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (string.IsNullOrWhiteSpace(conditionDefinitionId))
        {
            throw new ArgumentException("ConditionDefinitionId is required.", nameof(conditionDefinitionId));
        }

        if (workflowTriggerId == Guid.Empty)
        {
            throw new ArgumentException("WorkflowTriggerId cannot be empty.", nameof(workflowTriggerId));
        }

        return await _dbContext.ConditionTriggerEvaluations
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.ConditionDefinitionId == conditionDefinitionId && x.WorkflowTriggerId == workflowTriggerId)
            .OrderByDescending(x => x.EvaluatedUtc)
            .ThenByDescending(x => x.CreatedUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task AddAsync(ConditionTriggerEvaluation evaluation, CancellationToken cancellationToken) =>
        await _dbContext.ConditionTriggerEvaluations.AddAsync(evaluation, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}

public sealed class MissingConditionMetricValueResolver : IConditionMetricValueResolver
{
    public Task<ConditionResolvedValue> ResolveMetricAsync(
        Guid companyId,
        string metricName,
        CancellationToken cancellationToken) =>
        Task.FromResult(ConditionResolvedValue.Missing(
            $"Metric '{metricName}' could not be resolved because no metric resolver has been configured."));
}

public sealed class MissingConditionEntityFieldValueResolver : IConditionEntityFieldValueResolver
{
    public Task<ConditionResolvedValue> ResolveEntityFieldAsync(
        Guid companyId,
        string entityType,
        string fieldPath,
        string? entityId,
        CancellationToken cancellationToken) =>
        Task.FromResult(ConditionResolvedValue.Missing(
            string.IsNullOrWhiteSpace(entityId)
                ? $"Entity field '{entityType}.{fieldPath}' could not be resolved without an entity id."
                : $"Entity field '{entityType}.{fieldPath}' for entity '{entityId}' could not be resolved because no entity-field resolver has been configured."));
}
