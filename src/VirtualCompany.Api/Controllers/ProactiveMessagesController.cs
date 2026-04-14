using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.ProactiveMessaging;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/companies/{companyId:guid}/proactive-messages")]
[Authorize(Policy = CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
public sealed class ProactiveMessagesController : ControllerBase
{
    private readonly IProactiveMessageService _proactiveMessages;

    public ProactiveMessagesController(IProactiveMessageService proactiveMessages)
    {
        _proactiveMessages = proactiveMessages;
    }

    [HttpPost("deliveries")]
    public async Task<ActionResult<ProactiveMessageDeliveryResultDto>> GenerateAndDeliverAsync(
        Guid companyId,
        [FromBody] GenerateProactiveMessageCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _proactiveMessages.GenerateAndDeliverAsync(companyId, command, cancellationToken);
            if (result.Delivered && result.Message is not null)
            {
                return CreatedAtAction(nameof(ListAsync), new { companyId }, result);
            }

            return Ok(result);
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

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProactiveMessageDto>>> ListAsync(
        Guid companyId,
        [FromQuery] ListProactiveMessagesQuery query,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _proactiveMessages.ListAsync(companyId, query, cancellationToken));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }
}
