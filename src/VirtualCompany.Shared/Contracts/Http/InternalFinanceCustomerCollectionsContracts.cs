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
public sealed class RunCustomerCollectionWorkerRequest
{
    public DateTime? AsOfUtc { get; set; }
    public int BatchSize { get; set; } = 100;
    public bool ResetBlockedLease { get; set; }
}

public sealed class SendCustomerReminderRequest
{
    public long ExpectedDraftVersion { get; set; }
    public string ExpectedSourceHash { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;

    public SendCustomerReminderRequest()
    {
    }

    public SendCustomerReminderRequest(long ExpectedDraftVersion, string ExpectedSourceHash, string IdempotencyKey)
    {
        this.ExpectedDraftVersion = ExpectedDraftVersion;
        this.ExpectedSourceHash = ExpectedSourceHash;
        this.IdempotencyKey = IdempotencyKey;
    }
}

public sealed class PrepareCustomerReminderRequest
{
    public int? RequestedStage { get; set; }
    public Guid? StatementId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;

    public PrepareCustomerReminderRequest()
    {
    }

    public PrepareCustomerReminderRequest(int? RequestedStage, Guid? StatementId, string IdempotencyKey)
    {
        this.RequestedStage = RequestedStage;
        this.StatementId = StatementId;
        this.IdempotencyKey = IdempotencyKey;
    }
}

public sealed class RecordCustomerCollectionResponseRequest
{
    public long ExpectedVersion { get; set; }
    public string ResponseType { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public Guid? OwnerUserId { get; set; }
    public DateTime? FollowUpDueUtc { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class ResolvePromiseToPayRequest
{
    public long ExpectedVersion { get; set; }
    public bool Kept { get; set; }
    public string Resolution { get; set; } = string.Empty;
}

public sealed class RecordPromiseToPayRequest
{
    public decimal Amount { get; set; }
    public DateOnly DueDate { get; set; }
    public Guid? OwnerUserId { get; set; }
    public DateTime? FollowUpDueUtc { get; set; }
    public long? ExpectedVersion { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class ResolveCustomerCollectionIssueRequest
{
    public long ExpectedVersion { get; set; }
    public string Resolution { get; set; } = string.Empty;
}

public sealed class RecordCustomerDisputeRequest
{
    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Guid? OwnerUserId { get; set; }
    public DateTime? FollowUpDueUtc { get; set; }
    public long? ExpectedVersion { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;

    public RecordCustomerDisputeRequest()
    {
    }

    public RecordCustomerDisputeRequest(decimal Amount, string Reason, Guid? OwnerUserId, DateTime? FollowUpDueUtc, long? ExpectedVersion, string IdempotencyKey)
    {
        this.Amount = Amount;
        this.Reason = Reason;
        this.OwnerUserId = OwnerUserId;
        this.FollowUpDueUtc = FollowUpDueUtc;
        this.ExpectedVersion = ExpectedVersion;
        this.IdempotencyKey = IdempotencyKey;
    }
}

public sealed class CustomerCollectionPolicyExceptionRequest
{
    public Guid CustomerId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateOnly? ExcludedUntilDate { get; set; }
}

public sealed class CustomerCollectionPolicyStageRequest
{
    public int Stage { get; set; }
    public int DaysAfterDue { get; set; }
    public string Channel { get; set; } = "email";
    public string TemplateKey { get; set; } = string.Empty;
    public bool RequiresApproval { get; set; } = true;
}

public sealed class UpsertCustomerCollectionPolicyRequest
{
    public long? ExpectedVersion { get; set; }
    public int GracePeriodDays { get; set; }
    public decimal MaterialityThreshold { get; set; }
    public string DefaultLocale { get; set; } = "en-US";
    public bool RequireApproval { get; set; } = true;
    public bool FeesEnabled { get; set; }
    public bool InterestEnabled { get; set; }
    public List<CustomerCollectionPolicyStageRequest>? Stages { get; set; } = [];
    public List<CustomerCollectionPolicyExceptionRequest>? CustomerExceptions { get; set; } = [];
}

public sealed class GenerateCustomerStatementRequest
{
    public Guid CustomerId { get; set; }
    public DateOnly FromDate { get; set; }
    public DateOnly CutoffDate { get; set; }
    public string TimeZoneId { get; set; } = "UTC";
    public string Locale { get; set; } = "en-US";
    public string Currency { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;

    public GenerateCustomerStatementRequest()
    {
    }

    public GenerateCustomerStatementRequest(Guid CustomerId, DateOnly FromDate, DateOnly CutoffDate, string TimeZoneId, string Locale, string Currency, string IdempotencyKey)
    {
        this.CustomerId = CustomerId;
        this.FromDate = FromDate;
        this.CutoffDate = CutoffDate;
        this.TimeZoneId = TimeZoneId;
        this.Locale = Locale;
        this.Currency = Currency;
        this.IdempotencyKey = IdempotencyKey;
    }
}
