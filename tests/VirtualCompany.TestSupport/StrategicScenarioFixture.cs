using System.Net.Http.Json;
using A=VirtualCompany.Application.Orchestration;
using F=VirtualCompany.Application.Finance;
using Ap=VirtualCompany.Application.Approvals;
using VirtualCompany.Domain.Entities;
namespace VirtualCompany.Api.Tests;
public sealed record StrategicScenarioFixture(AnnualPlanningFixture Annual,FinanceRollingPlanningFixture Finance,Guid PlanId,Guid ForecastId)
{
    public string Root=>$"/api/companies/{Annual.Quarter.Company.Company}/planning/scenarios";
    public F.StrategicScenarioInput Input=>new("Baseline sensitivity",Annual.Quarter.Company.Owner,PlanId,ForecastId,"SEK",
        new(3,"orders",3,100,80,20,10,10,20,.5m,100,0,0,.5m,.75m,100),
        [new(1,200,50,"Incremental tooling and assumed owner funding"),new(2,0,0,"No additional investment or funding"),new(3,0,0,"No additional investment or funding")],
        [new(2,"Capacity review",Annual.Quarter.Company.Owner,Annual.Quarter.Initiative)],"Explicit partial Finance source scaled by 3; sensitivity, not approved funding.");
    public static async Task<StrategicScenarioFixture> Seed(TestWebApplicationFactory f,AnnualPlanningFixture? existing=null,FinanceRollingPlanningFixture? existingFinance=null)
    {
        var a=existing??await AnnualPlanningFixture.Seed(f);var finance=existingFinance??await FinanceRollingPlanningFixture.Seed(f,a.Quarter.Company);
        using var h=a.Client(f);var plan=await AnnualPlanningFixture.Save(h,a);var reviewed=await AnnualPlanningFixture.Read<A.AnnualPlanDocument>(await h.PostAsJsonAsync(a.Root+$"/versions/{plan.Summary.Id}/review",new A.ReviewAnnualPlan(plan.Summary.StateRevision,plan.Plan.Fingerprint)));
        var apRoot=$"/api/companies/{a.Quarter.Company.Company}/approvals/{reviewed.Summary.ApprovalId}";var ap=await AnnualPlanningFixture.Read<Ap.ApprovalRequestDto>(await h.GetAsync(apRoot));
        await AnnualPlanningFixture.Read<Ap.ApprovalDecisionResultDto>(await h.PostAsJsonAsync(apRoot+"/decisions",new Ap.ApprovalDecisionCommand(ap.Id,"approve",Comment:"Test actor annual planning governance",ClientRequestId:Guid.NewGuid(),ReviewToken:ap.Review!.Token)));
        var input=finance.Input() with{Query=finance.Query with{FinanceAccountId=null},Assumptions=[new(FinanceRollingPlanningFixture.Date(2026,11),finance.Account,null,"SEK",-1250.25m,"Revenue assumption"),new(FinanceRollingPlanningFixture.Date(2026,12),finance.Account,null,"SEK",-1300.50m,"Revenue assumption"),new(FinanceRollingPlanningFixture.Date(2026,11),finance.Expense,null,"SEK",800,"Payroll assumption"),new(FinanceRollingPlanningFixture.Date(2026,12),finance.Expense,null,"SEK",900,"Payroll assumption")]};
        var p=await AnnualPlanningFixture.Read<F.FinanceForecastPreview>(await h.PostAsJsonAsync(finance.Root+"/preview",input));
        var forecast=await AnnualPlanningFixture.Read<F.FinanceForecastRevision>(await h.PostAsJsonAsync(finance.Root+"/versions",new F.SaveFinanceForecast(Guid.NewGuid(),"Strategic Finance baseline",input,p.Fingerprint)));
        return new(a,finance,plan.Summary.Id,forecast.Summary.Id);
    }
    public static async Task<F.StrategicScenarioDocument> Save(HttpClient h,StrategicScenarioFixture s,F.StrategicScenarioInput? input=null,F.StrategicScenarioDocument? previous=null)
    {
        input??=s.Input;var p=await AnnualPlanningFixture.Read<F.StrategicScenarioPreview>(await h.PostAsJsonAsync(s.Root+"/preview",input));
        return await AnnualPlanningFixture.Read<F.StrategicScenarioDocument>(await h.PostAsJsonAsync(s.Root+"/versions",new F.SaveStrategicScenario(Guid.NewGuid(),input,p.Fingerprint,previous?.Summary.Id,previous?.Summary.Revision??0)));
    }
}
