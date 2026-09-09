using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/integrations/microsoft-graph/meeting-transcripts")]
public sealed class MicrosoftGraphMeetingTranscriptWebhookController(
    IMicrosoftGraphTranscriptWebhookService webhooks,
    IOptions<SalesMeetingTranscriptOptions> options) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> ReceiveAsync([FromQuery] string? validationToken,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(validationToken))
        {
            if (validationToken.Length > 4096 || validationToken.Any(char.IsControl)) return BadRequest();
            return Content(validationToken, "text/plain", Encoding.UTF8);
        }
        var payload = await ReadBoundedAsync(cancellationToken);
        if (payload is null) return StatusCode(StatusCodes.Status413PayloadTooLarge);
        await webhooks.ReceiveAsync(payload, HttpContext.TraceIdentifier, cancellationToken);
        return Accepted();
    }

    [HttpPost("lifecycle")]
    public async Task<IActionResult> ReceiveLifecycleAsync([FromQuery] string? validationToken,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(validationToken))
        {
            if (validationToken.Length > 4096 || validationToken.Any(char.IsControl)) return BadRequest();
            return Content(validationToken, "text/plain", Encoding.UTF8);
        }
        var payload = await ReadBoundedAsync(cancellationToken);
        if (payload is null) return StatusCode(StatusCodes.Status413PayloadTooLarge);
        await webhooks.ReceiveLifecycleAsync(payload, cancellationToken);
        return Accepted();
    }

    private async Task<string?> ReadBoundedAsync(CancellationToken cancellationToken)
    {
        var maximum = Math.Clamp(options.Value.MaximumWebhookBytes, 1024, 1_000_000);
        if (Request.ContentLength > maximum) return null;
        var buffer = new byte[maximum + 1];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await Request.Body.ReadAsync(buffer.AsMemory(total, buffer.Length - total), cancellationToken);
            if (read == 0) break;
            total += read;
        }
        if (total > maximum) return null;
        try { return new UTF8Encoding(false, true).GetString(buffer, 0, total); }
        catch (DecoderFallbackException) { return null; }
    }
}
