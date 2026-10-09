using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Application.Tasks;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Companies;

public sealed partial class MultiAgentCoordinator
{
    private static Guid StableStepId(Guid parent, int sequence) => new(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{parent:D}:step:{sequence}"))[..16]);
    private static Guid? SourceTask(StartMultiAgentCollaborationCommand command) =>
        command.InputPayload?.TryGetValue("sourceTaskId", out var node) == true &&
        Guid.TryParse(node?.ToString(), out var id) && id != Guid.Empty ? id : null;

    private async Task<WorkTask?> FindRetainedParentAsync(StartMultiAgentCollaborationCommand command,
        string correlation, IReadOnlyList<WorkerSubtaskRequest> workers, CancellationToken ct)
    {
        var parent = await _dbContext.WorkTasks.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId &&
            x.Type == MultiAgentCollaborationTaskTypes.Parent && x.CorrelationId == correlation, ct);
        if (parent is null) return null;
        _ = await _taskQueries.GetByIdAsync(command.CompanyId, new(parent.Id), ct);
        if (parent.AssignedAgentId != command.CoordinatorAgentId || parent.Description != command.Objective ||
            !parent.InputPayload.TryGetValue("executionWorkers", out var saved) || saved?.ToJsonString() != JsonSerializer.Serialize(workers))
            throw new MultiAgentCollaborationValidationException(new Dictionary<string, string[]> { ["correlationId"] = ["This execution identity belongs to a different plan. Use a new identity for revised work."] });
        return parent;
    }
    private async Task<WorkTask?> FindRetainedChildAsync(Guid company, Guid parent, int sequence, CancellationToken ct)
    {
        var children = await _dbContext.WorkTasks.AsNoTracking().Where(x => x.CompanyId == company && x.ParentTaskId == parent)
            .OrderBy(x => x.CreatedUtc).Take(100).ToListAsync(ct);
        var child = children.FirstOrDefault(x => x.InputPayload.TryGetValue("plan", out var plan) &&
            plan?["Steps"]?.AsArray().Any(s => s?["Sequence"]?.GetValue<int>() == sequence &&
                s?["AssignedAgentId"]?.ToString() == x.AssignedAgentId?.ToString()) == true);
        // Worker correlation is persisted before execution and is independent of output/attempt state.
        child = children.FirstOrDefault(x => x.CorrelationId?.EndsWith($":worker:{sequence}", StringComparison.Ordinal) == true) ?? child;
        if (child is not null) _ = await _taskQueries.GetByIdAsync(company, new(child.Id), ct);
        return child;
    }
    private async Task<List<CollaborationContribution>> InputsAsync(Guid company, Guid parent, int sequence, CancellationToken ct)
    {
        var rows = await _dbContext.CollaborationContributions.AsNoTracking().Where(x => x.CompanyId == company &&
            x.ParentTaskId == parent && x.Sequence < sequence).OrderByDescending(x => x.Version).ToListAsync(ct);
        return rows.DistinctBy(x => x.Sequence).OrderBy(x => x.Sequence).ToList();
    }
    private async Task<CollaborationContribution?> ExecuteContributionAsync(StartMultiAgentCollaborationCommand command,
        Guid plan, Guid parent, CollaborationStepDto step, WorkerSubtaskRequest worker, CancellationToken ct)
    {
        var last = await _dbContext.CollaborationContributions.AsNoTracking().Where(x => x.CompanyId == command.CompanyId &&
            x.ParentTaskId == parent && x.Sequence == step.Sequence).OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
        if (last?.Status is "completed" or "needs_review" or "awaiting_approval") return last;
        if (worker.Pattern != "sequential_handoff") return null;
        var inputs = await InputsAsync(command.CompanyId, parent, step.Sequence, ct);
        if (inputs.Any(x => x.Status != OrchestrationStatusValues.Completed))
        {
            const string reason = "An earlier contribution has failed or needs review. Resolve it before this handoff can run.";
            await _taskCommands.UpdateStatusAsync(command.CompanyId, step.SubtaskId!.Value, new("blocked", new(), reason, null), ct);
            return await RetainContributionAsync(command, plan, parent, step, worker, "blocked", "", reason, ct);
        }
        var task = await _dbContext.WorkTasks.SingleAsync(x => x.CompanyId == command.CompanyId && x.Id == step.SubtaskId, ct);
        // Pass the exact retained versions; preserve the owning task's lifecycle and summary.
        task.InputPayload["contributionInputs"] = JsonSerializer.SerializeToNode(inputs.Select(x => new
            { x.Id, x.Version, x.SourceTaskId, x.Output, x.Rationale }));
        await _dbContext.SaveChangesAsync(ct);
        return null;
    }
    private async Task<CollaborationContribution> RetainContributionAsync(StartMultiAgentCollaborationCommand command,
        Guid plan, Guid parent, CollaborationStepDto step, WorkerSubtaskRequest worker,
        string status, string output, string? rationale, CancellationToken ct)
    {
        var version = await _dbContext.CollaborationContributions.Where(x => x.CompanyId == command.CompanyId &&
            x.ParentTaskId == parent && x.Sequence == step.Sequence).Select(x => (int?)x.Version).MaxAsync(ct) ?? 0;
        var receipt = new CollaborationContribution(command.CompanyId, parent, step.SubtaskId!.Value, plan,
            step.AssignedAgentId, step.Sequence, version + 1, OperatingCollaborationRoleValues.Parse(worker.Role),
            OperatingCollaborationPatternValues.Parse(worker.Pattern), worker.Objective, status, output, rationale,
            worker.Role is "reviewer" or "challenger" ? rationale : null);
        _dbContext.CollaborationContributions.Add(receipt);
        if (worker.Pattern == "sequential_handoff")
            foreach (var input in await InputsAsync(command.CompanyId, parent, step.Sequence, ct))
                _dbContext.CollaborationArtifactHandoffs.Add(new(command.CompanyId, input.Id, receipt.Id,
                    input.Status == OrchestrationStatusValues.Completed && status != "blocked",
                    input.Status == OrchestrationStatusValues.Completed ? null : "Input requires recovery or review."));
        await _dbContext.SaveChangesAsync(ct);
        return receipt;
    }
}
