using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
namespace VirtualCompany.Api.Tests;

public sealed record FinanceRollingPlanningFixture(WeeklyWorkspaceFixture Company, Guid Account, Guid Expense, Guid Period,
    Guid Dimension, Guid ForeignDimension, Guid Journal)
{
    public FinancePlanningQuery Query => new(2026,9,4,"approved",null,"SEK",Account);
    public PreviewFinanceForecast Input(decimal amount=-1250.25m) => new(Query,Date(2026,10),
        [new(Date(2026,11),Account,null,"SEK",amount,"November signed revenue assumption"),new(Date(2026,12),Account,null,"SEK",-1300.50m,"December revenue timing")],"Recorded commercial assumptions; no cash collection timing implied.");
    public static DateTime Date(int year,int month)=>new(year,month,1,0,0,0,DateTimeKind.Utc);
    public static async Task<FinanceRollingPlanningFixture> Seed(TestWebApplicationFactory f, WeeklyWorkspaceFixture? existing=null)
    {
        var company=existing??await WeeklyWorkspaceFixture.Seed(f);var s=new FinanceRollingPlanningFixture(company,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid());
        await f.SeedAsync(db=>{
            var cash=Guid.NewGuid();var foreignAccount=Guid.NewGuid();
            db.FinanceAccounts.AddRange(new FinanceAccount(s.Account,company.Company,"P23-3000","Planning revenue","revenue","SEK",0,Date(2025,1)),
                new FinanceAccount(s.Expense,company.Company,"P23-5000","Planning payroll","expense","SEK",0,Date(2025,1)),
                new FinanceAccount(cash,company.Company,"P23-1000","Planning cash","asset","SEK",0,Date(2025,1)),
                new FinanceAccount(foreignAccount,company.Foreign,"P23-F","Foreign secret planning","revenue","SEK",0,Date(2025,1)));
            db.FiscalPeriods.AddRange(new FiscalPeriod(s.Period,company.Company,"September actuals",Date(2026,9),Date(2026,10),true),
                new FiscalPeriod(Guid.NewGuid(),company.Company,"Prior September",Date(2025,9),Date(2025,10)));
            var priorPeriod=db.FiscalPeriods.Local.Single(x=>x.CompanyId==company.Company&&x.Name=="Prior September").Id;
            AddJournal(s.Journal,s.Period,Date(2026,9).AddDays(12),1000.10m,LedgerEntryStatuses.Posted);
            AddJournal(Guid.NewGuid(),s.Period,Date(2026,9).AddDays(13),777,LedgerEntryStatuses.Draft);
            AddJournal(Guid.NewGuid(),priorPeriod,Date(2025,9).AddDays(8),900.05m,LedgerEntryStatuses.Posted);
            void AddJournal(Guid id,Guid period,DateTime date,decimal amount,string status){
                db.LedgerEntries.Add(new LedgerEntry(id,company.Company,period,"P23-"+id.ToString("N")[..8],date,status,"Planning source",postedAtUtc:status==LedgerEntryStatuses.Posted?date:null,postingDate:DateOnly.FromDateTime(date),baseCurrency:"SEK"));
                db.LedgerEntryLines.AddRange(new LedgerEntryLine(Guid.NewGuid(),company.Company,id,s.Account,0,amount,"SEK",costCenterId:null,description:"Recorded revenue"),new LedgerEntryLine(Guid.NewGuid(),company.Company,id,cash,amount,0,"SEK",costCenterId:null,description:"Cash balancing line"));}
            db.Budgets.AddRange(new Budget(Guid.NewGuid(),company.Company,s.Account,Date(2026,9),"approved",-1100.00m,"SEK"),
                new Budget(Guid.NewGuid(),company.Company,s.Account,Date(2026,9),"working",-3000,"SEK"),
                new Budget(Guid.NewGuid(),company.Company,s.Account,Date(2026,11),"approved",-1200,"SEK"),
                new Budget(Guid.NewGuid(),company.Foreign,foreignAccount,Date(2026,9),"approved",-9999,"SEK"));
            var type=Guid.NewGuid();var foreignType=Guid.NewGuid();
            db.AccountingDimensionTypes.AddRange(new AccountingDimensionType(type,company.Company,AccountingDimensionCodes.CostCenter,"Cost center",null,false,"active",new DateOnly(2025,1,1),null,company.Owner,WeeklyWorkspaceFixture.Now),
                new AccountingDimensionType(foreignType,company.Foreign,AccountingDimensionCodes.CostCenter,"Foreign cost center",null,false,"active",new DateOnly(2025,1,1),null,company.Owner,WeeklyWorkspaceFixture.Now));
            db.AccountingDimensionMembers.AddRange(new AccountingDimensionMember(s.Dimension,company.Company,type,null,"P23","Planning team","active",new DateOnly(2025,1,1),null,company.Owner,WeeklyWorkspaceFixture.Now),
                new AccountingDimensionMember(s.ForeignDimension,company.Foreign,foreignType,null,"SECRET","Foreign secret","active",new DateOnly(2025,1,1),null,company.Owner,WeeklyWorkspaceFixture.Now));
            return Task.CompletedTask;
        });return s;
    }
    public HttpClient Client(TestWebApplicationFactory f,string subject="p19-owner") {var http=WeeklyWorkspaceFixture.Client(f,subject);http.DefaultRequestHeaders.Add("X-Company-Id",Company.Company.ToString());return http;}
    public string Root=>$"/internal/companies/{Company.Company:D}/finance/planning";
}
