using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using VirtualCompany.Web.Services;
namespace VirtualCompany.Web.Pages.Support;

public abstract class SupportQualityPageBase:SupportPageBase,IDisposable
{
    [Inject]protected SupportQualityApiClient Quality{get;set;}=null!;
    [Inject]protected IJSRuntime JS{get;set;}=null!;
    [SupplyParameterFromQuery]public int? Year{get;set;}
    [SupplyParameterFromQuery]public int? Month{get;set;}
    [SupplyParameterFromQuery]public string? Category{get;set;}
    protected SupportQualityReport? Report;
    protected bool Busy;
    protected string? Error;
    protected CancellationTokenSource Reads=new();
    private bool disposed;
    protected Guid Company=>ResolvedCompanyId??Guid.Empty;
    protected SupportQualityQuery Query()=>new(Year??DateTime.UtcNow.Year,Month??DateTime.UtcNow.Month,string.IsNullOrWhiteSpace(Category)?null:Category);
    protected string QualityPath(string kind)=>BuildPath($"/support/reports/{kind}?{SupportQualityApiClient.Parameters(Query())}");
    protected string ContextLink(string path)=>RecordPath(path);
    protected override async Task OnParametersSetAsync()=>await Reload();
    protected virtual async Task Read(CancellationToken ct){var value=await Quality.Report(Company,Query(),ct);if(!ct.IsCancellationRequested)Report=value;}
    protected virtual void Clear(){Report=null;}
    protected async Task Reload()
    {
        Reads.Cancel();Reads.Dispose();Reads=new();var token=Reads.Token;Clear();Busy=true;Error=null;
        try{if(await ResolveCompanyAsync(token)&&!token.IsCancellationRequested)await Read(token);}
        catch(OperationCanceledException)when(token.IsCancellationRequested){}
        catch(Exception ex)when(ex is TodayWorkspaceAccessException or InvalidOperationException or InvalidDataException or ArgumentException or OnboardingApiException or HttpRequestException){if(!token.IsCancellationRequested){Clear();Error=ex is TodayWorkspaceAccessException?"Support responsibility is required. Choose an authorized company or ask your administrator.":ex.Message;}}
        finally{if(!token.IsCancellationRequested)Busy=false;}
    }
    protected async Task Run(Func<CancellationToken,Task> action)
    {
        if(Busy)return;Busy=true;Error=null;var token=Reads.Token;
        try{await action(token);}
        catch(OperationCanceledException)when(token.IsCancellationRequested){}
        catch(Exception ex)when(ex is TodayWorkspaceAccessException or InvalidOperationException or InvalidDataException or ArgumentException or HttpRequestException or JSException){if(!token.IsCancellationRequested){if(ex is TodayWorkspaceAccessException)Clear();Error=ex is TodayWorkspaceAccessException?"Support responsibility is required. Reload an authorized company.":ex is JSException?"The CSV could not be downloaded. Try the download again.":ex.Message;}}
        finally{if(!token.IsCancellationRequested)Busy=false;}
    }
    protected async Task Download(SupportQualityExport e,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await using var module=await JS.InvokeAsync<IJSObjectReference>("import",ct,"./js/reportDownload.js");
        ct.ThrowIfCancellationRequested();
        await module.InvokeVoidAsync("downloadReport",ct,e.FileName,e.Content);
    }
    protected static string Number(decimal? n)=>n?.ToString("N2",System.Globalization.CultureInfo.InvariantCulture)??"Unavailable";
    protected static string Date(DateTime? n)=>n?.ToString("yyyy-MM-dd HH:mm")+ (n.HasValue?" UTC":"Unavailable");
    public void Dispose(){if(disposed)return;disposed=true;Reads.Cancel();Reads.Dispose();GC.SuppressFinalize(this);}
}
