using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Notifications;
using VirtualCompany.Infrastructure.Tenancy;
using AppApprovals = VirtualCompany.Application.Approvals;
using AppNotifications = VirtualCompany.Application.Notifications;
using MobileDtos = VirtualCompany.Shared.Mobile;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/companies/{companyId:guid}")]
[Authorize(Policy = CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
public sealed class NotificationsController : ControllerBase
{
    private readonly INotificationInboxService _notificationInboxService;

    public NotificationsController(INotificationInboxService notificationInboxService)
    {
        _notificationInboxService = notificationInboxService;
    }

    [HttpGet("inbox")]
    [HttpGet]
    public async Task<ActionResult<AppNotifications.NotificationInboxDto>> GetInboxAsync(Guid companyId, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _notificationInboxService.GetInboxAsync(companyId, cancellationToken));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet("inbox/mobile")]
    public async Task<ActionResult<MobileDtos.MobileInboxDto>> GetMobileInboxAsync(
        Guid companyId,
        [FromQuery] int? take,
        [FromQuery] DateTime? sinceUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            var inbox = await _notificationInboxService.GetInboxAsync(companyId, cancellationToken);
            var limit = Math.Clamp(take ?? 30, 1, 50);
            var since = sinceUtc?.ToUniversalTime();
            var alerts = inbox.Notifications
                .Where(x => !since.HasValue || x.CreatedAt > since.Value)
                .Take(limit)
                .Select(x => new MobileDtos.MobileAlertListItemDto
                {
                    Id = x.Id,
                    Type = x.Type,
                    Priority = x.Priority,
                    Title = x.Title,
                    Summary = TrimForMobile(x.Body, 180),
                    Status = x.Status,
                    RelatedEntityType = x.RelatedEntityType,
                    RelatedEntityId = x.RelatedEntityId,
                    CreatedAt = x.CreatedAt,
                    ReadAt = x.ReadAt
                })
                .ToList();

            return Ok(new MobileDtos.MobileInboxDto
            {
                Alerts = alerts,
                PendingApprovals = inbox.PendingApprovals.Take(limit).Select(ToMobileApproval).ToList(),
                UnreadCount = inbox.UnreadCount,
                PendingApprovalCount = inbox.PendingApprovalCount,
                SyncedAtUtc = DateTime.UtcNow
            });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet("notifications")]
    public async Task<ActionResult<IReadOnlyList<AppNotifications.NotificationListItemDto>>> GetNotificationsAsync(
        [FromRoute] Guid companyId,
        [FromQuery] ListNotificationInboxQuery query,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _notificationInboxService.ListAsync(companyId, query, cancellationToken));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet("notifications/unread-count")]
    public async Task<ActionResult<NotificationUnreadCountDto>> GetUnreadCountAsync(
        [FromRoute] Guid companyId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _notificationInboxService.GetUnreadCountAsync(companyId, cancellationToken));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpPost("notifications")]
    public async Task<ActionResult<AppNotifications.NotificationListItemDto>> CreateNotificationAsync(
        Guid companyId,
        [FromBody] CreateNotificationCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            var notification = await _notificationInboxService.CreateAsync(companyId, command, cancellationToken);
            return CreatedAtAction(nameof(GetInboxAsync), new { companyId }, notification);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                [nameof(command)] = [ex.Message]
            })
            {
                Title = "Validation failed",
                Status = StatusCodes.Status400BadRequest
            });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet("approvals/inbox")]
    public async Task<ActionResult<IReadOnlyList<AppNotifications.ApprovalInboxItemDto>>> GetApprovalInboxAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        try
        {
            var inbox = await _notificationInboxService.GetInboxAsync(companyId, cancellationToken);
            return Ok(inbox.PendingApprovals);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet("approvals/inbox/{approvalId:guid}")]
    public async Task<ActionResult<AppApprovals.ApprovalRequestDto>> GetApprovalInboxDetailAsync(
        Guid companyId,
        Guid approvalId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _notificationInboxService.GetApprovalDetailAsync(companyId, approvalId, cancellationToken));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpPost("approvals/inbox/{approvalId:guid}/decisions")]
    public async Task<ActionResult<AppApprovals.ApprovalDecisionResultDto>> DecideApprovalInboxAsync(
        Guid companyId,
        Guid approvalId,
        [FromBody] ApprovalDecisionCommand command,
        CancellationToken cancellationToken)
    {
        if (command.ApprovalId == Guid.Empty)
        {
            command = command with { ApprovalId = approvalId };
        }

        if (command.ApprovalId != approvalId)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                [nameof(command.ApprovalId)] = ["Approval id must match the route."]
            })
            {
                Title = "Validation failed",
                Status = StatusCodes.Status400BadRequest
            });
        }

        try
        {
            return Ok(await _notificationInboxService.DecideApprovalAsync(companyId, approvalId, command, cancellationToken));
        }
        catch (ApprovalValidationException ex)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>(ex.Errors, StringComparer.OrdinalIgnoreCase))
            {
                Title = "Validation failed",
                Status = StatusCodes.Status400BadRequest
            });
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ProblemDetails { Title = "Approval decision conflict", Detail = ex.Message, Status = StatusCodes.Status409Conflict });
        }
    }

    private static MobileDtos.MobileApprovalListItemDto ToMobileApproval(AppNotifications.ApprovalInboxItemDto approval) =>
        new()
        {
            Id = approval.Id,
            ApprovalType = approval.ApprovalType,
            TargetEntityType = approval.TargetEntityType,
            TargetEntityId = approval.TargetEntityId,
            Status = approval.Status,
            RationaleSummary = TrimForMobile(approval.RationaleSummary, 220) ?? string.Empty,
            AffectedDataSummary = TrimForMobile(approval.AffectedDataSummary, 180) ?? string.Empty,
            ThresholdSummary = string.IsNullOrWhiteSpace(approval.ThresholdSummary) ? null : TrimForMobile(approval.ThresholdSummary, 160),
            CurrentStep = ToMobileApprovalStep(approval.CurrentStep),
            CreatedAt = approval.CreatedAt
        };

    private static MobileDtos.ApprovalStepDto? ToMobileApprovalStep(AppApprovals.ApprovalStepDto? step) =>
        step is null
            ? null
            : new MobileDtos.ApprovalStepDto
            {
                Id = step.Id,
                SequenceNo = step.SequenceNo,
                ApproverType = step.ApproverType,
                ApproverRef = step.ApproverRef,
                Status = step.Status,
                DecidedByUserId = step.DecidedByUserId,
                DecidedAt = step.DecidedAt,
                Comment = step.Comment
            };

    private static string? TrimForMobile(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.ReplaceLineEndings(" ").Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength].TrimEnd() + "...";
    }

    [HttpPost("notifications/{notificationId:guid}/read")]
    [HttpPatch("notifications/{notificationId:guid}/read")]
    public Task<ActionResult<AppNotifications.NotificationListItemDto>> MarkReadAsync(Guid companyId, Guid notificationId, CancellationToken cancellationToken) =>
        SetStatusAsync(companyId, notificationId, new SetNotificationStatusCommand("read"), cancellationToken);

    [HttpPost("notifications/{notificationId:guid}/unread")]
    [HttpPatch("notifications/{notificationId:guid}/unread")]
    public Task<ActionResult<AppNotifications.NotificationListItemDto>> MarkUnreadAsync(Guid companyId, Guid notificationId, CancellationToken cancellationToken) =>
        SetStatusAsync(companyId, notificationId, new SetNotificationStatusCommand("unread"), cancellationToken);

    [HttpPost("notifications/{notificationId:guid}/actioned")]
    [HttpPatch("notifications/{notificationId:guid}/actioned")]
    public Task<ActionResult<AppNotifications.NotificationListItemDto>> MarkActionedAsync(Guid companyId, Guid notificationId, CancellationToken cancellationToken) =>
        SetStatusAsync(companyId, notificationId, new SetNotificationStatusCommand("actioned"), cancellationToken);

    [HttpPatch("inbox/notifications/{notificationId:guid}/status")]
    [HttpPost("inbox/notifications/{notificationId:guid}/status")]
    [HttpPatch("notifications/{notificationId:guid}/status")]
    public async Task<ActionResult<AppNotifications.NotificationListItemDto>> SetStatusAsync(
        Guid companyId,
        Guid notificationId,
        [FromBody] SetNotificationStatusCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _notificationInboxService.SetStatusAsync(companyId, notificationId, command, cancellationToken));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or InvalidOperationException)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                [nameof(command.Status)] = [ex.Message]
            })
            {
                Title = "Validation failed",
                Status = StatusCodes.Status400BadRequest
            });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }
}
