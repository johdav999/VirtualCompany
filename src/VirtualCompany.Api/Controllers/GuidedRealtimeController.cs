using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.GuidedWork;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/companies/{companyId:guid}/guided-work-sessions/{sessionId:guid}/voice")]
[Authorize(Policy=CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
public sealed class GuidedRealtimeController(IGuidedRealtimeCallService calls) : ControllerBase
{
    [HttpPost("calls")]
    [Consumes("application/sdp","text/plain")]
    public async Task<IActionResult> Create(Guid companyId,Guid sessionId,CancellationToken ct)
    {
        try{using var reader=new StreamReader(Request.Body);var offer=await reader.ReadToEndAsync(ct);var result=await calls.CreateCallAsync(companyId,sessionId,offer,ct);Response.Headers["X-Guided-Voice-Binding"]=result.VoiceBindingId.ToString("N");Response.Headers["X-Guided-Voice-Expires"]=result.ExpiresAt.ToString("O");return Content(result.AnswerSdp,"application/sdp");}
        catch(GuidedWorkValidationException ex){return BadRequest(new ValidationProblemDetails(new Dictionary<string,string[]>(ex.Errors,StringComparer.OrdinalIgnoreCase)));}
        catch(GuidedRealtimeRateLimitedException ex){if(ex.RetryAfterSeconds is >0)Response.Headers["Retry-After"]=ex.RetryAfterSeconds.Value.ToString();return Problem(title:"Realtime voice temporarily limited",detail:ex.Message,statusCode:StatusCodes.Status429TooManyRequests);}
        catch(GuidedCheckpointUnavailableException ex){return Problem(title:"Realtime voice unavailable",detail:ex.Message,statusCode:StatusCodes.Status503ServiceUnavailable);}
        catch(GuidedWorkConflictException ex){return Problem(title:"Realtime voice conflict",detail:ex.Message,statusCode:StatusCodes.Status409Conflict);}
        catch(KeyNotFoundException){return NotFound();}catch(UnauthorizedAccessException){return Forbid();}
    }
    [HttpDelete("calls/{bindingId:guid}")]
    public async Task<IActionResult> End(Guid companyId,Guid sessionId,Guid bindingId,CancellationToken ct){try{await calls.EndCallAsync(companyId,sessionId,bindingId,ct);return NoContent();}catch(KeyNotFoundException){return NotFound();}catch(UnauthorizedAccessException){return Forbid();}}
}
