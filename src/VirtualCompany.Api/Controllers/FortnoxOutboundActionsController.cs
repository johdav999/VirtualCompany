using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/companies/{companyId:guid}/finance/integrations/fortnox/outbound-actions")]
[Authorize(Policy = CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
public sealed class FortnoxOutboundActionsController : ControllerBase
{
    private readonly IFortnoxOutboundActionExecutor _executor;
    private readonly IFinanceIntegrationProviderRegistry _providerRegistry;
    private readonly ICompanyContextAccessor _companyContextAccessor;

    public FortnoxOutboundActionsController(
        IFortnoxOutboundActionExecutor executor,
        IFinanceIntegrationProviderRegistry providerRegistry,
        ICompanyContextAccessor companyContextAccessor)
    {
        _executor = executor;
        _providerRegistry = providerRegistry;
        _companyContextAccessor = companyContextAccessor;
    }

    [Authorize(Policy = CompanyPolicies.CompanyAdmin)]
    [HttpPost]
    public async Task<ActionResult<FinanceIntegrationWriteResult>> RequestAsync(
        Guid companyId,
        [FromBody] FortnoxOutboundActionRequest request,
        CancellationToken cancellationToken)
    {
        var provider = _providerRegistry.GetRequired(FinanceIntegrationProviderKeys.Fortnox);
        var payload = request.Payload ?? new JsonObject();
        var sanitizedPayload = FortnoxWritePayloadSanitizer.CreateSanitizedJson(payload);
        var payloadSummary = string.IsNullOrWhiteSpace(request.PayloadSummary)
            ? FortnoxWritePayloadSanitizer.CreateSummary(payload)
            : request.PayloadSummary.Trim();
        var payloadHash = string.IsNullOrWhiteSpace(request.PayloadHash)
            ? FortnoxWritePayloadSanitizer.CreatePayloadHash(payload)
            : request.PayloadHash.Trim().ToLowerInvariant();
        var writeRequestId = request.WriteRequestId is { } id && id != Guid.Empty
            ? id
            : Guid.NewGuid();

        try
        {
            var result = await provider.WriteCommands.RequestApprovalAsync(
                new FinanceIntegrationWriteCommand(
                    provider.ProviderKey,
                    companyId,
                    request.ConnectionId,
                    ResolveUserId(),
                    FinanceIntegrationWriteCommandTypes.Normalize(request.CommandType),
                    request.HttpMethod,
                    request.Path,
                    request.TargetCompany,
                    payloadSummary,
                    payloadHash,
                    new FinanceIntegrationWritePayload(sanitizedPayload, request.ProviderPayloadType),
                    writeRequestId,
                    request.CorrelationId),
                cancellationToken);

            return Accepted(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ProblemDetails { Title = "Fortnox action request was invalid", Detail = ex.Message, Status = StatusCodes.Status400BadRequest });
        }
    }

    [HttpPost("{writeRequestId:guid}/execute")]
    public async Task<ActionResult<FinanceIntegrationOutboundExecutionResult>> ExecuteAsync(
        Guid companyId,
        Guid writeRequestId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _executor.ExecuteApprovedAsync(companyId, writeRequestId, cancellationToken));
        }
        catch (FortnoxApprovalRequiredException ex)
        {
            return StatusCode(StatusCodes.Status409Conflict, new ProblemDetails
            {
                Title = "Approval required",
                Detail = ex.SafeMessage,
                Status = StatusCodes.Status409Conflict
            });
        }
        catch (FortnoxApiException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new ProblemDetails
            {
                Title = "Fortnox action failed",
                Detail = ex.SafeMessage,
                Status = StatusCodes.Status502BadGateway
            });
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    private Guid ResolveUserId() =>
        _companyContextAccessor.UserId is { } userId && userId != Guid.Empty
            ? userId
            : throw new UnauthorizedAccessException("A resolved user is required.");

    public sealed record FortnoxOutboundActionRequest(
        string? CommandType,
        string HttpMethod,
        string Path,
        string TargetCompany,
        JsonNode? Payload,
        Guid? ConnectionId = null,
        Guid? WriteRequestId = null,
        string? PayloadSummary = null,
        string? PayloadHash = null,
        string? ProviderPayloadType = null,
        string? CorrelationId = null);
}