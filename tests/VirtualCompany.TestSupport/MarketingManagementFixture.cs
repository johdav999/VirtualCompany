using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Marketing;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed record MarketingManagementFixture(WeeklyWorkspaceFixture Company, Guid Model, Guid Email, Guid Social, Guid Segment, Guid Experiment)
{
    public static readonly DateTime Start = WeeklyWorkspaceFixture.Start;
    public static readonly DateTime End = Start.AddDays(1);
    public MarketingManagementQuery Query => new(2026,9,"SEK",ModelId:Model);
    public static async Task<MarketingManagementFixture> Seed(TestWebApplicationFactory factory)
    {
        var s=await WeeklyWorkspaceFixture.Seed(factory);Guid model=default,email=default,social=default,segment=default,experiment=default;
        await factory.SeedAsync(async db=>{
            var campaign=await db.SalesCampaigns.IgnoreQueryFilters().SingleAsync(x=>x.Id==s.Campaign);
            db.Entry(campaign).Property(x=>x.PlannedBudget).CurrentValue=200m;
            db.Entry(campaign).Property(x=>x.BudgetCurrency).CurrentValue="SEK";
            var plan=new MarketingPlan(Guid.NewGuid(),s.Company,"Recorded planning limit","Portfolio context",Start,Start.AddMonths(2),600,"SEK");
            var other=new SalesCampaign(Guid.NewGuid(),s.Company,campaign.SalesSequenceId,"Other recorded allocation","customers",createdUtc:Start);
            var segmentRow=new MarketingCustomerSegment(Guid.NewGuid(),s.Company,"Recorded audience","Evidence-backed context",s.Owner);
            var version=new MarketingCustomerSegmentVersion(Guid.NewGuid(),s.Company,segmentRow.Id,2,"{}","{}","{}","[]","{}",100,200,"bottom_up",.8m,"{}","{}",80,"[]",Start,s.Owner,"p22:segment");segment=version.Id;
            var ps=new MarketingPlanSegment(Guid.NewGuid(),s.Company,plan.Id,segment,"primary",1,"Audience context","Recorded contribution");
            var pc=new MarketingPlanCampaign(Guid.NewGuid(),s.Company,plan.Id,s.Campaign,"Recorded purpose",200,"SEK",1,"Contribution",null,"p22:campaign");
            var exp=new MarketingExperiment(Guid.NewGuid(),s.Company,"Recorded guarded experiment","Review response effect","responses","complaints",100,Start,End,s.Campaign);experiment=exp.Id;
            db.AddRange(plan,other,segmentRow,version,ps,pc,exp,
                new MarketingPlanCampaign(Guid.NewGuid(),s.Company,plan.Id,other.Id,"Other allocation",150,"SEK",2,"Contribution",null,"p22:other"),
                new MarketingPlanCampaignSegment(Guid.NewGuid(),s.Company,pc.Id,ps.Id,"Recorded mapping","Current campaign audience"));
            await db.SaveChangesAsync();
            var measurement=new MarketingMeasurementService(db);
            model=(await measurement.CreateModelAsync(s.Company,new("Even recorded","even","{}","Configured attribution; no causal uplift.",30,"p22:model"),default)).Id;
            email=(await measurement.RecordTouchAsync(s.Company,new("campaign",s.Campaign,"click","email","=quoted,source",1,Start.AddHours(1),100,"SEK","{}","p22:email"),default)).Id;
            social=(await measurement.RecordTouchAsync(s.Company,new("campaign",s.Campaign,"click","social","social-source",1,Start.AddHours(2),null,"SEK","{}","p22:social"),default)).Id;
            await measurement.RecordTouchAsync(s.Foreign,new("campaign",s.Campaign,"click","email","foreign-secret-source",1,Start.AddHours(3),999,"SEK","{}","p22:foreign"),default);
            await measurement.RunAttributionAsync(s.Company,new(model,"campaign",s.Campaign,10,"leads",Start,End,"p22:run"),default);
            await measurement.EvaluateExperimentAsync(s.Company,new(experiment,100,100,0,true,true,"{\"guardrail\":\"breached\"}"),default);
            await Stabilize(db,s.Company);
        });
        return new(s,model,email,social,segment,experiment);
    }
    public static async Task Stabilize(VirtualCompanyDbContext db,Guid company)
    {
        foreach(var row in await db.MarketingAttributionModels.IgnoreQueryFilters().Where(x=>x.CompanyId==company).ToArrayAsync())db.Entry(row).Property(x=>x.CreatedUtc).CurrentValue=WeeklyWorkspaceFixture.Now.AddHours(-1);
        foreach(var row in await db.MarketingAttributionResults.IgnoreQueryFilters().Where(x=>x.CompanyId==company).ToArrayAsync())db.Entry(row).Property(x=>x.CreatedUtc).CurrentValue=WeeklyWorkspaceFixture.Now.AddHours(-1);
        foreach(var row in await db.MarketingExperimentDecisions.IgnoreQueryFilters().Where(x=>x.CompanyId==company).ToArrayAsync())db.Entry(row).Property(x=>x.CreatedUtc).CurrentValue=WeeklyWorkspaceFixture.Now.AddHours(-1);
    }
    public static HttpClient Client(TestWebApplicationFactory factory,Guid company,string subject="p19-owner")
    {var http=WeeklyWorkspaceFixture.Client(factory,subject);http.DefaultRequestHeaders.Add("X-Company-Id",company.ToString());return http;}
    public SaveMarketingBudgetProposal Command(Guid? previous=null,int? revision=null)=>new(Guid.NewGuid(),Query,new("SEK",300,500,[new(Company.Campaign,300,"Assume improved qualified demand; no predicted uplift.")],"Recorded planning assumption"),previous,revision);
}
