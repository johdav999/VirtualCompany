using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Api.Controllers;

public sealed partial class InternalFinanceController
{
    [Authorize(Policy = CompanyPolicies.AccountingView)]
    [HttpGet("accounting/authority")]
    public async Task<ActionResult<AccountingAuthorityReadModel>> GetAccountingAuthorityAsync(
        Guid companyId,
        [FromQuery] DateOnly? asOf,
        [FromQuery] int exportLimit,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() => _accountingAuthorityService.GetAsync(
            new GetAccountingAuthorityQuery(companyId, asOf, exportLimit <= 0 ? 50 : exportLimit), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/authority/preview")]
    public async Task<ActionResult<AccountingAuthorityChangePreview>> PreviewAccountingAuthorityChangeAsync(
        Guid companyId,
        [FromBody] PreviewAccountingAuthorityChangeRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(() => _accountingAuthorityService.PreviewChangeAsync(
            new PreviewAccountingAuthorityChangeQuery(
                companyId, request.EffectiveFiscalPeriodId, request.TargetAuthority, request.ProviderKey),
            cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/authority/change")]
    public async Task<ActionResult<AccountingAuthorityReadModel>> StartAccountingAuthorityChangeAsync(
        Guid companyId,
        [FromBody] StartAccountingAuthorityChangeRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => _accountingAuthorityService.StartChangeAsync(
            new StartAccountingAuthorityChangeCommand(
                companyId, request.EffectiveFiscalPeriodId, request.TargetAuthority, request.ProviderKey,
                request.Reason, request.PreviewToken, request.ExpectedCurrentVersion,
                ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required."),
                ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPut("accounting/authority/{authorityPeriodId:guid}/cutover-validation")]
    public async Task<ActionResult<AccountingAuthorityReadModel>> RecordAccountingCutoverValidationAsync(
        Guid companyId,
        Guid authorityPeriodId,
        [FromBody] RecordAccountingCutoverValidationRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => _accountingAuthorityService.RecordCutoverValidationAsync(
            new RecordAccountingCutoverValidationCommand(
                companyId, authorityPeriodId, request.OpeningBalancesReconciled,
                request.TrialBalanceReconciled, request.SourceMappingsReconciled, request.ConflictCount,
                request.Summary, request.ExpectedVersion,
                ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required."),
                ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/authority/{authorityPeriodId:guid}/complete")]
    public async Task<ActionResult<AccountingAuthorityReadModel>> CompleteAccountingAuthorityCutoverAsync(
        Guid companyId,
        Guid authorityPeriodId,
        [FromBody] CompleteAccountingAuthorityCutoverRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => _accountingAuthorityService.CompleteCutoverAsync(
            new CompleteAccountingAuthorityCutoverCommand(
                companyId, authorityPeriodId, request.ExpectedVersion,
                ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required."),
                ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/provider-exports")]
    public async Task<ActionResult<AccountingProviderExportDto>> QueueAccountingProviderExportAsync(
        Guid companyId,
        [FromBody] QueueAccountingProviderExportRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => _accountingProviderExportService.QueueAsync(
            new QueueAccountingProviderExportCommand(
                companyId, request.LedgerEntryId, request.ProviderKey,
                ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required."),
                ResolveCorrelationId()), cancellationToken));

    [Authorize(Policy = CompanyPolicies.AccountingAdmin)]
    [HttpPost("accounting/provider-exports/{exportId:guid}/reconcile")]
    public async Task<ActionResult<AccountingProviderExportDto>> ReconcileAccountingProviderExportAsync(
        Guid companyId,
        Guid exportId,
        [FromBody] ReconcileAccountingProviderExportRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(() => _accountingProviderExportService.ReconcileAsync(
            new ReconcileAccountingProviderExportCommand(
                companyId, exportId, request.ProviderConfirmedSuccess, request.ProviderExternalId,
                request.Summary, request.ExpectedVersion,
                ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required."),
                ResolveCorrelationId()), cancellationToken));
}

public sealed record PreviewAccountingAuthorityChangeRequest(
    Guid EffectiveFiscalPeriodId,
    string TargetAuthority,
    string? ProviderKey);
public sealed record StartAccountingAuthorityChangeRequest(
    Guid EffectiveFiscalPeriodId,
    string TargetAuthority,
    string? ProviderKey,
    string Reason,
    string PreviewToken,
    long ExpectedCurrentVersion);
public sealed record RecordAccountingCutoverValidationRequest(
    bool OpeningBalancesReconciled,
    bool TrialBalanceReconciled,
    bool SourceMappingsReconciled,
    int ConflictCount,
    string Summary,
    long ExpectedVersion);
public sealed record CompleteAccountingAuthorityCutoverRequest(long ExpectedVersion);
public sealed record QueueAccountingProviderExportRequest(Guid LedgerEntryId, string ProviderKey);
public sealed record ReconcileAccountingProviderExportRequest(
    bool ProviderConfirmedSuccess,
    string? ProviderExternalId,
    string Summary,
    long ExpectedVersion);
