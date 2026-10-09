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
public sealed class ChangeAccountingScheduleStateRequest : AccountingScheduleVersionRequest
{
    public bool GenerateMissed { get; set; }
}

public class AccountingScheduleVersionRequest
{
    public long ExpectedVersion { get; set; }
}

public sealed class DecideAccountingScheduleApprovalRequest : AccountingScheduleVersionRequest
{
    public bool Approve { get; set; }
    public string? Comment { get; set; }
    public Guid ClientRequestId { get; set; }
}

public sealed class AccountingScheduleActionRequest : AccountingScheduleVersionRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class AccountingScheduleLineRequest
{
    public Guid FinanceAccountId { get; set; }
    public decimal DebitAmount { get; set; }
    public decimal CreditAmount { get; set; }
    public string Description { get; set; } = string.Empty;
    public List<Guid> DimensionMemberIds { get; set; } = [];
}

public sealed class SaveAccountingScheduleRequest
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ScheduleType { get; set; } = "recurring_fixed";
    public string Cadence { get; set; } = "monthly";
    public string AmountBasis { get; set; } = "per_occurrence";
    public string ProrationRule { get; set; } = "none";
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public int OccurrenceDay { get; set; } = 1;
    public string TimeZoneId { get; set; } = "Europe/Stockholm";
    public string VoucherSeriesCode { get; set; } = "A";
    public string Currency { get; set; } = "SEK";
    public string ReversalRule { get; set; } = "none";
    public string Description { get; set; } = string.Empty;
    public List<AccountingScheduleLineRequest> Lines { get; set; } = [];
    public List<Guid> EvidenceDocumentIds { get; set; } = [];
    public long ExpectedVersion { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}
