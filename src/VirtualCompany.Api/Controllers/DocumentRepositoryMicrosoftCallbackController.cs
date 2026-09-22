using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using VirtualCompany.Application.Documents;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/document-repositories/microsoft")]
[Authorize]
public sealed class DocumentRepositoryMicrosoftCallbackController(
    IDocumentRepositoryMicrosoftOnboardingService onboarding,
    IConfiguration configuration) : ControllerBase
{
    [HttpGet("callback")]
    public async Task<IActionResult> CallbackAsync(
        [FromQuery] string state, [FromQuery] string? code, [FromQuery] string? error,
        [FromQuery(Name = "error_description")] string? errorDescription, CancellationToken cancellationToken)
    {
        try
        {
            var webOrigin = configuration["Microsoft365DocumentOnboarding:WebOrigin"];
            Uri? origin = null;
            if (!string.IsNullOrWhiteSpace(webOrigin) &&
                (!Uri.TryCreate(webOrigin, UriKind.Absolute, out origin) ||
                 (origin.Scheme != Uri.UriSchemeHttps && !(origin.Scheme == Uri.UriSchemeHttp && origin.IsLoopback)) ||
                 origin.AbsolutePath != "/" || !string.IsNullOrEmpty(origin.Query) ||
                 !string.IsNullOrEmpty(origin.Fragment) || !string.IsNullOrEmpty(origin.UserInfo)))
            {
                return Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "Microsoft 365 return address is not configured correctly.");
            }
            var status = await onboarding.CompleteCallbackAsync(new(state, code, error, errorDescription), cancellationToken);
            var returnPath = QueryHelpers.AddQueryString(status.ReturnPath, "microsoft365Session", status.SessionHandle);
            return Redirect(origin is null ? returnPath : new Uri(origin, returnPath).AbsoluteUri);
        }
        catch (DocumentRepositoryOnboardingException exception)
        {
            var status = exception.Code == "provider_unavailable" ? StatusCodes.Status503ServiceUnavailable :
                exception.Code == "provider_throttled" ? StatusCodes.Status429TooManyRequests : StatusCodes.Status409Conflict;
            var problem = new ProblemDetails { Status = status, Title = "Microsoft 365 authorization failed", Detail = exception.SafeMessage, Instance = Request.Path };
            problem.Extensions["code"] = exception.Code;
            return StatusCode(status, problem);
        }
    }
}
