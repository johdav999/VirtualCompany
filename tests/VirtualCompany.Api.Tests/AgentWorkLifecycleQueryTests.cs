using VirtualCompany.Application.Cockpit;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Companies;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class AgentWorkLifecycleQueryTests
{
    [Theory]
    [InlineData("pending", AgentWorkStates.Planned)]
    [InlineData("running", AgentWorkStates.Active)]
    [InlineData("approval", AgentWorkStates.AwaitingApproval)]
    [InlineData("retry", AgentWorkStates.Blocked)]
    [InlineData("blocked", AgentWorkStates.Blocked)]
    [InlineData("dead_letter", AgentWorkStates.Failed)]
    [InlineData("worker_completed", AgentWorkStates.Planned)]
    [InlineData("review_success", AgentWorkStates.Planned)]
    [InlineData("review_pause", AgentWorkStates.Paused)]
    [InlineData("review_stop", AgentWorkStates.Blocked)]
    [InlineData("outcome_completed", AgentWorkStates.Completed)]
    public void Outcome_state_uses_owning_lifecycle_not_artifact_or_worker_completion(string fixture,string expected)
    {
        var company=Guid.NewGuid();var plan=Guid.NewGuid();var task=Guid.NewGuid();var now=DateTime.UtcNow.AddSeconds(1);
        var initiative=new OperatingInitiative(Guid.NewGuid(),company,plan,Guid.NewGuid(),"Renewal","Customer outcome",CompanyGoalPriority.Normal,"Signed evidence",null,null,null);
        var dispatch=new OperatingDispatch(Guid.NewGuid(),company,initiative.Id,task,OperatingDispatchKind.SingleAgent,"test",maxAttempts:1);
        OperatingReview? review=null;
        if(fixture is not ("pending" or "review_success" or "review_pause" or "review_stop" or "outcome_completed"))
        {
            Assert.True(dispatch.TryClaim("test",now,TimeSpan.FromMinutes(1)));dispatch.Start("test",now);
            switch(fixture)
            {
                case "approval":dispatch.AwaitApproval("Human decision required",now);break;
                case "retry":dispatch=new OperatingDispatch(Guid.NewGuid(),company,initiative.Id,task,OperatingDispatchKind.SingleAgent,"test");dispatch.TryClaim("test",now,TimeSpan.FromMinutes(1));dispatch.Start("test",now);dispatch.Retry("pending_evidence","Review evidence",now.AddMinutes(1),now);break;
                case "blocked":dispatch.Block("dependency","Obtain signed terms",now);break;
                case "dead_letter":dispatch.Retry("unavailable","Recover first",now.AddMinutes(1),now);break;
                case "worker_completed":dispatch.Complete(null,null,now);break;
            }
        }
        if(fixture.StartsWith("review_"))review=new OperatingReview(Guid.NewGuid(),company,plan,1,initiative.Id,
            fixture=="review_pause"?OperatingReviewOutcome.Pause:fixture=="review_stop"?OperatingReviewOutcome.Stop:OperatingReviewOutcome.CloseSuccessful,
            "Retained review","Customer evidence","Retained draft","Human must record the outcome","v1",0.7m);
        if(fixture=="outcome_completed"){initiative.LinkWork(task,null);initiative.Complete();review=new OperatingReview(Guid.NewGuid(),company,plan,1,initiative.Id,OperatingReviewOutcome.Pause,"Old review","Evidence",null,"Review","v1",null);}
        Assert.Equal(expected,CompanyAgentWorkQueryService.InitiativeState(initiative,dispatch,review,null));
    }
}
