using Microsoft.AspNetCore.Components;
using VirtualCompany.Web.Services;
using VirtualCompany.Shared;

namespace VirtualCompany.Web.Pages.Finance;

public partial class CounterpartiesPage : FinancePageBase
{
    [Inject] private FinanceApiClient FinanceApiClient { get; set; } = default!;

    private IReadOnlyList<FinanceCounterpartyResponse> Items { get; set; } = [];
    private FinanceCounterpartyResponse? Selected { get; set; }
    private Guid? SelectedId { get; set; }
    private string SelectedType { get; set; } = "customer";
    private CounterpartyEditorModel Editor { get; set; } = new();
    private bool IsCreatingNew { get; set; } = true;
    private bool IsListLoading { get; set; }
    private bool IsSaving { get; set; }
    private string? ListErrorMessage { get; set; }
    private string? ValidationMessage { get; set; }
    private string? SaveMessage { get; set; }
    private bool CanEdit => FinanceAccess.CanEdit(AccessState.MembershipRole);

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();
        if (!AccessState.IsAllowed || AccessState.CompanyId is not Guid companyId)
        {
            return;
        }

        await LoadListAsync(companyId);
    }

    private async Task ReloadAsync()
    {
        if (AccessState.CompanyId is Guid companyId)
        {
            await LoadListAsync(companyId);
        }
    }

    private async Task HandleTypeChangedAsync(string type)
    {
        SelectedType = type;
        SelectedId = null;
        Selected = null;
        IsCreatingNew = true;
        Editor = CounterpartyEditorModel.CreateDefaults(type);
        await ReloadAsync();
    }

    private Task HandleNewAsync()
    {
        SelectedId = null;
        Selected = null;
        IsCreatingNew = true;
        Editor = CounterpartyEditorModel.CreateDefaults(SelectedType);
        ValidationMessage = null;
        SaveMessage = null;
        return Task.CompletedTask;
    }

    private async Task HandleSelectAsync(Guid id)
    {
        if (AccessState.CompanyId is not Guid companyId)
        {
            return;
        }

        Selected = SelectedType == "customer"
            ? await FinanceApiClient.GetCustomerAsync(companyId, id)
            : await FinanceApiClient.GetSupplierAsync(companyId, id);

        if (Selected is null)
        {
            return;
        }

        SelectedId = id;
        IsCreatingNew = false;
        Editor = CounterpartyEditorModel.FromResponse(Selected);
        ValidationMessage = null;
        SaveMessage = null;
    }

    private async Task LoadListAsync(Guid companyId)
    {
        IsListLoading = true;
        ListErrorMessage = null;
        try
        {
            Items = SelectedType == "customer"
                ? await FinanceApiClient.GetCustomersAsync(companyId)
                : await FinanceApiClient.GetSuppliersAsync(companyId);
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

    private async Task HandleSaveAsync()
    {
        if (!CanEdit || AccessState.CompanyId is not Guid companyId)
        {
            return;
        }

        ValidationMessage = null;
        SaveMessage = null;
        IsSaving = true;
        try
        {
            var request = Editor.ToRequest();
            Selected = IsCreatingNew
                ? SelectedType == "customer" ? await FinanceApiClient.CreateCustomerAsync(companyId, request) : await FinanceApiClient.CreateSupplierAsync(companyId, request)
                : SelectedType == "customer" ? await FinanceApiClient.UpdateCustomerAsync(companyId, SelectedId!.Value, request) : await FinanceApiClient.UpdateSupplierAsync(companyId, SelectedId!.Value, request);
            SelectedId = Selected.Id;
            IsCreatingNew = false;
            Editor = CounterpartyEditorModel.FromResponse(Selected);
            SaveMessage = $"{TitleCase(SelectedType)} saved.";
            await LoadListAsync(companyId);
        }
        catch (FinanceApiValidationException ex)
        {
            ValidationMessage = ex.Errors.Values.SelectMany(x => x).FirstOrDefault() ?? ex.Message;
        }
        catch (FinanceApiException ex)
        {
            ValidationMessage = ex.Message;
        }
        finally
        {
            IsSaving = false;
        }
    }

    private static string TitleCase(string value) =>
        string.IsNullOrWhiteSpace(value) ? "Counterparty" : char.ToUpperInvariant(value[0]) + value[1..];

    private static string FormatOptionalValue(string? value, string fallback = "Not set") =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string FormatCreditLimit(decimal? value) =>
        value.HasValue ? value.Value.ToString("0.##") : "Not set";

    private static string BuildListMetadata(FinanceCounterpartyResponse response)
    {
        var segments = new List<string>
        {
            $"Tax ID: {FormatOptionalValue(response.TaxId)}",
            $"Credit limit: {FormatCreditLimit(response.CreditLimit)}"
        };

        segments.Add($"Mapping: {FormatOptionalValue(response.DefaultAccountMapping)}");

        return string.Join(" | ", segments);
    }

    private sealed class CounterpartyEditorModel
    {
        public string Name { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? PaymentTerms { get; set; }
        public string? TaxId { get; set; }
        public decimal? CreditLimit { get; set; }
        public string? PreferredPaymentMethod { get; set; }
        public string? DefaultAccountMapping { get; set; }

        public static CounterpartyEditorModel CreateDefaults(string type) => new()
        {
            PaymentTerms = "Net30",
            CreditLimit = 0m,
            PreferredPaymentMethod = "bank_transfer",
            DefaultAccountMapping = type == "customer" ? "1100" : "2000"
        };

        public static CounterpartyEditorModel FromResponse(FinanceCounterpartyResponse response) => new()
        {
            Name = response.Name,
            Email = response.Email,
            PaymentTerms = response.PaymentTerms,
            TaxId = response.TaxId,
            CreditLimit = response.CreditLimit,
            PreferredPaymentMethod = response.PreferredPaymentMethod,
            DefaultAccountMapping = response.DefaultAccountMapping
        };

        public UpsertFinanceCounterpartyRequest ToRequest() => new()
        {
            Name = Name,
            Email = Email,
            PaymentTerms = PaymentTerms,
            TaxId = TaxId,
            CreditLimit = CreditLimit,
            PreferredPaymentMethod = PreferredPaymentMethod,
            DefaultAccountMapping = DefaultAccountMapping
        };
    }
}