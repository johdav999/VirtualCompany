using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("webhooks/finance/payment-initiation")]
public sealed class PaymentProviderWebhooksController(IPaymentBatchExecutionService service) : ControllerBase
{
    private const int MaximumPayloadBytes = 64 * 1024;

    [HttpPost("{providerKey}")]
    public async Task<IActionResult> ReceiveAsync(string providerKey, CancellationToken cancellationToken)
    {
        if (Request.ContentLength is > MaximumPayloadBytes)
            return StatusCode(StatusCodes.Status413PayloadTooLarge);
        var authorization = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization)) return Unauthorized();
        try
        {
            await using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            while (true)
            {
                var read = await Request.Body.ReadAsync(chunk, cancellationToken);
                if (read == 0) break;
                if (buffer.Length + read > MaximumPayloadBytes)
                    return StatusCode(StatusCodes.Status413PayloadTooLarge);
                await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
            }
            await service.IngestWebhookAsync(new(providerKey.Trim().ToLowerInvariant(), authorization,
                buffer.ToArray(), HttpContext.TraceIdentifier), cancellationToken);
            return NoContent();
        }
        catch (PaymentProviderOperationException exception)
        {
            var problem = CreateProblem("Payment webhook rejected", exception.SafeMessage,
                StatusCodes.Status401Unauthorized);
            problem.Extensions["reasonCode"] = exception.ReasonCode;
            return Unauthorized(problem);
        }
        catch (PaymentExecutionException exception)
        {
            var problem = CreateProblem("Payment webhook conflicted with retained evidence",
                exception.Message, StatusCodes.Status409Conflict);
            problem.Extensions["reasonCode"] = exception.ReasonCode;
            return Conflict(problem);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    private ProblemDetails CreateProblem(string title, string detail, int status) => new()
    {
        Title = title,
        Detail = detail,
        Status = status,
        Instance = HttpContext.Request.Path
    };
}
