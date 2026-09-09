using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Sales;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/platform/teams-presenter/companies/{companyId:guid}")]
[Authorize(Policy = CompanyPolicies.PlatformAdministration)]
public sealed class TeamsTenantRegistrationsController(
    ITeamsTenantRegistrationService service,
    ICurrentUserAccessor currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<TeamsTenantRegistrationDto>> GetAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var result = await service.GetAsync(companyId, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPut]
    public Task<TeamsTenantRegistrationDto> AssociateAsync(
        Guid companyId,
        [FromBody] AssociateTeamsTenantRequest request,
        CancellationToken cancellationToken) =>
        service.AssociateAsync(new AssociateTeamsTenantCommand(
            companyId,
            request.EntraTenantId,
            request.ApprovedMediaRoute ?? string.Empty,
            UserId(),
            CorrelationId()), cancellationToken);

    [HttpPost("admin-consent")]
    public Task<TeamsAdminConsentStartResult> StartAdminConsentAsync(Guid companyId, CancellationToken cancellationToken) =>
        service.StartAdminConsentAsync(new StartTeamsAdminConsentCommand(companyId, UserId(), CorrelationId()), cancellationToken);

    [HttpPost("policy-attestation")]
    public Task<TeamsTenantRegistrationDto> AttestPolicyAsync(
        Guid companyId,
        [FromBody] TeamsTenantPolicyAttestationRequest request,
        CancellationToken cancellationToken) =>
        service.AttestPolicyAsync(new AttestTeamsTenantPolicyCommand(
            companyId, request.Approved, UserId(), CorrelationId()), cancellationToken);

    [HttpPost("disable")]
    public Task<TeamsTenantRegistrationDto> DisableAsync(
        Guid companyId,
        [FromBody] DisableTeamsTenantRequest request,
        CancellationToken cancellationToken) =>
        service.DisableAsync(new DisableTeamsTenantCommand(
            companyId, request.ConsentRevoked, UserId(), CorrelationId()), cancellationToken);

    private Guid UserId() => currentUser.UserId
        ?? throw new UnauthorizedAccessException("A resolved platform administrator is required.");

    private string CorrelationId() =>
        Request.Headers.TryGetValue("X-Correlation-Id", out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.ToString()
            : HttpContext.TraceIdentifier;
}

public sealed record AssociateTeamsTenantRequest(Guid EntraTenantId, string? ApprovedMediaRoute);
public sealed record TeamsTenantPolicyAttestationRequest(bool Approved);
public sealed record DisableTeamsTenantRequest(bool ConsentRevoked);

[ApiController]
[Route("api/platform/teams-presenter/admin-consent/callback")]
[Authorize(Policy = CompanyPolicies.PlatformAdministration)]
public sealed class TeamsAdminConsentCallbackController(
    ITeamsTenantRegistrationService service,
    ICurrentUserAccessor currentUser) : ControllerBase
{
    [HttpGet]
    public Task<TeamsTenantRegistrationDto> CompleteAsync(
        [FromQuery] string? state,
        [FromQuery] string? tenant,
        [FromQuery(Name = "admin_consent")] string? adminConsent,
        [FromQuery] string? error,
        CancellationToken cancellationToken) =>
        service.CompleteAdminConsentAsync(new CompleteTeamsAdminConsentCommand(
            state ?? string.Empty,
            tenant,
            adminConsent,
            error,
            currentUser.UserId ?? throw new UnauthorizedAccessException("A resolved platform administrator is required."),
            HttpContext.TraceIdentifier), cancellationToken);
}

[ApiController]
[Route("api/integrations/teams/calls")]
[AllowAnonymous]
public sealed class TeamsCallingNotificationsController(
    ITeamsCallbackAuthenticator authenticator,
    ITeamsCallCallbackReceiver receiver,
    Microsoft.Extensions.Options.IOptions<VirtualCompany.Infrastructure.Sales.TeamsPresenterOptions> configured,
    ILogger<TeamsCallingNotificationsController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> ReceiveAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (Request.ContentLength is > 0 && Request.ContentLength > configured.Value.CallbackMaxBytes)
                return StatusCode(StatusCodes.Status413PayloadTooLarge);
            var identity = await authenticator.AuthenticateAsync(Request.Headers.Authorization, cancellationToken);
            if (Request.ContentLength is null or 0) return NoContent();
            await using var callbackBody = new MemoryStream();
            var chunk = new byte[8192];
            while (true)
            {
                var read = await Request.Body.ReadAsync(chunk, cancellationToken);
                if (read == 0) break;
                if (callbackBody.Length + read > configured.Value.CallbackMaxBytes)
                    return StatusCode(StatusCodes.Status413PayloadTooLarge);
                await callbackBody.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
            }
            callbackBody.Position = 0;
            Guid? callId = Guid.TryParse(Request.Query["callId"], out var parsed) ? parsed : null;
            long? joinGeneration = long.TryParse(Request.Query["joinGeneration"], out var parsedVersion) ? parsedVersion : null;
            var result = await receiver.ReceiveAsync(identity, callId, joinGeneration, callbackBody, cancellationToken);
            logger.LogInformation("Accepted Teams calling notifications; registrationId={RegistrationId}, accepted={Accepted}, duplicates={Duplicates}, ignored={Ignored}.",
                identity.RegistrationId, result.Accepted, result.Duplicates, result.Ignored);
            return result.RequiresGraphProtocol ? NoContent() : Accepted();
        }
        catch (TeamsIdentityException)
        {
            return Unauthorized();
        }
        catch (System.Text.Json.JsonException) { return BadRequest(); }
    }
}
