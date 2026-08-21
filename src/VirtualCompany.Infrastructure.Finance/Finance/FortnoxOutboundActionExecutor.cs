using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class FortnoxOutboundActionExecutor : IFortnoxOutboundActionExecutor
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IFortnoxApiClient _fortnoxApiClient;
    private readonly IFinanceIntegrationWriteApprovalService _writeApprovalService;
    private readonly FinanceBillFortnoxRegistrationCompletionService _billCompletionService;
    private readonly IAccountingAuthorityPolicy _authorityPolicy;
    private readonly IAccountingProviderExportExecutionTracker _exportTracker;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<FortnoxOutboundActionExecutor> _logger;

    public FortnoxOutboundActionExecutor(
        VirtualCompanyDbContext dbContext,
        IFortnoxApiClient fortnoxApiClient,
        IFinanceIntegrationWriteApprovalService writeApprovalService,
        FinanceBillFortnoxRegistrationCompletionService billCompletionService,
        IAccountingAuthorityPolicy authorityPolicy,
        IAccountingProviderExportExecutionTracker exportTracker,
        TimeProvider timeProvider,
        ILogger<FortnoxOutboundActionExecutor> logger)
    {
        _dbContext = dbContext;
        _fortnoxApiClient = fortnoxApiClient;
        _writeApprovalService = writeApprovalService;
        _billCompletionService = billCompletionService;
        _authorityPolicy = authorityPolicy;
        _exportTracker = exportTracker;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<FinanceIntegrationOutboundExecutionResult> ExecuteApprovedAsync(
        Guid companyId,
        Guid writeRequestId,
        CancellationToken cancellationToken)
    {
        var command = await _dbContext.FinanceIntegrationWriteCommands
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == writeRequestId, cancellationToken)
            ?? throw new KeyNotFoundException("Accounting-system action was not found.");
        _logger.LogInformation(
            "Fortnox outbound action loaded. CompanyId: {CompanyId}. WriteRequestId: {WriteRequestId}. Status: {Status}. ApprovalId: {ApprovalId}. Path: {Path}. PayloadHash: {PayloadHash}. AttemptCount: {AttemptCount}.",
            companyId,
            writeRequestId,
            command.Status,
            command.ApprovalId,
            command.Path,
            command.PayloadHash,
            command.ExecutionAttemptCount);
        _logger.LogDebug(
            "Fortnox outbound action persisted state. CompanyId: {CompanyId}. WriteRequestId: {WriteRequestId}. ConnectionId: {ConnectionId}. Status: {Status}. FailureCategory: {FailureCategory}. ResponseStatusCode: {ResponseStatusCode}. RetrySupported: {RetrySupported}. FailedUtc: {FailedUtc}.",
            companyId,
            writeRequestId,
            command.ConnectionId,
            command.Status,
            command.FailureCategory,
            command.ResponseStatusCode,
            command.RetrySupported,
            command.FailedUtc);

        if (command.Status is FinanceIntegrationWriteCommandRecordStatuses.Executed or FinanceIntegrationWriteCommandRecordStatuses.Executing)
        {
            _logger.LogInformation(
                "Fortnox outbound action skipped because it is already in terminal or active execution state. CompanyId: {CompanyId}. WriteRequestId: {WriteRequestId}. Status: {Status}.",
                companyId,
                writeRequestId,
                command.Status);
            return ToResult(command, "This accounting-system action is already being handled.", executed: false);
        }

        if (command.Status is FinanceIntegrationWriteCommandRecordStatuses.Rejected or
            FinanceIntegrationWriteCommandRecordStatuses.Expired or
            FinanceIntegrationWriteCommandRecordStatuses.Cancelled)
        {
            await WriteAuditAsync(command, "write_skipped", FinanceIntegrationAuditOutcomes.Skipped, "This accounting-system action was not approved and was not sent to Fortnox.", cancellationToken);
            return ToResult(command, "This accounting-system action was not approved and was not sent to Fortnox.", executed: false);
        }

        if (command.ApprovalId is not Guid approvalId)
        {
            throw new FortnoxApprovalRequiredException(command.Id, "Approve this action before data is sent to the accounting system.");
        }

        var approval = await _dbContext.ApprovalRequests
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == approvalId, cancellationToken)
            ?? throw new FortnoxApprovalRequiredException(approvalId, "Approve this action before data is sent to the accounting system.");

        if (approval.Status != Domain.Enums.ApprovalRequestStatus.Approved)
        {
            _logger.LogWarning(
                "Fortnox outbound action blocked because approval is not approved. CompanyId: {CompanyId}. WriteRequestId: {WriteRequestId}. ApprovalId: {ApprovalId}. ApprovalStatus: {ApprovalStatus}.",
                companyId,
                writeRequestId,
                approvalId,
                approval.Status);
            throw new FortnoxApprovalRequiredException(approvalId, "Approve this action before data is sent to the accounting system.");
        }

        var activeConnection = await _dbContext.FinanceIntegrationConnections
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.ProviderKey == FinanceIntegrationProviderKeys.Fortnox &&
                x.Status == FinanceIntegrationConnectionStatuses.Connected &&
                (!command.ConnectionId.HasValue || x.Id == command.ConnectionId.Value))
            .OrderByDescending(x => x.ConnectedUtc ?? x.UpdatedUtc)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new FortnoxApiException("Fortnox is not connected.", null, "authorization", requiresReconnect: true);
        _logger.LogDebug(
            "Fortnox outbound action resolved an active company connection. CompanyId: {CompanyId}. WriteRequestId: {WriteRequestId}. CommandConnectionId: {CommandConnectionId}. ActiveConnectionId: {ActiveConnectionId}. ConnectionStatus: {ConnectionStatus}. ConnectionUpdatedUtc: {ConnectionUpdatedUtc}.",
            companyId,
            writeRequestId,
            command.ConnectionId,
            activeConnection.Id,
            activeConnection.Status,
            activeConnection.UpdatedUtc);

        var approvalCheck = new FinanceIntegrationWriteApprovalCheck(
            FinanceIntegrationProviderKeys.Fortnox,
            companyId,
            activeConnection.Id,
            command.ActorUserId,
            approvalId,
            command.CommandType,
            command.HttpMethod,
            command.Path,
            command.TargetCompany,
            command.PayloadSummary,
            command.PayloadHash,
            new FinanceIntegrationWritePayload(command.SanitizedPayloadJson),
            command.Id,
            AccountingDate: command.AccountingDate,
            AuthorityOperation: command.AuthorityOperation);
        try
        {
            await _exportTracker.EnsureExecutionAllowedAsync(companyId, writeRequestId, cancellationToken);
            var isTrackedCommittedExport = await _dbContext.AccountingProviderExports
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(x => x.CompanyId == companyId && x.WriteRequestId == writeRequestId, cancellationToken);
            if (!isTrackedCommittedExport && IsProviderAuthoritativeAccountingAction(command.CommandType))
            {
                var authority = await _authorityPolicy.EvaluateAsync(new(
                    companyId,
                    command.AccountingDate ?? DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime),
                    command.AuthorityOperation ?? AccountingAuthorityOperationValues.ProviderAuthoritativeWrite,
                    FinanceIntegrationProviderKeys.Fortnox), cancellationToken);
                if (!authority.IsAllowed && authority.ReasonCode != AccountingAuthorityReasonCodes.AuthorityNotConfigured)
                {
                    throw new AccountingAuthorityException(
                        authority.ReasonCode ?? AccountingAuthorityReasonCodes.ProviderPostingBlocked,
                        authority.Explanation,
                        true);
                }

                if (command.AuthorityPeriodId.HasValue && command.AuthorityPeriodId != authority.AuthorityPeriodId)
                {
                    throw new AccountingAuthorityException(
                        AccountingAuthorityReasonCodes.PreviewStale,
                        "The accounting authority changed after this provider action was approved. Request a new approval.",
                        true);
                }
            }
        }
        catch (AccountingAuthorityException exception)
        {
            throw new FortnoxApiException(exception.Message, null, "accounting_authority");
        }
        await WriteAuditAsync(command, "write_execution_started", FinanceIntegrationAuditOutcomes.Succeeded, "Approved accounting-system action is being sent to Fortnox.", cancellationToken);
        var payload = ParsePayload(command.SanitizedPayloadJson);
        var context = new FortnoxRequestContext(companyId, activeConnection.Id, command.CorrelationId, approvalId, command.ActorUserId, command.Id, command.RetrySupported);
        _logger.LogInformation(
            "Fortnox outbound action sending request. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. WriteRequestId: {WriteRequestId}. ApprovalId: {ApprovalId}. Method: {Method}. Path: {Path}. PayloadHash: {PayloadHash}.",
            companyId,
            activeConnection.Id,
            command.Id,
            approvalId,
            command.HttpMethod,
            command.Path,
            command.PayloadHash);

        var providerAcceptedRequest = false;
        try
        {
            await _exportTracker.MarkExecutionStartedAsync(companyId, writeRequestId, cancellationToken);
            JsonNode? response = command.HttpMethod switch
            {
                "POST" => await _fortnoxApiClient.PostAsync<JsonNode?, JsonNode?>(context, command.Path, payload, cancellationToken),
                "PUT" => await _fortnoxApiClient.PutAsync<JsonNode?, JsonNode?>(context, command.Path, payload, cancellationToken),
                "DELETE" => await ExecuteDeleteAsync(context, command.Path, cancellationToken),
                _ => throw new FortnoxApiException("This Fortnox action type is not supported for execution.", null, "unsupported_action")
            };
            providerAcceptedRequest = true;

            await _writeApprovalService.RecordExecutionSucceededAsync(approvalCheck, response, cancellationToken);
            var commandAfterSuccess = await ReloadAsync(companyId, writeRequestId, cancellationToken);
            await _exportTracker.MarkExecutionSucceededAsync(
                companyId,
                writeRequestId,
                commandAfterSuccess.ExternalId,
                commandAfterSuccess.SafeResponseSummary ?? "The provider accepted the committed journal export.",
                cancellationToken);
            await WriteAuditAsync(command, "write_execution_succeeded", FinanceIntegrationAuditOutcomes.Succeeded, "Fortnox accepted the approved accounting-system action.", cancellationToken);
            var refreshed = await ReloadAsync(companyId, writeRequestId, cancellationToken);
            await _billCompletionService.CompleteAsync(refreshed, cancellationToken);
            _logger.LogInformation(
                "Fortnox outbound action succeeded. CompanyId: {CompanyId}. WriteRequestId: {WriteRequestId}. ApprovalId: {ApprovalId}. ExternalId: {ExternalId}.",
                companyId,
                writeRequestId,
                approvalId,
                refreshed.ExternalId);
            return ToResult(refreshed, "Fortnox accepted the approved accounting-system action.", executed: true);
        }
        catch (Exception exception) when (exception is FortnoxApiException or HttpRequestException or TaskCanceledException)
        {
            var safeSummary = exception is FortnoxApiException apiException
                ? apiException.SafeMessage
                : "Fortnox could not complete the approved accounting-system action.";
            _logger.LogWarning(
                exception,
                "Fortnox outbound action failed. CompanyId: {CompanyId}. WriteRequestId: {WriteRequestId}. ApprovalId: {ApprovalId}. Method: {Method}. Path: {Path}. SafeSummary: {SafeSummary}.",
                companyId,
                writeRequestId,
                approvalId,
                command.HttpMethod,
                command.Path,
                safeSummary);
            await _writeApprovalService.RecordExecutionFailedAsync(approvalCheck, exception, cancellationToken);
            await _exportTracker.MarkExecutionFailedAsync(
                companyId, writeRequestId, exception, providerAcceptedRequest, cancellationToken);
            await WriteAuditAsync(command, "write_execution_failed", FinanceIntegrationAuditOutcomes.Failed, safeSummary, cancellationToken);
            var refreshed = await ReloadAsync(companyId, writeRequestId, cancellationToken);
            return ToResult(refreshed, safeSummary, executed: false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            const string safeSummary = "Fortnox could not complete the approved accounting-system action. Review the connection and try again.";
            _logger.LogError(
                exception,
                "Fortnox outbound action failed unexpectedly. CompanyId: {CompanyId}. WriteRequestId: {WriteRequestId}. ApprovalId: {ApprovalId}. Method: {Method}. Path: {Path}.",
                companyId,
                writeRequestId,
                approvalId,
                command.HttpMethod,
                command.Path);
            await _writeApprovalService.RecordExecutionFailedAsync(approvalCheck, exception, cancellationToken);
            await _exportTracker.MarkExecutionFailedAsync(
                companyId, writeRequestId, exception, providerAcceptedRequest, cancellationToken);
            await WriteAuditAsync(command, "write_execution_failed", FinanceIntegrationAuditOutcomes.Failed, safeSummary, cancellationToken);
            var refreshed = await ReloadAsync(companyId, writeRequestId, cancellationToken);
            return ToResult(refreshed, safeSummary, executed: false);
        }
    }

    private static bool IsProviderAuthoritativeAccountingAction(string commandType) =>
        FinanceIntegrationWriteCommandTypes.Normalize(commandType) is
            FinanceIntegrationWriteCommandTypes.InvoiceExport or
            FinanceIntegrationWriteCommandTypes.Payment or
            FinanceIntegrationWriteCommandTypes.VoucherCreate or
            FinanceIntegrationWriteCommandTypes.AccountingRecord;

    private async Task<JsonNode?> ExecuteDeleteAsync(FortnoxRequestContext context, string path, CancellationToken cancellationToken)
    {
        await _fortnoxApiClient.DeleteAsync(context, path, cancellationToken);
        return null;
    }

    private async Task<FinanceIntegrationWriteCommandRecord> ReloadAsync(Guid companyId, Guid writeRequestId, CancellationToken cancellationToken) =>
        await _dbContext.FinanceIntegrationWriteCommands
            .AsNoTracking()
            .SingleAsync(x => x.CompanyId == companyId && x.Id == writeRequestId, cancellationToken);

    private async Task WriteAuditAsync(
        FinanceIntegrationWriteCommandRecord command,
        string eventType,
        string outcome,
        string summary,
        CancellationToken cancellationToken)
    {
        var alreadyRecorded = await _dbContext.FinanceIntegrationAuditEvents
            .AsNoTracking()
            .AnyAsync(x =>
                x.CompanyId == command.CompanyId &&
                x.ProviderKey == FinanceIntegrationProviderKeys.Fortnox &&
                x.EventType == eventType &&
                x.InternalRecordId == command.Id &&
                x.CorrelationId == (command.ApprovalId.HasValue ? command.ApprovalId.Value.ToString("N") : command.Id.ToString("N")),
                cancellationToken);

        if (alreadyRecorded)
        {
            return;
        }

        _dbContext.FinanceIntegrationAuditEvents.Add(new FinanceIntegrationAuditEvent(
            Guid.NewGuid(),
            command.CompanyId,
            command.ConnectionId,
            FinanceIntegrationProviderKeys.Fortnox,
            eventType,
            outcome,
            command.CommandType,
            command.Id,
            command.ExternalId,
            command.ApprovalId?.ToString("N"),
            summary,
            _timeProvider.GetUtcNow().UtcDateTime,
            errorCount: outcome == FinanceIntegrationAuditOutcomes.Failed ? 1 : 0));
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static JsonNode? ParsePayload(string sanitizedPayloadJson) =>
        string.IsNullOrWhiteSpace(sanitizedPayloadJson)
            ? new JsonObject()
            : JsonNode.Parse(sanitizedPayloadJson) ?? new JsonObject();

    private static FinanceIntegrationOutboundExecutionResult ToResult(
        FinanceIntegrationWriteCommandRecord command,
        string summary,
        bool executed) =>
        new(
            FinanceIntegrationProviderKeys.Fortnox,
            command.Id,
            command.ApprovalId,
            command.Status,
            command.ResponseStatusCode,
            command.SafeFailureSummary ?? command.SafeResponseSummary ?? summary,
            executed);
}
