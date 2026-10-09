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




public sealed class ConfigureCurrencyRevaluationScheduleRequest
{
    public bool IsEnabled { get; set; }
    public int DaysBeforePeriodEnd { get; set; }
    public bool AutomaticReversal { get; set; }
    public string VoucherSeriesCode { get; set; } = string.Empty;
    public long? ExpectedVersion { get; set; }
}




public sealed class ConfigureCurrencyRevaluationAccountRequest
{
    public string MonetaryClass { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public long? ExpectedVersion { get; set; }
}



public sealed class CurrencyRevaluationActionRequest : CurrencyRevaluationVersionRequest
{ public string IdempotencyKey { get; set; } = string.Empty; }




public class CurrencyRevaluationVersionRequest { public long ExpectedVersion { get; set; } }




public sealed class ReviewCurrencyRevaluationItemRequest
{
    public string Action { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public long ExpectedVersion { get; set; }
}


public sealed class PreviewCurrencyRevaluationRequest
{
    public Guid FiscalPeriodId { get; set; }
    public string VoucherSeriesCode { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
}
