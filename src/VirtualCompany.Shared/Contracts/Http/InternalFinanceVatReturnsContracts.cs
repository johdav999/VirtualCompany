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




public sealed class CreateVatReturnCorrectionRequest
{
    public string Reason { get; set; } = string.Empty;
    public string EvidenceReference { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
}




public sealed class VatReturnEvidenceRequest
{
    public string ExpectedInputHash { get; set; } = string.Empty;
}



public sealed class SetVatFilingPeriodDueDateRequest { public DateOnly DueDate {get;set;} }




public sealed class CalculateVatReturnRequest
{
    public Guid FilingPeriodId { get; set; }
    public Guid? VatReturnId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}


public sealed class CreateVatFilingPeriodRequest
{
    public string PeriodCode { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string Currency { get; set; } = "SEK";
    public Guid? FiscalPeriodId { get; set; }
    public DateOnly? DueDate { get; set; }
}
