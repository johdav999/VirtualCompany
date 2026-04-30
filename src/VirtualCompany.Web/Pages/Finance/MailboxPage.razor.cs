using System.Globalization;
using Microsoft.AspNetCore.Components;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Finance;

public partial class MailboxPage : FinancePageBase
{
    [Inject] private FinanceApiClient FinanceApiClient { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "mailboxConnection")]
    public string? MailboxConnectionResult { get; set; }

    [SupplyParameterFromQuery(Name = "mailboxMessage")]
    public string? MailboxConnectionMessage { get; set; }

    private MailboxConnectionStatusResponse? Status { get; set; }
    private MailboxProviderAvailabilityResponse? ProviderAvailability { get; set; }
    private IReadOnlyList<MailboxScannedMessageResponse> ScannedMessages { get; set; } = [];
    private MailboxScannedMessageResponse? SelectedMessage { get; set; }
    private bool IsStatusLoading { get; set; }
    private bool IsMessagesLoading { get; set; }
    private bool IsStartingConnection { get; set; }
    private bool IsScanInFlight { get; set; }
    private string? StatusErrorMessage { get; set; }
    private string? MessagesErrorMessage { get; set; }
    private string? ActionMessage { get; set; }
    private string? ActionErrorMessage { get; set; }
    private string? ProviderSetupMessage { get; set; }

    private bool IsActionBusy => IsStartingConnection || IsScanInFlight;
    private bool CanScan => Status?.IsConnected == true;
    private bool IsScanDisabled => !CanScan || IsActionBusy || IsStatusLoading;
    private bool IsGmailConnectDisabled => IsActionBusy || IsStatusLoading;
    private bool IsMicrosoft365ConnectDisabled => IsActionBusy || IsStatusLoading;

    private string ConnectionStatusLabel =>
        Status is null || string.IsNullOrWhiteSpace(Status.ConnectionStatus)
            ? "Not connected"
            : FormatConnectionStatus(Status.ConnectionStatus);

    private string ConnectionBadgeClass =>
        Status?.IsConnected == true
            ? "badge text-bg-success"
            : Status?.ConnectionStatus is "failed" or "token_expired" or "revoked"
                ? "badge text-bg-danger"
                : "badge text-bg-light";

    private string ProviderLabel => FormatProvider(Status?.Provider);
    private string GmailProviderStatus => GetProviderAvailabilityLabel(ProviderAvailability?.Gmail);
    private string Microsoft365ProviderStatus => GetProviderAvailabilityLabel(ProviderAvailability?.Microsoft365);

    private string MailboxLabel =>
        string.IsNullOrWhiteSpace(Status?.EmailAddress)
            ? "No mailbox connected"
            : string.IsNullOrWhiteSpace(Status.DisplayName)
                ? Status.EmailAddress!
                : $"{Status.DisplayName} ({Status.EmailAddress})";

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();

        Status = null;
        ProviderAvailability = null;
        ScannedMessages = [];
        SelectedMessage = null;
        StatusErrorMessage = null;
        MessagesErrorMessage = null;
        ApplyMailboxConnectionResult();
        ProviderSetupMessage = null;

        if (!AccessState.IsAllowed || AccessState.CompanyId is not Guid companyId)
        {
            return;
        }

        await LoadStatusAndProvidersAsync(companyId);
    }

    private async Task ReloadAsync()
    {
        ActionMessage = null;
        ActionErrorMessage = null;
        ProviderSetupMessage = null;

        if (AccessState.CompanyId is not Guid companyId)
        {
            return;
        }

        await LoadStatusAndProvidersAsync(companyId);
    }

    private async Task LoadStatusAndProvidersAsync(Guid companyId)
    {
        IsStatusLoading = true;
        StatusErrorMessage = null;

        try
        {
            Status = await FinanceApiClient.GetMailboxConnectionStatusAsync(companyId);
            ProviderAvailability = await FinanceApiClient.GetMailboxProviderAvailabilityAsync(companyId);
            await LoadScannedMessagesAsync(companyId);
        }
        catch (FinanceApiException ex)
        {
            Status = null;
            ProviderAvailability = null;
            ScannedMessages = [];
            SelectedMessage = null;
            StatusErrorMessage = ex.Message;
        }
        finally
        {
            IsStatusLoading = false;
        }
    }

    private async Task LoadScannedMessagesAsync(Guid companyId)
    {
        IsMessagesLoading = true;
        MessagesErrorMessage = null;

        try
        {
            ScannedMessages = await FinanceApiClient.GetMailboxScannedMessagesAsync(companyId);
            SelectedMessage = SelectedMessage is null
                ? ScannedMessages.FirstOrDefault()
                : ScannedMessages.FirstOrDefault(message => message.Id == SelectedMessage.Id) ?? ScannedMessages.FirstOrDefault();
        }
        catch (FinanceApiException ex)
        {
            ScannedMessages = [];
            SelectedMessage = null;
            MessagesErrorMessage = ex.Message;
        }
        finally
        {
            IsMessagesLoading = false;
        }
    }

    private void SelectMessage(MailboxScannedMessageResponse message)
    {
        SelectedMessage = message;
    }

    private async Task StartConnectionAsync(string provider)
    {
        if (AccessState.CompanyId is not Guid companyId)
        {
            return;
        }

        IsStartingConnection = true;
        ActionMessage = null;
        ActionErrorMessage = null;
        ProviderSetupMessage = null;

        try
        {
            var authorizationUrl = await FinanceApiClient.StartMailboxConnectionAsync(companyId, provider, BuildMailboxReturnUri(companyId));
            Navigation.NavigateTo(authorizationUrl, forceLoad: true);
        }
        catch (MailboxProviderNotConfiguredApiException ex)
        {
            ProviderSetupMessage = $"{FormatProvider(provider)} sign-in is not enabled for this workspace yet. Ask an administrator to configure the email provider in Finance settings. {ex.Message}";
        }
        catch (FinanceApiException ex)
        {
            ActionErrorMessage = ex.Message;
        }
        finally
        {
            IsStartingConnection = false;
        }
    }

    private async Task TriggerScanAsync()
    {
        if (AccessState.CompanyId is not Guid companyId || !CanScan)
        {
            return;
        }

        IsScanInFlight = true;
        ActionMessage = null;
        ActionErrorMessage = null;
        ProviderSetupMessage = null;

        try
        {
            var result = await FinanceApiClient.TriggerManualMailboxScanAsync(companyId);
            ActionMessage = string.Equals(result.Status, "completed", StringComparison.OrdinalIgnoreCase)
                ? $"Scan completed. Messages scanned: {result.ScannedMessageCount}; candidates detected: {result.DetectedCandidateCount}."
                : "Mailbox scan started. Refresh this page to view the latest audit summary.";
            await LoadStatusAndProvidersAsync(companyId);
        }
        catch (FinanceApiException ex)
        {
            ActionErrorMessage = ex.Message;
        }
        finally
        {
            IsScanInFlight = false;
        }
    }

    private void ApplyMailboxConnectionResult()
    {
        ActionMessage = null;
        ActionErrorMessage = null;

        if (string.IsNullOrWhiteSpace(MailboxConnectionResult))
        {
            return;
        }

        switch (MailboxConnectionResult.Trim().ToLowerInvariant())
        {
            case "connected":
                ActionMessage = "Mailbox connected. You can scan the inbox for bills now.";
                break;
            case "denied":
                ActionErrorMessage = "Mailbox connection was cancelled before access was granted.";
                break;
            case "failed":
                ActionErrorMessage = string.IsNullOrWhiteSpace(MailboxConnectionMessage)
                    ? "Mailbox connection failed. Try connecting again."
                    : $"Mailbox connection failed: {MailboxConnectionMessage}";
                break;
        }
    }

    private string BuildMailboxReturnUri(Guid companyId)
    {
        var relativeUri = FinanceRoutes.WithCompanyContext(FinanceRoutes.Mailbox, companyId);
        return Navigation.ToAbsoluteUri(relativeUri).ToString();
    }

    private static string GetProviderAvailabilityLabel(MailboxProviderAvailability? availability) =>
        availability?.IsConfigured == true ? "Ready for users" : "Admin setup required";

    private static string GetProviderAvailabilityBadgeClass(MailboxProviderAvailability? availability) =>
        availability?.IsConfigured == true ? "badge text-bg-success" : "badge text-bg-warning";

    private static string FormatProvider(string? provider) => provider switch
    {
        "gmail" => "Gmail",
        "microsoft365" => "Microsoft 365",
        _ => "Not connected"
    };

    private static string FormatConnectionStatus(string status) =>
        string.Join(" ", status.Replace("-", "_", StringComparison.Ordinal).Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string FormatDateTime(DateTime? value) =>
        value.HasValue ? value.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC" : "Not available";

    private static string FormatSender(MailboxScannedMessageResponse message)
    {
        if (!string.IsNullOrWhiteSpace(message.FromDisplayName) && !string.IsNullOrWhiteSpace(message.FromAddress))
        {
            return $"{message.FromDisplayName} <{message.FromAddress}>";
        }

        return !string.IsNullOrWhiteSpace(message.FromDisplayName)
            ? message.FromDisplayName!
            : string.IsNullOrWhiteSpace(message.FromAddress) ? "Unknown sender" : message.FromAddress!;
    }

    private static string FormatMailboxCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Not available";
        }

        return string.Join(
            " ",
            value.Replace("-", "_", StringComparison.Ordinal)
                .Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(part => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(part)));
    }

    private static string FormatRules(IReadOnlyCollection<string> rules) =>
        rules.Count == 0 ? "Not available" : string.Join(", ", rules.Select(FormatMailboxCode));

    private static string FormatAttachments(IReadOnlyCollection<MailboxScannedAttachmentResponse> attachments) =>
        attachments.Count == 0
            ? "None"
            : string.Join(", ", attachments.Select(attachment => string.IsNullOrWhiteSpace(attachment.FileName) ? "Unnamed attachment" : attachment.FileName));
}
