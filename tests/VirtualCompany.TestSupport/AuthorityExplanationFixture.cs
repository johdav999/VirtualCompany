using System.Text.Json.Nodes;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Api.Tests;

public sealed record AuthorityFixtureProfile(Guid CompanyId, Guid AgentId, Guid TaskId, Guid InitiativeId, Guid PlanId);
public static class AuthorityExplanationFixture
{
    public static async Task<AuthorityFixtureProfile> SeedAsync(TestWebApplicationFactory factory, Guid company, Guid owner,
        CompanyAutonomyLevel companyLevel = CompanyAutonomyLevel.ControlledExecution,
        AgentAutonomyLevel agentLevel = AgentAutonomyLevel.Level2, bool external = false)
    {
        var agentId=Guid.NewGuid(); var taskId=Guid.NewGuid(); var goalId=Guid.NewGuid(); var planId=Guid.NewGuid(); var initiativeId=Guid.NewGuid();
        await factory.SeedAsync(db => {
            var agent = new Agent(agentId,company,"p14-authority","P14 Authority specialist","Finance specialist","Finance",null,
                AgentSeniority.Senior,AgentStatus.Active,agentLevel,
                tools:new Dictionary<string,JsonNode?> { ["allowed"]=new JsonArray("get_cash_balance","list_transactions","categorize_transaction","recommend_invoice_approval_decision","finance.removed_tool"),
                    ["actions"]=new JsonArray("read","recommend","execute"), ["integrationAvailability"]=new JsonObject{["list_transactions"]=false} },
                scopes:new Dictionary<string,JsonNode?> { ["read"]=new JsonArray("finance"),["recommend"]=new JsonArray("finance"),["execute"]=new JsonArray("finance") },
                thresholds:new Dictionary<string,JsonNode?> { ["financePolicy"]=new JsonObject{["requireApprovalForExecute"]=true} },
                escalationRules:new Dictionary<string,JsonNode?>{["escalateTo"]=JsonValue.Create("company_owner")}); db.Add(agent);
            var config=new CompanyOperatingConfiguration(Guid.NewGuid(),company);
            config.Update(agentId,companyLevel,"UTC",6,60,4,5,12,3,120,4,20,500);
            db.Add(config);
            var goal=new CompanyGoal(goalId,company,"P14 source-linked review","Review retained evidence within current limits.",CompanyGoalPriority.High,
                DateTime.UtcNow.AddDays(-1),DateTime.UtcNow.AddDays(7),ownerAgentId:agentId);goal.Activate();db.Add(goal);
            var cycle=new OperatingCycle(Guid.NewGuid(),company,"manual",null,agentId,"p14","p14-cycle",config.Version);db.Add(cycle);
            var plan=new OperatingPlan(planId,company,cycle.Id,1,"P14 authority task","Bounded internal evidence review.");plan.SubmitForReview();db.Add(plan);
            var task=new WorkTask(taskId,company,"finance_review","P14 review current authority","Review current limits; no provider execution.",
                WorkTaskPriority.Normal,agentId,null,"user",owner);db.Add(task);
            var initiative=new OperatingInitiative(initiativeId,company,planId,goalId,"P14 retained authority review","Produce an internal source-linked review.",
                CompanyGoalPriority.High,"Reviewed evidence retained.",agentId,DateTime.UtcNow.AddDays(3),null);initiative.LinkWork(taskId,null);db.Add(initiative);
            var decision=new OperatingDecision(Guid.NewGuid(),company,planId,initiativeId,external?OperatingActionClass.ExternalExecute:OperatingActionClass.Recommend,
                "review","task",taskId.ToString("N"),agentId,"Current owning policy decides eligibility.",.9m,"low",false,"p14-decision");db.Add(decision);
            db.Add(new OperatingValidationResult(Guid.NewGuid(),company,planId,decision.Id,"fixture-policy","1",OperatingValidationOutcome.Allowed,
                "allowed","Current validation passed.",false,config.Version)); return Task.CompletedTask;
        });
        return new(company,agentId,taskId,initiativeId,planId);
    }
}
