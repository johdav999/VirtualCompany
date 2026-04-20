using Microsoft.AspNetCore.Components;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Finance;

public partial class TransparencyEventsPage : FinanceTransparencyPageBase
{
    [Parameter]
    public Guid? EventId { get; set; }

    private string Summary { get; set; } = string.Empty;
    private IReadOnlyList<FinanceTransparencyEventListItemViewModel> Events { get; set; } = [];
    private FinanceTransparencyEventDetailViewModel? SelectedEvent { get; set; }
    private bool IsListLoading { get; set; }
    private bool IsDetailLoading { get; set; }
    private string? ListErrorMessage { get; set; }
    private string? DetailErrorMessage { get; set; }
    private bool IsListEmpty => !IsListLoading && string.IsNullOrWhiteSpace(ListErrorMessage) && Events.Count == 0;

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();

        Summary = string.Empty;
        Events = [];
        SelectedEvent = null;
        ListErrorMessage = null;
        DetailErrorMessage = null;

        if (!HasTransparencyAccess)
        {
            return;
        }

        await LoadEventsAsync(CompanyScopeId);
        if (EventId is Guid eventId)
        {
            await LoadDetailAsync(CompanyScopeId, eventId);
        }
    }

    private async Task ReloadAsync()
    {
        if (!HasTransparencyAccess)
        {
            return;
        }

        await LoadEventsAsync(CompanyScopeId);
        if (EventId is Guid eventId)
        {
            await LoadDetailAsync(CompanyScopeId, eventId);
        }
    }

    private async Task LoadEventsAsync(Guid companyId)
    {
        IsListLoading = true;
        ListErrorMessage = null;

        try
        {
            var response = await SandboxAdminService.GetTransparencyEventsAsync(companyId);
            Summary = response?.Summary ?? string.Empty;
            Events = response?.Items ?? [];
        }
        catch (FinanceApiException ex)
        {
            Events = [];
            ListErrorMessage = ex.Message;
        }
        finally { IsListLoading = false; }
    }

    private async Task LoadDetailAsync(Guid companyId, Guid eventId)
    {
        IsDetailLoading = true;
        DetailErrorMessage = null;

        try { SelectedEvent = await SandboxAdminService.GetTransparencyEventDetailAsync(companyId, eventId); }
        catch (FinanceApiException ex) { DetailErrorMessage = ex.Message; }
        finally { DetailErrorMessage ??= SelectedEvent is null ? "The selected finance event could not be found in the active company context." : null; IsDetailLoading = false; }
    }
}
