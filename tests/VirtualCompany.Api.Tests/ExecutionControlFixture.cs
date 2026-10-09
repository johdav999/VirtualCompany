using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Agents;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
namespace VirtualCompany.Api.Tests;

public sealed record ExecutionFixtureProfile(Guid CompanyId,Guid AgentId,Guid SecondAgentId,Guid RecordId,Guid TaskId);
public static class ExecutionControlFixture
{
    public static async Task<ExecutionFixtureProfile> SeedAsync(TestWebApplicationFactory factory,Guid company,Guid owner,string subject="p13-owner")
    {
        var agent=Guid.NewGuid();var second=Guid.NewGuid();var record=Guid.NewGuid();
        await factory.SeedAsync(async db=>{
            if(!await db.Companies.IgnoreQueryFilters().AnyAsync(x=>x.Id==company))db.AddRange(new Company(company,"P16 Pause Company"),new CompanyMembership(Guid.NewGuid(),company,owner,CompanyMembershipRole.Owner,CompanyMembershipStatus.Active));
            foreach(var id in new[]{agent,second}){
                db.Add(new Agent(id,company,"p16-research-"+id.ToString("N"),id==agent?"Alex research":"Sam research","Sales specialist","Sales",null,AgentSeniority.Senior,AgentStatus.Active,AgentAutonomyLevel.Level2,
                    tools:new Dictionary<string,JsonNode?>{["allowed"]=new JsonArray("sales.research_prospect"),["actions"]=new JsonArray("recommend")},scopes:new Dictionary<string,JsonNode?>{["recommend"]=new JsonArray("sales"),["responsibilityPolicy"]=new JsonObject{["allowedDomains"]=new JsonArray("sales"),["deniedDomains"]=new JsonArray()}},escalationRules:new Dictionary<string,JsonNode?>{["escalateTo"]=JsonValue.Create("company_owner")}));
                var goal=new CompanyGoal(Guid.NewGuid(),company,"Review company-owned prospects","Retain current research evidence.",CompanyGoalPriority.Normal,DateTime.UtcNow.AddDays(-1),DateTime.UtcNow.AddDays(5),ownerAgentId:id);goal.Activate();db.Add(goal);
                var policy=new TaskTypePolicy(company,id,"sales.account_research");var revision=new TaskTypePolicyRevision(policy,"automatic",5,DateTime.UtcNow.AddDays(1),owner,"Reviewed bounded internal research",new string('a',64),"{}",DateTime.UtcNow);policy.Activate(revision.Id);db.AddRange(policy,revision);
            }
            var config=new CompanyOperatingConfiguration(Guid.NewGuid(),company);config.Update(agent,CompanyAutonomyLevel.ControlledExecution,"UTC",6,60,4,5,12,3,120,4,20,500);db.Add(config);
            var profile=new IdealCustomerProfile(Guid.NewGuid(),company,"Owned prospect profile",1,owner,"SE","software",1,1000,null,null,"buyer","","","Evidence recorded","",null);profile.Activate();db.Add(profile);
            var run=new ProspectingRun(Guid.NewGuid(),company,profile.Id,owner,"Company-owned fixture",10,10,"first_party","SE",30,0,null);db.Add(run);
            db.Add(new ProspectAccount(record,company,run.Id,profile.Id,"P16 internal account","example.test","SE","software",20,1000,"first_party","p16-owned-evidence",DateTime.UtcNow));
        });
        using var client=BusinessWorkEvidenceIntegrationTests.Client(factory,subject);
        using var response=await client.PostAsJsonAsync($"/api/companies/{company}/task-policies/queue",new QueueTaskPolicyWork(agent,"sales.account_research",record,1));
        if(!response.IsSuccessStatusCode)throw new InvalidOperationException("P16 fixture queue failed: "+await response.Content.ReadAsStringAsync(),(factory as ExecutionControlNativeFactory)?.LastError);
        return new(company,agent,second,record,await response.Content.ReadFromJsonAsync<Guid>());
    }
}
