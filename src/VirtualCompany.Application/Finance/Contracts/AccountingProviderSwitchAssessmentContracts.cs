using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Application.Finance;

public static class AccountingProviderSwitchCapabilityKeys
{
    public const string Accounts = "accounts";
    public const string Tax = "tax";
    public const string FiscalPeriods = "fiscal_periods";
    public const string PeriodLocks = "period_locks";
    public const string VoucherNumbering = "voucher_numbering";
    public const string Customers = "customers";
    public const string Suppliers = "suppliers";
    public const string Invoices = "invoices";
    public const string Credits = "credits";
    public const string Payments = "payments";
    public const string Allocations = "allocations";
    public const string BankReconciliation = "bank_reconciliation";
    public const string Currencies = "currencies";
    public const string ExchangeRates = "exchange_rates";
    public const string Dimensions = "dimensions";
    public const string Journals = "journals";
    public const string Attachments = "attachments";
    public const string StableIdentifiers = "stable_identifiers";
    public const string IncrementalExtraction = "incremental_extraction";
    public const string SandboxPreview = "sandbox_preview";
    public const string RateLimits = "rate_limits";
    public const string ReconciliationLookup = "reconciliation_lookup";

    public static readonly string[] All =
    [
        Accounts, Tax, FiscalPeriods, PeriodLocks, VoucherNumbering, Customers, Suppliers, Invoices,
        Credits, Payments, Allocations, BankReconciliation, Currencies, ExchangeRates, Dimensions,
        Journals, Attachments, StableIdentifiers, IncrementalExtraction, SandboxPreview, RateLimits,
        ReconciliationLookup
    ];
}

public static class AccountingProviderSwitchDatasetKeys
{
    public const string Accounts = "accounts";
    public const string Tax = "tax";
    public const string FiscalPeriods = "fiscal_periods";
    public const string VoucherNumbering = "voucher_numbering";
    public const string Customers = "customers";
    public const string Suppliers = "suppliers";
    public const string Invoices = "invoices";
    public const string Credits = "credits";
    public const string Payments = "payments";
    public const string Allocations = "allocations";
    public const string BankReconciliation = "bank_reconciliation";
    public const string Currencies = "currencies";
    public const string ExchangeRates = "exchange_rates";
    public const string Dimensions = "dimensions";
    public const string Journals = "journals";
    public const string Attachments = "attachments";
    public const string StableIdentifiers = "stable_identifiers";

    public static readonly string[] All =
    [
        Accounts, Tax, FiscalPeriods, VoucherNumbering, Customers, Suppliers, Invoices, Credits,
        Payments, Allocations, BankReconciliation, Currencies, ExchangeRates, Dimensions, Journals,
        Attachments, StableIdentifiers
    ];
}

public static class AccountingProviderSwitchEndpointRoles
{
    public const string Source = "source";
    public const string Target = "target";
}

public sealed record ProviderMigrationCapability(
    string Key,
    string Level,
    string Explanation,
    string? RequiredScope = null);

public sealed record ProviderMigrationCapabilityProfile(
    string EndpointKind,
    string? ProviderKey,
    IReadOnlyList<ProviderMigrationCapability> Capabilities,
    DateTime ObservedUtc);

public sealed record ProviderSwitchInventoryExtractionRequest(
    Guid CompanyId,
    Guid SwitchId,
    string EndpointRole,
    AccountingProviderSwitchEndpointDto Endpoint,
    string DatasetKey,
    string? Cursor,
    int PageSize,
    string CorrelationId);

public sealed record ProviderSwitchInventoryExtractionResult(
    string DatasetKey,
    string Availability,
    string CapabilityLevel,
    long RecordCount,
    decimal FinancialTotal,
    string? Currency,
    string? NextCursor,
    string? SourceVersion,
    string IntegrityHash,
    string EvidenceJson,
    bool IsComplete,
    string? FailureCode = null,
    string? FailureSummary = null);

public interface IAccountingProviderSwitchAdapter
{
    bool CanHandle(string endpointKind, string? providerKey);
    Task<ProviderMigrationCapabilityProfile> GetCapabilityProfileAsync(
        Guid companyId,
        AccountingProviderSwitchEndpointDto endpoint,
        string correlationId,
        CancellationToken cancellationToken);
    Task<ProviderSwitchInventoryExtractionResult> ExtractInventoryAsync(
        ProviderSwitchInventoryExtractionRequest request,
        CancellationToken cancellationToken);
}

public interface IAccountingProviderSwitchAdapterResolver
{
    IAccountingProviderSwitchAdapter GetRequired(string endpointKind, string? providerKey);
}

public sealed record StartAccountingProviderSwitchAssessmentCommand(
    Guid CompanyId,
    Guid SwitchId,
    long ExpectedSwitchVersion,
    Guid ActorUserId,
    string CorrelationId,
    string IdempotencyKey);

public sealed record ReplayAccountingProviderSwitchAssessmentCommand(
    Guid CompanyId,
    Guid SwitchId,
    Guid AssessmentId,
    long ExpectedSwitchVersion,
    Guid ActorUserId,
    string CorrelationId,
    string IdempotencyKey);

public sealed record GetAccountingProviderSwitchAssessmentQuery(Guid CompanyId, Guid SwitchId, Guid? AssessmentId = null);

public sealed record AccountingProviderSwitchCapabilityDto(
    string EndpointRole, string CapabilityKey, string Level, string Explanation, string? RequiredScope, DateTime ObservedUtc);

public sealed record AccountingProviderSwitchDatasetDto(
    string EndpointRole, string DatasetKey, string Availability, string CapabilityLevel, long RecordCount,
    decimal FinancialTotal, string? Currency, string? SourceCursor, string? SourceVersion, string IntegrityHash,
    string EvidenceJson, string? FailureCode, string? FailureSummary, DateTime ExtractedUtc);

public sealed record AccountingProviderSwitchGapDto(
    Guid Id, string Category, string? DatasetKey, string Severity, bool IsBlocking, string ReasonCode,
    string Explanation, string EvidenceJson, string OperatorAction, DateTime CreatedUtc);

public sealed record AccountingProviderSwitchAssessmentDto(
    Guid Id,
    Guid CompanyId,
    Guid SwitchId,
    string Status,
    int CompletedWorkItems,
    int TotalWorkItems,
    int ProgressPercent,
    int AttemptCount,
    DateTime? NextAttemptUtc,
    string? FailureCode,
    string? FailureSummary,
    DateTime RequestedUtc,
    DateTime? StartedUtc,
    DateTime? CompletedUtc,
    IReadOnlyList<AccountingProviderSwitchCapabilityDto> Capabilities,
    IReadOnlyList<AccountingProviderSwitchDatasetDto> Datasets,
    IReadOnlyList<AccountingProviderSwitchGapDto> Gaps,
    bool HasBlockingGaps,
    string AllowedNextAction,
    string AllowedNextActionExplanation);

public sealed record AccountingProviderSwitchAssessmentProgressDto(
    Guid AssessmentId,
    string Status,
    int CompletedWorkItems,
    int TotalWorkItems,
    int ProgressPercent,
    int AttemptCount,
    DateTime? NextAttemptUtc,
    string? FailureCode,
    string? FailureSummary,
    bool HasBlockingGaps,
    string AllowedNextAction,
    string AllowedNextActionExplanation);

public interface IAccountingProviderSwitchAssessmentService
{
    Task<AccountingProviderSwitchAssessmentDto> StartAsync(StartAccountingProviderSwitchAssessmentCommand command, CancellationToken cancellationToken);
    Task<AccountingProviderSwitchAssessmentDto> ReplayAsync(ReplayAccountingProviderSwitchAssessmentCommand command, CancellationToken cancellationToken);
    Task<AccountingProviderSwitchAssessmentDto> GetAsync(GetAccountingProviderSwitchAssessmentQuery query, CancellationToken cancellationToken);
}

public interface IAccountingProviderSwitchAssessmentJobRunner
{
    Task<int> RunDueAsync(CancellationToken cancellationToken);
}

public sealed record AccountingProviderSwitchGapInput(
    string Strategy,
    IReadOnlyList<AccountingProviderSwitchCapabilityDto> Capabilities,
    IReadOnlyList<AccountingProviderSwitchDatasetDto> Datasets);

public sealed record AccountingProviderSwitchGapDecision(
    string Category,
    string? DatasetKey,
    string Severity,
    bool IsBlocking,
    string ReasonCode,
    string Explanation,
    string EvidenceJson,
    string OperatorAction);

public interface IAccountingProviderSwitchGapPolicy
{
    IReadOnlyList<AccountingProviderSwitchGapDecision> Evaluate(AccountingProviderSwitchGapInput input);
}
