using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auth;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Shared;
using VirtualCompany.Infrastructure.Tenancy;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Api.ProblemHandling;
using System.Text.Json.Nodes;
using System.Text.Json;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("internal/companies/{companyId:guid}/finance")]
[Authorize(Policy = CompanyPolicies.FinanceView)]
[RequireCompanyContext]
public sealed class InternalFinanceAccountingOperationsController : InternalFinanceControllerBase
{
    private readonly IAccountingMigrationService _accountingMigrationService;
    private readonly IAccountingOperationsReadService _accountingOperationsReadService;
    private readonly IAccountingRecoveryVerificationService _accountingRecoveryVerificationService;

    public InternalFinanceAccountingOperationsController(
        IAccountingMigrationService accountingMigrationService,
        IAccountingOperationsReadService accountingOperationsReadService,
        IAccountingRecoveryVerificationService accountingRecoveryVerificationService,
        FinanceInitializationProblemHandler initializationProblems,
        ILogger<InternalFinanceAccountingOperationsController> logger)
        : base(initializationProblems, logger)
    {
        _accountingMigrationService = accountingMigrationService;
        _accountingOperationsReadService = accountingOperationsReadService;
        _accountingRecoveryVerificationService = accountingRecoveryVerificationService;
    }
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
