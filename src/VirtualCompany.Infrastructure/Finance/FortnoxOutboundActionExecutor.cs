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
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<FortnoxOutboundActionExecutor> _logger;

    public FortnoxOutboundActionExecutor(
        VirtualCompanyDbContext dbContext,
        IFortnoxApiClient fortnoxApiClient,
        TimeProvider timeProvider,
        ILogger<FortnoxOutboundActionExecutor> logger)
    {
        _dbContext = dbContext;
        _fortnoxApiClient = fortnoxApiClient;
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

        try
        {
            JsonNode? response = command.HttpMethod switch
            {
                "POST" => await _fortnoxApiClient.PostAsync<JsonNode?, JsonNode?>(context, command.Path, payload, cancellationToken),
                "PUT" => await _fortnoxApiClient.PutAsync<JsonNode?, JsonNode?>(context, command.Path, payload, cancellationToken),
                "DELETE" => await ExecuteDeleteAsync(context, command.Path, cancellationToken),
                _ => throw new FortnoxApiException("This Fortnox action type is not supported for execution.", null, "unsupported_action")
            };

            await WriteAuditAsync(command, "write_execution_succeeded", FinanceIntegrationAuditOutcomes.Succeeded, "Fortnox accepted the approved accounting-system action.", cancellationToken);
            var refreshed = await ReloadAsync(companyId, writeRequestId, cancellationToken);
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
            await WriteAuditAsync(command, "write_execution_failed", FinanceIntegrationAuditOutcomes.Failed, safeSummary, cancellationToken);
            var refreshed = await ReloadAsync(companyId, writeRequestId, cancellationToken);
            return ToResult(refreshed, safeSummary, executed: false);
        }
    }

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
