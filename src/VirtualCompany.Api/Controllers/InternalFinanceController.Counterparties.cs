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
    [HttpGet("customers")]
    public async Task<ActionResult<IReadOnlyList<FinanceCounterpartyDto>>> GetCustomersAsync(
        Guid companyId,
        [FromQuery] int limit,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(
            () => _financeReadService.GetCounterpartiesAsync(
                new GetFinanceCounterpartiesQuery(companyId, "customer", Limit: limit),
                cancellationToken));

    [HttpGet("customers/{counterpartyId:guid}")]
    public async Task<ActionResult<FinanceCounterpartyDto>> GetCustomerAsync(
        Guid companyId,
        Guid counterpartyId,
        CancellationToken cancellationToken) =>
        await ExecuteReadOptionalAsync(
            () => _financeReadService.GetCounterpartyAsync(
                new GetFinanceCounterpartyQuery(companyId, counterpartyId, "customer"),
                cancellationToken),
            "Finance customer was not found.");

    [HttpGet("suppliers")]
    public async Task<ActionResult<IReadOnlyList<FinanceCounterpartyDto>>> GetSuppliersAsync(
        Guid companyId,
        [FromQuery] int limit,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(
            () => _financeReadService.GetCounterpartiesAsync(
                new GetFinanceCounterpartiesQuery(companyId, "supplier", Limit: limit),
                cancellationToken));

    [HttpGet("suppliers/{counterpartyId:guid}")]
    public async Task<ActionResult<FinanceCounterpartyDto>> GetSupplierAsync(
        Guid companyId,
        Guid counterpartyId,
        CancellationToken cancellationToken) =>
        await ExecuteReadOptionalAsync(
            () => _financeReadService.GetCounterpartyAsync(
                new GetFinanceCounterpartyQuery(companyId, counterpartyId, "supplier"),
                cancellationToken),
            "Finance supplier was not found.");

    [Authorize(Policy = CompanyPolicies.FinanceEdit)]
    [HttpPost("customers")]
    public async Task<ActionResult<FinanceCounterpartyDto>> CreateCustomerAsync(
        Guid companyId,
        [FromBody] UpsertFinanceCounterpartyRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(
            () => _financeCommandService.CreateCounterpartyAsync(
                new CreateFinanceCounterpartyCommand(companyId, "customer", request.ToDto()),
                cancellationToken));

    [Authorize(Policy = CompanyPolicies.FinanceEdit)]
    [HttpPut("customers/{counterpartyId:guid}")]
    public async Task<ActionResult<FinanceCounterpartyDto>> UpdateCustomerAsync(
        Guid companyId,
        Guid counterpartyId,
        [FromBody] UpsertFinanceCounterpartyRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(
            () => _financeCommandService.UpdateCounterpartyAsync(
                new UpdateFinanceCounterpartyCommand(companyId, counterpartyId, "customer", request.ToDto()),
                cancellationToken));

    [Authorize(Policy = CompanyPolicies.FinanceEdit)]
    [HttpPost("suppliers")]
    public async Task<ActionResult<FinanceCounterpartyDto>> CreateSupplierAsync(
        Guid companyId,
        [FromBody] UpsertFinanceCounterpartyRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(
            () => _financeCommandService.CreateCounterpartyAsync(
                new CreateFinanceCounterpartyCommand(companyId, "supplier", request.ToDto()),
                cancellationToken));

    [Authorize(Policy = CompanyPolicies.FinanceEdit)]
    [HttpPut("suppliers/{counterpartyId:guid}")]
    public async Task<ActionResult<FinanceCounterpartyDto>> UpdateSupplierAsync(
        Guid companyId,
        Guid counterpartyId,
        [FromBody] UpsertFinanceCounterpartyRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(
            () => _financeCommandService.UpdateCounterpartyAsync(
                new UpdateFinanceCounterpartyCommand(companyId, counterpartyId, "supplier", request.ToDto()),
                cancellationToken));

}
