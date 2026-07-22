using System.Net;
using System.Net.Http.Json;
using System.Globalization;
using Microsoft.Extensions.Logging;
using VirtualCompany.Shared;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VirtualCompany.Web.Services;

public sealed partial class FinanceApiClient
{
    public Task<IReadOnlyList<FinanceBillInboxRowResponse>> GetBillInboxAsync(
        Guid companyId,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (_useOfflineMode)
        {
            return Task.FromResult<IReadOnlyList<FinanceBillInboxRowResponse>>([]);
        }

        var uri = $"internal/companies/{companyId}/finance/bill-inbox{BuildQuery(("limit", limit.ToString(CultureInfo.InvariantCulture)))}";
        return GetListAsync<FinanceBillInboxRowResponse>(companyId, uri, cancellationToken);
    }

    public Task<FinanceBillInboxDetailResponse?> GetBillInboxDetailAsync(Guid companyId, Guid billId, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult<FinanceBillInboxDetailResponse?>(null)
            : GetAsync<FinanceBillInboxDetailResponse>(companyId, $"internal/companies/{companyId}/finance/bill-inbox/{billId}", allowNotFound: true, cancellationToken);

    public Task<FinanceBillReviewActionResultResponse> ApproveBillInboxItemAsync(Guid companyId, Guid billId, string rationale, CancellationToken cancellationToken = default) =>
        SendCompanyScopedAsync<FinanceBillReviewActionRequest, FinanceBillReviewActionResultResponse>(
            companyId, HttpMethod.Post, $"internal/companies/{companyId}/finance/bill-inbox/{billId}/approve", new FinanceBillReviewActionRequest(rationale), cancellationToken);

    public Task<FinanceBillReviewActionResultResponse> RejectBillInboxItemAsync(Guid companyId, Guid billId, string rationale, CancellationToken cancellationToken = default) =>
        SendCompanyScopedAsync<FinanceBillReviewActionRequest, FinanceBillReviewActionResultResponse>(
            companyId, HttpMethod.Post, $"internal/companies/{companyId}/finance/bill-inbox/{billId}/reject", new FinanceBillReviewActionRequest(rationale), cancellationToken);

    public Task<FinanceBillReviewActionResultResponse> RequestBillInboxClarificationAsync(Guid companyId, Guid billId, string rationale, CancellationToken cancellationToken = default) =>
        SendCompanyScopedAsync<FinanceBillReviewActionRequest, FinanceBillReviewActionResultResponse>(
            companyId, HttpMethod.Post, $"internal/companies/{companyId}/finance/bill-inbox/{billId}/request-clarification", new FinanceBillReviewActionRequest(rationale), cancellationToken);

    public Task<FinanceBillFortnoxRegistrationResponse> RequestBillInboxFortnoxRegistrationAsync(Guid companyId, Guid billId, string rationale, CancellationToken cancellationToken = default) =>
        SendCompanyScopedAsync<FinanceBillReviewActionRequest, FinanceBillFortnoxRegistrationResponse>(
            companyId, HttpMethod.Post, $"internal/companies/{companyId}/finance/bill-inbox/{billId}/fortnox-registration/request", new FinanceBillReviewActionRequest(rationale), cancellationToken);

    public Task<FinanceBillFortnoxRegistrationResponse> SendBillInboxFortnoxRegistrationAsync(Guid companyId, Guid billId, CancellationToken cancellationToken = default) =>
        SendCompanyScopedAsync<object, FinanceBillFortnoxRegistrationResponse>(
            companyId, HttpMethod.Post, $"internal/companies/{companyId}/finance/bill-inbox/{billId}/fortnox-registration/send", new { }, cancellationToken);

    public Task<MailboxConnectionStatusResponse?> GetMailboxConnectionStatusAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult<MailboxConnectionStatusResponse?>(new MailboxConnectionStatusResponse())
            : GetAsync<MailboxConnectionStatusResponse>(companyId, $"api/companies/{companyId}/mailbox-connections/current", allowNotFound: false, cancellationToken);

    public Task<MailboxConnectionStatusResponse?> GetMailboxConnectionStatusAsync(
        Guid companyId,
        string purpose,
        CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult<MailboxConnectionStatusResponse?>(new MailboxConnectionStatusResponse { Purpose = purpose })
            : GetAsync<MailboxConnectionStatusResponse>(
                companyId,
                $"api/companies/{companyId}/mailbox-connections/purposes/{Uri.EscapeDataString(purpose)}",
                allowNotFound: false,
                cancellationToken);

    public Task<MailboxProviderAvailabilityResponse> GetMailboxProviderAvailabilityAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult(new MailboxProviderAvailabilityResponse())
            : GetAsync<MailboxProviderAvailabilityResponse>(companyId, $"api/companies/{companyId}/mailbox-connections/providers", allowNotFound: false, cancellationToken)!;

    public Task<IReadOnlyList<StandardMailboxProfileResponse>> GetStandardMailboxProfilesAsync(
        Guid companyId,
        CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult<IReadOnlyList<StandardMailboxProfileResponse>>([])
            : GetListAsync<StandardMailboxProfileResponse>(
                companyId,
                $"api/companies/{companyId}/mailbox-connections/standard/profiles",
                cancellationToken);

    public Task<StandardMailboxConnectionResultResponse> TestStandardMailboxAsync(
        Guid companyId,
        string purpose,
        StandardMailboxConnectionRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<StandardMailboxConnectionRequest, StandardMailboxConnectionResultResponse>(
            companyId,
            HttpMethod.Post,
            $"api/companies/{companyId}/mailbox-connections/purposes/{Uri.EscapeDataString(purpose)}/standard/test",
            request,
            cancellationToken);
    }

    public Task<StandardMailboxConnectionResultResponse> SaveStandardMailboxAsync(
        Guid companyId,
        string purpose,
        StandardMailboxConnectionRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<StandardMailboxConnectionRequest, StandardMailboxConnectionResultResponse>(
            companyId,
            HttpMethod.Post,
            $"api/companies/{companyId}/mailbox-connections/purposes/{Uri.EscapeDataString(purpose)}/standard",
            request,
            cancellationToken);
    }

    public async Task<string> StartStandardMailboxOAuthAsync(
        Guid companyId,
        string purpose,
        StandardMailboxConnectionRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        var response = await SendCompanyScopedAsync<StandardMailboxConnectionRequest, StartMailboxConnectionResponse>(
            companyId,
            HttpMethod.Post,
            $"api/companies/{companyId}/mailbox-connections/purposes/{Uri.EscapeDataString(purpose)}/standard/oauth/start",
            request,
            cancellationToken);
        return response.AuthorizationUrl;
    }

    public Task<IReadOnlyList<MailboxScannedMessageResponse>> GetMailboxScannedMessagesAsync(
        Guid companyId,
        int limit = 50,
        CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult<IReadOnlyList<MailboxScannedMessageResponse>>([])
            : GetListAsync<MailboxScannedMessageResponse>(
                companyId,
                $"api/companies/{companyId}/mailbox-connections/messages{BuildQuery(("limit", limit.ToString(CultureInfo.InvariantCulture)))}",
                cancellationToken);

    public async Task<string> StartMailboxConnectionAsync(
        Guid companyId,
        string provider,
        string? returnUri = null,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        var response = await SendCompanyScopedAsync<StartMailboxConnectionRequest, StartMailboxConnectionResponse>(
            companyId,
            HttpMethod.Post,
            $"api/companies/{companyId}/mailbox-connections/{provider}/start",
            new StartMailboxConnectionRequest { ReturnUri = returnUri },
            cancellationToken);

        return response.AuthorizationUrl;
    }

    public async Task<string> StartPurposeMailboxConnectionAsync(
        Guid companyId,
        string purpose,
        string provider,
        string? returnUri = null,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        var response = await SendCompanyScopedAsync<StartMailboxConnectionRequest, StartMailboxConnectionResponse>(
            companyId,
            HttpMethod.Post,
            $"api/companies/{companyId}/mailbox-connections/purposes/{Uri.EscapeDataString(purpose)}/{Uri.EscapeDataString(provider)}/start",
            new StartMailboxConnectionRequest { ReturnUri = returnUri },
            cancellationToken);

        return response.AuthorizationUrl;
    }

    public Task<MailboxConnectionStatusResponse> DisconnectMailboxConnectionAsync(
        Guid companyId,
        string purpose,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<object, MailboxConnectionStatusResponse>(
            companyId,
            HttpMethod.Post,
            $"api/companies/{companyId}/mailbox-connections/purposes/{Uri.EscapeDataString(purpose)}/disconnect",
            new { },
            cancellationToken);
    }

    public Task<ManualMailboxScanResponse> TriggerManualMailboxScanAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<object, ManualMailboxScanResponse>(
            companyId,
            HttpMethod.Post,
            $"api/companies/{companyId}/mailbox-connections/scan",
            new { },
            cancellationToken);
    }

    public Task<FinanceEmailSettingsResponse> GetEmailSettingsAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult(new FinanceEmailSettingsResponse())
            : GetAsync<FinanceEmailSettingsResponse>(companyId, $"api/companies/{companyId}/mailbox-provider-settings", allowNotFound: false, cancellationToken)!;

    public Task<FinanceEmailSettingsResponse> UpdateEmailSettingsAsync(
        Guid companyId,
        UpdateFinanceEmailSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<UpdateFinanceEmailSettingsRequest, FinanceEmailSettingsResponse>(
            companyId,
            HttpMethod.Put,
            $"api/companies/{companyId}/mailbox-provider-settings",
            request,
            cancellationToken);
    }

}
