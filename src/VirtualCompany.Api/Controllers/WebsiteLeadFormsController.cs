using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Sales;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/public/website-leads")]
[Route("api/website/forms/leads")]
[AllowAnonymous]
public sealed class WebsiteLeadFormsController : ControllerBase
{
    private readonly IWebsiteLeadCaptureService _leadCapture;

    public WebsiteLeadFormsController(IWebsiteLeadCaptureService leadCapture)
    {
        _leadCapture = leadCapture;
    }

    [HttpPost]
    public async Task<ActionResult<WebsiteLeadSubmissionResponse>> SubmitAsync([FromBody] WebsiteLeadSubmissionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Accepted(await _leadCapture.SubmitAsync(request, cancellationToken));
        }
        catch (SalesValidationException ex)
        {
            return ValidationProblem(ex.Errors);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(title: "Website lead submission could not be accepted.", detail: ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }

    private ActionResult ValidationProblem(IReadOnlyDictionary<string, string[]> errors) =>
        ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>(errors, StringComparer.OrdinalIgnoreCase))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Validation failed."
        });
}
