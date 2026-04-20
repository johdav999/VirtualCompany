using Microsoft.AspNetCore.Components;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Finance;

public partial class TransparencyToolExecutionsPage : FinanceTransparencyPageBase
{
    [Parameter]
    public Guid? ExecutionId { get; set; }

    private string Summary { get; set; } = string.Empty;
    private IReadOnlyList<FinanceTransparencyToolExecutionListItemViewModel> Items { get; set; } = [];
    private FinanceTransparencyToolExecutionDetailViewModel? SelectedExecution { get; set; }
    private bool IsListLoading { get; set; }
    private bool IsDetailLoading { get; set; }
    private string? ListErrorMessage { get; set; }
    private string? DetailErrorMessage { get; set; }
    private bool IsListEmpty => !IsListLoading && string.IsNullOrWhiteSpace(ListErrorMessage) && Items.Count == 0;

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();

        Summary = string.Empty;
        Items = [];
        SelectedExecution = null;
        ListErrorMessage = null;
        DetailErrorMessage = null;

        if (!HasTransparencyAccess)
        {
            return;
        }

        await LoadExecutionsAsync(CompanyScopeId);
        if (ExecutionId is Guid executionId)
        {
            await LoadDetailAsync(CompanyScopeId, executionId);
        }
    }

    private async Task ReloadAsync()
    {
        if (!HasTransparencyAccess)
        {
            return;
        }

        await LoadExecutionsAsync(CompanyScopeId);
        if (ExecutionId is Guid executionId)
        {
            await LoadDetailAsync(CompanyScopeId, executionId);
        }
    }

    private async Task LoadExecutionsAsync(Guid companyId)
    {
        IsListLoading = true;
        ListErrorMessage = null;

        try
        {
            var response = await SandboxAdminService.GetTransparencyToolExecutionsAsync(companyId);
            Summary = response?.Summary ?? string.Empty;
            Items = response?.Items ?? [];
        }
        catch (FinanceApiException ex)
        {
            Items = [];
            ListErrorMessage = ex.Message;
        }
        finally { IsListLoading = false; }
    }

    private async Task LoadDetailAsync(Guid companyId, Guid executionId)
    {
        IsDetailLoading = true;
        DetailErrorMessage = null;

        try { SelectedExecution = await SandboxAdminService.GetTransparencyToolExecutionDetailAsync(companyId, executionId); }
        catch (FinanceApiException ex) { DetailErrorMessage = ex.Message; }
        finally { DetailErrorMessage ??= SelectedExecution is null ? "The selected tool execution could not be found in the active company context." : null; IsDetailLoading = false; }
    }
}
