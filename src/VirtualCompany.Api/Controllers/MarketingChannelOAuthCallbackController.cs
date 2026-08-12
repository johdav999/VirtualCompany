using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Marketing;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/marketing/channel-oauth")]
public sealed class MarketingChannelOAuthCallbackController(IMarketingChannelConnectionService connections) : ControllerBase
{
    [HttpGet("callback")]
    public async Task<ActionResult<MarketingChannelOAuthCompletionDto>> CallbackAsync(
        [FromQuery] string? state,[FromQuery] string? code,[FromQuery] string? error,
        [FromQuery(Name="error_description")] string? errorDescription,CancellationToken ct)
    {
        if(!string.IsNullOrWhiteSpace(error))
            return Problem(statusCode:400,title:"Marketing channel authorization was declined.",
                detail:"The provider did not authorize the connection. Return to Marketing settings and try again.");
        if(string.IsNullOrWhiteSpace(state)||string.IsNullOrWhiteSpace(code))
            return Problem(statusCode:400,title:"Marketing channel authorization failed.",detail:"Authorization state and code are required.");
        try{return Ok(await connections.CompleteOAuthAsync(new(state,code),ct));}
        catch(InvalidOperationException ex){return Problem(statusCode:400,title:"Marketing channel authorization failed.",detail:ex.Message);}
    }
}
