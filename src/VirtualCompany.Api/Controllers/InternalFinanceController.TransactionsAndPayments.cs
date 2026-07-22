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
using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Shared;
using VirtualCompany.Infrastructure.Tenancy;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using System.Text.Json.Nodes;
using System.Text.Json;

namespace VirtualCompany.Api.Controllers;

public sealed partial class InternalFinanceController
{
    [HttpGet("transactions")]
    public async Task<ActionResult<IReadOnlyList<FinanceTransactionDto>>> GetTransactionsAsync(
        Guid companyId,
        [FromQuery] DateTime? startUtc,
        [FromQuery] DateTime? endUtc,
        [FromQuery] string? category,
        [FromQuery] string? flagged,
        [FromQuery] int limit,
        CancellationToken cancellationToken,
        [FromQuery] string source = FinanceDataSources.All) =>
        await ExecuteReadAsync(
            () => _financeReadService.GetTransactionsAsync(
                new GetFinanceTransactionsQuery(companyId, startUtc, endUtc, limit, category, flagged, source),
                cancellationToken));

    [HttpGet("transactions/{transactionId:guid}")]
    public async Task<ActionResult<FinanceTransactionDetailDto>> GetTransactionDetailAsync(
        Guid companyId,
        Guid transactionId,
        CancellationToken cancellationToken) =>
        await ExecuteReadOptionalAsync(
            () => _financeReadService.GetTransactionDetailAsync(
                new GetFinanceTransactionDetailQuery(companyId, transactionId),
                cancellationToken),
            "Finance transaction was not found.");

    [HttpGet("payments")]
    public async Task<ActionResult<IReadOnlyList<FinancePaymentDto>>> GetPaymentsAsync(
        Guid companyId,
        [FromQuery(Name = "type")] string? paymentType,
        [FromQuery] int limit,
        CancellationToken cancellationToken,
        [FromQuery] string source = FinanceDataSources.All) =>
        await ExecuteReadAsync(
            () => _financePaymentReadService.GetPaymentsAsync(
                new GetFinancePaymentsQuery(companyId, paymentType, limit, source),
                cancellationToken));

    [HttpGet("payments/{paymentId:guid}")]
    public async Task<ActionResult<FinancePaymentDto>> GetPaymentDetailAsync(
        Guid companyId,
        Guid paymentId,
        CancellationToken cancellationToken) =>
        await ExecuteReadOptionalAsync(
            () => _financePaymentReadService.GetPaymentDetailAsync(
                new GetFinancePaymentDetailQuery(companyId, paymentId),
                cancellationToken),
            "Finance payment was not found.");

    [HttpGet("payments/{paymentId:guid}/allocations")]
    public async Task<ActionResult<IReadOnlyList<FinancePaymentAllocationDto>>> GetPaymentAllocationsAsync(
        Guid companyId,
        Guid paymentId,
        CancellationToken cancellationToken) =>
        await ExecuteReadOptionalAsync(
            () => _financePaymentReadService.GetAllocationsByPaymentAsync(
                new GetFinancePaymentAllocationsByPaymentQuery(companyId, paymentId),
                cancellationToken),
            "Finance payment was not found.");

    [Authorize(Policy = CompanyPolicies.FinanceEdit)]
    [HttpPost("payments")]
    public async Task<ActionResult<FinancePaymentDto>> CreatePaymentAsync(
        Guid companyId,
        [FromBody] CreateFinancePaymentRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(
            () => _financePaymentCommandService.CreatePaymentAsync(
                new CreateFinancePaymentCommand(companyId, request.ToDto()),
                cancellationToken));

    [Authorize(Policy = CompanyPolicies.FinanceEdit)]
    [HttpPost("payments/{paymentId:guid}/allocations")]
    public async Task<ActionResult<FinancePaymentAllocationDto>> CreatePaymentAllocationAsync(
        Guid companyId,
        Guid paymentId,
        [FromBody] CreateFinancePaymentAllocationRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(
            () => _financePaymentCommandService.CreateAllocationAsync(
                new CreateFinancePaymentAllocationCommand(companyId, request.ToDto(paymentId)),
                cancellationToken));

}

