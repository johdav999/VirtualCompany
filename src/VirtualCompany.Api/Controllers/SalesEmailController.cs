using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Sales;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/sales/email")]
[Authorize(Policy = CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
public sealed class SalesEmailController : ControllerBase
{
    private readonly ICompanyContextAccessor _companyContextAccessor;
    private readonly ISalesEmailIngestionService _salesEmailIngestion;

    public SalesEmailController(
        ICompanyContextAccessor companyContextAccessor,
        ISalesEmailIngestionService salesEmailIngestion)
    {
        _companyContextAccessor = companyContextAccessor;
        _salesEmailIngestion = salesEmailIngestion;
    }

    [HttpPost("process-message")]
    public async Task<ActionResult<SalesEmailIngestionResult>> ProcessMessageAsync(
        [FromBody] ProcessSalesEmailMessageRequest request,
        CancellationToken cancellationToken)
    {
        var companyId = ResolveCompanyId();
        var userId = ResolveUserId();
        try
        {
            var result = await _salesEmailIngestion.ProcessMessageAsync(
                new ProcessSalesEmailMessageCommand(companyId, userId, request.MailboxConnectionId, request.ProviderMessageId),
                cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(title: "Sales email could not be processed.", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (ArgumentException ex)
        {
            return Problem(title: "Sales email request is invalid.", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [HttpPost("process-thread")]
    public async Task<ActionResult<SalesEmailIngestionResult>> ProcessThreadAsync(
        [FromBody] ProcessSalesEmailThreadRequest request,
        CancellationToken cancellationToken)
    {
        var companyId = ResolveCompanyId();
        var userId = ResolveUserId();
        try
        {
            var result = await _salesEmailIngestion.ProcessThreadAsync(
                new ProcessSalesEmailThreadCommand(companyId, userId, request.MailboxConnectionId, request.ProviderThreadId),
                cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(title: "Sales email thread could not be processed.", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (ArgumentException ex)
        {
            return Problem(title: "Sales email request is invalid.", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private Guid ResolveCompanyId() =>
        _companyContextAccessor.CompanyId is { } companyId && companyId != Guid.Empty
            ? companyId
            : throw new UnauthorizedAccessException("A resolved company is required.");

    private Guid ResolveUserId() =>
        _companyContextAccessor.UserId is { } userId && userId != Guid.Empty
            ? userId
            : throw new UnauthorizedAccessException("A resolved user is required.");

    public sealed record ProcessSalesEmailMessageRequest(
        Guid MailboxConnectionId,
        string ProviderMessageId);

    public sealed record ProcessSalesEmailThreadRequest(
        Guid MailboxConnectionId,
        string ProviderThreadId);
}