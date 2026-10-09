using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Companies;

public sealed partial class CompanyAgentWorkQueryService(VirtualCompanyDbContext db, CompanyWorkVisibility visibility,
    TimeProvider clock, ILogger<CompanyAgentWorkQueryService> logger) : IAgentWorkQueryService
{
    private const int SourceLimit = 2000;
    private static readonly Meter Meter = new("VirtualCompany.AgentWork", "1.0");
    private static readonly Counter<long> Missing = Meter.CreateCounter<long>("agent_work.missing_projection");
    private static readonly Counter<long> Stale = Meter.CreateCounter<long>("agent_work.stale_projection");
    private static readonly Counter<long> Failed = Meter.CreateCounter<long>("agent_work.failed_refresh");
    private static readonly Histogram<double> Latency = Meter.CreateHistogram<double>("agent_work.query_ms");

    public async Task<AgentWorkBoardDto> ListAsync(AgentWorkQuery query, CancellationToken token)
    {
        if (query.CompanyId == Guid.Empty || query.Skip < 0 || query.Take is < 1 or > 100 || query.Objective?.Length > 200 ||
            query.State is not null && !AgentWorkStates.All.Contains(query.State) ||
            query.Responsibility is not null && !new[] { "company", "finance", "sales", "marketing", "support" }.Contains(query.Responsibility))
            throw new ArgumentException("Choose valid work filters and a page size between 1 and 100.");
        var timer = Stopwatch.StartNew();
        try
        {
            var scope = await visibility.ResolveAsync(query.CompanyId, token);
            var source = await LoadAsync(scope, null, null, token);
            var filtered = source.Items.Where(x => query.Responsibility is null || x.Responsibility == query.Responsibility)
                .Where(x => query.AgentId is null || x.Agents.Any(a => a.Id == query.AgentId))
                .Where(x => string.IsNullOrWhiteSpace(query.Objective) || x.Objective.Contains(query.Objective, StringComparison.OrdinalIgnoreCase) || x.Title.Contains(query.Objective, StringComparison.OrdinalIgnoreCase))
                .Where(x => query.State is null || x.State == query.State)
                .OrderByDescending(x => x.UpdatedUtc).ThenBy(x => x.Kind, StringComparer.Ordinal).ThenBy(x => x.Id).ToList();
            var counts = AgentWorkStates.All.ToDictionary(state => state, state => filtered.Count(x => x.State == state));
            // Page each lane after authorization and filtering so completed history cannot crowd out active work.
            var page = query.PerState
                ? AgentWorkStates.All.SelectMany(state => filtered.Where(x => x.State == state).Skip(query.Skip).Take(query.Take)).ToList()
                : filtered.Skip(query.Skip).Take(query.Take).ToList();
            var hasNext = query.PerState ? counts.Values.Any(count => (long)query.Skip + query.Take < count)
                : (long)query.Skip + query.Take < filtered.Count;
            var diagnostics = new List<string>();
            if (source.Partial) diagnostics.Add("Source window is limited to 2,000 records per work family. Filters, counts and pages describe this window. Older known records can be opened directly.");
            if (source.Items.Any(x => x.Diagnostics.Count > 0)) diagnostics.Add("Some work has missing links or older lifecycle observations. Open work to review the recorded evidence.");
            return new(query.CompanyId, scope.Resolution.CompanyName, clock.GetUtcNow().UtcDateTime,
                page, counts,
                source.Items.SelectMany(x => x.Agents).DistinctBy(x => x.Id).OrderBy(x => x.Name).ToList(),
                scope.Areas.Order().ToList(), filtered.Count, query.Skip, query.Take,
                hasNext, source.Partial, diagnostics);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Failed.Add(1); logger.LogWarning("Agent work refresh failed ({FailureType})", ex.GetType().Name); throw;
        }
        finally { Latency.Record(timer.Elapsed.TotalMilliseconds); }
    }

    public async Task<AgentWorkItemDto> GetAsync(Guid companyId, string kind, Guid id, CancellationToken token)
    {
        if (companyId == Guid.Empty || id == Guid.Empty || !new[] { "task", "initiative", "case", "deal" }.Contains(kind))
            throw new ArgumentException("A valid company and work identity are required.");
        var timer = Stopwatch.StartNew();
        try
        {
            var scope = await visibility.ResolveAsync(companyId, token);
            var source = await LoadAsync(scope, kind, id, token);
            var item = source.Items.SingleOrDefault(x => x.Kind == kind && x.Id == id);
            if (item is null) { Missing.Add(1); throw new KeyNotFoundException("Work is unavailable in the current company and access scope."); }
            if (kind == "task") item = item with { RelatedRecords = item.RelatedRecords.Concat(source.Items.Where(x => x.Kind == "initiative")
                .Select(x => new AgentWorkLinkDto($"Company outcome: {x.Title}",x.DetailRoute,x.UpdatedUtc))).ToList() };
            if (source.Partial) item = item with { Diagnostics = item.Diagnostics.Concat(["Some linked history is outside the bounded source window. Review the owning records for older evidence."]).ToList() };
            return item;
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or KeyNotFoundException))
        { Failed.Add(1); logger.LogWarning("Agent work detail refresh failed ({FailureType})", ex.GetType().Name); throw; }
        finally { Latency.Record(timer.Elapsed.TotalMilliseconds); }
    }

    private async Task<(List<AgentWorkItemDto> Items, bool Partial)> LoadAsync(CompanyWorkScope scope, string? kind, Guid? id, CancellationToken token)
    {
        var company = scope.Resolution.CompanyId;
        var authorizedTasks = scope.Tasks(db.WorkTasks.AsNoTracking().Where(x => x.CompanyId == company));
        var authorizedAgents = scope.Agents(db.Agents.AsNoTracking());
        var roster = await authorizedAgents.OrderBy(x => x.Id).Take(SourceLimit + 1).ToListAsync(token);
        var taskQuery = authorizedTasks;
        // Linked tasks and worker subtasks belong to their company outcome, rather than separate board cards.
        if (kind is null) taskQuery = taskQuery.Where(t => t.ParentTaskId == null &&
            !db.OperatingInitiatives.Any(i => i.CompanyId == company && i.TaskId == t.Id) &&
            !db.OperatingDispatches.Any(d => d.CompanyId == company && d.TaskId == t.Id));
        else taskQuery = taskQuery.Where(x => kind == "task" && x.Id == id);
        var tasks = await taskQuery.Include(x => x.AssignedAgent).Include(x => x.WorkflowInstance)
            .OrderByDescending(x => x.UpdatedUtc).ThenBy(x => x.Id).Take(SourceLimit + 1).ToListAsync(token);

        var initiativeQuery = db.OperatingInitiatives.AsNoTracking().Where(x => x.CompanyId == company);
        if (!scope.Executive) initiativeQuery = initiativeQuery.Where(x =>
            x.TaskId != null && authorizedTasks.Any(t => t.Id == x.TaskId) ||
            x.TaskId == null && x.OwnerAgentId != null && authorizedAgents.Any(a => a.Id == x.OwnerAgentId));
        // Owning task and all participants must be in scope; do not expose an outcome's hidden dependencies.
        initiativeQuery = initiativeQuery.Where(x => (x.TaskId == null || authorizedTasks.Any(t => t.Id == x.TaskId)) &&
            (x.OwnerAgentId == null || authorizedAgents.Any(a => a.Id == x.OwnerAgentId)) && x.Goal.CompanyId == company &&
            !db.OperatingInitiativeCollaborators.Any(c => c.CompanyId == company && c.InitiativeId == x.Id && !authorizedAgents.Any(a => a.Id == c.AgentId)));
        var authorizedInitiatives = initiativeQuery;
        if (kind == "task") initiativeQuery = initiativeQuery.Where(x => x.TaskId == id ||
            db.OperatingDispatches.Any(d => d.CompanyId == company && d.InitiativeId == x.Id && d.TaskId == id));
        else if (kind is not null) initiativeQuery = initiativeQuery.Where(x => kind == "initiative" && x.Id == id);
        var initiatives = await initiativeQuery.Include(x => x.Goal).ThenInclude(x => x.OwnerUser)
            .Include(x => x.OwnerAgent).Include(x => x.Task).ThenInclude(x => x!.AssignedAgent)
            .Include(x => x.Task).ThenInclude(x => x!.WorkflowInstance)
            .OrderByDescending(x => x.UpdatedUtc).ThenBy(x => x.Id).Take(SourceLimit + 1).ToListAsync(token);
        var initiativeIds = initiatives.Take(SourceLimit).Select(x => x.Id).ToArray();
        var dispatches = await db.OperatingDispatches.AsNoTracking().Where(x => x.CompanyId == company && initiativeIds.Contains(x.InitiativeId))
            .OrderByDescending(x => x.UpdatedUtc).ThenBy(x => x.Id).Take(SourceLimit + 1).ToListAsync(token);
        var collaborators = await db.OperatingInitiativeCollaborators.AsNoTracking().Include(x => x.Agent)
            .Where(x => x.CompanyId == company && initiativeIds.Contains(x.InitiativeId)).Take(SourceLimit + 1).ToListAsync(token);
        var initiativeSourceCount = initiatives.Count;
        initiatives = initiatives.Where(x => scope.Allows(CompanyWorkScope.Area(x.OwnerAgent?.Department, x.Task?.Type)) &&
            !collaborators.Any(c => c.InitiativeId == x.Id && !scope.Allows(CompanyWorkScope.Area(c.Agent.Department)))).ToList();
        var taskIds = tasks.Take(SourceLimit).Select(x => x.Id).Concat(initiatives.Select(x => x.TaskId ?? Guid.Empty)).ToArray();
        var approvals = await db.ApprovalRequests.AsNoTracking().Where(x => x.CompanyId == company &&
            x.TargetEntityType == "task" && taskIds.Contains(x.TargetEntityId))
            .OrderByDescending(x => x.CreatedUtc).Take(SourceLimit + 1).ToListAsync(token);
        var reviews = await db.OperatingReviews.AsNoTracking().Where(x => x.CompanyId == company && initiativeIds.Contains(x.InitiativeId))
            .OrderByDescending(x => x.CreatedUtc).ThenBy(x => x.Id).Take(SourceLimit + 1).ToListAsync(token);
        var dependencies = await db.OperatingPlanDependencies.AsNoTracking().Where(x => x.CompanyId == company && initiativeIds.Contains(x.InitiativeId))
            .OrderBy(x => x.Id).Take(SourceLimit + 1).ToListAsync(token);
        var dependencyIds = dependencies.Take(SourceLimit).Select(x => x.DependsOnInitiativeId).Distinct().ToArray();
        var dependencySource = await authorizedInitiatives.Where(x => dependencyIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Title, x.Status, x.UpdatedUtc, Department = x.OwnerAgent == null ? null : x.OwnerAgent.Department,
                TaskType = x.Task == null ? null : x.Task.Type }).ToListAsync(token);
        var dependencyTargets = dependencySource.Where(x => scope.Allows(CompanyWorkScope.Area(x.Department, x.TaskType))).ToList();
        var items = new List<AgentWorkItemDto>();
        foreach (var task in tasks.Take(SourceLimit)) items.Add(await TaskItemAsync(scope, task, approvals.FirstOrDefault(x => x.TargetEntityId == task.Id), token));
        foreach (var initiative in initiatives.Take(SourceLimit))
        {
            var task = initiative.Task;
            var dispatch = dispatches.FirstOrDefault(x => x.InitiativeId == initiative.Id);
            var review = reviews.FirstOrDefault(x => x.InitiativeId == initiative.Id);
            var approval = approvals.FirstOrDefault(x => x.TargetEntityId == task?.Id);
            var state = InitiativeState(initiative, dispatch, review, approval);
            var participants = collaborators.Where(x => x.InitiativeId == initiative.Id && x.Agent.CompanyId == company)
                .Select(x => Person(x.Agent)).Concat(initiative.OwnerAgent is { CompanyId: var ownerCompany } owner && ownerCompany == company ? [Person(owner)] : [])
                .DistinctBy(x => x.Id).ToList();
            var links = new List<AgentWorkLinkDto>(); var evidence = new List<AgentWorkLinkDto>(); var outputs = new List<AgentWorkOutputDto>();
            var workRoute = task is not null ? WorkRoute(company, task.Id) : null;
            if (workRoute is not null) links.Add(new("Open in Work", workRoute, task!.UpdatedUtc));
            if (approval is not null) links.Add(new("Review approval", ApprovalRoute(company, approval.Id), approval.CreatedUtc));
            if (task is not null)
            {
                var projected = await TaskItemAsync(scope, task, approval, token);
                links.AddRange(projected.RelatedRecords.Where(x => x.Label != "Open in Work" && x.Label != "Review approval"));
                evidence.AddRange(projected.Evidence); outputs.AddRange(projected.Outputs);
            }
            if (review is not null)
                outputs.Add(new("Latest outcome review", $"{review.Summary} Recorded evidence: {review.ActualEvidence ?? "No actual evidence retained."} Expected evidence: {review.ExpectedEvidence}", review.CreatedUtc));
            var outcomeDependencies = dependencies.Where(x => x.InitiativeId == initiative.Id).ToList();
            var permittedDependencies = dependencyTargets.Where(x => outcomeDependencies.Any(d => d.DependsOnInitiativeId == x.Id)).ToList();
            links.AddRange(permittedDependencies.Select(x => new AgentWorkLinkDto($"Dependency: {x.Title}", DetailRoute(company, "initiative", x.Id), x.UpdatedUtc)));
            var dependencySummary = outcomeDependencies.Count == 0 ? null :
                "Recorded dependencies: " + string.Join("; ", permittedDependencies.Select(x => $"{x.Title} ({x.Status.ToStorageValue()})")) +
                (permittedDependencies.Count < outcomeDependencies.Count ? " A dependency is unavailable in this access scope; ask the accountable human to reconcile it." : ". Review their recorded evidence before proceeding.");
            var dependency = state == AgentWorkStates.Paused ? "The recorded goal or outcome review is paused. The accountable human must review its continuation." : IsReviewedInternalQueued(task)?"The reviewed internal action is queued; current policy and pause will be rechecked before execution.":
                dispatch?.FailureSummary ?? review?.NextAction ?? (approval is not null ? "The current task approval needs a human decision." : dependencySummary ??
                (state == AgentWorkStates.Blocked ? "The accountable human must record the missing dependency and recovery evidence." : "No outstanding dependency is recorded."));
            var updated = new[] { initiative.UpdatedUtc, initiative.Goal.UpdatedUtc, task?.UpdatedUtc ?? initiative.UpdatedUtc, dispatch?.UpdatedUtc ?? initiative.UpdatedUtc, review?.CreatedUtc ?? initiative.UpdatedUtc }.Max();
            var diagnostics = Diagnostics(updated, task is null && initiative.TaskId.HasValue);
            if (state == AgentWorkStates.Blocked && dispatch?.FailureSummary is null && review?.NextAction is null) diagnostics.Add("A specific blocking reason has not been retained; ask the accountable human to record it.");
            items.Add(new("initiative", initiative.Id, initiative.Title, initiative.DesiredOutcome,
                CompanyWorkScope.Area(initiative.OwnerAgent?.Department, task?.Type), state, initiative.Status.ToStorageValue(),
                initiative.Goal?.OwnerUser?.DisplayName ?? scope.Human(CompanyWorkScope.Area(initiative.OwnerAgent?.Department, task?.Type)), participants,
                state == AgentWorkStates.Completed ? "Company outcome recorded as completed" : IsReviewedInternalQueued(task)?"Queued for internal execution":task?.Status == WorkTaskStatus.Completed
                    ? $"The linked task ‘{task.Title}’ is completed. The company outcome still needs a recorded decision against this evidence: {initiative.CompletionEvidence}"
                    : task?.Title ?? "Review and organize the company outcome",
                dependency, initiative.CreatedUtc, updated, state == AgentWorkStates.Completed ? initiative.UpdatedUtc : null,
                DetailRoute(company, "initiative", initiative.Id), workRoute, links, outputs, evidence, diagnostics));
        }
        var caseQuery = db.SupportCases.AsNoTracking().Where(x => x.CompanyId == company && scope.Allows("support"));
        if (kind is not null) caseQuery = caseQuery.Where(x => kind == "case" && x.Id == id);
        var cases = await caseQuery.OrderByDescending(x => x.UpdatedUtc).ThenBy(x => x.Id).Take(SourceLimit + 1).ToListAsync(token);
        foreach (var row in cases.Take(SourceLimit))
        {
            var state = row.Status is "resolved" or "closed" ? AgentWorkStates.Completed : row.Status == "awaiting_approval" ? AgentWorkStates.AwaitingApproval :
                row.Status is SupportCaseStatuses.WaitingForCustomer or SupportCaseStatuses.WaitingInternal ? AgentWorkStates.Blocked :
                row.Status == SupportCaseStatuses.New ? AgentWorkStates.Planned : AgentWorkStates.Active;
            var route = $"/support/cases/{row.Id:D}?companyId={company:D}";
            items.Add(new("case", row.Id, row.Subject, row.Summary ?? row.Subject, "support", state, row.Status,
                scope.Human("support"), roster.FirstOrDefault(x => x.Id == row.AssignedAgentId) is { } agent ? [Person(agent)] : [],
                "Review the current customer case", state == AgentWorkStates.Blocked
                    ? row.Status == SupportCaseStatuses.WaitingInternal ? "Obtain the recorded internal specialist evidence; review the case handoff and deadline." : "Awaiting the customer's response; review the recorded case messages and deadline."
                    : "Review current source evidence before replying.",
                row.CreatedUtc, row.UpdatedUtc, state == AgentWorkStates.Completed ? row.ClosedUtc ?? row.ResolvedUtc : null, DetailRoute(company, "case", row.Id), null,
                [new("Open business record", route, row.UpdatedUtc)], [], [new("Case evidence and messages", route, row.UpdatedUtc)], Diagnostics(row.UpdatedUtc, false)));
        }
        var dealQuery = db.Deals.AsNoTracking().Where(x => x.CompanyId == company && !x.IsDeleted && scope.Allows("sales"));
        if (kind is not null) dealQuery = dealQuery.Where(x => kind == "deal" && x.Id == id);
        var deals = await dealQuery.OrderByDescending(x => x.UpdatedUtc).ThenBy(x => x.Id).Take(SourceLimit + 1).ToListAsync(token);
        foreach (var row in deals.Take(SourceLimit))
        {
            var state = row.Status is SalesStatuses.Won or SalesStatuses.Completed ? AgentWorkStates.Completed : row.Status is SalesStatuses.Lost or SalesStatuses.Failed ? AgentWorkStates.Failed :
                row.Status == SalesStatuses.WaitingForApproval ? AgentWorkStates.AwaitingApproval : row.Status == SalesStatuses.Blocked ? AgentWorkStates.Blocked :
                row.Status == SalesStatuses.Paused ? AgentWorkStates.Paused : AgentWorkStates.Active;
            var route = $"/app/sales/deals/{row.Id:D}?companyId={company:D}";
            items.Add(new("deal", row.Id, row.Title, row.Title, "sales", state, row.Status, scope.Human("sales"), [],
                "Review the current opportunity", row.NextStep ?? (state == AgentWorkStates.Blocked ? "No blocking dependency is recorded. Ask the accountable human to record the missing next action." : "Review the recorded next action and customer commitments."), row.CreatedUtc, row.UpdatedUtc,
                state == AgentWorkStates.Completed ? row.UpdatedUtc : null, DetailRoute(company, "deal", row.Id), null,
                [new("Open business record", route, row.UpdatedUtc)], [], [new("Opportunity evidence", route, row.UpdatedUtc)], Diagnostics(row.UpdatedUtc, false)));
        }
        var partial = new[] { roster.Count, tasks.Count, initiativeSourceCount, dispatches.Count, collaborators.Count, approvals.Count, reviews.Count, dependencies.Count, cases.Count, deals.Count }.Any(x => x > SourceLimit);
        return (items, partial);
    }

    private async Task<AgentWorkItemDto> TaskItemAsync(CompanyWorkScope scope, WorkTask task, ApprovalRequest? approval, CancellationToken token)
    {
        var company = scope.Resolution.CompanyId; var area = CompanyWorkScope.Area(task.AssignedAgent?.Department, task.Type);
        var state = task.Status switch { WorkTaskStatus.Completed => AgentWorkStates.Completed, WorkTaskStatus.Failed => AgentWorkStates.Failed,
            WorkTaskStatus.Blocked => AgentWorkStates.Blocked, WorkTaskStatus.AwaitingApproval => AgentWorkStates.AwaitingApproval,
            WorkTaskStatus.InProgress => AgentWorkStates.Active, _ => AgentWorkStates.Planned };
        if (approval?.Status == ApprovalRequestStatus.Pending && task.Status is not (WorkTaskStatus.Completed or WorkTaskStatus.Failed)) state = AgentWorkStates.AwaitingApproval;
        var route = WorkRoute(company, task.Id); var links = new List<AgentWorkLinkDto> { new("Open in Work", route, task.UpdatedUtc) };
        if (approval is not null) links.Add(new("Review approval", ApprovalRoute(company, approval.Id), approval.CreatedUtc));
        if(task.OutputPayload.GetValueOrDefault("reviewedActionQueue") is System.Text.Json.Nodes.JsonObject queue&&Guid.TryParse(queue["approvalId"]?.ToString(),out var queuedApproval)&&
            await db.ApprovalRequests.AnyAsync(x=>x.CompanyId==company&&x.Id==queuedApproval&&x.TargetEntityType=="action"&&db.ToolExecutionAttempts.Any(a=>a.CompanyId==company&&a.Id==x.TargetEntityId&&a.TaskId==task.Id),token))
            links.Add(new("Review internal action decision",ApprovalRoute(company,queuedApproval),task.UpdatedUtc));
        if (task.ParentTaskId is Guid parent && await scope.Tasks(db.WorkTasks).AnyAsync(x => x.CompanyId == company && x.Id == parent, token))
            links.Add(new("Parent work", DetailRoute(company, "task", parent)));
        var business = await BusinessLinksAsync(scope, task, token);
        links.AddRange(business.Links);
        var evidence = new List<AgentWorkLinkDto>();
        if (task.RationaleSummary is not null) evidence.Add(new("Recorded task rationale", route, task.UpdatedUtc));
        if (task.WorkflowInstance is { } workflow && workflow.CompanyId == company)
            evidence.Add(new("Owning workflow", $"/workflows?companyId={company:D}&workflowInstanceId={workflow.Id:D}", workflow.UpdatedUtc));
        var outputs = task.OutputPayload.Count > 0 ? new List<AgentWorkOutputDto> { new("Recorded task output", "An output is retained. Its existence does not establish completion; review it in Work.", task.UpdatedUtc, route) } : [];
        var diagnostics = Diagnostics(task.UpdatedUtc, task.AssignedAgentId.HasValue && task.AssignedAgent is null);
        diagnostics.AddRange(business.Diagnostics);
        if (evidence.Count == 0) diagnostics.Add("No retained rationale or linked workflow evidence is available.");
        var canReadContent = await CollaborationContentVisibility.AllowsAsync(db, scope, task.Id, token);
        var dependency = !canReadContent ? "Some contribution evidence is unavailable in your access scope. Ask the accountable human to review the dependency." : state switch { AgentWorkStates.Blocked => task.RationaleSummary ?? task.Description ?? "Ask the accountable human to record the blocking dependency and recovery evidence.",
            AgentWorkStates.Failed => task.RationaleSummary ?? "Review the recorded failure and owning recovery policy in Work.",
            AgentWorkStates.AwaitingApproval => approval is not null ? "The current task approval needs a human decision." : "The task records a need for approval, but no current approval is linked. Ask the accountable human to reconcile it.",
            _ => IsReviewedInternalQueued(task)?"The reviewed internal action is queued; current policy and pause will be rechecked before execution.":"No outstanding dependency is recorded." };
        return new("task", task.Id, task.Title, task.Description ?? task.Title, area, state, task.Status.ToStorageValue(),
            scope.Human(area), task.AssignedAgent is { } agent && agent.CompanyId == company ? [Person(agent)] : [],
            IsReviewedInternalQueued(task)?"Queued for internal execution":task.WorkflowInstance?.CurrentStep ?? task.Title, dependency, task.CreatedUtc, task.UpdatedUtc, task.CompletedUtc,
            DetailRoute(company, "task", task.Id), route, links, outputs, evidence, diagnostics);
    }

    public static string InitiativeState(OperatingInitiative initiative, OperatingDispatch? dispatch, OperatingReview? review, ApprovalRequest? approval)
    {
        if (initiative.Status == OperatingInitiativeStatus.Completed) return AgentWorkStates.Completed;
        if (initiative.Status == OperatingInitiativeStatus.Failed) return AgentWorkStates.Failed;
        if (initiative.Goal?.Status == CompanyGoalStatus.Paused || review?.Outcome == OperatingReviewOutcome.Pause) return AgentWorkStates.Paused;
        if (initiative.Status == OperatingInitiativeStatus.Cancelled || review?.Outcome == OperatingReviewOutcome.Stop || initiative.Status == OperatingInitiativeStatus.Blocked) return AgentWorkStates.Blocked;
        if (dispatch?.Status is OperatingDispatchStatus.Failed or OperatingDispatchStatus.DeadLettered) return AgentWorkStates.Failed;
        if (dispatch?.Status == OperatingDispatchStatus.Paused) return AgentWorkStates.Paused;
        if (dispatch?.Status == OperatingDispatchStatus.Uncertain) return AgentWorkStates.Blocked;
        if (dispatch?.Status is OperatingDispatchStatus.Blocked or OperatingDispatchStatus.RetryScheduled) return AgentWorkStates.Blocked;
        if (approval?.Status == ApprovalRequestStatus.Pending) return AgentWorkStates.AwaitingApproval;
        if(dispatch?.Status==OperatingDispatchStatus.AwaitingApproval)return IsReviewedInternalQueued(initiative.Task)?AgentWorkStates.Planned:AgentWorkStates.AwaitingApproval;
        return initiative.Status == OperatingInitiativeStatus.Active || dispatch?.Status is OperatingDispatchStatus.Running or OperatingDispatchStatus.Claimed
            ? AgentWorkStates.Active : AgentWorkStates.Planned;
    }
    private static bool IsReviewedInternalQueued(WorkTask? task)=>task?.Status==WorkTaskStatus.New&&
        task.InputPayload.ContainsKey("taskPolicyVersion")&&task.OutputPayload.GetValueOrDefault("reviewedActionQueue") is System.Text.Json.Nodes.JsonObject&&
        VirtualCompany.Application.Agents.TaskTypePolicyCatalogue.All.Any(x=>!x.Finance&&x.Code==task.Type);
    private List<string> Diagnostics(DateTime updated, bool missing)
    {
        var result = new List<string>();
        if (missing) { Missing.Add(1); result.Add("An expected owning record is missing. Ask the accountable human to restore or reconcile the link."); }
        if (clock.GetUtcNow().UtcDateTime - updated > TimeSpan.FromDays(1)) { Stale.Add(1); result.Add("The last recorded lifecycle update is older than one day. Refreshing this view does not establish newer business evidence."); }
        return result;
    }
    private static AgentWorkPersonDto Person(Agent agent) => new(agent.Id, agent.DisplayName, agent.RoleName, agent.AvatarUrl);
    private static string DetailRoute(Guid company, string kind, Guid id) => $"/agents/work/{kind}/{id:D}?companyId={company:D}";
    private static string WorkRoute(Guid company, Guid task) => $"/work?companyId={company:D}&tab=tasks&taskId={task:D}";
    private static string ApprovalRoute(Guid company, Guid approval) => $"/work?companyId={company:D}&tab=approvals&itemId={approval:D}";
}
