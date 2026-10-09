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
public static class StatutoryDocumentRequestMapping
{
    public static StatutoryDocumentInput ToInput(this StatutoryDocumentRequest request) => new(request.DocumentType, request.Authority, request.CounterpartyId, request.CounterpartyLegalName, request.CounterpartyAddressLine1, request.CounterpartyPostalCode, request.CounterpartyCity, request.CounterpartyCountryCode, request.CounterpartyVatIdentifier, request.IssueDate, request.SupplyDate, request.AccountingDate, request.DueDate, request.Currency, request.PaymentTerms, request.ExplanatoryText, request.NetTotal, request.VatTotal, request.GrossTotal, request.Lines.Select(x => new StatutoryDocumentLineInput(x.Description, x.Quantity, x.UnitPrice, x.NetAmount, x.VatRate, x.VatAmount)).ToArray(), request.OriginalIssuedDocumentId, request.ProviderDocumentNumber, request.TaxFactsJson, request.ApprovalIds, request.SourceVersion);
}
