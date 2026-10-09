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
public static class SupplierBillAccountingRequestMapping
{
    public static SupplierBillAccountingInput ToInput(this SupplierBillAccountingRequest request) => new(request.FiscalPeriodId, request.VoucherSeriesCode, request.ExchangeRate, request.Lines.Select(x => new SupplierBillAccountingLineInput(x.Description, x.Amount, x.CostAccountId, x.TaxRuleKey, x.LineClassification, x.CounterpartyJurisdiction, x.CounterpartyVatStatus, x.TaxEvidence.Select(evidence => new AccountingTaxEvidenceInput(evidence.Classification, evidence.SourceReference)).ToArray())).ToArray());
}
