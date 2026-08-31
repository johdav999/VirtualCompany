using Microsoft.AspNetCore.Components;
using VirtualCompany.Shared;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Finance;

public partial class ComplianceCalendarPage : FinancePageBase
{
    [Inject] private FinanceApiClient Api { get;set;}=default!;
    private ComplianceCalendarResponse? Calendar; private ComplianceObligationResponse? Selected;
    private IReadOnlyList<VatFilingPeriodResponse> VatPeriods=[];private Guid? MissingPeriodId;private DateOnly ExplicitDueDate=DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);private bool CalendarView;
    private string? WorkspaceMessage; private bool HasError; private bool IsWorking; private Guid? LoadedCompany;
    private string EvidenceReference=""; private string EvidenceHash=""; private string AcknowledgementKind="received";
    private int ViewYear=>DateTime.UtcNow.Year; private string PackLabel=>Selected is null?"policy pack from accounting setup":$"{Selected.PolicyPackKey} {Selected.PolicyPackVersion}";
    private bool CanManage=>FinanceAccess.CanManageAccounting(AccessState.MembershipRole);
    private bool CanApprove=>FinanceAccess.CanApproveInvoices(AccessState.MembershipRole);
    protected override async Task OnParametersSetAsync(){await base.OnParametersSetAsync();if(!AccessState.IsAllowed||AccessState.CompanyId is not Guid company||company==LoadedCompany)return;LoadedCompany=company;await LoadAsync(company);}
    private async Task LoadAsync(Guid company){try{WorkspaceMessage=null;HasError=false;var calendar=Api.GetComplianceCalendarAsync(company,new(ViewYear,1,1),new(ViewYear,12,31));var periods=Api.GetVatFilingPeriodsAsync(company);await Task.WhenAll(calendar,periods);Calendar=await calendar;VatPeriods=await periods;MissingPeriodId=MissingPeriodId??VatPeriods.FirstOrDefault(x=>x.DueDate is null)?.Id;Selected=Calendar?.Obligations.FirstOrDefault(x=>x.Id==Selected?.Id)??Calendar?.Obligations.FirstOrDefault();}catch(FinanceApiException ex){HasError=true;WorkspaceMessage=ex.Message;}}
    private void Select(ComplianceObligationResponse item)=>Selected=item;
    private async Task GenerateAsync()=>await Work(async company=>{await Api.GenerateComplianceObligationsAsync(company,$"compliance-ui-generate-{Guid.NewGuid():N}");WorkspaceMessage="Obligations refreshed from explicit profile, policy-pack, VAT, and close sources.";});
    private async Task SetDueDateAsync(){if(MissingPeriodId is not Guid periodId)return;await Work(async company=>{await Api.SetVatFilingPeriodDueDateAsync(company,periodId,ExplicitDueDate);MissingPeriodId=null;WorkspaceMessage="Explicit filing deadline recorded. The date was not inferred by the application.";});}
    private async Task TransitionAsync(string action){if(Selected is null)return;await Work(async company=>{Selected=await Api.TransitionComplianceObligationAsync(company,Selected.Id,action,Selected.Version,$"compliance-ui-{action}-{Guid.NewGuid():N}");WorkspaceMessage=$"Workflow action '{ActionLabel(action)}' recorded.";});}
    private async Task RecordSubmissionAsync(){if(Selected is null)return;await Work(async company=>{Selected=await Api.RecordComplianceSubmissionAsync(company,Selected.Id,EvidenceReference,EvidenceHash,Selected.Version,$"compliance-ui-submit-{Guid.NewGuid():N}");EvidenceReference=EvidenceHash="";WorkspaceMessage="Manual submission evidence recorded. Authority receipt is still unconfirmed.";});}
    private async Task RecordAcknowledgementAsync(){if(Selected is null)return;await Work(async company=>{Selected=await Api.RecordComplianceAcknowledgementAsync(company,Selected.Id,AcknowledgementKind,EvidenceReference,EvidenceHash,Selected.Version,$"compliance-ui-ack-{Guid.NewGuid():N}");EvidenceReference=EvidenceHash="";WorkspaceMessage="Authority evidence recorded as a distinct lifecycle event.";});}
    private async Task ReviewEvidenceAsync(Guid evidenceId,bool accepted){if(Selected is null)return;await Work(async company=>{Selected=await Api.ReviewComplianceEvidenceAsync(company,Selected.Id,evidenceId,accepted,Selected.Version,$"compliance-ui-evidence-review-{Guid.NewGuid():N}");WorkspaceMessage=accepted?"Manual submission evidence accepted by an independent reviewer.":"Manual submission evidence rejected for correction.";});}
    private async Task Work(Func<Guid,Task> operation){if(AccessState.CompanyId is not Guid company)return;IsWorking=true;HasError=false;WorkspaceMessage=null;try{await operation(company);await LoadAsync(company);}catch(FinanceApiException ex){HasError=true;WorkspaceMessage=ex.Message;}finally{IsWorking=false;}}
    private static string Label(string value)=>value.Replace('_',' ').Replace("vat","VAT",StringComparison.OrdinalIgnoreCase);
    private static string ActionLabel(string value)=>value switch{"submit_review"=>"Send for review","approve"=>"Approve","reject"=>"Reject","export"=>"Record validated export","prepare"=>"Prepare",_=>Label(value)};
    private static string StatusClass(string value)=>value switch{"rejected"=>"red","authority_approved"=>"green","exported" or "manual_submission_recorded" or "authority_received"=>"blue","under_review"=>"amber",_=>"neutral"};
    private static string Short(Guid id)=>id.ToString("N")[..8]; private static string ShortHash(string value)=>value.Length>16?value[..16]+"…":value;
    private IEnumerable<int> CalendarDays=>Enumerable.Range(1,DateTime.DaysInMonth(ViewYear,DateTime.UtcNow.Month));private IEnumerable<ComplianceObligationResponse> DueOn(int day)=>Calendar?.Obligations.Where(x=>x.DueDate.Month==DateTime.UtcNow.Month&&x.DueDate.Day==day)??[];
}
