using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Chat;
using VirtualCompany.Application.Tasks;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/companies/{companyId:guid}")]
[Authorize(Policy = CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
public sealed class DirectChatController : ControllerBase
{
    private readonly ICompanyDirectChatService _directChatService;

    public DirectChatController(ICompanyDirectChatService directChatService)
    {
        _directChatService = directChatService;
    }

    [HttpPost("agents/{agentId:guid}/conversations/direct")]
    public async Task<ActionResult<DirectConversationDto>> OpenDirectConversationAsync(
        Guid companyId,
        Guid agentId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _directChatService.GetOrCreateDirectAgentConversationAsync(companyId, agentId, cancellationToken));
        }
        catch (DirectChatValidationException ex)
        {
            return ValidationProblem(ex.Errors);
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

    [HttpGet("conversations/direct")]
    public async Task<ActionResult<DirectConversationPageDto>> GetDirectConversationsAsync(
        Guid companyId,
        [FromQuery] int? skip,
        [FromQuery] int? take,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _directChatService.GetDirectConversationsAsync(companyId, new GetDirectConversationsQuery(skip, take), cancellationToken));
        }
        catch (DirectChatValidationException ex)
        {
            return ValidationProblem(ex.Errors);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet("conversations/{conversationId:guid}/messages")]
    public async Task<ActionResult<ChatMessagePageDto>> GetMessagesAsync(
        Guid companyId,
        Guid conversationId,
        [FromQuery] int? skip,
        [FromQuery] int? take,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _directChatService.GetMessagesAsync(companyId, conversationId, new GetConversationMessagesQuery(skip, take), cancellationToken));
        }
        catch (DirectChatValidationException ex)
        {
            return ValidationProblem(ex.Errors);
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

    [HttpPost("conversations/{conversationId:guid}/messages")]
    public async Task<ActionResult<SendDirectAgentMessageResultDto>> SendMessageAsync(
        Guid companyId,
        Guid conversationId,
        [FromBody] SendDirectAgentMessageCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _directChatService.SendMessageAsync(companyId, conversationId, command, cancellationToken));
        }
        catch (DirectChatValidationException ex)
        {
            return ValidationProblem(ex.Errors);
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

    [HttpPost("conversations/{conversationId:guid}/tasks")]
    public async Task<ActionResult<CreateTaskFromChatResultDto>> CreateTaskFromChatAsync(
        Guid companyId,
        Guid conversationId,
        [FromBody] CreateTaskFromChatCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _directChatService.CreateTaskFromChatAsync(companyId, conversationId, command, cancellationToken);
            return Created($"/api/companies/{companyId}/tasks/{result.TaskId}", result);
        }
        catch (DirectChatValidationException ex)
        {
            return ValidationProblem(ex.Errors);
        }
        catch (TaskValidationException ex)
        {
            return ValidationProblem(ex.Errors);
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

    [HttpPost("conversations/{conversationId:guid}/task-links")]
    public async Task<ActionResult<LinkConversationToTaskResultDto>> LinkConversationToTaskAsync(
        Guid companyId,
        Guid conversationId,
        [FromBody] LinkConversationToTaskCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _directChatService.LinkConversationToTaskAsync(companyId, conversationId, command, cancellationToken));
        }
        catch (DirectChatValidationException ex)
        {
            return ValidationProblem(ex.Errors);
        }
        catch (TaskValidationException ex)
        {
            return ValidationProblem(ex.Errors);
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

    [HttpGet("conversations/{conversationId:guid}/tasks")]
    public async Task<ActionResult<ConversationRelatedTaskListDto>> GetConversationTasksAsync(
        Guid companyId,
        Guid conversationId,
        [FromQuery] int? skip,
        [FromQuery] int? take,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _directChatService.GetRelatedTasksAsync(companyId, conversationId, new GetConversationRelatedTasksQuery(skip, take), cancellationToken));
        }
        catch (DirectChatValidationException ex)
        {
            return ValidationProblem(ex.Errors);
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

    private ActionResult ValidationProblem(IReadOnlyDictionary<string, string[]> errors) =>
        ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>(errors, StringComparer.OrdinalIgnoreCase))
        {
            Title = "Validation failed",
            Status = StatusCodes.Status400BadRequest
        });
}