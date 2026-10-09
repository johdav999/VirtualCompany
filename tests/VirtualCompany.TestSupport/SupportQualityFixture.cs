using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Support;
using VirtualCompany.Domain.Entities;
namespace VirtualCompany.Api.Tests;

public sealed record SupportQualityFixture(WeeklyWorkspaceFixture Company,Guid First,Guid Second,Guid Waiting,Guid Prior,Guid Legacy,Guid Alias)
{
    public static readonly DateTime Now=new(2026,10,5,12,0,0,DateTimeKind.Utc);
    public sealed class Clock:TimeProvider{public override DateTimeOffset GetUtcNow()=>new(Now);}
    public static DateTime D(int month,int day,int hour=8)=>new(2026,month,day,hour,0,0,DateTimeKind.Utc);
    public SupportQualityQuery Query=>new(2026,9,SupportCaseCategories.Billing);
    public string Root=>"api/support/quality";
    public string Url=>Root+"?year=2026&month=9&category=billing";
    public PreviewSupportCapacity Input=>new(Query,new(2026,10,20,5,30,1,4,80,60,"Explicit handling and capacity assumptions; no schedules or promises."));
    public HttpClient Client(TestWebApplicationFactory f,bool restricted=false){var h=f.CreateClient();h.DefaultRequestHeaders.Add("X-Company-Id",Company.Company.ToString());h.DefaultRequestHeaders.Add("X-Dev-Auth-Subject",restricted?Company.ManagerSubject:Company.OwnerSubject);h.DefaultRequestHeaders.Add("X-Dev-Auth-Email",restricted?"p19-manager@example.test":"p19-owner@example.test");return h;}
    public static async Task<SupportQualityFixture> Seed(TestWebApplicationFactory f,WeeklyWorkspaceFixture? existing=null)
    {
        var company=existing??await WeeklyWorkspaceFixture.Seed(f);var s=new SupportQualityFixture(company,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid());
        await f.SeedAsync(db=>{
            var c=db.Companies.IgnoreQueryFilters().Single(x=>x.Id==company.Company);c.Settings.Extensions["supportBusinessCalendar"]=new System.Text.Json.Nodes.JsonObject{["timeZoneId"]="Europe/Stockholm",["workdayStart"]="08:00",["workdayEnd"]="17:00",["workingDays"]=new System.Text.Json.Nodes.JsonArray(1,2,3,4,5),["holidays"]=new System.Text.Json.Nodes.JsonArray("2026-10-12")};
            SupportCase Case(Guid id,string number,DateTime created){var x=new SupportCase(id,company.Company,number,"P24 billing evidence "+number,null,"manual",createdUtc:created);x.SetCategory(SupportCaseCategories.Billing);db.Add(x);return x;}
            var a=Case(s.First,"P24-A",D(9,1));a.MarkFirstResponseSent(D(9,1,9));a.SetSla(D(9,1,10),D(9,3));
            a.SetStatus(SupportCaseStatuses.Reopened);
            var b=Case(s.Second,"P24-B",D(9,5));b.MarkFirstResponseSent(D(9,7,7));b.SetSla(D(9,7,10),D(9,12));
            b.SetStatus(SupportCaseStatuses.Resolved);db.Entry(b).Property(x=>x.ResolvedUtc).CurrentValue=D(9,10,10);
            var waiting=Case(s.Waiting,"P24-W",D(9,8));waiting.SetStatus(SupportCaseStatuses.WaitingForCustomer);
            Case(s.Prior,"P24-P",D(8,15)).SetStatus(SupportCaseStatuses.Reopened);var legacy=Case(s.Legacy,"P24-L",D(8,1));legacy.SetStatus(SupportCaseStatuses.WaitingInternal);
            Case(s.Alias,"P24-M",D(9,1,7)).SetStatus(SupportCaseStatuses.Closed);
            void State(Guid id,DateTime time,string? from,string to)=>db.Add(new SupportCaseEvent(Guid.NewGuid(),company.Company,id,SupportCaseEventTypes.StateRecorded,"Recorded test case state.","human",company.Owner,time,fromStatus:from,toStatus:to));
            void Event(Guid id,DateTime time,string type)=>db.Add(new SupportCaseEvent(Guid.NewGuid(),company.Company,id,type,"Recorded "+type+" evidence.","human",company.Owner,time));
            foreach(var x in new[]{(s.First,D(9,1)),(s.Second,D(9,5)),(s.Waiting,D(9,8)),(s.Prior,D(8,15)),(s.Alias,D(9,1,7))})State(x.Item1,x.Item2,null,SupportCaseStatuses.New);
            State(s.First,D(9,2,10),SupportCaseStatuses.New,SupportCaseStatuses.Resolved);Event(s.First,D(9,2,10),SupportCaseEventTypes.Resolved);
            State(s.First,D(10,2,10),SupportCaseStatuses.Resolved,SupportCaseStatuses.Reopened);Event(s.First,D(10,2,10),SupportCaseEventTypes.Reopened);
            State(s.Second,D(9,10,10),SupportCaseStatuses.New,SupportCaseStatuses.Resolved);Event(s.Second,D(9,10,10),SupportCaseEventTypes.Resolved);
            State(s.Waiting,D(9,8,10),SupportCaseStatuses.New,SupportCaseStatuses.WaitingForCustomer);
            State(s.Prior,D(8,20),SupportCaseStatuses.New,SupportCaseStatuses.Resolved);Event(s.Prior,D(8,20),SupportCaseEventTypes.Resolved);
            State(s.Prior,D(9,12),SupportCaseStatuses.Resolved,SupportCaseStatuses.Reopened);Event(s.Prior,D(9,12),SupportCaseEventTypes.Reopened);
            Event(s.Alias,D(9,2),SupportCaseEventTypes.Resolved);db.Add(new SupportCaseEvent(Guid.NewGuid(),company.Company,s.Alias,SupportCaseEventTypes.Merged,"Recorded alias merge into P24-B.","human",company.Owner,D(9,3),mergeTargetCaseId:s.Second));
            var foreign=new SupportCase(Guid.NewGuid(),company.Foreign,"FOREIGN-P24","Foreign secret issue",null,"manual",createdUtc:D(9,1));foreign.SetCategory(SupportCaseCategories.Billing);db.Add(foreign);
            return Task.CompletedTask;
        });return s;
    }
}
