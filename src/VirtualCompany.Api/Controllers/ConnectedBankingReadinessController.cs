using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Authorize(Policy = CompanyPolicies.FinanceView)]
[RequireCompanyContext]
[Route("api/companies/{companyId:guid}/finance/connected-banking-readiness")]
public sealed class ConnectedBankingReadinessController(
    IConnectedBankingReadinessService readiness,
    IConnectedBankingRecoveryVerificationService recovery) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ConnectedBankingReadinessReadModel>> GetAsync(
        Guid companyId,
        [FromQuery] string profile = ConnectedBankingCapacityProfileKeys.Small,
        [FromQuery] DateTime? asOfUtc = null,
        CancellationToken cancellationToken = default) =>
        Ok(await readiness.GetAsync(
            new GetConnectedBankingReadinessQuery(companyId, profile, asOfUtc),
            cancellationToken));

    [HttpPost("recovery-verification")]
    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    public async Task<ActionResult<ConnectedBankingRecoveryVerificationDto>> VerifyRecoveryAsync(
        Guid companyId,
        [FromBody] ConnectedBankingRecoveryVerificationRequest request,
        CancellationToken cancellationToken) =>
        Ok(await recovery.VerifyAsync(new VerifyConnectedBankingRecoveryCommand(
            companyId,
            request.VerifyObjectContent,
            ResolveActorUserId(),
            request.CorrelationId), cancellationToken));

    private Guid ResolveActorUserId()
    {
        var value = User.FindFirstValue(CurrentUserClaimTypes.UserId) ??
                    User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                    User.FindFirstValue("sub");
        return Guid.TryParse(value, out var userId) && userId != Guid.Empty
            ? userId
            : throw new UnauthorizedAccessException(
                "An authenticated user id is required for connected-banking recovery verification.");
    }
}

public sealed record ConnectedBankingRecoveryVerificationRequest(
    bool VerifyObjectContent,
    string? CorrelationId = null);
