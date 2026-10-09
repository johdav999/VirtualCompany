using System.Globalization;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Agents;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Shared;
using System.Text.Json.Nodes;
using System.Text.Json;

namespace VirtualCompany.Api.Controllers;




public sealed class RequestAccountingExportRequest : AccountingPeriodRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
    public string ExportType { get; set; } = "generic_json";
    public string? CorrelationId { get; set; }
}


public class AccountingPeriodRequest
{
    public Guid FiscalPeriodId { get; set; }
}
