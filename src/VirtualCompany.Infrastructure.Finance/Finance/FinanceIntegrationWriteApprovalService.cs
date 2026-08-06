using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class FinanceIntegrationWriteApprovalService : IFinanceIntegrationWriteApprovalService, IFinanceIntegrationWriteCommandService
{
    private const string ApprovalType = "finance_integration_write";
    private readonly IApprovalRequestService _approvalRequestService;
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly IFortnoxIntegrationDiagnostics? _diagnostics;
    private readonly ILogger<FinanceIntegrationWriteApprovalService>? _logger;

    public FinanceIntegrationWriteApprovalService(
        IApprovalRequestService approvalRequestService,
        VirtualCompanyDbContext dbContext,
        TimeProvider timeProvider,
        IFortnoxIntegrationDiagnostics? diagnostics = null,
        ILogger<FinanceIntegrationWriteApprovalService>? logger = null)
    {
        _approvalRequestService = approvalRequestService;
        _dbContext = dbContext;
        _timeProvider = timeProvider;
        _diagnostics = diagnostics;
        _logger = logger;
    }

    public string ProviderKey => FinanceIntegrationProviderKeys.Fortnox;

    public async Task EnsureApprovedAsync(FinanceIntegrationWriteApprovalCheck check, CancellationToken cancellationToken)
    {
        var request = ToRequest(check);
        var result = check.ApprovedApprovalId.HasValue
            ? await EnsureApprovedForExecutionAsync(request, cancellationToken)
            : await RequestApprovalAsync(request, cancellationToken);

        if (!result.CanExecute)
        {
            throw new FortnoxApprovalRequiredException(
                result.ApprovalId ?? check.WriteRequestId,
                result.Message);
        }
    }

    public async Task<FinanceIntegrationWriteResult> RequestApprovalAsync(
        FinanceIntegrationWriteCommand request,
        CancellationToken cancellationToken)
    {
        var commandType = FinanceIntegrationWriteCommandTypes.Normalize(request.CommandType);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var existing = await _dbContext.FinanceIntegrationWriteCommands
            .SingleOrDefaultAsync(x => x.CompanyId == request.CompanyId && x.Id == request.WriteRequestId, cancellationToken);

        if (existing?.Status == FinanceIntegrationWriteCommandRecordStatuses.Executed &&
            string.Equals(existing.PayloadHash, request.PayloadHash, StringComparison.OrdinalIgnoreCase))
        {
            return new FinanceIntegrationWriteResult(request.ProviderKey, existing.Id, existing.ApprovalId, existing.Status, "This accounting-system action has already completed.", false);
        }

        if (existing?.Status == FinanceIntegrationWriteCommandRecordStatuses.Executed)
        {
            existing.ReplaceOutdatedExecutedRequest(
                request.ConnectionId,
                request.ActorUserId,
                request.HttpMethod,
                request.Path,
                await ResolveTargetCompanyAsync(request.CompanyId, cancellationToken),
                request.PayloadSummary,
                request.PayloadHash,
                request.Payload.SanitizedJson,
                request.CorrelationId,
                now);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        if (existing is { Status: FinanceIntegrationWriteCommandRecordStatuses.Failed, RetrySupported: false })
        {
            existing.ReplaceUnexecutedRequest(
                request.ConnectionId,
                request.ActorUserId,
                request.HttpMethod,
                request.Path,
                await ResolveTargetCompanyAsync(request.CompanyId, cancellationToken),
                request.PayloadSummary,
                request.PayloadHash,
                request.Payload.SanitizedJson,
                request.CorrelationId,
                now);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        if (existing is not null &&
            CanReplaceExistingRequest(existing.Status) &&
            !string.Equals(existing.PayloadHash, request.PayloadHash, StringComparison.OrdinalIgnoreCase))
        {
            existing.ReplaceUnexecutedRequest(
                request.ConnectionId,
                request.ActorUserId,
                request.HttpMethod,
                request.Path,
                await ResolveTargetCompanyAsync(request.CompanyId, cancellationToken),
                request.PayloadSummary,
                request.PayloadHash,
                request.Payload.SanitizedJson,
                request.CorrelationId,
                now);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        if (existing?.ApprovalId is Guid existingApprovalId)
        {
            return new FinanceIntegrationWriteResult(request.ProviderKey, existing.Id, existingApprovalId, existing.Status, "Approve this action before data is sent to the accounting system.", false);
        }

        var command = existing ?? new FinanceIntegrationWriteCommandRecord(
            request.WriteRequestId,
            request.CompanyId,
            request.ConnectionId,
            request.ActorUserId,
            commandType,
            request.HttpMethod,
            request.Path,
            await ResolveTargetCompanyAsync(request.CompanyId, cancellationToken),
            request.PayloadSummary,
            request.PayloadHash,
            request.Payload.SanitizedJson,
            request.CorrelationId,
            now);

        if (existing is null)
        {
            _dbContext.FinanceIntegrationWriteCommands.Add(command);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        var approval = await CreateApprovalAsync(request, command, cancellationToken);
        command.AttachApproval(approval.Id, now);
        await _dbContext.SaveChangesAsync(cancellationToken);
        if (request.ConnectionId is Guid connectionId)
        {
            _diagnostics?.ApprovalCreated(request.CompanyId, connectionId, approval.Id, commandType, request.PayloadHash);
        }
        return new FinanceIntegrationWriteResult(request.ProviderKey, command.Id, approval.Id, command.Status, "Approve this action before data is sent to the accounting system.", false);
    }

    private static bool CanReplaceExistingRequest(string status) =>
        status is FinanceIntegrationWriteCommandRecordStatuses.AwaitingApproval or
            FinanceIntegrationWriteCommandRecordStatuses.Approved or
            FinanceIntegrationWriteCommandRecordStatuses.Failed;

    public async Task<FinanceIntegrationWriteResult> EnsureApprovedForExecutionAsync(
        FinanceIntegrationWriteCommand request,
        CancellationToken cancellationToken)
    {
        var command = await _dbContext.FinanceIntegrationWriteCommands
            .SingleOrDefaultAsync(x => x.CompanyId == request.CompanyId && x.Id == request.WriteRequestId, cancellationToken)
            ?? throw new FortnoxApprovalRequiredException(request.WriteRequestId, "No pending accounting-system approval was found.");

        if (command.Status == FinanceIntegrationWriteCommandRecordStatuses.Executed)
        {
            return new FinanceIntegrationWriteResult(request.ProviderKey, command.Id, command.ApprovalId, command.Status, "This accounting-system action has already completed.", false);
        }

        if (command.Status == FinanceIntegrationWriteCommandRecordStatuses.Failed &&
            !command.RetrySupported &&
            IsRecoverablePreflightAuthorizationFailure(command))
        {
            var previousFailureCategory = command.FailureCategory;
            var previousFailedUtc = command.FailedUtc;
            _logger?.LogInformation(
                "Reopening approved finance integration write after a recoverable authorization preflight failure. CompanyId: {CompanyId}. WriteRequestId: {WriteRequestId}. ApprovalId: {ApprovalId}. ConnectionId: {ConnectionId}. PreviousFailureCategory: {PreviousFailureCategory}. PreviousFailedUtc: {PreviousFailedUtc}. ExecutionAttemptCount: {ExecutionAttemptCount}.",
                command.CompanyId,
                command.Id,
                command.ApprovalId,
                request.ConnectionId,
                previousFailureCategory,
                previousFailedUtc,
                command.ExecutionAttemptCount);
            var recoveryUtc = _timeProvider.GetUtcNow().UtcDateTime;
            command.PrepareApprovedRetryAfterPreflightFailure(
                request.ConnectionId,
                recoveryUtc);
            var recoveryAudit = new FinanceIntegrationAuditEvent(
                Guid.NewGuid(),
                command.CompanyId,
                command.ConnectionId,
                request.ProviderKey,
                "write_retry_after_reconnect",
                FinanceIntegrationAuditOutcomes.Succeeded,
                command.CommandType,
                command.Id,
                null,
                command.ApprovalId?.ToString("N"),
                "The approved accounting-system action was reopened after the connection was restored.",
                recoveryUtc);
            recoveryAudit.Metadata["previousFailureCategory"] = previousFailureCategory;
            recoveryAudit.Metadata["previousFailedUtc"] = previousFailedUtc?.ToString("O");
            recoveryAudit.Metadata["executionAttemptCount"] = command.ExecutionAttemptCount;
            _dbContext.FinanceIntegrationAuditEvents.Add(recoveryAudit);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        if (command.Status == FinanceIntegrationWriteCommandRecordStatuses.Failed && !command.RetrySupported)
        {
            _logger?.LogWarning(
                "Finance integration write retry blocked because the previous failure is not safely retryable. CompanyId: {CompanyId}. WriteRequestId: {WriteRequestId}. ApprovalId: {ApprovalId}. FailureCategory: {FailureCategory}. ResponseStatusCode: {ResponseStatusCode}. ExecutionAttemptCount: {ExecutionAttemptCount}.",
                command.CompanyId,
                command.Id,
                command.ApprovalId,
                command.FailureCategory,
                command.ResponseStatusCode,
                command.ExecutionAttemptCount);
            throw new FortnoxApiException(
                command.SafeFailureSummary ?? "This accounting-system action failed and cannot be retried automatically.",
                null,
                "not_retryable");
        }
        if (request.ApprovedApprovalId is not Guid approvalId || command.ApprovalId != approvalId)
        {
            throw new FortnoxApprovalRequiredException(command.ApprovalId ?? command.Id, "Approve this action before execution.");
        }

        var approval = await _approvalRequestService.GetAsync(request.CompanyId, approvalId, cancellationToken);
        if (!string.Equals(approval.Status, "approved", StringComparison.OrdinalIgnoreCase))
        {
            throw new FortnoxApprovalRequiredException(approval.Id, "This accounting-system action has not been approved.");
        }

        var approver = approval.Steps.FirstOrDefault(x => x.DecidedByUserId.HasValue)?.DecidedByUserId;
        command.MarkApproved(approval.Id, approver, _timeProvider.GetUtcNow().UtcDateTime);
        command.MarkExecutionStarted(_timeProvider.GetUtcNow().UtcDateTime);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger?.LogDebug(
            "Finance integration write passed approval recheck and entered execution. CompanyId: {CompanyId}. WriteRequestId: {WriteRequestId}. ApprovalId: {ApprovalId}. ConnectionId: {ConnectionId}. ExecutionAttemptCount: {ExecutionAttemptCount}.",
            command.CompanyId,
            command.Id,
            approvalId,
            command.ConnectionId,
            command.ExecutionAttemptCount);
        return new FinanceIntegrationWriteResult(request.ProviderKey, command.Id, approval.Id, command.Status, "Accounting-system action is approved for execution.", true);
    }

    public async Task RecordExecutionSucceededAsync(
        FinanceIntegrationWriteApprovalCheck check,
        object? responsePayload,
        CancellationToken cancellationToken)
    {
        var command = await _dbContext.FinanceIntegrationWriteCommands
            .SingleOrDefaultAsync(x => x.CompanyId == check.CompanyId && x.Id == check.WriteRequestId, cancellationToken);
        if (command is null || command.Status == FinanceIntegrationWriteCommandRecordStatuses.Executed)
        {
            return;
        }

        var externalId = TryReadExternalId(responsePayload);
        command.MarkExecuted(externalId, 200, CreateSafeResponseSummary(responsePayload), _timeProvider.GetUtcNow().UtcDateTime);
        if (command.ApprovalId is not Guid approvalId)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var alreadyRecorded = await _dbContext.FinanceIntegrationAuditEvents
            .AsNoTracking()
            .AnyAsync(x =>
                x.CompanyId == check.CompanyId &&
                x.ProviderKey == check.ProviderKey &&
                x.EventType == "approved_write" &&
                x.CorrelationId == approvalId.ToString("N"),
                cancellationToken);

        if (alreadyRecorded)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var audit = new FinanceIntegrationAuditEvent(
            Guid.NewGuid(),
            check.CompanyId,
            check.ConnectionId,
            check.ProviderKey,
            "approved_write",
            FinanceIntegrationAuditOutcomes.Succeeded,
            check.CommandType,
            check.WriteRequestId,
            externalId,
            approvalId.ToString("N"),
            $"{check.HttpMethod} {check.CommandType} sent to accounting system after approval.",
            now);

        audit.Metadata["approver"] = command.ApprovedByUserId?.ToString("D") ?? "approved";
        audit.Metadata["direction"] = "outbound";
        audit.Metadata["payloadHash"] = check.PayloadHash;
        audit.Metadata["payloadSummary"] = check.PayloadSummary;
        _dbContext.FinanceIntegrationAuditEvents.Add(audit);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordExecutionFailedAsync(
        FinanceIntegrationWriteApprovalCheck check,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var command = await _dbContext.FinanceIntegrationWriteCommands
            .SingleOrDefaultAsync(x => x.CompanyId == check.CompanyId && x.Id == check.WriteRequestId, cancellationToken);
        if (command is null || command.Status == FinanceIntegrationWriteCommandRecordStatuses.Executed)
        {
            return;
        }

        var safeMessage = exception is FortnoxApiException apiException
            ? apiException.SafeMessage
            : "Accounting-system action could not be completed safely.";
        var category = exception is FortnoxApiException fortnoxException
            ? fortnoxException.Category
            : exception.GetType().Name;

        _logger?.LogWarning(
            "Recording finance integration write failure. CompanyId: {CompanyId}. WriteRequestId: {WriteRequestId}. ApprovalId: {ApprovalId}. ConnectionId: {ConnectionId}. FailureCategory: {FailureCategory}. ResponseStatusCode: {ResponseStatusCode}. RequiresReconnect: {RequiresReconnect}. IsTransient: {IsTransient}. ExecutionAttemptCount: {ExecutionAttemptCount}.",
            check.CompanyId,
            check.WriteRequestId,
            command.ApprovalId,
            check.ConnectionId,
            category,
            (exception as FortnoxApiException)?.StatusCode,
            (exception as FortnoxApiException)?.RequiresReconnect ?? false,
            (exception as FortnoxApiException)?.IsTransient ?? false,
            command.ExecutionAttemptCount);
        command.MarkFailed(category, safeMessage, (exception as FortnoxApiException)?.StatusCode is { } statusCode ? (int)statusCode : null, _timeProvider.GetUtcNow().UtcDateTime);
        _dbContext.FinanceIntegrationAuditEvents.Add(new FinanceIntegrationAuditEvent(
            Guid.NewGuid(),
            check.CompanyId,
            check.ConnectionId,
            check.ProviderKey,
            "approved_write",
            FinanceIntegrationAuditOutcomes.Failed,
            check.CommandType,
            check.WriteRequestId,
            null,
            command.ApprovalId?.ToString("N"),
            safeMessage,
            _timeProvider.GetUtcNow().UtcDateTime,
            errorCount: 1));
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static bool IsRecoverablePreflightAuthorizationFailure(FinanceIntegrationWriteCommandRecord command)
    {
        if (command.ResponseStatusCode.HasValue ||
            !string.IsNullOrWhiteSpace(command.ExternalId) ||
            !command.ApprovalId.HasValue)
        {
            return false;
        }

        if (string.Equals(command.FailureCategory, "authorization", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(command.FailureCategory, "not_retryable", StringComparison.OrdinalIgnoreCase) &&
               command.SafeFailureSummary?.Contains("reconnect", StringComparison.OrdinalIgnoreCase) == true;
    }

    public Task RecordExecutionSucceededAsync(FinanceIntegrationWriteCommand request, object? responsePayload, CancellationToken cancellationToken) =>
        RecordExecutionSucceededAsync(ToCheck(request), responsePayload, cancellationToken);

    public Task RecordExecutionFailedAsync(FinanceIntegrationWriteCommand request, Exception exception, CancellationToken cancellationToken) =>
        RecordExecutionFailedAsync(ToCheck(request), exception, cancellationToken);

    private async Task<ApprovalRequestDto> CreateApprovalAsync(
        FinanceIntegrationWriteCommand request,
        FinanceIntegrationWriteCommandRecord command,
        CancellationToken cancellationToken)
    {
        var context = new Dictionary<string, JsonNode?>
        {
            ["provider"] = request.ProviderKey,
            ["targetCompany"] = command.TargetCompany,
            ["direction"] = "outbound",
            ["commandType"] = command.CommandType,
            ["httpMethod"] = request.HttpMethod,
            ["path"] = request.Path,
            ["payloadSummary"] = request.PayloadSummary,
            ["payloadHash"] = request.PayloadHash
        };

        return await _approvalRequestService.CreateAsync(
            request.CompanyId,
            new CreateApprovalRequestCommand(
                ApprovalTargetEntityType.FinanceIntegrationWrite.ToStorageValue(),
                command.Id,
                "human",
                ResolveRequester(request),
                ApprovalType,
                context,
                RequiredRole: "finance_approver"),
            cancellationToken);
    }

    private async Task<string> ResolveTargetCompanyAsync(Guid companyId, CancellationToken cancellationToken) =>
        await _dbContext.Companies
            .AsNoTracking()
            .Where(x => x.Id == companyId)
            .Select(x => x.Name)
            .SingleOrDefaultAsync(cancellationToken) ?? "Current company";

    private static Guid ResolveRequester(FinanceIntegrationWriteCommand request) =>
        request.ActorUserId is { } actorUserId && actorUserId != Guid.Empty
            ? actorUserId
            : request.CompanyId;

    private static FinanceIntegrationWriteCommand ToRequest(FinanceIntegrationWriteApprovalCheck check) =>
        new(
            check.ProviderKey,
            check.CompanyId,
            check.ConnectionId,
            check.ActorUserId,
            check.CommandType,
            check.HttpMethod,
            check.Path,
            check.TargetCompany,
            check.PayloadSummary,
            check.PayloadHash,
            check.Payload,
            check.WriteRequestId,
            null,
            check.ApprovedApprovalId);

    private static FinanceIntegrationWriteApprovalCheck ToCheck(FinanceIntegrationWriteCommand request) =>
        new(
            request.ProviderKey,
            request.CompanyId,
            request.ConnectionId,
            request.ActorUserId,
            request.ApprovedApprovalId,
            request.CommandType,
            request.HttpMethod,
            request.Path,
            request.TargetCompany,
            request.PayloadSummary,
            request.PayloadHash,
            request.Payload,
            request.WriteRequestId);

    private static string? TryReadExternalId(object? responsePayload)
    {
        if (responsePayload is null)
        {
            return null;
        }

        var json = System.Text.Json.JsonSerializer.SerializeToNode(responsePayload, FortnoxJson.Options);
        if (json is not JsonObject obj)
        {
            return null;
        }

        return obj.SelectMany(x => x.Value is JsonObject nested ? nested : obj)
            .FirstOrDefault(x => x.Key.Contains("Number", StringComparison.OrdinalIgnoreCase) || x.Key.Equals("id", StringComparison.OrdinalIgnoreCase))
            .Value?.ToString();
    }

    private static string? CreateSafeResponseSummary(object? responsePayload)
    {
        if (responsePayload is null)
        {
            return "Fortnox accepted the request.";
        }

        var text = System.Text.Json.JsonSerializer.Serialize(responsePayload, FortnoxJson.Options);
        return text.Length <= 1000 ? text : string.Concat(text.AsSpan(0, 997), "...");
    }
}

