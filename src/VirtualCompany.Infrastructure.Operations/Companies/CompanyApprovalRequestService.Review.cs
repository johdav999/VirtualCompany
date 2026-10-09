using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Infrastructure.Companies;

public sealed partial class CompanyApprovalRequestService
{
    // An immutable evidence snapshot in the existing decision record; not a second approval identity.
    // Owning modules retain their own execution-time version and policy validation.
    private async Task BindReviewAsync(ApprovalRequest approval, CancellationToken ct)
    {
        var chain = CloneNodes(approval.DecisionChain);
        chain["reviewMaterialHash"] = await MaterialHashAsync(approval, ct);
        approval.SetDecisionChain(chain);
    }

    private async Task<string> MaterialHashAsync(ApprovalRequest approval, CancellationToken ct)
    {
        object? material = null;
        if (approval.TargetEntityType == "annual_plan_version")
            material = await _serviceProvider.GetRequiredService<VirtualCompany.Application.Orchestration.IAnnualPlanningService>()
                .ApprovalMaterialAsync(approval.CompanyId, approval.TargetEntityId, ct);
        if (approval.TargetEntityType == "task")
        {
            var task = await _dbContext.WorkTasks.AsNoTracking().SingleAsync(x => x.CompanyId == approval.CompanyId && x.Id == approval.TargetEntityId, ct);
            var artifacts = await _dbContext.CollaborationContributions.AsNoTracking()
                .Where(x => x.CompanyId == approval.CompanyId && (x.SourceTaskId == task.Id ||
                    _dbContext.WorkTasks.Any(t => t.CompanyId == approval.CompanyId && t.Id == x.ParentTaskId && t.ParentTaskId == task.Id)))
                .OrderBy(x => x.Id).Select(x => new { x.Id, x.Version }).ToListAsync(ct);
            var artifactIds = artifacts.Select(x => x.Id).ToArray();
            var handoffs = await _dbContext.CollaborationArtifactHandoffs.AsNoTracking()
                .Where(x => x.CompanyId == approval.CompanyId && artifactIds.Contains(x.ReceivingContributionId))
                .OrderBy(x => x.Id).Select(x => new { x.Id, x.InputContributionId, x.ReceivingContributionId, x.Passed, x.Reason }).ToListAsync(ct);
            var planningOrigin = await _dbContext.Set<DecisionWorkOrigin>().AsNoTracking().Where(x => x.CompanyId == approval.CompanyId && x.TaskId == task.Id)
                .Select(x => new { x.OwnerUserId, x.DueUtc, x.Objective, x.AcceptanceOutcome, x.ProposedConstraints, x.SourceKind, x.SourceVersionId, x.SourceVersion, x.SourceFingerprint, x.PreviewChecksum }).SingleOrDefaultAsync(ct);
            material = new { task.Title, task.Description, task.Type, task.AssignedAgentId, task.InputPayload, task.OutputPayload, artifacts, handoffs };
            if (planningOrigin != null) material = new { task.Title, task.Description, task.Type, task.AssignedAgentId, task.InputPayload, task.OutputPayload, artifacts, handoffs, planningOrigin };
        }
        else if (approval.TargetEntityType == "action")
        {
            var action = await _dbContext.ToolExecutionAttempts.AsNoTracking().SingleAsync(x => x.CompanyId == approval.CompanyId && x.Id == approval.TargetEntityId, ct);
            material = new { action.ToolName, action.ToolVersion, action.AgentId, action.ActionType, action.Scope, action.RequestPayload };
        }
        else if (approval.TargetEntityType == "sales_meeting_invitation")
        {
            var invitation = await _dbContext.SalesMeetingInvitations.AsNoTracking().SingleAsync(x => x.CompanyId == approval.CompanyId && x.Id == approval.TargetEntityId, ct);
            material = new { invitation.Title, invitation.Description, invitation.AttendeeEmail, invitation.OrganizerEmail,
                invitation.StartsUtc, invitation.EndsUtc, invitation.TimeZoneId, invitation.Location, invitation.CalendarConnectionId, invitation.Conferencing };
        }
        else if (approval.TargetEntityType == "sales_meeting_change_proposal")
        {
            var proposal = await _dbContext.SalesMeetingChangeProposals.AsNoTracking().SingleAsync(x => x.CompanyId == approval.CompanyId && x.Id == approval.TargetEntityId, ct);
            material = new { proposal.TargetType, proposal.TargetId, proposal.Action, proposal.Field, proposal.ProposedValueJson,
                proposal.BeforeValueJson, proposal.TargetVersion, proposal.EvidenceVersionHash, proposal.SourceIdsJson, proposal.PolicyVersion };
        }
        else if (approval.TargetEntityType == "marketing_channel_action")
        {
            var action = await _dbContext.MarketingChannelActions.AsNoTracking().SingleAsync(x => x.CompanyId == approval.CompanyId && x.Id == approval.TargetEntityId, ct);
            material = new { action.MarketingChannelConnectionId, action.MarketingContentBriefId, action.ContentBriefVersion,
                action.DestinationReference, action.ActionType, action.PayloadJson, action.ScheduledUtc };
        }
        else if (approval.TargetEntityType == "finance_integration_write")
        {
            var write = await _dbContext.FinanceIntegrationWriteCommands.AsNoTracking().SingleAsync(x => x.CompanyId == approval.CompanyId && x.Id == approval.TargetEntityId, ct);
            material = new { write.CommandType, write.HttpMethod, write.Path, write.TargetCompany, write.ConnectionId, write.PayloadHash, write.SanitizedPayloadJson };
        }
        else if (approval.TargetEntityType == "workflow")
        {
            var workflow = await _dbContext.WorkflowInstances.AsNoTracking().SingleAsync(x => x.CompanyId == approval.CompanyId && x.Id == approval.TargetEntityId, ct);
            material = new { workflow.DefinitionId, workflow.InputPayload, workflow.ContextJson };
        }
        // Other immutable/versioned proposals use the owner's stored approval binding and revalidation.
        var node = JsonSerializer.SerializeToNode(new { approval.CompanyId, approval.TargetEntityId, approval.TargetEntityType,
            approval.ApprovalType, approval.RequiredRole, approval.RequiredUserId, approval.ThresholdContext, material });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Canonical(node))));
    }

    private static string Canonical(JsonNode? node) => node switch
    {
        JsonObject obj => "{" + string.Join(",", obj.OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => JsonSerializer.Serialize(x.Key) + ":" + Canonical(x.Value))) + "}",
        JsonArray array => "[" + string.Join(",", array.Select(Canonical)) + "]",
        _ => node?.ToJsonString() ?? "null"
    };

    private static DateTime? ReviewExpiry(ApprovalRequest approval)
    {
        var node = approval.ThresholdContext.GetValueOrDefault("expiresUtc") ??
            (approval.ThresholdContext.GetValueOrDefault("approvalBinding") as JsonObject)?["expiresUtc"];
        return DateTime.TryParse(node?.ToString(), System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out var expiry) ? expiry.ToUniversalTime() : null;
    }

    private async Task<bool> CanReadReviewAsync(ApprovalRequest approval, ResolvedCompanyMembershipContext membership, CancellationToken ct)
    {
        if (approval.TargetEntityType == "annual_plan_version" && !await _serviceProvider
            .GetRequiredService<VirtualCompany.Application.Orchestration.IAnnualPlanningService>().CanReadApprovalAsync(approval.CompanyId, approval.TargetEntityId, ct)) return false;
        if (membership.MembershipRole is not (CompanyMembershipRole.Owner or CompanyMembershipRole.Admin) &&
            !IsInitiatingUser(approval, membership.UserId) && !approval.Steps.Any(step => CanDecide(step, membership))) return false;
        var finance = approval.TargetEntityType is not ("task" or "workflow" or "action" or "operating_plan" or "operating_decision" or
            "sales_meeting_invitation" or "sales_meeting_change_request" or "sales_meeting_change_proposal" or "marketing_channel_action") ||
            approval.ApprovalType.StartsWith("finance", StringComparison.Ordinal) || RequiresIndependentFinanceReview(approval);
        if (approval.TargetEntityType == "task")
            finance |= await _dbContext.WorkTasks.Where(x => x.CompanyId == approval.CompanyId && x.Id == approval.TargetEntityId)
                .AnyAsync(x => x.Type.StartsWith("finance") || x.Type.StartsWith("accounting") || x.Type.StartsWith("supplier") || x.Type.StartsWith("invoice") ||
                    x.Type.StartsWith("payment") || x.AssignedAgent != null && x.AssignedAgent.Department.ToLower() == "finance", ct);
        if (finance && !(await _serviceProvider.GetRequiredService<IAuthorizationService>().AuthorizeAsync(
            _serviceProvider.GetRequiredService<ICurrentUserAccessor>().Principal, approval.CompanyId, CompanyPolicies.FinanceView)).Succeeded) return false;
        // P11 derived content retains all transitive source scopes, even for a designated reviewer.
        if (approval.TargetEntityType == "task" && (await _dbContext.WorkTasks.AnyAsync(x => x.CompanyId == approval.CompanyId && x.ParentTaskId == approval.TargetEntityId, ct) || await _dbContext.CollaborationContributions.AnyAsync(x =>
            x.CompanyId == approval.CompanyId && (x.SourceTaskId == approval.TargetEntityId ||
            _dbContext.WorkTasks.Any(t => t.CompanyId == approval.CompanyId && t.Id == x.ParentTaskId && t.ParentTaskId == approval.TargetEntityId)), ct)))
        {
            var scope = await _serviceProvider.GetRequiredService<CompanyWorkVisibility>().ResolveAsync(approval.CompanyId, ct);
            return await scope.Tasks(_dbContext.WorkTasks.Where(x => x.CompanyId == approval.CompanyId)).AnyAsync(x => x.Id == approval.TargetEntityId, ct)
                && await CollaborationContentVisibility.AllowsAsync(_dbContext, scope, approval.TargetEntityId, ct);
        }
        return true;
    }

    private async Task<ApprovalReviewDto> ReviewAsync(ApprovalRequest approval, CancellationToken ct)
    {
        var membership = await _companyMembershipContextResolver.ResolveAsync(approval.CompanyId, ct);
        var hash = await MaterialHashAsync(approval, ct);
        var binding = approval.DecisionChain.GetValueOrDefault("reviewMaterialHash")?.ToString();
        var changed = binding is not null && binding != hash;
        var expiry = ReviewExpiry(approval);
        var step = approval.CurrentActionableStep;
        var reviewer = step?.ApproverRef.Replace('_', ' ') ?? "Review completed";
        if (step?.ApproverType == ApprovalStepApproverType.User && Guid.TryParse(step.ApproverRef, out var reviewerId))
            reviewer = await _dbContext.Users.Where(x => x.Id == reviewerId && _dbContext.CompanyMemberships.Any(m => m.CompanyId == approval.CompanyId && m.UserId == x.Id))
                .Select(x => x.DisplayName).FirstOrDefaultAsync(ct) ?? "Designated reviewer";
        var comparisons = new List<ApprovalComparisonDto>();
        // Display only explicit business evidence, never an arbitrary serialized command/provider payload.
        var before = approval.ThresholdContext.GetValueOrDefault("before") as JsonObject;
        var proposed = approval.ThresholdContext.GetValueOrDefault("proposed") as JsonObject;
        foreach (var field in new[] { "Discount", "Amount", "Currency", "Recipient", "Subject", "Start", "End", "Terms" })
        {
            string? Read(JsonObject? source) => source?[field] is JsonValue value ? value.ToString() : null;
            if (Read(before) is not null || Read(proposed) is not null) comparisons.Add(new(field, Read(before), Read(proposed)));
        }
        var evidence = new List<ApprovalEvidenceDto>();
        string? executionStatus = null;
        if (approval.TargetEntityType == "annual_plan_version")
        {
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
        }
        if (approval.TargetEntityType == "task")
        {
            executionStatus = (await _dbContext.WorkTasks.Where(x => x.CompanyId == approval.CompanyId && x.Id == approval.TargetEntityId)
                .Select(x => x.Status).SingleAsync(ct)).ToStorageValue().Replace('_', ' ');
            evidence.Add(new("Open proposed work and source evidence", $"/work?companyId={approval.CompanyId}&tab=tasks&taskId={approval.TargetEntityId}"));
            if (approval.ApprovalType == "planning_work_review")
            {
                var origin = await _dbContext.Set<DecisionWorkOrigin>().AsNoTracking().SingleAsync(x => x.CompanyId == approval.CompanyId && x.TaskId == approval.TargetEntityId, ct);
                comparisons.Add(new("Objective", null, origin.Objective));
                comparisons.Add(new("Accountable owner", null, await _dbContext.Users.Where(x => x.Id == origin.OwnerUserId).Select(x => x.DisplayName).SingleAsync(ct)));
                comparisons.Add(new("Due date (UTC)", null, origin.DueUtc.ToString("yyyy-MM-dd HH:mm")));
                comparisons.Add(new("Acceptance outcome", null, origin.AcceptanceOutcome));
                comparisons.Add(new("Proposed constraints", null, origin.ProposedConstraints));
                var people = await _dbContext.Set<DecisionWorkCollaborator>().Where(x => x.CompanyId == approval.CompanyId && x.OriginId == origin.Id).OrderBy(x => x.Name).Select(x => x.Name).ToListAsync(ct);
                comparisons.Add(new("Proposed collaborators", null, people.Count == 0 ? "None proposed" : string.Join(", ", people)));
                comparisons.Add(new("Retained source version", null, $"Version {origin.SourceVersion}"));
                evidence.Add(new("Open retained source decision and work snapshot", $"/work/source?companyId={approval.CompanyId}&taskId={approval.TargetEntityId}"));
            }
            var artifacts = await _dbContext.CollaborationContributions.AsNoTracking().Where(x => x.CompanyId == approval.CompanyId &&
                (x.SourceTaskId == approval.TargetEntityId || _dbContext.WorkTasks.Any(t => t.CompanyId == approval.CompanyId && t.Id == x.ParentTaskId && t.ParentTaskId == approval.TargetEntityId)))
                .OrderBy(x => x.Sequence).ThenBy(x => x.Version).Take(50).ToListAsync(ct);
            evidence.AddRange(artifacts.Select(x => new ApprovalEvidenceDto($"{x.Objective}, version {x.Version}",
                $"/agents/work/task/{approval.TargetEntityId}/collaboration?companyId={approval.CompanyId}&artifactId={x.Id}&view=list")));
        }
        if (approval.TargetEntityType == "sales_meeting_invitation")
        {
            var invitation = await _dbContext.SalesMeetingInvitations.AsNoTracking().SingleAsync(x => x.CompanyId == approval.CompanyId && x.Id == approval.TargetEntityId, ct);
            comparisons.Add(new("Recipient", null, invitation.AttendeeEmail));
            comparisons.Add(new("Subject", null, invitation.Title));
            executionStatus = invitation.Status.ToStorageValue().Replace('_', ' ');
            evidence.Add(new("Open Sales meeting record", $"/app/sales/leads/{invitation.LeadId}?companyId={approval.CompanyId}"));
        }
        if (approval.TargetEntityType == "action")
        {
            executionStatus = (await _dbContext.ToolExecutionAttempts.Where(x => x.CompanyId == approval.CompanyId && x.Id == approval.TargetEntityId)
                .Select(x => x.Status).SingleAsync(ct)).ToStorageValue().Replace('_', ' ');
            if(approval.Status==ApprovalRequestStatus.Approved&&executionStatus=="awaiting approval"&&await _dbContext.CompanyOutboxMessages.AnyAsync(x=>x.CompanyId==approval.CompanyId&&x.Topic==ReviewedTaskPolicyMessage.Topic&&x.IdempotencyKey==$"reviewed-task:{approval.Id:N}:{approval.TargetEntityId:N}",ct))executionStatus="Queued for internal execution";
        }
        if (approval.TargetEntityType == "finance_integration_write")
            executionStatus = (await _dbContext.FinanceIntegrationWriteCommands.Where(x => x.CompanyId == approval.CompanyId && x.Id == approval.TargetEntityId)
                .Select(x => x.Status).SingleAsync(ct)).Replace('_', ' ');
        return new($"{approval.UpdatedUtc.Ticks}:{hash}", approval.Status == ApprovalRequestStatus.Pending &&
            !changed && !(expiry <= DateTime.UtcNow) && step is not null && membership is not null && CanDecide(step, membership), changed, expiry,
            reviewer,
            binding is null ? "Creation version was not recorded. This review binds the current proposal; earlier before/after evidence may be unavailable."
                : "Proposal bound to its recorded creation version. Execution also rechecks the owning workflow.", comparisons, evidence, executionStatus);
    }

    private async Task EnsureReviewedVersionAsync(ApprovalRequest approval, ApprovalDecisionCommand command, Guid actor, CancellationToken ct)
    {
        var hash = await MaterialHashAsync(approval, ct);
        var original = approval.DecisionChain.GetValueOrDefault("reviewMaterialHash")?.ToString();
        var current = $"{approval.UpdatedUtc.Ticks}:{hash}";
        if (original is not null && original != hash || command.ReviewToken is not null && command.ReviewToken != current &&
            command.ReviewToken.StartsWith($"{approval.UpdatedUtc.Ticks}:", StringComparison.Ordinal))
        {
            approval.MarkStale("The proposal changed. Create and review a new approval request.");
            await UpdateLinkedEntityAfterDecisionAsync(approval, ct);
            await WriteReviewAuditAsync(approval, actor, "proposal_changed", ct);
            EnqueueApprovalUpdatedEvent(approval, "stale");
            await _dbContext.SaveChangesAsync(ct);
            throw new ApprovalProposalChangedException();
        }
        if (command.ReviewToken is not null && command.ReviewToken != current)
            throw new InvalidOperationException("The review changed. Refresh the current proposal and reviewer step before deciding.");
    }

    private Task WriteReviewAuditAsync(ApprovalRequest approval, Guid actor, string decision, CancellationToken ct) =>
        _auditEventWriter.WriteAsync(new AuditEventWriteRequest(approval.CompanyId, "user", actor,
            "approval.review." + decision, AuditTargetTypes.ApprovalRequest, approval.Id.ToString("N"),
            decision, DataSources: ["approvals"], RationaleSummary: approval.DecisionSummary,
            Metadata: new Dictionary<string, string?> { ["approvalRequestId"] = approval.Id.ToString("N"), ["targetEntityType"] = approval.TargetEntityType,
                ["targetEntityId"] = approval.TargetEntityId.ToString("N"), ["materialHash"] = approval.DecisionChain.GetValueOrDefault("reviewMaterialHash")?.ToString(),
                ["reviewedMaterialHash"] = approval.DecisionChain.GetValueOrDefault("lastReviewedMaterialHash")?.ToString() }), ct);
}

internal sealed class ApprovalProposalChangedException : InvalidOperationException
{
    public ApprovalProposalChangedException() : base("The proposal changed. Create and review a new approval request.") { }
}

