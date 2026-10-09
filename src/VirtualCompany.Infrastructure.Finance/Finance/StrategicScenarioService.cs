using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;
namespace VirtualCompany.Infrastructure.Finance;

public sealed class StrategicScenarioService(VirtualCompanyDbContext db, ITodayWorkspaceLensResolver lenses,
    IFinanceRollingPlanningService finance, IAnnualPlanningService annual, TimeProvider clock) : IStrategicScenarioService
{
    private static readonly JsonSerializerOptions CanonicalJson = new(){Converters={new CanonicalDecimal()}};
    private sealed class CanonicalDecimal : System.Text.Json.Serialization.JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader,Type type,JsonSerializerOptions options)=>reader.GetDecimal();
        public override void Write(Utf8JsonWriter writer,decimal value,JsonSerializerOptions options)=>writer.WriteRawValue(value.ToString("G29",System.Globalization.CultureInfo.InvariantCulture));
    }
    private static string Json<T>(T value)=>JsonSerializer.Serialize(value,CanonicalJson);
    private static string Hash<T>(T value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Json(value))));
    private IQueryable<StrategicScenarioVersion> Versions(Guid c)=>db.Set<StrategicScenarioVersion>().Where(x=>x.CompanyId==c).Include(x=>x.Cash).Include(x=>x.Outputs).Include(x=>x.Checkpoints);
    private async Task<Guid> Require(Guid c,CancellationToken ct)
    {
        var lens=await lenses.ResolveAsync(c,TodayWorkspaceLenses.Finance,ct);
        if(!lens.AvailableLenses.Any(x=>x.Lens==TodayWorkspaceLenses.Finance))throw new UnauthorizedAccessException();
        return lens.UserId;
    }
    public async Task<StrategicScenarioOptions> OptionsAsync(Guid c,int year,CancellationToken ct)
    {
        await Require(c,ct);var options=await annual.OptionsAsync(c,year,ct);
        var plans=await annual.HistoryAsync(c,year,ct);var authorized=new List<AnnualPlanSummary>();
        foreach(var p in plans.Where(x=>x.Status=="approved"))if(await annual.CanReadApprovalAsync(c,p.Id,ct))authorized.Add(p);
        return new(c,year,authorized,await finance.HistoryAsync(c,0,ct),options.Owners);
    }
    public async Task<StrategicScenarioHistoryPage> HistoryAsync(Guid c,int skip,CancellationToken ct)
    {
        await Require(c,ct);if(skip is <0 or >10000)throw new ArgumentException("Choose a supported history page.");
        var rows=await db.Set<StrategicScenarioVersion>().AsNoTracking().Where(x=>x.CompanyId==c).OrderByDescending(x=>x.SavedUtc).ThenByDescending(x=>x.Id).Skip(skip).Take(21).ToListAsync(ct);
        var result=new List<StrategicScenarioSummary>();foreach(var r in rows.Take(20))if(await annual.CanReadApprovalAsync(c,r.AnnualPlanId,ct))result.Add(Summary(r));return new(c,skip,rows.Count>20&&skip<10000,result);
    }
    private static StrategicScenarioInput Validate(StrategicScenarioInput i)
    {
        if(i is null||i.Drivers is null||i.Cash is null||i.Checkpoints is null||string.IsNullOrWhiteSpace(i.Name)||i.Name.Length>64||
            string.IsNullOrWhiteSpace(i.Notes)||i.Notes.Length>2000||i.OwnerId==Guid.Empty||i.AnnualPlanId==Guid.Empty||i.ForecastRevisionId==Guid.Empty||
            i.Currency is null||i.Currency.Length!=3||!i.Currency.All(char.IsAsciiLetterUpper)||i.Checkpoints.Count>50||i.Cash.Any(x=>x is null)||
            i.Checkpoints.Any(x=>x is null||x.Year<1||x.Year>i.Drivers.Years||string.IsNullOrWhiteSpace(x.Title)||x.Title.Length>200||x.OwnerId==Guid.Empty||x.InitiativeId==Guid.Empty)||
            i.Checkpoints.GroupBy(x=>new{x.Year,x.Title,x.OwnerId,x.InitiativeId}).Any(g=>g.Count()>1))
            throw new ArgumentException("Choose source versions, currency, accountable owner and explicit assumptions, rationale and checkpoints.");
        foreach(var p in typeof(ScenarioDrivers).GetProperties().Where(p=>p.PropertyType==typeof(decimal)))
            if(decimal.Round((decimal)p.GetValue(i.Drivers)!,10)!=(decimal)p.GetValue(i.Drivers)!)throw new ArgumentException("Assumptions support at most ten decimal places.");
        if(new[]{i.Drivers.OpeningCash,i.Drivers.OpeningReceivables,i.Drivers.OpeningPayables,i.Drivers.CashFloor}.Any(x=>decimal.Round(x,2)!=x))throw new ArgumentException("Cash balances use two decimal places.");
        return i with{Cash=i.Cash.OrderBy(x=>x.Year).ToArray(),Checkpoints=i.Checkpoints.OrderBy(x=>x.Year).ThenBy(x=>x.InitiativeId).ThenBy(x=>x.Title).ToArray()};
    }
    private async Task<ScenarioSource> Source(Guid c,StrategicScenarioInput input,CancellationToken ct)
    {
        var plan=await annual.OpenAsync(c,input.AnnualPlanId,ct);
        if(plan.Summary.Status!="approved")throw new ArgumentException("Choose a retained approved annual baseline.");
        var forecast=await finance.OpenAsync(c,input.ForecastRevisionId,ct);var report=forecast.Preview.Report;var q=report.Query;
        var start=new DateTime(q.Year,q.Month,1,0,0,0,DateTimeKind.Utc);var end=start.AddMonths(q.Months);
        var zone=TimeZoneInfo.FindSystemTimeZoneById(plan.Plan.Period.Timezone);
        var localStart=TimeZoneInfo.ConvertTimeFromUtc(plan.Plan.Period.StartUtc,zone);var localEnd=TimeZoneInfo.ConvertTimeFromUtc(plan.Plan.Period.EndUtc,zone);
        if(q.FinanceAccountId.HasValue||q.CostCenterId.HasValue||q.Currency!=input.Currency||input.Currency!=plan.Plan.Period.Currency||
            localStart.Day!=1||localEnd.Day!=1||start<new DateTime(localStart.Year,localStart.Month,1,0,0,0,DateTimeKind.Utc)||end>new DateTime(localEnd.Year,localEnd.Month,1,0,0,0,DateTimeKind.Utc))
            throw new ArgumentException("Use one matching currency, an unfiltered Finance snapshot inside the annual fiscal year, and month-start fiscal boundaries. No implicit FX or annualization.");
        var accounts=await db.FinanceAccounts.Where(x=>x.CompanyId==c).ToDictionaryAsync(x=>x.Id,ct);
        var revenue=0m;var expense=0m;var revenueCount=0;var expenseCount=0;var provenance=new List<string>();
        foreach(var v in forecast.Preview.Values)
        {
            if(!accounts.TryGetValue(v.AccountId,out var a))throw new InvalidDataException("A retained Finance account classification is unavailable.");
            if(a.AccountType is not("revenue" or "expense"))continue;
            if(v.Currency!=input.Currency)throw new ArgumentException("Mixed source currencies cannot be calculated.");
            var amount=v.Actual??v.Forecast;if(!amount.HasValue){provenance.Add($"{a.Code} {v.MonthUtc:yyyy-MM}: no amount; excluded from partial source totals.");continue;}
            if(a.AccountType=="revenue"){revenue-=amount.Value;revenueCount++;}else{expense+=amount.Value;expenseCount++;}
            provenance.Add($"{a.Id:D} · {a.Code} · {a.AccountType} · {v.MonthUtc:yyyy-MM} · {(v.Actual.HasValue?"retained actual":"retained forecast")} · signed {amount.Value} {v.Currency}");
        }
        if(revenueCount==0||expenseCount==0||revenue<0||expense<0)throw new ArgumentException("Retain known revenue credits and operating expense values before creating a scenario.");
        var options=await annual.OptionsAsync(c,plan.Summary.FiscalYear,ct);var ids=plan.Plan.Input.Objectives.SelectMany(x=>x.InitiativeIds).ToHashSet();
        return new(plan.Summary.Id,plan.Summary.Version,plan.Plan.Fingerprint,plan.Summary.Status,plan.Summary.FiscalYear,
            plan.Plan.Period.StartUtc,plan.Plan.Period.EndUtc,plan.Plan.Period.Timezone,input.Currency,forecast.Summary.Id,forecast.Summary.Name,
            forecast.Summary.NativeVersion,forecast.Checksum,forecast.Summary.SourceAsOfUtc,q.Year,q.Month,q.Months,revenue,expense,
            provenance.Concat(report.Coverage).ToArray(),options.Initiatives.Where(x=>ids.Contains(x.Id)).ToArray());
    }
    public async Task<StrategicScenarioPreview> PreviewAsync(Guid c,StrategicScenarioInput input,CancellationToken ct)
    {
        await Require(c,ct);input=Validate(input);var source=await Source(c,input,ct);
        var options=await annual.OptionsAsync(c,source.FiscalYear,ct);
        var owner=options.Owners.SingleOrDefault(x=>x.Id==input.OwnerId)??throw new ArgumentException("Choose an active accountable owner.");
        foreach(var p in input.Checkpoints)if(!options.Owners.Any(x=>x.Id==p.OwnerId)||!source.Dependencies.Any(x=>x.Id==p.InitiativeId))throw new ArgumentException("Checkpoints require an active owner and a native initiative from the annual baseline.");
        return Preview(c,input,owner.Name,source,input.Checkpoints.Select(p=>new ScenarioCheckpointView(p.Year,p.Title,p.OwnerId,options.Owners.Single(o=>o.Id==p.OwnerId).Name,p.InitiativeId)).ToArray());
    }
    private static StrategicScenarioPreview Preview(Guid c,StrategicScenarioInput input,string owner,ScenarioSource source,IReadOnlyList<ScenarioCheckpointView> checkpoints)
    {
        var years=StrategicScenarioCalculation.Calculate(source.Revenue,source.Expense,input.Drivers,input.Cash);
        var warnings=new List<string>{$"Source coverage: {source.SourceYear}-{source.SourceMonth:00}, {source.SourceMonths} native months; explicit annual multiplier {input.Drivers.SourceToAnnualScale}×. These are sensitivity assumptions, not complete annual actuals."};
        if(years.Any(x=>x.ClosingCash<0))warnings.Add("Negative closing cash: assumed funding does not cover the model's cash needs.");
        if(years.Any(x=>x.CapacityShortfall>0))warnings.Add("Demand exceeds capacity; unfulfilled demand is excluded from revenue.");
        if(years.Any(x=>x.FundingGap>0))warnings.Add("Additional funding is needed to reach the assumed cash floor; funding is not approved.");
        return new(c,input,owner,source,StrategicScenarioCalculation.Version,years,checkpoints,warnings,
            Hash(new{company=c,input,owner,source,version=StrategicScenarioCalculation.Version,years,checkpoints}),StrategicScenarioCalculation.Formulas,StrategicScenarioCalculation.Limitations);
    }
    private static StrategicScenarioSummary Summary(StrategicScenarioVersion r)=>new(r.Id,r.CompanyId,r.SeriesId,r.Revision,r.PreviousId,r.DerivedFromId,r.Name,r.OwnerId,r.OwnerName,r.SavedUtc,r.FiscalYear,r.Currency);
    private static StrategicScenarioInput Input(StrategicScenarioVersion r)=>new(r.Name,r.OwnerId,r.AnnualPlanId,r.ForecastRevisionId,r.Currency,r.Drivers,
        r.Cash.OrderBy(x=>x.Year).Select(x=>new ScenarioCashInput(x.Year,x.Investment,x.Funding,x.Rationale)).ToArray(),
        r.Checkpoints.Select(x=>new ScenarioCheckpointInput(x.Year,x.Title,x.OwnerId,x.InitiativeId)).OrderBy(x=>x.Year).ThenBy(x=>x.InitiativeId).ThenBy(x=>x.Title).ToArray(),r.Notes);
    public async Task<StrategicScenarioDocument> OpenAsync(Guid c,Guid id,CancellationToken ct)
    {
        await Require(c,ct);var r=await Versions(c).AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new KeyNotFoundException("Scenario unavailable.");
        var plan=await annual.OpenAsync(c,r.AnnualPlanId,ct);var forecast=await finance.OpenAsync(c,r.ForecastRevisionId,ct);
        ScenarioSource source;try{source=JsonSerializer.Deserialize<ScenarioSource>(r.SourceJson)??throw new InvalidDataException();}catch(JsonException e){throw new InvalidDataException("Scenario provenance is unreadable.",e);}
        if(r.CalculationVersion!=StrategicScenarioCalculation.Version||source.AnnualPlanId!=r.AnnualPlanId||source.ForecastRevisionId!=r.ForecastRevisionId||
            source.ForecastChecksum!=forecast.Checksum||source.AnnualFingerprint!=plan.Plan.Fingerprint||source.Currency!=r.Currency||source.FiscalYear!=r.FiscalYear||source.Revenue!=r.SourceRevenue||source.Expense!=r.SourceExpense)
            throw new InvalidDataException("Scenario source or calculation version is inconsistent.");
        StrategicScenarioPreview p;try{p=Preview(c,Validate(Input(r)),r.OwnerName,source,r.Checkpoints.OrderBy(x=>x.Year).ThenBy(x=>x.InitiativeId).ThenBy(x=>x.Title).Select(x=>new ScenarioCheckpointView(x.Year,x.Title,x.OwnerId,x.OwnerName,x.InitiativeId)).ToArray());}catch(ArgumentException e){throw new InvalidDataException("Retained scenario inputs are invalid.",e);}
        if(p.Fingerprint!=r.Checksum||!p.Years.SequenceEqual(r.Outputs.OrderBy(x=>x.Result.Year).Select(x=>x.Result)))throw new InvalidDataException("Scenario outputs do not reproduce from retained inputs.");
        return new(Summary(r),p);
    }
    public Task<StrategicScenarioDocument> SaveAsync(Guid c,SaveStrategicScenario cmd,CancellationToken ct)=>db.Database.CreateExecutionStrategy().ExecuteAsync(async()=>
    {
        var actor=await Require(c,ct);if(cmd is null||cmd.RequestId==Guid.Empty)throw new ArgumentException("Provide a stable save request identity.");
        var hash=Hash(new{actor,cmd});var retry=await Retry(c,cmd.RequestId,hash,ct);if(retry!=null)return retry;
        var p=await PreviewAsync(c,cmd.Input,ct);if(p.Fingerprint!=cmd.ExpectedFingerprint)throw new InvalidOperationException("Sources or assumptions changed. Preview again.");
        StrategicScenarioVersion? previous=null;if(cmd.PreviousId.HasValue){previous=await Versions(c).AsNoTracking().SingleOrDefaultAsync(x=>x.Id==cmd.PreviousId,ct)??throw new KeyNotFoundException();await OpenAsync(c,previous.Id,ct);
            if(previous.Revision!=cmd.ExpectedRevision||previous.AnnualPlanId!=p.Source.AnnualPlanId||previous.ForecastRevisionId!=p.Source.ForecastRevisionId)throw new InvalidOperationException("Revision source or expected version differs.");}
        else if(cmd.ExpectedRevision!=0)throw new InvalidOperationException("Initial scenario revision must be zero.");
        return await Persist(c,actor,cmd.RequestId,hash,p,previous?.SeriesId??Guid.NewGuid(),previous?.Revision+1??1,previous?.Id,null,ct);
    });
    public Task<StrategicScenarioDocument> DuplicateAsync(Guid c,Guid id,DuplicateStrategicScenario cmd,CancellationToken ct)=>db.Database.CreateExecutionStrategy().ExecuteAsync(async()=>
    {
        var actor=await Require(c,ct);if(cmd is null||cmd.RequestId==Guid.Empty)throw new ArgumentException("Provide a stable duplicate request identity.");
        var hash=Hash(new{actor,id,cmd});var retry=await Retry(c,cmd.RequestId,hash,ct);if(retry!=null)return retry;
        var old=await OpenAsync(c,id,ct);var input=Validate(old.Scenario.Input with{Name=cmd.Name});
        var options=await annual.OptionsAsync(c,old.Scenario.Source.FiscalYear,ct);
        if(!options.Owners.Any(x=>x.Id==input.OwnerId)||input.Checkpoints.Any(p=>!options.Owners.Any(x=>x.Id==p.OwnerId)))throw new ArgumentException("Choose currently active accountable owners before duplicating this scenario.");
        var p=Preview(c,input,old.Scenario.Owner,old.Scenario.Source,old.Scenario.Checkpoints);
        return await Persist(c,actor,cmd.RequestId,hash,p,Guid.NewGuid(),1,null,id,ct);
    });
    private async Task<StrategicScenarioDocument?> Retry(Guid c,Guid request,string hash,CancellationToken ct)
    {
        var r=await db.Set<StrategicScenarioVersion>().AsNoTracking().SingleOrDefaultAsync(x=>x.CompanyId==c&&x.RequestId==request,ct);
        if(r==null)return null;if(r.CommandHash!=hash)throw new InvalidOperationException("Request identity already contains a different command.");return await OpenAsync(c,r.Id,ct);
    }
    private async Task<StrategicScenarioDocument> Persist(Guid c,Guid actor,Guid request,string hash,StrategicScenarioPreview p,Guid series,int revision,Guid? previous,Guid? derived,CancellationToken ct)
    {
        if(Encoding.UTF8.GetByteCount(Json(p.Source))>256*1024)throw new ArgumentException("Scenario source provenance exceeds the supported range.");
        var id=Guid.NewGuid();var r=new StrategicScenarioVersion{Id=id,CompanyId=c,SeriesId=series,Revision=revision,PreviousId=previous,DerivedFromId=derived,RequestId=request,AuthorId=actor,
            OwnerId=p.Input.OwnerId,OwnerName=p.Owner,Name=p.Input.Name,Notes=p.Input.Notes,SavedUtc=clock.GetUtcNow().UtcDateTime,AnnualPlanId=p.Source.AnnualPlanId,
            ForecastRevisionId=p.Source.ForecastRevisionId,FiscalYear=p.Source.FiscalYear,Currency=p.Source.Currency,CalculationVersion=p.CalculationVersion,SourceJson=Json(p.Source),
            SourceRevenue=p.Source.Revenue,SourceExpense=p.Source.Expense,Drivers=p.Input.Drivers,Checksum=p.Fingerprint,CommandHash=hash,
            Cash=p.Input.Cash.Select(x=>new StrategicScenarioCash{Id=Guid.NewGuid(),CompanyId=c,ScenarioId=id,Year=x.Year,Investment=x.Investment,Funding=x.Funding,Rationale=x.Rationale}).ToList(),
            Outputs=p.Years.Select(x=>new StrategicScenarioOutput{Id=Guid.NewGuid(),CompanyId=c,ScenarioId=id,Result=x}).ToList(),
            Checkpoints=p.Input.Checkpoints.Select(x=>new StrategicScenarioCheckpoint{Id=Guid.NewGuid(),CompanyId=c,ScenarioId=id,Year=x.Year,Title=x.Title,OwnerId=x.OwnerId,
                OwnerName=p.Checkpoints.Single(v=>v.Year==x.Year&&v.Title==x.Title&&v.OwnerId==x.OwnerId&&v.InitiativeId==x.InitiativeId).Owner,InitiativeId=x.InitiativeId}).ToList()};
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        if(previous.HasValue&&await db.Set<StrategicScenarioVersion>().AnyAsync(x=>x.CompanyId==c&&x.PreviousId==previous,ct))throw new InvalidOperationException("This revision already has a successor. Open the latest revision.");
        db.Add(r);db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(),c,"user",actor,derived.HasValue?"finance.scenario.duplicated":"finance.scenario.version_saved","strategic_scenario",id.ToString("D"),"succeeded",
            $"{p.CalculationVersion}; annual {p.Source.AnnualPlanId:D}; forecast {p.Source.ForecastRevisionId:D}; source checksum {p.Source.ForecastChecksum}; scenario checksum {p.Fingerprint}; predecessor {previous}; derived {derived}. No execution authority."));
        try{await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);}catch(DbUpdateException e){await tx.RollbackAsync(ct);await tx.DisposeAsync();db.ChangeTracker.Clear();var retry=await Retry(c,request,hash,ct);if(retry!=null)return retry;throw new InvalidOperationException("A concurrent revision won. Reload before saving.",e);}
        return await OpenAsync(c,id,ct);
    }
    public async Task<StrategicScenarioComparison> CompareAsync(Guid c,Guid a,Guid b,CancellationToken ct)
    {
        var left=await OpenAsync(c,a,ct);var right=await OpenAsync(c,b,ct);var x=left.Scenario;var y=right.Scenario;
        if(x.Source.AnnualPlanId!=y.Source.AnnualPlanId||x.Source.AnnualFingerprint!=y.Source.AnnualFingerprint||x.Source.ForecastChecksum!=y.Source.ForecastChecksum||
            x.Source.Revenue!=y.Source.Revenue||x.Source.Expense!=y.Source.Expense||x.Input.Currency!=y.Input.Currency||x.Input.Drivers.Years!=y.Input.Drivers.Years||x.Input.Drivers.CapacityUnit!=y.Input.Drivers.CapacityUnit)
            throw new ArgumentException("Compare scenarios with the same annual and Finance baseline, currency, capacity unit and horizon.");
        var changes=typeof(ScenarioDrivers).GetProperties().Where(p=>!Equals(p.GetValue(x.Input.Drivers),p.GetValue(y.Input.Drivers)))
            .Select(p=>$"{DriverLabel(p.Name,x.Input.Drivers.CapacityUnit)}: {Number(p.GetValue(x.Input.Drivers))} → {Number(p.GetValue(y.Input.Drivers))}").ToList();
        if(Json(x.Input.Cash)!=Json(y.Input.Cash))changes.Add("Annual incremental investment, assumed funding or rationale changed.");
        if(Json(x.Input.Checkpoints)!=Json(y.Input.Checkpoints))changes.Add("Strategic checkpoints changed.");
        if(x.Input.OwnerId!=y.Input.OwnerId)changes.Add($"Accountable owner: {x.Owner} → {y.Owner}.");
        return new(c,left,right,x.Years.Zip(y.Years,(l,r)=>new StrategicScenarioDelta(l.Year,r.Revenue-l.Revenue,r.OperatingCost-l.OperatingCost,r.ClosingCash-l.ClosingCash,r.FundingGap-l.FundingGap,r.CapacityShortfall-l.CapacityShortfall)).ToArray(),changes);
    }
    private static string Number(object? value)=>value is decimal d?d.ToString("G29",System.Globalization.CultureInfo.InvariantCulture):Convert.ToString(value,System.Globalization.CultureInfo.InvariantCulture)??"";
    private static string DriverLabel(string name,string unit)=>name switch{
        "Years"=>"Horizon (years)","CapacityUnit"=>"Capacity unit","SourceToAnnualScale"=>"Source-to-annual multiplier (×)","DemandUnits"=>$"Base demand ({unit})","CapacityUnits"=>$"Base capacity ({unit})",
        "DemandGrowthPercent"=>"Demand growth (%)","PriceGrowthPercent"=>"Price growth (%)","CostGrowthPercent"=>"Cost growth (%)","CapacityGrowthPercent"=>"Capacity growth (%)",
        "VariableCostShare"=>"Variable cost share","OpeningCash"=>"Opening cash","OpeningReceivables"=>"Opening receivables","OpeningPayables"=>"Opening payables","CollectionShare"=>"Same-year collection share","PaymentShare"=>"Same-year payment share","CashFloor"=>"Cash floor",_=>throw new InvalidOperationException("Unsupported scenario driver.")};
}
