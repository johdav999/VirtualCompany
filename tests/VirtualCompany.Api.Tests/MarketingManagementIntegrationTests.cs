using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Marketing;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed class MarketingManagementIntegrationTests
{
    public const string Root="/api/marketing/management";
    public static string Path(MarketingManagementQuery q)=>Root+$"?year={q.Year}&month={q.Month}&currency={q.Currency}&modelId={q.ModelId}"+(q.CampaignId.HasValue?$"&campaignId={q.CampaignId}":"")+(q.SegmentVersionId.HasValue?$"&segmentVersionId={q.SegmentVersionId}":"");
    public static async Task<MarketingManagementReport> Report(HttpClient http,MarketingManagementQuery q)
    {var response=await http.GetAsync(Path(q));Assert.Equal(HttpStatusCode.OK,response.StatusCode);return(await response.Content.ReadFromJsonAsync<MarketingManagementReport>())!;}
    public static async Task<MarketingBudgetProposal> Save(HttpClient http,SaveMarketingBudgetProposal c)
    {var response=await http.PostAsJsonAsync(Root+"/proposals",c);Assert.Equal(HttpStatusCode.OK,response.StatusCode);return(await response.Content.ReadFromJsonAsync<MarketingBudgetProposal>())!;}
    [Fact]
    public async Task Native_sources_reconcile_channel_units_versions_unknown_costs_and_experiment_guardrail()
    {
        using var f=new TestWebApplicationFactory(new WeeklyWorkspaceFixture.Clock());var s=await MarketingManagementFixture.Seed(f);using var http=MarketingManagementFixture.Client(f,s.Company.Company);
        var r=await Report(http,s.Query);var email=Assert.Single(r.Channels,x=>x.Channel=="email");Assert.Equal(100,email.KnownCost);var metric=Assert.Single(email.Economics);Assert.Equal(5,metric.AttributedValue);Assert.Equal(20,metric.CostPerAttributedUnit);
        var social=Assert.Single(r.Channels,x=>x.Channel=="social");Assert.Null(social.KnownCost);Assert.Null(Assert.Single(social.Economics).CostPerAttributedUnit);Assert.Equal(1,social.UnknownCostTouches);
        Assert.Equal(s.Model,Assert.Single(r.Attribution).ModelId);Assert.Equal(1,r.Attribution[0].ModelVersion);Assert.Equal(30,r.Attribution[0].LookbackDays);Assert.Equal(10,r.Attribution[0].Allocations.Sum(x=>x.Value));
        Assert.False(Assert.Single(r.Experiments).Decision!.CausalEligible);Assert.True(r.Experiments[0].Decision!.GuardrailBreached);Assert.DoesNotContain(r.Costs,x=>x.SourceReference=="foreign-secret-source");
        var inspect=await Report(http,s.Query with{ModelId=null});Assert.All(inspect.Attribution,x=>Assert.False(x.Included));Assert.All(inspect.Channels,x=>Assert.Empty(x.Economics));
        var segment=await Report(http,s.Query with{SegmentVersionId=s.Segment});Assert.Equal(s.Company.Campaign,Assert.Single(segment.Campaigns).Id);Assert.Equal(2,Assert.Single(segment.Campaigns[0].Segments).Version);
        var csv=(await http.GetFromJsonAsync<MarketingManagementExport>(Root+"/export"+Path(s.Query)[Root.Length..]))!.Csv;
        using var parser=new Microsoft.VisualBasic.FileIO.TextFieldParser(new StringReader(csv)){HasFieldsEnclosedInQuotes=true};parser.SetDelimiters(",");string[]? channel=null;while(!parser.EndOfData){var fields=parser.ReadFields()!;if(fields.Length==10&&fields[0]=="email")channel=fields;}
        Assert.NotNull(channel);Assert.Equal("SEK",channel[1]);Assert.Equal(100,decimal.Parse(channel[2],System.Globalization.CultureInfo.InvariantCulture));Assert.Equal(5,decimal.Parse(channel[7],System.Globalization.CultureInfo.InvariantCulture));Assert.Equal(20,decimal.Parse(channel[8],System.Globalization.CultureInfo.InvariantCulture));Assert.Contains(s.Email.ToString(),csv);Assert.Contains("'=quoted,source",csv);Assert.DoesNotContain("foreign-secret",csv);Assert.Contains("marketing-management.v1",csv);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Repeated_outcome_cohorts_count_once_or_withhold_conflicting_values(bool conflict)
    {
        using var f=new TestWebApplicationFactory(new WeeklyWorkspaceFixture.Clock());var s=await MarketingManagementFixture.Seed(f);using var http=MarketingManagementFixture.Client(f,s.Company.Company);
        await f.SeedAsync(async db=>{await new MarketingMeasurementService(db).RunAttributionAsync(s.Company.Company,new(s.Model,"campaign",s.Company.Campaign,conflict?20:10,"leads",MarketingManagementFixture.Start,MarketingManagementFixture.End,"p22:repeat"),default);await MarketingManagementFixture.Stabilize(db,s.Company.Company);});
        var r=await Report(http,s.Query);Assert.Equal(conflict?0:1,r.Attribution.Count(x=>x.Included));if(conflict)Assert.All(r.Channels,x=>Assert.Empty(x.Economics));else Assert.Equal(5,Assert.Single(r.Channels.Single(x=>x.Channel=="email").Economics).AttributedValue);
    }
    [Fact]
    public async Task Duplicate_cost_versions_and_conflicting_latest_costs_never_double_spend_or_invent_economics()
    {
        using var f=new TestWebApplicationFactory(new WeeklyWorkspaceFixture.Clock());var s=await MarketingManagementFixture.Seed(f);using var http=MarketingManagementFixture.Client(f,s.Company.Company);
        await f.SeedAsync(async db=>{await new MarketingMeasurementService(db).RecordTouchAsync(s.Company.Company,new("campaign",s.Company.Campaign,"click","email","=quoted,source",2,MarketingManagementFixture.Start.AddHours(1),120,"SEK","{}","p22:cost-v2"),default);});
        var r=await Report(http,s.Query);Assert.Equal(120,r.Channels.Single(x=>x.Channel=="email").KnownCost);Assert.Single(r.Costs.Where(x=>x.Channel=="email"&&x.Included));Assert.False(r.Attribution[0].Included);
        await f.SeedAsync(async db=>{await new MarketingMeasurementService(db).RecordTouchAsync(s.Company.Company,new("campaign",s.Company.Campaign,"click","email","=quoted,source",2,MarketingManagementFixture.Start.AddHours(1),121,"SEK","{}","p22:cost-conflict"),default);});
        r=await Report(http,s.Query);Assert.Null(r.Channels.Single(x=>x.Channel=="email").KnownCost);
    }
    [Theory][InlineData("version")][InlineData("overlap")][InlineData("allocation")][InlineData("shape")]
    public async Task Unknown_model_versions_overlapping_windows_and_invalid_allocations_are_excluded(string kind)
    {
        using var f=new TestWebApplicationFactory(new WeeklyWorkspaceFixture.Clock());var s=await MarketingManagementFixture.Seed(f);using var http=MarketingManagementFixture.Client(f,s.Company.Company);
        await f.SeedAsync(async db=>{
            if(kind=="overlap"){await new MarketingMeasurementService(db).RunAttributionAsync(s.Company.Company,new(s.Model,"campaign",s.Company.Campaign,10,"leads",MarketingManagementFixture.Start,MarketingManagementFixture.End.AddHours(1),"p22:overlap"),default);await MarketingManagementFixture.Stabilize(db,s.Company.Company);}
            else if(kind is "version" or "shape"){var run=await db.MarketingAttributionResults.IgnoreQueryFilters().SingleAsync(x=>x.CompanyId==s.Company.Company);db.Entry(run).Property(x=>x.EvidenceJson).CurrentValue=kind=="shape"?"[]":JsonSerializer.Serialize(new{Id=s.Model,Version=99});}
            else{var allocation=await db.MarketingAttributionAllocations.IgnoreQueryFilters().FirstAsync(x=>x.CompanyId==s.Company.Company);db.Entry(allocation).Property(x=>x.AttributedValue).CurrentValue=999m;}
        });var r=await Report(http,s.Query);Assert.All(r.Attribution,x=>Assert.False(x.Included));Assert.All(r.Channels,x=>Assert.Empty(x.Economics));
    }
    [Fact]
    public async Task Version_selection_keeps_other_models_traceable_without_adding_their_outcomes()
    {
        using var f=new TestWebApplicationFactory(new WeeklyWorkspaceFixture.Clock());var s=await MarketingManagementFixture.Seed(f);using var http=MarketingManagementFixture.Client(f,s.Company.Company);Guid other=default;
        await f.SeedAsync(async db=>{var m=new MarketingMeasurementService(db);other=(await m.CreateModelAsync(s.Company.Company,new("Even recorded","last_touch","{}","Configured version two",60,"p22:model-v2"),default)).Id;await m.RunAttributionAsync(s.Company.Company,new(other,"campaign",s.Company.Campaign,50,"leads",MarketingManagementFixture.Start,MarketingManagementFixture.End,"p22:run-v2"),default);await MarketingManagementFixture.Stabilize(db,s.Company.Company);});
        var r=await Report(http,s.Query);Assert.Equal(2,r.Models.Count);Assert.Equal(s.Model,Assert.Single(r.Attribution.Where(x=>x.Included)).ModelId);Assert.Equal(2,r.Attribution.Single(x=>x.ModelId==other).ModelVersion);
    }
    [Fact]
    public async Task Proposal_retry_revisions_audit_and_snapshot_survive_source_corrections_without_changing_authority()
    {
        using var f=new TestWebApplicationFactory(new WeeklyWorkspaceFixture.Clock());var s=await MarketingManagementFixture.Seed(f);using var http=MarketingManagementFixture.Client(f,s.Company.Company);var c=s.Command();var saved=await Save(http,c);
        Assert.Equal(100,saved.Result.Difference);Assert.Equal(saved.Summary.Id,(await Save(http,c)).Summary.Id);
        Assert.Equal(HttpStatusCode.Conflict,(await http.PostAsJsonAsync(Root+"/proposals",c with{Assumptions=c.Assumptions with{Notes="Changed"}})).StatusCode);
        var monthly=await MonthlyReviewSnapshotIntegrationTests.Save(http,s.Company.Company,"marketing");Assert.NotNull(monthly.Workspace.MarketingManagement);
        await f.SeedAsync(async db=>{var cost=await db.MarketingAttributionTouches.IgnoreQueryFilters().SingleAsync(x=>x.Id==s.Email);db.Entry(cost).Property(x=>x.Cost).CurrentValue=200m;});
        var old=(await http.GetFromJsonAsync<MarketingBudgetProposal>(Root+"/proposals/"+saved.Summary.Id))!;Assert.Equal(JsonSerializer.Serialize(saved),JsonSerializer.Serialize(old));
        var retained=await(await http.PostAsync(MonthlyReviewSnapshotIntegrationTests.Root(s.Company.Company)+$"/{monthly.Summary.Id}/open",null)).Content.ReadFromJsonAsync<VirtualCompany.Application.Cockpit.MonthlyReviewSnapshotDto>();Assert.Equal(100,retained!.Workspace.MarketingManagement!.Channels.Single(x=>x.Channel=="email").KnownCost);
        var next=await Save(http,s.Command(saved.Summary.Id,1));Assert.Equal(2,next.Summary.Revision);Assert.Equal(200,next.Report.Channels.Single(x=>x.Channel=="email").KnownCost);Assert.Equal(saved.Summary.SeriesId,next.Summary.SeriesId);
        Assert.Equal(HttpStatusCode.Conflict,(await http.PostAsJsonAsync(Root+"/proposals",s.Command(saved.Summary.Id,1))).StatusCode);
        await f.SeedAsync(async db=>{Assert.Equal(2,await db.MarketingBudgetProposalRevisions.IgnoreQueryFilters().CountAsync(x=>x.CompanyId==s.Company.Company));Assert.Equal(2,await db.AuditEvents.IgnoreQueryFilters().CountAsync(x=>x.CompanyId==s.Company.Company&&x.Action=="marketing.budget.proposal_saved"));Assert.Equal(200,(await db.SalesCampaigns.IgnoreQueryFilters().SingleAsync(x=>x.Id==s.Company.Campaign)).PlannedBudget);Assert.Equal(600,(await db.MarketingPlans.IgnoreQueryFilters().SingleAsync(x=>x.CompanyId==s.Company.Company)).PlannedBudget);});
    }
    [Theory][InlineData("sum")][InlineData("currency")][InlineData("ceiling")][InlineData("assumed")][InlineData("duplicate")][InlineData("foreign")]
    public async Task Budget_total_currency_portfolio_other_allocations_and_assumed_ceiling_are_validated(string kind)
    {
        using var f=new TestWebApplicationFactory(new WeeklyWorkspaceFixture.Clock());var s=await MarketingManagementFixture.Seed(f);using var http=MarketingManagementFixture.Client(f,s.Company.Company);var c=s.Command();
        c=kind switch{"sum"=>c with{Assumptions=c.Assumptions with{ProposedTotal=301}},"currency"=>c with{Assumptions=c.Assumptions with{Currency="EUR"}},"ceiling"=>c with{Assumptions=c.Assumptions with{ProposedTotal=500,Allocations=[new(s.Company.Campaign,500,"Assumption")]}},"assumed"=>c with{Assumptions=c.Assumptions with{AssumedCeiling=200}},"duplicate"=>c with{Assumptions=c.Assumptions with{Allocations=[new(s.Company.Campaign,150,"A"),new(s.Company.Campaign,150,"B")]}},_=>c with{Assumptions=c.Assumptions with{Allocations=[new(Guid.NewGuid(),300,"Foreign") ]}}};
        Assert.Equal(HttpStatusCode.BadRequest,(await http.PostAsJsonAsync(Root+"/proposals",c)).StatusCode);Assert.Empty((await http.GetFromJsonAsync<MarketingBudgetProposalSummary[]>(Root+"/proposals"))!);
    }
    [Fact]
    public async Task Current_marketing_responsibility_and_private_company_user_scope_apply_to_report_export_and_revisions()
    {
        using var f=new TestWebApplicationFactory(new WeeklyWorkspaceFixture.Clock());var s=await MarketingManagementFixture.Seed(f);using var owner=MarketingManagementFixture.Client(f,s.Company.Company);using var manager=MarketingManagementFixture.Client(f,s.Company.Company,s.Company.ManagerSubject);var saved=await Save(owner,s.Command());
        Assert.Equal(HttpStatusCode.Forbidden,(await manager.GetAsync(Path(s.Query))).StatusCode);
        await f.SeedAsync(async db=>{var membership=await db.CompanyMemberships.IgnoreQueryFilters().SingleAsync(x=>x.UserId==s.Company.Manager&&x.CompanyId==s.Company.Company);db.Add(new CompanyResponsibilityAssignment(Guid.NewGuid(),s.Company.Company,ResponsibilityArea.Marketing,ResponsibilityAssignmentKind.Primary,membership.Id,null,AgentAutonomyLevel.Level1,null,null));});
        Assert.Equal(HttpStatusCode.NotFound,(await manager.GetAsync(Root+"/proposals/"+saved.Summary.Id)).StatusCode);Assert.Empty((await manager.GetFromJsonAsync<MarketingBudgetProposalSummary[]>(Root+"/proposals"))!);
        using var foreign=MarketingManagementFixture.Client(f,s.Company.Foreign);Assert.Equal(HttpStatusCode.Forbidden,(await foreign.GetAsync(Path(s.Query))).StatusCode);
        await f.SeedAsync(async db=>{db.CompanyResponsibilityAssignments.RemoveRange(await db.CompanyResponsibilityAssignments.IgnoreQueryFilters().Where(x=>x.CompanyId==s.Company.Company&&x.ResponsibilityArea==ResponsibilityArea.Marketing).ToArrayAsync());});
        foreach(var path in new[]{Path(s.Query),Root+"/export"+Path(s.Query)[Root.Length..],Root+"/proposals",Root+"/proposals/"+saved.Summary.Id})Assert.Equal(HttpStatusCode.Forbidden,(await owner.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await owner.PostAsJsonAsync(Root+"/proposals",s.Command())).StatusCode);
    }
}
