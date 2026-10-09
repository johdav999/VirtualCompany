namespace VirtualCompany.Web.Services;

public sealed partial class FinanceApiClient
{
    public Task<AccountingScheduleListResponse?> ListAccountingSchedulesAsync(Guid companyId, string? status = null,
        CancellationToken cancellationToken = default)
    {
        var suffix = string.IsNullOrWhiteSpace(status) ? string.Empty : $"?status={Uri.EscapeDataString(status)}";
        return GetAsync<AccountingScheduleListResponse>(companyId,
            $"internal/companies/{companyId}/finance/accounting/schedules{suffix}", false, cancellationToken);
    }

    public Task<AccountingScheduleResponse?> GetAccountingScheduleAsync(Guid companyId, Guid scheduleId,
        CancellationToken cancellationToken = default) => GetAsync<AccountingScheduleResponse>(companyId,
            $"internal/companies/{companyId}/finance/accounting/schedules/{scheduleId:D}", true, cancellationToken);

    public Task<AccountingScheduleResponse> CreateAccountingScheduleAsync(Guid companyId,
        SaveAccountingScheduleApiRequest request, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<SaveAccountingScheduleApiRequest, AccountingScheduleResponse>(companyId,
            HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/schedules", request, cancellationToken);
    }

    public Task<AccountingScheduleResponse> UpdateAccountingScheduleAsync(Guid companyId, Guid scheduleId,
        SaveAccountingScheduleApiRequest request, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<SaveAccountingScheduleApiRequest, AccountingScheduleResponse>(companyId,
            HttpMethod.Put, $"internal/companies/{companyId}/finance/accounting/schedules/{scheduleId:D}", request,
            cancellationToken);
    }

    public Task<AccountingSchedulePreviewResponse> PreviewAccountingScheduleAsync(Guid companyId, Guid scheduleId,
        long expectedVersion, CancellationToken cancellationToken = default) =>
        SendCompanyScopedAsync<AccountingScheduleVersionApiRequest, AccountingSchedulePreviewResponse>(companyId,
            HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/schedules/{scheduleId:D}/preview",
            new() { ExpectedVersion = expectedVersion }, cancellationToken);

    public Task<AccountingScheduleResponse> SubmitAccountingScheduleAsync(Guid companyId, Guid scheduleId,
        long expectedVersion, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<AccountingScheduleActionApiRequest, AccountingScheduleResponse>(companyId,
            HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/schedules/{scheduleId:D}/submit",
            new() { ExpectedVersion = expectedVersion, IdempotencyKey = idempotencyKey }, cancellationToken);
    }

    public Task<AccountingScheduleResponse> DecideAccountingScheduleApprovalAsync(Guid companyId, Guid scheduleId,
        long expectedVersion, bool approve, string? comment, Guid clientRequestId, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<DecideAccountingScheduleApprovalApiRequest, AccountingScheduleResponse>(companyId,
            HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/schedules/{scheduleId:D}/approval",
            new() { ExpectedVersion = expectedVersion, Approve = approve, Comment = comment, ClientRequestId = clientRequestId }, cancellationToken);
    }

    public Task<AccountingScheduleResponse> ActivateAccountingScheduleAsync(Guid companyId, Guid scheduleId,
        long expectedVersion, CancellationToken cancellationToken = default) =>
        SendScheduleVersionActionAsync(companyId, scheduleId, "activate", expectedVersion, cancellationToken);

    public Task<AccountingScheduleResponse> ChangeAccountingScheduleStateAsync(Guid companyId, Guid scheduleId,
        string action, long expectedVersion, bool generateMissed = false, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<ChangeAccountingScheduleStateApiRequest, AccountingScheduleResponse>(companyId,
            HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/schedules/{scheduleId:D}/{action}",
            new() { ExpectedVersion = expectedVersion, GenerateMissed = generateMissed }, cancellationToken);
    }

    public Task<AccountingScheduleResponse> RegenerateAccountingScheduleOccurrenceAsync(Guid companyId,
        Guid scheduleId, Guid occurrenceId, long expectedVersion, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<AccountingScheduleVersionApiRequest, AccountingScheduleResponse>(companyId,
            HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/schedules/{scheduleId:D}/occurrences/{occurrenceId:D}/regenerate",
            new() { ExpectedVersion = expectedVersion }, cancellationToken);
    }

    private Task<AccountingScheduleResponse> SendScheduleVersionActionAsync(Guid companyId, Guid scheduleId,
        string action, long expectedVersion, CancellationToken cancellationToken)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<AccountingScheduleVersionApiRequest, AccountingScheduleResponse>(companyId,
            HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/schedules/{scheduleId:D}/{action}",
            new() { ExpectedVersion = expectedVersion }, cancellationToken);
    }
}
