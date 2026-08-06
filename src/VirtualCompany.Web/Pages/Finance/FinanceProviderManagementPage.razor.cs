using System.ComponentModel.DataAnnotations;
using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Finance;

public partial class FinanceProviderManagementPage
{
    [Inject] private FinanceIntegrationApplicationApiClient ApiClient { get; set; } = default!;
    [Inject] private IJSRuntime Js { get; set; } = default!;

    private List<FinanceIntegrationApplicationConfigurationResponse> Providers { get; set; } = [];
    private FinanceIntegrationApplicationConfigurationResponse? SelectedProvider { get; set; }
    private ProviderConfigurationForm Form { get; set; } = new();
    private FinanceIntegrationApplicationValidationResponse? Validation { get; set; }
    private IReadOnlyList<FinanceIntegrationApplicationAuditItemResponse> AuditItems { get; set; } = [];
    private bool IsLoading { get; set; } = true;
    private bool IsSaving { get; set; }
    private bool IsValidating { get; set; }
    private string? ErrorMessage { get; set; }
    private string? AccessError { get; set; }
    private string? ActionError { get; set; }
    private string? SuccessMessage { get; set; }
    private bool IsBusy => IsLoading || IsSaving || IsValidating;
    private string ClientIdPlaceholder => SelectedProvider?.ClientIdConfigured == true
        ? $"Configured ({SelectedProvider.ClientIdHint})"
        : "Enter the provider client ID";
    private string PreserveClientIdHelp => SelectedProvider?.ClientIdConfigured == true
        ? "Leave blank to keep the configured client ID."
        : "Required before company users can connect.";
    private string SecretPlaceholder => SelectedProvider?.ClientSecretConfigured == true
        ? "Configured — leave blank to keep"
        : "Enter the provider client secret";
    private string SecretHelp => SelectedProvider?.SecretBackendSupportsWrites == true
        ? "The value is write-only. Saving a new value creates a new secret version."
        : "The configured production secret backend is unavailable or read-only.";

    protected override Task OnInitializedAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        AccessError = null;
        try
        {
            var response = await ApiClient.GetAllAsync();
            Providers = response.Providers.ToList();
            var selectedKey = SelectedProvider?.ProviderKey ?? Providers.FirstOrDefault()?.ProviderKey;
            await SelectProviderAsync(selectedKey);
        }
        catch (FinanceIntegrationApplicationApiException ex) when (
            ex.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
        {
            AccessError = ex.Message;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task SelectProviderAsync(string? providerKey)
    {
        var provider = Providers.FirstOrDefault(x =>
            string.Equals(x.ProviderKey, providerKey, StringComparison.OrdinalIgnoreCase));
        if (provider is null)
        {
            SelectedProvider = null;
            return;
        }

        SelectedProvider = provider;
        Form = new ProviderConfigurationForm
        {
            Enabled = provider.Enabled,
            RedirectUri = provider.RedirectUri,
            Scopes = provider.SelectedScopes.ToHashSet(StringComparer.OrdinalIgnoreCase)
        };
        Validation = null;
        ActionError = null;
        SuccessMessage = null;

        try
        {
            var history = await ApiClient.GetAuditHistoryAsync(provider.ProviderKey);
            AuditItems = history.Items;
        }
        catch (Exception ex)
        {
            ActionError = $"The audit history could not be loaded. {ex.Message}";
            AuditItems = [];
        }
    }

    private async Task SaveAsync()
    {
        if (SelectedProvider is null)
        {
            return;
        }

        IsSaving = true;
        ActionError = null;
        SuccessMessage = null;
        try
        {
            var saved = await ApiClient.SaveAsync(
                SelectedProvider.ProviderKey,
                new SaveFinanceIntegrationApplicationConfigurationRequest(
                    Form.Enabled,
                    Form.ClientId,
                    string.IsNullOrWhiteSpace(Form.ClientSecret) ? null : Form.ClientSecret,
                    Form.RedirectUri,
                    Form.Scopes.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray()));

            ReplaceProvider(saved);
            SelectedProvider = saved;
            Form.ClientId = string.Empty;
            Form.ClientSecret = string.Empty;
            SuccessMessage = "The provider configuration was saved safely.";
            var history = await ApiClient.GetAuditHistoryAsync(saved.ProviderKey);
            AuditItems = history.Items;
        }
        catch (Exception ex)
        {
            ActionError = ex.Message;
        }
        finally
        {
            IsSaving = false;
        }
    }

    private async Task ValidateAsync()
    {
        if (SelectedProvider is null)
        {
            return;
        }

        IsValidating = true;
        ActionError = null;
        SuccessMessage = null;
        try
        {
            Validation = await ApiClient.ValidateAsync(SelectedProvider.ProviderKey);
            SuccessMessage = Validation.Succeeded
                ? "The provider setup passed validation."
                : null;
            var refreshed = await ApiClient.GetAllAsync();
            Providers = refreshed.Providers.ToList();
            SelectedProvider = Providers.First(x =>
                string.Equals(x.ProviderKey, Validation.ProviderKey, StringComparison.OrdinalIgnoreCase));
            var history = await ApiClient.GetAuditHistoryAsync(SelectedProvider.ProviderKey);
            AuditItems = history.Items;
        }
        catch (Exception ex)
        {
            ActionError = ex.Message;
        }
        finally
        {
            IsValidating = false;
        }
    }

    private void ToggleScope(string scope, bool selected)
    {
        if (selected)
        {
            Form.Scopes.Add(scope);
        }
        else
        {
            Form.Scopes.Remove(scope);
        }
    }

    private Task CopyCallbackAsync() =>
        Js.InvokeVoidAsync("navigator.clipboard.writeText", Form.RedirectUri).AsTask();

    private void ReplaceProvider(FinanceIntegrationApplicationConfigurationResponse saved)
    {
        var index = Providers.FindIndex(x =>
            string.Equals(x.ProviderKey, saved.ProviderKey, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            Providers[index] = saved;
        }
    }

    private static string ProviderMark(string displayName) =>
        string.Concat(displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(x => x[0]))
            .ToUpperInvariant()[..Math.Min(2, displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length)];

    private static string StatusLabel(string status) => status switch
    {
        "ready" => "Ready",
        "disabled" => "Disabled",
        "invalid" => "Needs attention",
        _ => "Setup required"
    };

    private static string StatusBadge(string status) => status switch
    {
        "ready" => "badge text-bg-success",
        "invalid" => "badge text-bg-danger",
        "disabled" => "badge text-bg-secondary",
        _ => "badge text-bg-warning"
    };

    private sealed class ProviderConfigurationForm
    {
        public bool Enabled { get; set; }
        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;

        [Required]
        [Url]
        public string RedirectUri { get; set; } = string.Empty;

        public HashSet<string> Scopes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
