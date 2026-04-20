using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Finance;

public partial class TransparencyToolRegistryPage : FinanceTransparencyPageBase
{
    private string Summary { get; set; } = string.Empty;
    private IReadOnlyList<FinanceTransparencyToolManifestItemViewModel> Items { get; set; } = [];
    private bool IsListLoading { get; set; }
    private string? ListErrorMessage { get; set; }
    private bool IsListEmpty => !IsListLoading && string.IsNullOrWhiteSpace(ListErrorMessage) && Items.Count == 0;

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();

        Summary = string.Empty;
        Items = [];
        ListErrorMessage = null;

        if (!HasTransparencyAccess)
        {
            return;
        }

        await LoadAsync(CompanyScopeId);
    }

    private Task ReloadAsync() =>
        HasTransparencyAccess ? LoadAsync(CompanyScopeId) : Task.CompletedTask;

    private async Task LoadAsync(Guid companyId)
    {
        IsListLoading = true;
        ListErrorMessage = null;

        try
        {
            var response = await SandboxAdminService.GetTransparencyToolManifestsAsync(companyId);
            Summary = response?.Summary ?? string.Empty;
            Items = response?.Items ?? [];
        }
        catch (FinanceApiException ex)
        {
            Items = [];
            ListErrorMessage = ex.Message;
        }
        finally
        {
            IsListLoading = false;
        }
    }
}
