using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class FortnoxWriteApprovalService : IFortnoxWriteApprovalService, IFortnoxWriteCommandService
{
    private const string ApprovalType = "fortnox_write";
    private readonly IApprovalRequestService _approvalRequestService;
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly IFortnoxIntegrationDiagnostics? _diagnostics;

    public FortnoxWriteApprovalService(
        IApprovalRequestService approvalRequestService,
        VirtualCompanyDbContext dbContext,
        TimeProvider timeProvider,
        IFortnoxIntegrationDiagnostics? diagnostics = null)
    {
        _approvalRequestService = approvalRequestService;
        _dbContext = dbContext;
        _timeProvider = timeProvider;
        _diagnostics = diagnostics;
    }

    public async Task EnsureApprovedAsync(FortnoxWriteApprovalCheck check, CancellationToken cancellationToken)
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

    public async Task<FortnoxWriteCommandResult> RequestApprovalAsync(
        FortnoxWriteCommandRequest request,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var existing = await _dbContext.FortnoxWriteCommands
            .SingleOrDefaultAsync(x => x.CompanyId == request.CompanyId && x.Id == request.WriteRequestId, cancellationToken);

        if (existing?.Status == FortnoxWriteCommandStatuses.Executed)
        {
            return new FortnoxWriteCommandResult(existing.Id, existing.ApprovalId, existing.Status, "This Fortnox write has already completed.", false);
        }

        if (existing?.ApprovalId is Guid existingApprovalId)
        {
            return new FortnoxWriteCommandResult(existing.Id, existingApprovalId, existing.Status, "Fortnox writes require approval before data is sent to Fortnox.", false);
        }

        var command = existing ?? new FortnoxWriteCommand(
            request.WriteRequestId,
            request.CompanyId,
            request.ConnectionId,
            request.ActorUserId,
            request.HttpMethod,
            request.Path,
            await ResolveTargetCompanyAsync(request.CompanyId, cancellationToken),
            request.EntityType,
            request.PayloadSummary,
            request.PayloadHash,
            request.SanitizedPayloadJson,
            request.CorrelationId,
            now);

        if (existing is null)
        {
            _dbContext.FortnoxWriteCommands.Add(command);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        var approval = await CreateApprovalAsync(request, command, cancellationToken);
        command.AttachApproval(approval.Id, now);
        await _dbContext.SaveChangesAsync(cancellationToken);
        if (request.ConnectionId is Guid connectionId)
        {
            _diagnostics?.ApprovalCreated(request.CompanyId, connectionId, approval.Id, request.EntityType, request.PayloadHash);
        }
        return new FortnoxWriteCommandResult(command.Id, approval.Id, command.Status, "Fortnox writes require approval before data is sent to Fortnox.", false);
    }

    public async Task<FortnoxWriteCommandResult> EnsureApprovedForExecutionAsync(
        FortnoxWriteCommandRequest request,
        CancellationToken cancellationToken)
    {
        var command = await _dbContext.FortnoxWriteCommands
            .SingleOrDefaultAsync(x => x.CompanyId == request.CompanyId && x.Id == request.WriteRequestId, cancellationToken)
            ?? throw new FortnoxApprovalRequiredException(request.WriteRequestId, "No pending Fortnox write approval was found.");

        if (command.Status == FortnoxWriteCommandStatuses.Executed)
        {
            return new FortnoxWriteCommandResult(command.Id, command.ApprovalId, command.Status, "This Fortnox write has already completed.", false);
        }

        if (request.ApprovedApprovalId is not Guid approvalId || command.ApprovalId != approvalId)
        {
            throw new FortnoxApprovalRequiredException(command.ApprovalId ?? command.Id, "Fortnox write approval is required before execution.");
        }

        var approval = await _approvalRequestService.GetAsync(request.CompanyId, approvalId, cancellationToken);
        if (!string.Equals(approval.Status, "approved", StringComparison.OrdinalIgnoreCase))
        {
            throw new FortnoxApprovalRequiredException(approval.Id, "Fortnox write approval is not approved.");
        }

        var approver = approval.Steps.FirstOrDefault(x => x.DecidedByUserId.HasValue)?.DecidedByUserId;
        command.MarkApproved(approval.Id, approver, _timeProvider.GetUtcNow().UtcDateTime);
        command.MarkExecutionStarted(_timeProvider.GetUtcNow().UtcDateTime);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return new FortnoxWriteCommandResult(command.Id, approval.Id, command.Status, "Fortnox write is approved for execution.", true);
    }

    public async Task RecordExecutionSucceededAsync(
        FortnoxWriteApprovalCheck check,
        object? responsePayload,
        CancellationToken cancellationToken)
    {
        var command = await _dbContext.FortnoxWriteCommands
            .SingleOrDefaultAsync(x => x.CompanyId == check.CompanyId && x.Id == check.WriteRequestId, cancellationToken);
        if (command is null || command.Status == FortnoxWriteCommandStatuses.Executed)
        {
            return;
        }

        var externalId = TryReadExternalId(responsePayload);
        command.MarkExecuted(externalId, _timeProvider.GetUtcNow().UtcDateTime);

        var alreadyRecorded = await _dbContext.FinanceIntegrationAuditEvents
            .AsNoTracking()
            .AnyAsync(x =>
                x.CompanyId == check.CompanyId &&
                x.ProviderKey == FinanceIntegrationProviderKeys.Fortnox &&
                x.EventType == "approved_write" &&
                x.CorrelationId == command.ApprovalId!.Value.ToString("N"),
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
            FinanceIntegrationProviderKeys.Fortnox,
            "approved_write",
            FinanceIntegrationAuditOutcomes.Succeeded,
            check.EntityType,
            check.WriteRequestId,
            externalId,
            command.ApprovalId.Value.ToString("N"),
            $"{check.HttpMethod} {check.EntityType} sent to Fortnox after approval.",
            now);

        audit.Metadata["approver"] = command.ApprovedByUserId?.ToString("D") ?? "approved";
        audit.Metadata["direction"] = "outbound";
        audit.Metadata["payloadHash"] = check.PayloadHash;
        audit.Metadata["payloadSummary"] = check.PayloadSummary;
        _dbContext.FinanceIntegrationAuditEvents.Add(audit);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordExecutionFailedAsync(
        FortnoxWriteApprovalCheck check,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var command = await _dbContext.FortnoxWriteCommands
            .SingleOrDefaultAsync(x => x.CompanyId == check.CompanyId && x.Id == check.WriteRequestId, cancellationToken);
        if (command is null || command.Status == FortnoxWriteCommandStatuses.Executed)
        {
            return;
        }

        var safeMessage = exception is FortnoxApiException apiException
            ? apiException.SafeMessage
            : "Fortnox write could not be completed safely.";
        var category = exception is FortnoxApiException fortnoxException
            ? fortnoxException.Category
            : exception.GetType().Name;

        command.MarkFailed(category, safeMessage, _timeProvider.GetUtcNow().UtcDateTime);
        _dbContext.FinanceIntegrationAuditEvents.Add(new FinanceIntegrationAuditEvent(
            Guid.NewGuid(),
            check.CompanyId,
            check.ConnectionId,
            FinanceIntegrationProviderKeys.Fortnox,
            "approved_write",
            FinanceIntegrationAuditOutcomes.Failed,
            check.EntityType,
            check.WriteRequestId,
            null,
            command.ApprovalId?.ToString("N"),
            safeMessage,
            _timeProvider.GetUtcNow().UtcDateTime,
            errorCount: 1));
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task RecordExecutionSucceededAsync(FortnoxWriteCommandRequest request, object? responsePayload, CancellationToken cancellationToken) =>
        RecordExecutionSucceededAsync(ToCheck(request), responsePayload, cancellationToken);

    public Task RecordExecutionFailedAsync(FortnoxWriteCommandRequest request, Exception exception, CancellationToken cancellationToken) =>
        RecordExecutionFailedAsync(ToCheck(request), exception, cancellationToken);

    private async Task<ApprovalRequestDto> CreateApprovalAsync(
        FortnoxWriteCommandRequest request,
        FortnoxWriteCommand command,
        CancellationToken cancellationToken)
    {
        var context = new Dictionary<string, JsonNode?>
        {
            ["provider"] = "Fortnox",
            ["targetCompany"] = command.TargetCompany,
            ["direction"] = "outbound",
            ["entityType"] = request.EntityType,
            ["httpMethod"] = request.HttpMethod,
            ["path"] = request.Path,
            ["payloadSummary"] = request.PayloadSummary,
            ["payloadHash"] = request.PayloadHash
        };

        return await _approvalRequestService.CreateAsync(
            request.CompanyId,
            new CreateApprovalRequestCommand(
                "fortnox_write",
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

    private static Guid ResolveRequester(FortnoxWriteCommandRequest request) =>
        request.ActorUserId is { } actorUserId && actorUserId != Guid.Empty
            ? actorUserId
            : request.CompanyId;

    private static FortnoxWriteCommandRequest ToRequest(FortnoxWriteApprovalCheck check) =>
        new(
            check.CompanyId,
            check.ConnectionId,
            check.ActorUserId,
            check.HttpMethod,
            check.Path,
            check.TargetCompany,
            check.EntityType,
            check.PayloadSummary,
            check.PayloadHash,
            check.SanitizedPayloadJson,
            check.WriteRequestId,
            null,
            check.ApprovedApprovalId);

    private static FortnoxWriteApprovalCheck ToCheck(FortnoxWriteCommandRequest request) =>
        new(
            request.CompanyId,
            request.ConnectionId,
            request.ActorUserId,
            request.ApprovedApprovalId,
            request.HttpMethod,
            request.Path,
            request.TargetCompany,
            request.EntityType,
            request.PayloadSummary,
            request.PayloadHash,
            request.SanitizedPayloadJson,
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
}
