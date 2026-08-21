using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Api.Controllers;

public sealed partial class InternalFinanceController
{
    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/operations")]
    public async Task<ActionResult<AccountingOperationsReadModel>> GetAccountingOperationsAsync(
        Guid companyId,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() => _accountingOperationsReadService.GetAsync(
            new GetAccountingOperationsQuery(companyId), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/operations/migrations")]
    public async Task<ActionResult<AccountingMigrationRunDto>> StartAccountingMigrationAsync(
        Guid companyId,
        [FromBody] StartAccountingMigrationRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => _accountingMigrationService.StartAsync(
            new StartAccountingMigrationCommand(companyId, request.IdempotencyKey,
                ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required."),
                ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPut("accounting/operations/migration-conflicts/{conflictId:guid}/resolve")]
    public async Task<ActionResult<AccountingMigrationRunDto>> ResolveAccountingMigrationConflictAsync(
        Guid companyId,
        Guid conflictId,
        [FromBody] ResolveAccountingMigrationConflictRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => _accountingMigrationService.ResolveConflictAsync(
            new ResolveAccountingMigrationConflictCommand(companyId, conflictId, request.ResolutionSummary,
                request.ExpectedVersion,
                ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required."),
                ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/operations/recovery-verification")]
    public async Task<ActionResult<AccountingRecoveryVerificationDto>> VerifyAccountingRecoveryAsync(
        Guid companyId,
        [FromBody] VerifyAccountingRecoveryRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => _accountingRecoveryVerificationService.VerifyAsync(
            new VerifyAccountingRecoveryCommand(companyId, request.FiscalPeriodId, request.VerifyObjectContent,
                ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required."),
                ResolveCorrelationId()), cancellationToken));
}

public sealed record StartAccountingMigrationRequest(string IdempotencyKey);
public sealed record ResolveAccountingMigrationConflictRequest(string ResolutionSummary, long ExpectedVersion);
public sealed record VerifyAccountingRecoveryRequest(Guid? FiscalPeriodId, bool VerifyObjectContent);
