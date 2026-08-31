using Microsoft.AspNetCore.Components;
using VirtualCompany.Shared;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Finance;

public partial class ReportDefinitionsPage : FinancePageBase
{
    [Inject] private FinanceApiClient FinanceApiClient { get; set; } = default!;

    private IReadOnlyList<ReportSystemTemplateResponse> Templates { get; set; } = [];
    private IReadOnlyList<ReportDefinitionSummaryResponse> Definitions { get; set; } = [];
    private IReadOnlyList<AccountingAccountListItemResponse> Accounts { get; set; } = [];
    private IReadOnlyList<AccountingPeriodResponse> Periods { get; set; } = [];
    private ReportDefinitionVersionResponse? Selected { get; set; }
    private ReportDefinitionLineResponse? SelectedLine { get; set; }
    private CompleteFinancialReportResponse? Preview { get; set; }
    private Guid SelectedPeriodId { get; set; }
    private string SelectedTemplateKey { get; set; } = "cash-flow-management";
    private string NewDefinitionCode { get; set; } = "CASH_FLOW_MGMT";
    private string NewDefinitionName { get; set; } = "Management cash flow";
    private string DecisionNote { get; set; } = "Reviewed independently against the validated definition.";
    private DateOnly EffectiveDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    private bool IsBusy { get; set; }
    private string? ActionMessage { get; set; }
    private string? ActionError { get; set; }

    private bool CanManage => FinanceAccess.CanManageAccounting(AccessState.MembershipRole);
    private bool CanApprove => FinanceAccess.CanApproveInvoices(AccessState.MembershipRole);
    private ReportDefinitionAccountGroupResponse? SelectedGroup => SelectedLine?.AccountGroups.FirstOrDefault();

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();
        if (!AccessState.IsAllowed || AccessState.CompanyId is not Guid companyId) return;
        await LoadWorkspaceAsync(companyId);
    }

    private async Task LoadWorkspaceAsync(Guid companyId)
    {
        try
        {
            var templates = FinanceApiClient.GetReportSystemTemplatesAsync(companyId);
            var definitions = FinanceApiClient.GetReportDefinitionsAsync(companyId);
            var accounts = FinanceApiClient.GetAccountingAccountsAsync(companyId, status: "active");
            var years = FinanceApiClient.GetAccountingFiscalYearsAsync(companyId);
            await Task.WhenAll(templates, definitions, accounts, years);
            Templates = await templates; Definitions = await definitions; Accounts = await accounts;
            Periods = (await years).SelectMany(x => x.Periods).OrderByDescending(x => x.StartDate).ToArray();
            SelectedPeriodId = SelectedPeriodId == Guid.Empty ? Periods.FirstOrDefault()?.Id ?? Guid.Empty : SelectedPeriodId;
            if (Selected is null && Definitions.FirstOrDefault() is { } first) await SelectVersionAsync(first.LatestVersionId);
        }
        catch (FinanceApiException ex) { ActionError = ex.Message; }
    }

    private async Task SelectVersionAsync(Guid versionId)
    {
        if (AccessState.CompanyId is not Guid companyId) return;
        try
        {
            Selected = await FinanceApiClient.GetReportDefinitionVersionAsync(companyId, versionId);
            SelectedLine = Selected?.Sections.SelectMany(x => x.Lines).FirstOrDefault();
            Preview = null; ActionError = null;
        }
        catch (FinanceApiException ex) { ActionError = ex.Message; }
    }

    private async Task CopyTemplateAsync() => await ActAsync(async companyId =>
    {
        Selected = await FinanceApiClient.CopyReportSystemTemplateAsync(companyId, new()
        {
            TemplateKey = SelectedTemplateKey, Code = NewDefinitionCode.Trim(), Name = NewDefinitionName.Trim(),
            IdempotencyKey = $"report-copy:{companyId:N}:{SelectedTemplateKey}:{NewDefinitionCode.Trim().ToUpperInvariant()}"
        });
        SelectedLine = Selected.Sections.SelectMany(x => x.Lines).FirstOrDefault();
        await RefreshDefinitionsAsync(companyId);
        ActionMessage = "The system template was copied into a company-owned draft.";
    });

    private async Task SaveAsync() => await ActAsync(async companyId =>
    {
        if (Selected is null) return;
        Selected = await FinanceApiClient.UpdateReportDefinitionVersionAsync(companyId, Selected.VersionId, new()
        {
            Name = Selected.Name, ExpectedRevision = Selected.Revision,
            IdempotencyKey = $"report-update:{Selected.VersionId:N}:{Selected.Revision}:{Guid.NewGuid():N}",
            Comparison = Selected.Comparison,
            Sections = Selected.Sections.Select(s => new ReportDefinitionSectionInputRequest
            {
                Code = s.Code, Label = s.Label, DisplayOrder = s.DisplayOrder,
                Lines = s.Lines.Select(l => new ReportDefinitionLineInputRequest
                {
                    Code = l.Code, Label = l.Label, LineType = l.LineType, DisplayOrder = l.DisplayOrder,
                    Formula = string.IsNullOrWhiteSpace(l.Formula) ? null : l.Formula, SignRule = l.SignRule,
                    Scale = l.Scale, Decimals = l.Decimals, SuppressZero = l.SuppressZero,
                    CurrencyMode = l.CurrencyMode, DimensionTypeId = l.DimensionTypeId, DimensionMemberId = l.DimensionMemberId,
                    AccountGroups = l.AccountGroups.Select(g => new ReportDefinitionAccountGroupInputRequest
                    { Code = g.Code, Name = g.Name, FinanceAccountIds = [.. g.FinanceAccountIds] }).ToList()
                }).ToList()
            }).ToList()
        });
        SelectedLine = Selected.Sections.SelectMany(x => x.Lines).FirstOrDefault(x => x.Code == SelectedLine?.Code);
        await RefreshDefinitionsAsync(companyId); ActionMessage = "Draft changes saved with optimistic concurrency.";
    });

    private Task ValidateAsync() => RevisionActionAsync((companyId, selected) =>
        FinanceApiClient.ValidateReportDefinitionVersionAsync(companyId, selected.VersionId, selected.Revision),
        "Validation completed.");
    private Task SubmitAsync() => RevisionActionAsync((companyId, selected) =>
        FinanceApiClient.SubmitReportDefinitionVersionAsync(companyId, selected.VersionId, selected.Revision),
        "The definition was submitted for independent approval.");
    private Task ApproveAsync(bool approve) => RevisionActionAsync((companyId, selected) =>
        FinanceApiClient.DecideReportDefinitionVersionAsync(companyId, selected.VersionId, selected.Revision,
            approve, DecisionNote), approve ? "Definition approved." : "Definition rejected and returned to draft.");
    private Task ActivateAsync() => RevisionActionAsync((companyId, selected) =>
        FinanceApiClient.ActivateReportDefinitionVersionAsync(companyId, selected.VersionId, selected.Revision, EffectiveDate),
        "The approved version is active prospectively from the selected date.");
    private Task RetireAsync() => RevisionActionAsync((companyId, selected) =>
        FinanceApiClient.RetireReportDefinitionVersionAsync(companyId, selected.VersionId, selected.Revision, EffectiveDate),
        "The definition version was retired without changing historical snapshots.");

    private async Task CreateNextVersionAsync() => await ActAsync(async companyId =>
    {
        if (Selected is null) return;
        Selected = await FinanceApiClient.CreateReportDefinitionVersionAsync(companyId, Selected.DefinitionId, Selected.VersionId);
        SelectedLine = Selected.Sections.SelectMany(x => x.Lines).FirstOrDefault(); Preview = null;
        await RefreshDefinitionsAsync(companyId); ActionMessage = "A new editable version was created from the selected version.";
    });

    private async Task PreviewAsync() => await ActAsync(async companyId =>
    {
        if (Selected is null || SelectedPeriodId == Guid.Empty) return;
        Preview = await FinanceApiClient.PreviewReportDefinitionVersionAsync(companyId, Selected.VersionId, SelectedPeriodId);
        ActionMessage = Preview.Blockers.Count == 0 ? "Preview generated from posted journals." :
            $"Preview generated with {Preview.Blockers.Count} blocking issue(s).";
    });

    private async Task RevisionActionAsync(Func<Guid, ReportDefinitionVersionResponse, Task<ReportDefinitionVersionResponse>> action,
        string message) => await ActAsync(async companyId =>
    {
        if (Selected is null) return;
        Selected = await action(companyId, Selected); SelectedLine = Selected.Sections.SelectMany(x => x.Lines).FirstOrDefault();
        await RefreshDefinitionsAsync(companyId); ActionMessage = message;
    });

    private async Task ActAsync(Func<Guid, Task> action)
    {
        if (IsBusy || AccessState.CompanyId is not Guid companyId) return;
        IsBusy = true; ActionError = null; ActionMessage = null;
        try { await action(companyId); }
        catch (FinanceApiException ex) { ActionError = ex.Message; }
        finally { IsBusy = false; }
    }

    private async Task RefreshDefinitionsAsync(Guid companyId) => Definitions = await FinanceApiClient.GetReportDefinitionsAsync(companyId);

    private void AddSection()
    {
        if (Selected is null || !Selected.CanEdit) return;
        var order = Selected.Sections.Count + 1;
        Selected.Sections.Add(new() { Code = $"SECTION_{order}", Label = $"New section {order}", DisplayOrder = order });
    }

    private void AddLine(ReportDefinitionSectionResponse section)
    {
        if (Selected is null || !Selected.CanEdit) return;
        var order = section.Lines.Count + 1;
        var line = new ReportDefinitionLineResponse { Code = $"LINE_{section.DisplayOrder}_{order}", Label = "New line", DisplayOrder = order,
            AccountGroups = [new() { Code = $"GROUP_{section.DisplayOrder}_{order}", Name = "Mapped accounts" }] };
        section.Lines.Add(line); SelectedLine = line;
    }

    private void RemoveLine(ReportDefinitionSectionResponse section, ReportDefinitionLineResponse line)
    {
        if (Selected is null || !Selected.CanEdit) return;
        section.Lines.Remove(line); SelectedLine = Selected.Sections.SelectMany(x => x.Lines).FirstOrDefault();
    }

    private void ToggleAccount(Guid accountId, bool selected)
    {
        if (SelectedGroup is null) return;
        if (selected && !SelectedGroup.FinanceAccountIds.Contains(accountId)) SelectedGroup.FinanceAccountIds.Add(accountId);
        else if (!selected) SelectedGroup.FinanceAccountIds.Remove(accountId);
    }

    private static string StatusClass(string status) => status switch
    {
        "active" or "approved" => "status-ok", "submitted" => "status-warn", "retired" => "status-muted", _ => "status-draft"
    };
}
