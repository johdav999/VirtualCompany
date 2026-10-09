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




public sealed class CreateAccountingFiscalYearRequest : PreviewAccountingFiscalYearRequest
{
    public string? IdempotencyKey { get; set; }
}




public class PreviewAccountingFiscalYearRequest
{
    public DateOnly FiscalYearStart { get; set; }
}




public sealed class SubmitCommerceAccountingEventRequest
{
    public Guid EventId { get; set; }
    public long EventVersion { get; set; }
    public string ContractVersion { get; set; } = "finance-commerce.v1";
    public string EventType { get; set; } = string.Empty;
    public string SourceSystem { get; set; } = string.Empty;
    public DateTime OccurredUtc { get; set; }
    public bool RequiresInventoryAccounting { get; set; }
}




public sealed class RecordVoucherGapEvidenceRequest
{
    public int FiscalYear { get; set; }
    public long MissingNumber { get; set; }
    public string Reason { get; set; } = string.Empty;
}




public sealed class SaveAccountingSeriesPolicyRequest
{
    public Guid? PolicyId { get; set; }
    public string SeriesKind { get; set; } = "voucher";
    public Guid SeriesId { get; set; }
    public string SourceType { get; set; } = "*";
    public string TransactionType { get; set; } = "*";
    public int? FiscalYear { get; set; }
    public Guid? LocationDimensionMemberId { get; set; }
    public string? Jurisdiction { get; set; }
    public string? ProviderKey { get; set; }
    public string? ProviderSeriesCode { get; set; }
    public bool IsActive { get; set; } = true;
    public long? ExpectedVersion { get; set; }
}




public sealed class ApplyAccountingAccountLifecycleRequest : PreviewAccountingAccountLifecycleRequest
{
    public string Name { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public long ExpectedLifecycleVersion { get; set; }
}




public class PreviewAccountingAccountLifecycleRequest
{
    public string AccountClass { get; set; } = string.Empty;
    public string NormalBalance { get; set; } = string.Empty;
    public bool IsReportable { get; set; } = true;
    public string PostingRestriction { get; set; } = "none";
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public Guid? ReplacementAccountId { get; set; }
}




public sealed class DeactivateAccountingAccountRequest
{
    public DateOnly EffectiveTo { get; set; }
    public DateTime ExpectedUpdatedUtc { get; set; }
}




public sealed class RenameAccountingAccountRequest
{
    public string Name { get; set; } = string.Empty;
    public DateTime ExpectedUpdatedUtc { get; set; }
}




public sealed class CreateAccountingAccountFromChartCatalogRequest
{
    public string CatalogKey { get; set; } = AccountingChartCatalogDefaults.Bas2026CatalogKey;
    public string CatalogVersion { get; set; } = AccountingChartCatalogDefaults.Bas2026CatalogVersion;
    public string Code { get; set; } = string.Empty;
    public string? NameSv { get; set; }
    public string? AccountClass { get; set; }
    public string? NormalBalance { get; set; }
    public bool AccountingSemanticsConfirmed { get; set; }
    public bool CompanySuitabilityConfirmed { get; set; }
    public DateOnly EffectiveFrom { get; set; }
}




public sealed class CreateAccountingAccountRequest
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string AccountClass { get; set; } = string.Empty;
    public string NormalBalance { get; set; } = string.Empty;
    public DateOnly EffectiveFrom { get; set; }
}




public sealed class CompleteAccountingSetupRequest : PreviewAccountingSetupRequest
{
    public string? IdempotencyKey { get; set; }
}


public class PreviewAccountingSetupRequest
{
    public string BaseCurrency { get; set; } = string.Empty;
    public DateOnly FiscalYearStart { get; set; }
    public string PolicyPackKey { get; set; } = string.Empty;
    public string PolicyPackVersion { get; set; } = string.Empty;
    public string ChartTemplateKey { get; set; } = string.Empty;
    public Dictionary<string, string> AccountRoleCodeAssignments { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
