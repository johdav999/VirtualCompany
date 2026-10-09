using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
namespace VirtualCompany.Web.Services;
public sealed class StrategicScenarioApiClient(ICompanyApiTransport transport,bool offline=false)
{
    public async Task<StrategicScenarioOptions> Options(Guid c,int year,CancellationToken ct){var r=await Send<StrategicScenarioOptions>(c,HttpMethod.Get,$"/options?fiscalYear={year}",null,ct);Check(r.FiscalYear==year);return r;}
    public async Task<StrategicScenarioHistoryPage> History(Guid c,int skip,CancellationToken ct){var p=await Send<StrategicScenarioHistoryPage>(c,HttpMethod.Get,$"/versions?skip={skip}",null,ct);Check(p.Skip==skip);return p;}
    public async Task<StrategicScenarioPreview> Preview(Guid c,StrategicScenarioInput input,CancellationToken ct){var r=await Send<StrategicScenarioPreview>(c,HttpMethod.Post,"/preview",input,ct);Check(Same(r.Input,input));return r;}
    public async Task<StrategicScenarioDocument> Save(Guid c,SaveStrategicScenario cmd,CancellationToken ct){var r=await Send<StrategicScenarioDocument>(c,HttpMethod.Post,"/versions",cmd,ct);Check(r.Summary.PreviousId==cmd.PreviousId&&r.Summary.Revision==cmd.ExpectedRevision+1&&r.Scenario.Fingerprint==cmd.ExpectedFingerprint);return r;}
    public async Task<StrategicScenarioDocument> Open(Guid c,Guid id,CancellationToken ct){var r=await Send<StrategicScenarioDocument>(c,HttpMethod.Post,$"/versions/{id}/open",null,ct);Check(r.Summary.Id==id);return r;}
    public async Task<StrategicScenarioDocument> Duplicate(Guid c,Guid id,DuplicateStrategicScenario cmd,CancellationToken ct){var r=await Send<StrategicScenarioDocument>(c,HttpMethod.Post,$"/versions/{id}/duplicate",cmd,ct);Check(r.Summary.DerivedFromId==id&&r.Summary.Revision==1&&r.Summary.Name==cmd.Name);return r;}
    public async Task<StrategicScenarioComparison> Compare(Guid c,Guid a,Guid b,CancellationToken ct){var r=await Send<StrategicScenarioComparison>(c,HttpMethod.Post,"/compare",new{Baseline=a,Alternative=b},ct);Check(r.Baseline.Summary.Id==a&&r.Alternative.Summary.Id==b);return r;}
    private static bool Same<T>(T a,T b)=>JsonSerializer.Serialize(a)==JsonSerializer.Serialize(b);
    private static void Check(bool b){if(!b)throw new InvalidDataException("Scenario response differs from the requested version, period or input.");}
    private static bool Valid(Guid c,StrategicScenarioPreview p)=>p.CompanyId==c&&p.Input?.Drivers is {Years:>=3 and <=10}&&p.Input.Cash!=null&&p.Input.Checkpoints!=null&&p.Source!=null&&p.Source.Dependencies!=null&&p.Source.Provenance!=null&&p.Checkpoints!=null&&p.Warnings!=null&&p.Source.AnnualPlanId==p.Input.AnnualPlanId&&p.Source.ForecastRevisionId==p.Input.ForecastRevisionId&&p.Source.Currency==p.Input.Currency&&p.Fingerprint?.Length==64&&p.Years?.Count==p.Input.Drivers.Years&&p.Years.Select(x=>x.Year).SequenceEqual(Enumerable.Range(1,p.Input.Drivers.Years))&&p.CalculationVersion=="strategic-scenario.v1";
    private static bool Valid(Guid c,StrategicScenarioDocument d)=>d.Summary.CompanyId==c&&d.Summary.FiscalYear==d.Scenario.Source.FiscalYear&&d.Summary.Currency==d.Scenario.Input.Currency&&Valid(c,d.Scenario);
    private async Task<T> Send<T>(Guid c,HttpMethod method,string path,object? body,CancellationToken ct)
    {
        if(offline||c==Guid.Empty)throw new InvalidOperationException("Choose a connected company before strategic planning.");
        using var r=await transport.SendAsync(c,method,$"api/companies/{c}/planning/scenarios{path}",body==null?null:JsonContent.Create(body),ct);
        if(r.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)throw new TodayWorkspaceAccessException(r.StatusCode);
        if(!r.IsSuccessStatusCode){string? detail=null;try{using var j=JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct));if(j.RootElement.TryGetProperty("detail",out var d))detail=d.GetString();}catch(JsonException){}throw new InvalidOperationException(detail??"Scenarios are unavailable. Reload current company and version.");}
        T value;try{value=await r.Content.ReadFromJsonAsync<T>(ct)??throw new InvalidDataException("Scenario response is empty.");}catch(JsonException e){throw new InvalidDataException("Scenario response cannot be read.",e);}
        Check(value switch{StrategicScenarioOptions x=>x.CompanyId==c&&x.AnnualPlans!=null&&x.Forecasts!=null&&x.Owners!=null&&x.AnnualPlans.All(p=>p.CompanyId==c),
            StrategicScenarioPreview x=>Valid(c,x),StrategicScenarioDocument x=>Valid(c,x),StrategicScenarioComparison x=>x.CompanyId==c&&x.Baseline!=null&&x.Alternative!=null&&Valid(c,x.Baseline)&&Valid(c,x.Alternative)&&x.Deltas!=null&&x.Deltas.Count==x.Baseline.Scenario.Years.Count&&x.AssumptionChanges!=null,
            StrategicScenarioHistoryPage x=>x.CompanyId==c&&x.Items!=null&&x.Items.All(y=>y.CompanyId==c),_=>false});return value;
    }
}
