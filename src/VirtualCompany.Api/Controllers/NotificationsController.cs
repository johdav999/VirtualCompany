using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Notifications;
using VirtualCompany.Infrastructure.Tenancy;

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
    public async Task<ActionResult<NotificationInboxDto>> GetInboxAsync(Guid companyId, CancellationToken cancellationToken)
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

    [HttpGet("notifications")]
    public async Task<ActionResult<IReadOnlyList<NotificationListItemDto>>> GetNotificationsAsync(
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
    public async Task<ActionResult<NotificationListItemDto>> CreateNotificationAsync(
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
    public async Task<ActionResult<IReadOnlyList<ApprovalInboxItemDto>>> GetApprovalInboxAsync(
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
    public async Task<ActionResult<ApprovalRequestDto>> GetApprovalInboxDetailAsync(
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

    [HttpPost("notifications/{notificationId:guid}/read")]
    [HttpPatch("notifications/{notificationId:guid}/read")]
    public Task<ActionResult<NotificationListItemDto>> MarkReadAsync(Guid companyId, Guid notificationId, CancellationToken cancellationToken) =>
        SetStatusAsync(companyId, notificationId, new SetNotificationStatusCommand("read"), cancellationToken);

    [HttpPost("notifications/{notificationId:guid}/unread")]
    [HttpPatch("notifications/{notificationId:guid}/unread")]
    public Task<ActionResult<NotificationListItemDto>> MarkUnreadAsync(Guid companyId, Guid notificationId, CancellationToken cancellationToken) =>
        SetStatusAsync(companyId, notificationId, new SetNotificationStatusCommand("unread"), cancellationToken);

    [HttpPost("notifications/{notificationId:guid}/actioned")]
    [HttpPatch("notifications/{notificationId:guid}/actioned")]
    public Task<ActionResult<NotificationListItemDto>> MarkActionedAsync(Guid companyId, Guid notificationId, CancellationToken cancellationToken) =>
        SetStatusAsync(companyId, notificationId, new SetNotificationStatusCommand("actioned"), cancellationToken);

    [HttpPatch("inbox/notifications/{notificationId:guid}/status")]
    [HttpPost("inbox/notifications/{notificationId:guid}/status")]
    [HttpPatch("notifications/{notificationId:guid}/status")]
    public async Task<ActionResult<NotificationListItemDto>> SetStatusAsync(
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
