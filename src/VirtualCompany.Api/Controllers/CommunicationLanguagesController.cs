using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Api.ProblemHandling;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Communication;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/communication-languages")]
[Authorize(Policy = CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
public sealed class CommunicationLanguagesController(
    ICompanyContextAccessor companyContextAccessor,
    ICommunicationLanguageService languages) : ControllerBase
{
    [HttpGet("contacts/{id:guid}")]
    public async Task<ActionResult<CommunicationLanguagePreferenceDto>> GetContactAsync(Guid id, CancellationToken cancellationToken) =>
        ToResult(await languages.GetContactAsync(CompanyId(), id, cancellationToken));

    [HttpPut("contacts/{id:guid}")]
    public Task<ActionResult<CommunicationLanguagePreferenceDto>> UpdateContactAsync(Guid id, UpdateCommunicationLanguageRequest request, CancellationToken cancellationToken) =>
        UpdateAsync(() => languages.UpdateContactAsync(CompanyId(), UserId(), id, request, cancellationToken));

    [HttpGet("campaigns/{id:guid}")]
    public async Task<ActionResult<CommunicationLanguagePreferenceDto>> GetCampaignAsync(Guid id, CancellationToken cancellationToken) =>
        ToResult(await languages.GetCampaignAsync(CompanyId(), id, cancellationToken));

    [HttpPut("campaigns/{id:guid}")]
    public Task<ActionResult<CommunicationLanguagePreferenceDto>> UpdateCampaignAsync(Guid id, UpdateCommunicationLanguageRequest request, CancellationToken cancellationToken) =>
        UpdateAsync(() => languages.UpdateCampaignAsync(CompanyId(), UserId(), id, request, cancellationToken));

    [HttpGet("support-cases/{id:guid}")]
    public async Task<ActionResult<CommunicationLanguagePreferenceDto>> GetSupportCaseAsync(Guid id, CancellationToken cancellationToken) =>
        ToResult(await languages.GetSupportCaseAsync(CompanyId(), id, cancellationToken));

    [HttpPut("support-cases/{id:guid}")]
    public Task<ActionResult<CommunicationLanguagePreferenceDto>> UpdateSupportCaseAsync(Guid id, UpdateCommunicationLanguageRequest request, CancellationToken cancellationToken) =>
        UpdateAsync(() => languages.UpdateSupportCaseAsync(CompanyId(), UserId(), id, request, cancellationToken));

    private async Task<ActionResult<CommunicationLanguagePreferenceDto>> UpdateAsync(Func<Task<CommunicationLanguagePreferenceDto?>> update)
    {
        try
        {
            return ToResult(await update());
        }
        catch (CommunicationLanguageValidationException exception)
        {
            return BadRequest(StableProblemDetails.CreateValidation(
                HttpContext,
                new Dictionary<string, string[]> { ["languageTag"] = [exception.Message] },
                ApiProblemCodes.CommunicationLanguageInvalid,
                "The communication language is invalid."));
        }
    }

    private ActionResult<CommunicationLanguagePreferenceDto> ToResult(CommunicationLanguagePreferenceDto? result) =>
        result is null ? NotFound() : Ok(result);

    private Guid CompanyId() =>
        companyContextAccessor.CompanyId is { } companyId && companyId != Guid.Empty
            ? companyId
            : throw new UnauthorizedAccessException("A resolved company is required.");

    private Guid UserId() =>
        companyContextAccessor.UserId is { } userId && userId != Guid.Empty
            ? userId
            : throw new UnauthorizedAccessException("A resolved user is required.");
}
