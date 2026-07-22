using System.Globalization;
using System.Data;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Shared;

namespace VirtualCompany.Infrastructure.Finance;


public sealed partial class CompanyFinanceReadService
{
    public async Task<IReadOnlyList<FinanceCounterpartyDto>> GetCounterpartiesAsync(
        GetFinanceCounterpartiesQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        await EnsureFinanceInitializedAsync(query.CompanyId, cancellationToken);

        var normalizedType = NormalizeCounterpartyType(query.CounterpartyType);
        var counterparties = FilterCounterpartiesByType(
            _dbContext.FinanceCounterparties
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId),
            normalizedType);

        var rows = await counterparties
            .OrderBy(x => x.Name)
            .ThenBy(x => x.CreatedUtc)
            .Take(NormalizeLimit(query.Limit))
            .Select(x => new FinanceCounterpartyRow(
                x.Id,
                x.CompanyId,
                NormalizeCounterpartyType(x.CounterpartyType),
                x.Name,
                x.Email,
                x.PaymentTerms,
                x.TaxId,
                x.CreditLimit,
                x.PreferredPaymentMethod,
                x.DefaultAccountMapping,
                x.CreatedUtc,
                x.UpdatedUtc))
            .ToListAsync(cancellationToken);

        return rows.Select(MapCounterparty).ToList();
    }

    public async Task<FinanceCounterpartyDto?> GetCounterpartyAsync(
        GetFinanceCounterpartyQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        await EnsureFinanceInitializedAsync(query.CompanyId, cancellationToken);

        var normalizedType = NormalizeCounterpartyType(query.CounterpartyType);
        var counterparties = FilterCounterpartiesByType(
            _dbContext.FinanceCounterparties
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.Id == query.CounterpartyId),
            normalizedType);

        var row = await counterparties
            .Select(x => new FinanceCounterpartyRow(x.Id, x.CompanyId, NormalizeCounterpartyType(x.CounterpartyType), x.Name, x.Email, x.PaymentTerms, x.TaxId, x.CreditLimit, x.PreferredPaymentMethod, x.DefaultAccountMapping, x.CreatedUtc, x.UpdatedUtc))
            .SingleOrDefaultAsync(cancellationToken);

        return row is null ? null : MapCounterparty(row);
    }

}

