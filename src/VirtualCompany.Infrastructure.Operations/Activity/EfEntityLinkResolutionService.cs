using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Activity;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Activity;

public sealed class EfEntityLinkResolutionService : IEntityLinkResolutionService
{
    private readonly VirtualCompanyDbContext _dbContext;

    public EfEntityLinkResolutionService(VirtualCompanyDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyDictionary<string, ActivityLinkedEntityDto>> ResolveAsync(
        Guid tenantId,
        IEnumerable<ActivityEntityReferenceDto> references,
        CancellationToken cancellationToken)
    {
        var distinct = references
            .Where(x => x.EntityId != Guid.Empty && ActivityEntityTypes.IsSupported(x.EntityType))
            .GroupBy(x => ActivityEntityTypes.ToKey(x.EntityType, x.EntityId), StringComparer.OrdinalIgnoreCase)
            .Select(x => new ActivityEntityReferenceDto(ActivityEntityTypes.Normalize(x.First().EntityType), x.First().EntityId))
            .ToList();

        var resolved = distinct.ToDictionary(
            x => ActivityEntityTypes.ToKey(x.EntityType, x.EntityId),
            x => Missing(x.EntityType, x.EntityId),
            StringComparer.OrdinalIgnoreCase);

        await ResolveTasksAsync(tenantId, distinct, resolved, cancellationToken);
        await ResolveWorkflowInstancesAsync(tenantId, distinct, resolved, cancellationToken);
        await ResolveApprovalsAsync(tenantId, distinct, resolved, cancellationToken);
        await ResolveToolExecutionsAsync(tenantId, distinct, resolved, cancellationToken);

        return resolved;
    }

    private async Task ResolveTasksAsync(
        Guid tenantId,
        IReadOnlyList<ActivityEntityReferenceDto> references,
        IDictionary<string, ActivityLinkedEntityDto> resolved,
        CancellationToken cancellationToken)
    {
        var ids = references.Where(x => x.EntityType == ActivityEntityTypes.Task).Select(x => x.EntityId).Distinct().ToList();
        if (ids.Count == 0)
        {
            return;
        }

        var rows = await _dbContext.WorkTasks.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == tenantId && ids.Contains(x.Id))
            .Select(x => new { x.Id, x.Title, x.Status, x.UpdatedUtc })
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            resolved[ActivityEntityTypes.ToKey(ActivityEntityTypes.Task, row.Id)] = Available(
                ActivityEntityTypes.Task,
                row.Id,
                row.Title,
                row.Status.ToStorageValue(),
                row.UpdatedUtc);
        }
    }

    private async Task ResolveWorkflowInstancesAsync(
        Guid tenantId,
        IReadOnlyList<ActivityEntityReferenceDto> references,
        IDictionary<string, ActivityLinkedEntityDto> resolved,
        CancellationToken cancellationToken)
    {
        var ids = references.Where(x => x.EntityType == ActivityEntityTypes.WorkflowInstance).Select(x => x.EntityId).Distinct().ToList();
        if (ids.Count == 0)
        {
            return;
        }

        var rows = await (
            from instance in _dbContext.WorkflowInstances.IgnoreQueryFilters().AsNoTracking()
            join definition in _dbContext.WorkflowDefinitions.IgnoreQueryFilters().AsNoTracking()
                on instance.DefinitionId equals definition.Id into definitions
            from definition in definitions.DefaultIfEmpty()
            where instance.CompanyId == tenantId && ids.Contains(instance.Id)
            select new
            {
                instance.Id,
                instance.State,
                instance.CurrentStep,
                instance.UpdatedUtc,
                DefinitionName = definition == null ? null : definition.Name
            })
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            var metadata = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(row.CurrentStep))
            {
                metadata["currentStep"] = JsonValue.Create(row.CurrentStep);
            }

            resolved[ActivityEntityTypes.ToKey(ActivityEntityTypes.WorkflowInstance, row.Id)] = Available(
                ActivityEntityTypes.WorkflowInstance,
                row.Id,
                row.DefinitionName ?? $"Workflow {row.Id:N}",
                row.State.ToStorageValue(),
                row.UpdatedUtc,
                metadata);
        }
    }

    private async Task ResolveApprovalsAsync(
        Guid tenantId,
        IReadOnlyList<ActivityEntityReferenceDto> references,
        IDictionary<string, ActivityLinkedEntityDto> resolved,
        CancellationToken cancellationToken)
    {
        var ids = references.Where(x => x.EntityType == ActivityEntityTypes.Approval).Select(x => x.EntityId).Distinct().ToList();
        if (ids.Count == 0)
        {
            return;
        }

        var rows = await _dbContext.ApprovalRequests.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == tenantId && ids.Contains(x.Id))
            .Select(x => new { x.Id, x.ApprovalType, x.ToolName, x.TargetEntityType, x.TargetEntityId, x.Status, x.UpdatedUtc })
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            resolved[ActivityEntityTypes.ToKey(ActivityEntityTypes.Approval, row.Id)] = Available(
                ActivityEntityTypes.Approval,
                row.Id,
                string.IsNullOrWhiteSpace(row.ApprovalType) ? row.ToolName : row.ApprovalType,
                row.Status.ToStorageValue(),
                row.UpdatedUtc,
                new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["targetEntityType"] = JsonValue.Create(row.TargetEntityType),
                    ["targetEntityId"] = JsonValue.Create(row.TargetEntityId)
                });
        }
    }

    private async Task ResolveToolExecutionsAsync(
        Guid tenantId,
        IReadOnlyList<ActivityEntityReferenceDto> references,
        IDictionary<string, ActivityLinkedEntityDto> resolved,
        CancellationToken cancellationToken)
    {
        var ids = references.Where(x => x.EntityType == ActivityEntityTypes.ToolExecution).Select(x => x.EntityId).Distinct().ToList();
        if (ids.Count == 0)
        {
            return;
        }

        var rows = await _dbContext.ToolExecutionAttempts.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == tenantId && ids.Contains(x.Id))
            .Select(x => new { x.Id, x.ToolName, x.Status, x.UpdatedUtc, x.TaskId, x.WorkflowInstanceId, x.ApprovalRequestId })
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            var metadata = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["taskId"] = row.TaskId,
                ["workflowInstanceId"] = row.WorkflowInstanceId,
                ["approvalRequestId"] = row.ApprovalRequestId
            };

            resolved[ActivityEntityTypes.ToKey(ActivityEntityTypes.ToolExecution, row.Id)] = Available(
                ActivityEntityTypes.ToolExecution,
                row.Id,
                row.ToolName,
                row.Status.ToStorageValue(),
                row.UpdatedUtc,
                metadata);
        }
    }

    private static ActivityLinkedEntityDto Available(
        string entityType,
        Guid entityId,
        string displayText,
        string currentStatus,
        DateTime lastUpdatedAt,
        Dictionary<string, JsonNode?>? metadata = null) =>
        new(entityType, entityId, ActivityLinkedEntityAvailability.Available, displayText, currentStatus, lastUpdatedAt, true, null, metadata ?? []);

    private static ActivityLinkedEntityDto Missing(string entityType, Guid entityId) =>
        Unavailable(entityType, entityId, ActivityLinkedEntityAvailability.UnavailableMissing, "missing");

    private static ActivityLinkedEntityDto Unavailable(
        string entityType,
        Guid entityId,
        string availability,
        string unavailableReason) =>
        new(entityType, entityId, availability, "Unavailable linked entity", null, null, false, unavailableReason, []);
}
