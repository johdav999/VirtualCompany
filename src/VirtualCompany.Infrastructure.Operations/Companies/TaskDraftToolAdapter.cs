using System.Text.Json;
using System.Text.Json.Nodes;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Marketing;
using VirtualCompany.Application.Sales;
using VirtualCompany.Application.Support;
using VirtualCompany.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;
namespace VirtualCompany.Infrastructure.Companies;

// Typed adapters into the existing module commands; no delivery operation is offered here.
public sealed class TaskDraftToolAdapter(InternalCompanyToolContract inner,ITaskTypePolicyEvaluator policies,
    ISalesAgentDecisionService sales,IMarketingOperationsService marketing,ISupportReplyDraftService support,IAgentExecutionControlGate controls,
    VirtualCompanyDbContext db,ICompanyContextAccessor companyContext):IInternalCompanyToolContract
{
    public async Task<InternalToolExecutionResponse> ExecuteAsync(InternalToolExecutionRequest request,CancellationToken ct)
    {
        Guid admission;
        try { admission=await controls.AdmitAsync(request.CompanyId,request.AgentId,"tool",request.Context.ExecutionId.ToString("D"),ct,request.TaskId); }
        catch(ExecutionPausedException){return InternalToolExecutionResponse.Failed("denied","execution_paused","New steps for this agent are paused. Review execution controls.");}
        catch(ExecutionControlConflictException){return InternalToolExecutionResponse.Failed("denied","execution_already_admitted","This step already has a recorded admission. Review its outcome before recovery.");}
        try {
            var result=await ExecuteCoreAsync(request,ct);
            await controls.AcknowledgeAsync(request.CompanyId,admission,result.Success||result.Status=="denied"||result.Status=="awaiting_approval",CancellationToken.None);
            return result;
        } catch { await controls.AcknowledgeAsync(request.CompanyId,admission,false,CancellationToken.None); throw; }
    }
    private async Task<InternalToolExecutionResponse> ExecuteCoreAsync(InternalToolExecutionRequest request,CancellationToken ct)
    {
        var check=await policies.CheckAsync(request.CompanyId,request.AgentId,request.ToolName,request.TaskId,true,ct,request.ExecutionId);
        if(check is not null && check.State!="available")return InternalToolExecutionResponse.Failed("denied",check.ReasonCode,check.Explanation);
        var entry=TaskTypePolicyCatalogue.All.SingleOrDefault(x=>x.ToolName==request.ToolName && !x.Finance && x.Code!="sales.account_research");
        if(entry is null)return await inner.ExecuteAsync(request,ct);
        if(request.ActionKind!=ToolActionType.Recommend || request.Scope!=entry.Department.ToLowerInvariant())return InternalToolExecutionResponse.Failed("denied","task_action_mismatch","Only the supported internal draft action is available.");
        if(!request.Payload.TryGetValue(entry.RecordKey,out var node)||!Guid.TryParse(node?.ToString(),out var record))return InternalToolExecutionResponse.Failed("denied","record_required","Choose a valid company record.");
        if(request.Context.ActorUserId is not { } actor || actor==Guid.Empty)return InternalToolExecutionResponse.Failed("denied","actor_required","An authorized originating actor is required.");
        var member=await db.CompanyMemberships.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x=>x.CompanyId==request.CompanyId&&x.UserId==actor&&x.Status==CompanyMembershipStatus.Active,ct);
        if(member is null)return InternalToolExecutionResponse.Failed("denied","actor_membership_unavailable","The originating actor no longer has active company access.");
        var previous=companyContext.Membership;
        companyContext.SetCompanyContext(new ResolvedCompanyMembershipContext(member.Id,request.CompanyId,actor,"",member.Role,member.Status));
        try {
        object? result=entry.Code switch {
            "sales.proposal_drafting"=>await sales.AdviseProposalAsync(request.CompanyId,request.AgentId,actor,new(record,Objective:"Prepare an internal proposal draft grounded in approved claims. Identify missing evidence, pricing and terms needing review; do not deliver it."),ct),
            "marketing.content_drafting"=>await marketing.GenerateContentVariantsAsync(request.CompanyId,actor,record,new(request.AgentId,"sales_enablement",1,"Create one internal evidence-backed content draft for human review; do not publish.",$"task-policy:{request.CompanyId:N}:{request.TaskId:N}"),ct),
            "support.grounded_reply_drafting"=>await support.GenerateDraftAsync(request.CompanyId,actor,record,new(ForceReview:true),ct),_=>null};
        if(result is null)return InternalToolExecutionResponse.Failed("failed","record_unavailable","The owning module did not produce a draft.");
        // An owner can return a review/restriction result without producing content.
        // Retain that evidence, but never count it as a prepared output.
        if(result is GenerateMarketingContentVariantsResult content && content.Variants.Count==0)
            return InternalToolExecutionResponse.Failed("failed","draft_evidence_missing","Marketing could not retain an evidence-backed draft. Review the brief and missing sources.",new(){["draft"]=JsonSerializer.SerializeToNode(content)});
        if(result is SalesProposalAdviceResult proposal && (proposal.Advice.Status is not (AgentAiRunStatuses.Completed or AgentAiRunStatuses.NeedsReview) || string.IsNullOrWhiteSpace(proposal.Advice.Summary)))
            return InternalToolExecutionResponse.Failed("failed","proposal_drafting_unavailable","Sales proposal reasoning did not produce a retained reviewable result.",new(){["draft"]=JsonSerializer.SerializeToNode(proposal)});
        return InternalToolExecutionResponse.Succeeded("An internal reviewable draft was retained. Customer delivery or publication requires its separately governed workflow.",new(){["draft"]=JsonSerializer.SerializeToNode(result)},new(){["taskType"]=JsonValue.Create(entry.Code),["deliveryPerformed"]=JsonValue.Create(false)});
        } finally {companyContext.SetCompanyContext(previous);}
    }
}
