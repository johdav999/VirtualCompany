using static VirtualCompany.Infrastructure.Companies.ApprovalPayloadValues;
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

    private Task<string> MaterialHashAsync(ApprovalRequest approval, CancellationToken ct) =>
        _serviceProvider.GetRequiredService<ApprovalReviewMaterialHasher>().ComputeHashAsync(approval, ct);

    private static DateTime? ReviewExpiry(ApprovalRequest approval)
    {
        var node = approval.ThresholdContext.GetValueOrDefault("expiresUtc") ??
            (approval.ThresholdContext.GetValueOrDefault("approvalBinding") as JsonObject)?["expiresUtc"];
        return DateTime.TryParse(node?.ToString(), System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out var expiry) ? expiry.ToUniversalTime() : null;
    }

    private async Task<bool> CanReadReviewAsync(ApprovalRequest approval, ResolvedCompanyMembershipContext membership, CancellationToken ct)
    {
        if (TargetHandler(approval) is { } targetHandler && !await targetHandler.CanReadAsync(approval, ct)) return false;
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
        if (TargetHandler(approval) is { } targetHandler)
        {
            var details = await targetHandler.GetReviewDetailsAsync(approval, ct);
            comparisons.AddRange(details.Comparison);
            evidence.AddRange(details.Evidence);
            executionStatus = details.ExecutionStatus;
        }
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

