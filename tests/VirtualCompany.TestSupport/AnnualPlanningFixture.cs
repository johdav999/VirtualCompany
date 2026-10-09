using A=VirtualCompany.Application.Orchestration;
using System.Net.Http.Json;
using VirtualCompany.Domain.Entities;
namespace VirtualCompany.Api.Tests;
public sealed record AnnualPlanningFixture(QuarterlyPlanningFixture Quarter,Guid ReviewId,Guid BudgetId,Guid AccountId)
{
 public string Root=>$"/api/companies/{Quarter.Company.Company}/planning/years";
 public A.AnnualPlanInput Input=>new(2026,[new(Quarter.Goal,1,Quarter.Company.Owner,ReviewId,100,"cases",Enumerable.Range(1,4).Select(q=>new A.AnnualMilestoneInput(q,$"Quarter {q} service checkpoint",new DateTime(2026,q*3,20,8,0,0,DateTimeKind.Utc))).ToArray(),[Quarter.Initiative])],[BudgetId],[new(BudgetId,Quarter.Company.Owner,Quarter.Goal,"investment","Service tooling",1000,3)],"Annual service capacity and budget governance");
 public HttpClient Client(TestWebApplicationFactory f,string subject="p19-owner")=>Quarter.Client(f,subject);
 public static async Task<AnnualPlanningFixture> Seed(TestWebApplicationFactory f,QuarterlyPlanningFixture? existing=null){var q=existing??await QuarterlyPlanningFixture.Seed(f);using var h=q.Client(f);var review=await QuarterlyPlanningFixture.Save(h,q);var budget=Guid.NewGuid();var account=Guid.NewGuid();await f.SeedAsync(db=>{db.Add(new FinanceAccount(account,q.Company.Company,"P26-5000","Annual service investment","expense","SEK",0,new DateTime(2026,1,1,0,0,0,DateTimeKind.Utc)));db.Add(new Budget(budget,q.Company.Company,account,new DateTime(2026,9,1,0,0,0,DateTimeKind.Utc),"annual-working",1000,"SEK"));return Task.CompletedTask;});return new(q,review.Summary.Id,budget,account);}
 public static async Task<T> Read<T>(HttpResponseMessage r){var body=await r.Content.ReadAsStringAsync();Assert.True(r.IsSuccessStatusCode,$"{r.StatusCode}: {body}");return System.Text.Json.JsonSerializer.Deserialize<T>(body,new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;}
 public static async Task<A.AnnualPlanDocument> Save(HttpClient h,AnnualPlanningFixture s,A.AnnualPlanInput? input=null,A.AnnualPlanDocument? prior=null){input??=s.Input;var p=await Read<A.AnnualPlanPreview>(await h.PostAsJsonAsync(s.Root+"/preview",input));return await Read<A.AnnualPlanDocument>(await h.PostAsJsonAsync(s.Root+"/versions",new A.SaveAnnualPlan(input,prior?.Summary.Id,prior?.Summary.Version??0,Guid.NewGuid(),p.Fingerprint)));}
}

