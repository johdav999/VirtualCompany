using System.Text.Json.Nodes;
using VirtualCompany.Application.Finance;
namespace VirtualCompany.Application.Agents;
public static class TaskTypePolicyCatalogue
{
    public static IReadOnlyList<TaskTypePolicyCatalogueEntry> All {get;}=[
        new("sales.account_research","Sales account research","Sales","sales.research_prospect","sales.research_prospect","recommend","prospectId"),
        new("sales.proposal_drafting","Sales proposal drafting","Sales","sales.proposal_advice","sales.draft_proposal","recommend","dealId"),
        new("marketing.content_drafting","Marketing content drafting","Marketing","marketing.content_variants","marketing.draft_content","recommend","briefId"),
        new("support.grounded_reply_drafting","Support grounded reply drafting","Support","support.reply_draft","support.draft_grounded_reply","recommend","caseId"),
        new("finance.stale_cash_monitoring","Finance stale-cash monitoring and internal review task","Finance",FinanceAgentCoverageCapabilityIds.DailyCash,"get_cash_balance","read","",true)];
    public static TaskTypePolicyCatalogueEntry Find(string code)=>All.SingleOrDefault(x=>x.Code==code)??throw new ArgumentException("Unsupported task type.");
}
public interface ITaskTypePolicyService
{
    Task<TaskPolicyView> GetAsync(Guid companyId,Guid agentId,string taskType,CancellationToken token);
    Task<TaskPolicyPreview> PreviewAsync(Guid companyId,TaskPolicyChange change,CancellationToken token);
    Task<TaskPolicyView> ApplyAsync(Guid companyId,TaskPolicyApply apply,CancellationToken token);
    Task<Guid> QueueAsync(Guid companyId,QueueTaskPolicyWork command,CancellationToken token);
    Task<TaskPolicyQueueContext> WorkChoicesAsync(Guid companyId,Guid agentId,string taskType,CancellationToken token);
}
public interface ITaskTypePolicyEvaluator
{
    Task<AuthorityCheckDto?> CheckAsync(Guid companyId,Guid agentId,string toolName,Guid? taskId,bool reserve,CancellationToken token,Guid? executionId=null);
}
