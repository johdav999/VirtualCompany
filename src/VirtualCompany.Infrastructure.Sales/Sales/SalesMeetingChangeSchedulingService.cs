using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Sales;

public sealed partial class SalesMeetingSchedulingService
{
    public async Task<IReadOnlyList<SalesMeetingChangeRequestResponse>> ListChangesAsync(
        Guid companyId, Guid invitationId, CancellationToken cancellationToken) =>
        (await _dbContext.SalesMeetingChangeRequests
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.InvitationId == invitationId)
            .OrderByDescending(x => x.CreatedUtc)
            .ToListAsync(cancellationToken))
        .Select(ToResponse)
        .ToArray();

    public Task<SalesMeetingChangeRequestResponse> RequestRescheduleAsync(
        Guid companyId, Guid userId, Guid invitationId,
        CreateSalesMeetingRescheduleRequest request, CancellationToken cancellationToken)
    {
        ValidateReschedule(request);
        return CreateChangeAsync(
            companyId, userId, invitationId, SalesMeetingChangeOperation.Reschedule,
            request, cancellationToken);
    }

    public Task<SalesMeetingChangeRequestResponse> RequestCancellationAsync(
        Guid companyId, Guid userId, Guid invitationId, CancellationToken cancellationToken) =>
        CreateChangeAsync(
            companyId, userId, invitationId, SalesMeetingChangeOperation.Cancel,
            request: null, cancellationToken);

    private async Task<SalesMeetingChangeRequestResponse> CreateChangeAsync(
        Guid companyId, Guid userId, Guid invitationId,
        SalesMeetingChangeOperation operation, CreateSalesMeetingRescheduleRequest? request,
        CancellationToken cancellationToken)
    {
        var invitation = await _dbContext.SalesMeetingInvitations
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == invitationId, cancellationToken)
            ?? throw new KeyNotFoundException("Meeting invitation not found.");
        if (invitation.Status != SalesMeetingInvitationStatus.Scheduled || string.IsNullOrWhiteSpace(invitation.ExternalEventId))
            throw Validation(nameof(invitationId), "Only a confirmed calendar meeting can be changed.");

        var hasOpenChange = await _dbContext.SalesMeetingChangeRequests.AnyAsync(
            x => x.CompanyId == companyId && x.InvitationId == invitationId &&
                (x.Status == SalesMeetingChangeRequestStatus.Draft ||
                 x.Status == SalesMeetingChangeRequestStatus.WaitingForApproval ||
                 x.Status == SalesMeetingChangeRequestStatus.Queued ||
                 x.Status == SalesMeetingChangeRequestStatus.Executing ||
                 x.Status == SalesMeetingChangeRequestStatus.Failed ||
                 x.Status == SalesMeetingChangeRequestStatus.ReconciliationRequired),
            cancellationToken);
        if (hasOpenChange)
            throw Validation(nameof(invitationId), "Resolve the current meeting change before requesting another one.");

        var change = new SalesMeetingChangeRequest(
            Guid.NewGuid(), companyId, invitationId, operation, userId,
            request?.StartsUtc, request?.EndsUtc, request?.TimeZoneId,
            request?.Title, request?.Description, request?.Location,
            request?.CreateOnlineMeeting);
        _dbContext.SalesMeetingChangeRequests.Add(change);
        await _dbContext.SaveChangesAsync(cancellationToken);

        ApprovalRequestDto approval;
        try
        {
            approval = await _approvalService.CreateAsync(
                companyId,
                new CreateApprovalRequestCommand(
                    ApprovalTargetEntityType.SalesMeetingChangeRequest.ToStorageValue(),
                    change.Id,
                    "user",
                    userId,
                    operation == SalesMeetingChangeOperation.Reschedule
                        ? SalesMeetingApprovalTypes.RescheduleInvitation
                        : SalesMeetingApprovalTypes.CancelInvitation,
                    BuildChangeApprovalContext(invitation, change),
                    RequiredRole: "owner"),
                cancellationToken);
        }
        catch
        {
            _dbContext.SalesMeetingChangeRequests.Remove(change);
            await _dbContext.SaveChangesAsync(cancellationToken);
            throw;
        }

        change.SubmitForApproval(approval.Id);
        _dbContext.SalesActivities.Add(new SalesActivity(
            Guid.NewGuid(), companyId, "meeting change",
            operation == SalesMeetingChangeOperation.Reschedule
                ? $"Reschedule requested for the meeting with {invitation.AttendeeEmail}."
                : $"Cancellation requested for the meeting with {invitation.AttendeeEmail}.",
            DateTime.UtcNow, invitation.LeadId, invitation.DealId, invitation.ContactId,
            status: SalesStatuses.Pending));
        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(change);
    }

    private static Dictionary<string, JsonNode?> BuildChangeApprovalContext(
        SalesMeetingInvitation invitation, SalesMeetingChangeRequest change) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["operation"] = JsonValue.Create(change.Operation.ToStorageValue()),
            ["organizer"] = JsonValue.Create(invitation.OrganizerEmail),
            ["attendee"] = JsonValue.Create(invitation.AttendeeEmail),
            ["currentStartsUtc"] = JsonValue.Create(invitation.StartsUtc),
            ["currentEndsUtc"] = JsonValue.Create(invitation.EndsUtc),
            ["proposedStartsUtc"] = JsonValue.Create(change.StartsUtc),
            ["proposedEndsUtc"] = JsonValue.Create(change.EndsUtc),
            ["timeZoneId"] = JsonValue.Create(change.TimeZoneId ?? invitation.TimeZoneId),
            ["provider"] = JsonValue.Create(invitation.Provider.ToStorageValue())
        };

    private static void ValidateReschedule(CreateSalesMeetingRescheduleRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (request.StartsUtc == default || request.EndsUtc <= request.StartsUtc) errors[nameof(request.EndsUtc)] = ["Choose a valid meeting time."];
        if (request.StartsUtc != default && NormalizeUtc(request.StartsUtc) <= DateTime.UtcNow.AddMinutes(5)) errors[nameof(request.StartsUtc)] = ["Choose a meeting time at least five minutes from now."];
        if (request.EndsUtc - request.StartsUtc > TimeSpan.FromHours(8)) errors[nameof(request.EndsUtc)] = ["A sales meeting cannot be longer than eight hours."];
        if (string.IsNullOrWhiteSpace(request.TimeZoneId)) errors[nameof(request.TimeZoneId)] = ["Choose a time zone."];
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 200) errors[nameof(request.Title)] = ["Enter a title of 200 characters or fewer."];
        if (string.IsNullOrWhiteSpace(request.Description) || request.Description.Trim().Length > 4000) errors[nameof(request.Description)] = ["Enter an agenda of 4,000 characters or fewer."];
        if (request.Location?.Trim().Length > 500) errors[nameof(request.Location)] = ["Location must be 500 characters or fewer."];
        if (errors.Count > 0) throw new SalesValidationException(errors);
    }

    internal static SalesMeetingChangeRequestResponse ToResponse(SalesMeetingChangeRequest x) =>
        new(
            x.Id, x.InvitationId, x.Operation.ToStorageValue(), x.Status.ToStorageValue(),
            x.StartsUtc, x.EndsUtc, x.TimeZoneId, x.Title, x.Description, x.Location,
            x.CreateOnlineMeeting, x.ApprovalRequestId, x.ExecutionAttemptCount,
            x.LastErrorCode, x.LastErrorSummary, x.CreatedUtc, x.UpdatedUtc, x.CompletedUtc);
}
