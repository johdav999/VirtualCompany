namespace VirtualCompany.Application.Finance;

public sealed record NormalizedMigrationAccount(string Code, string Name, string? AccountClass, string? NormalBalance,
    string? ControlAccountRole, string Currency);
public sealed record NormalizedMigrationTaxTreatment(string Code, string Name, decimal? Rate, string? SemanticRole);
public sealed record NormalizedMigrationCounterparty(string Kind, string Name, string? RegistrationNumber,
    string? TaxIdentifier, string? Email);
public sealed record NormalizedMigrationDocument(string DocumentType, string FileName, string? ContentType,
    string EvidenceReference);
public sealed record NormalizedMigrationJournalLine(string LineIdentity, string AccountKey, decimal Debit,
    decimal Credit, string Currency, string? TaxCode, IReadOnlyDictionary<string, string>? Dimensions);
public sealed record NormalizedMigrationJournal(string JournalIdentity, DateOnly AccountingDate, string? Number,
    string Description, IReadOnlyList<NormalizedMigrationJournalLine> Lines);
public sealed record NormalizedMigrationInvoice(string InvoiceIdentity, string CounterpartyKey, DateOnly InvoiceDate,
    DateOnly DueDate, decimal GrossAmount, decimal TaxAmount, string Currency, bool IsCredit);
public sealed record NormalizedMigrationPaymentAllocation(string AllocationIdentity, string PaymentIdentity,
    string OpenItemIdentity, decimal Amount, string Currency);
public sealed record NormalizedMigrationPayment(string PaymentIdentity, DateOnly PaymentDate, decimal Amount,
    string Currency, IReadOnlyList<NormalizedMigrationPaymentAllocation> Allocations);
public sealed record NormalizedMigrationBankState(string AccountKey, DateOnly AsOfDate, decimal LedgerBalance,
    decimal ReconciledBalance, string Currency);
public sealed record NormalizedMigrationCurrencyRate(string FromCurrency, string ToCurrency, DateOnly RateDate,
    decimal Rate, string EvidenceReference);
public sealed record NormalizedMigrationDimension(string DimensionType, string Code, string Name);
public sealed record NormalizedMigrationOpenItem(string OpenItemIdentity, string CounterpartyKey, DateOnly DueDate,
    decimal OriginalAmount, decimal OpenAmount, string Currency);
public sealed record NormalizedMigrationOpeningBalanceCandidate(string AccountKey, DateOnly EffectiveDate,
    decimal Debit, decimal Credit, string Currency, string EvidenceReference);

public sealed record StageAccountingProviderSwitchRecordCommand(
    Guid CompanyId,
    Guid SwitchId,
    Guid ExtractionBatchId,
    string Dataset,
    string SourceIdentity,
    string SourceVersion,
    DateTime? ProviderModifiedUtc,
    string SourceHash,
    string NormalizedDataJson,
    string EvidenceJson,
    decimal FinancialAmount,
    string? Currency,
    string InitialDisposition,
    Guid ActorUserId,
    string CorrelationId);

public sealed record ListAccountingProviderSwitchStagedRecordsQuery(Guid CompanyId, Guid SwitchId,
    string? Dataset = null, string? Disposition = null, bool IncludeSuperseded = false, int Limit = 200);

public sealed record PreviewAccountingProviderSwitchMappingCommand(
    Guid CompanyId,
    Guid SwitchId,
    string MappingType,
    string SourceKey,
    string? ProposedTargetKey,
    string? SourceSemantic,
    IReadOnlyList<Guid> AffectedStagedRecordIds,
    bool IsMaterial,
    Guid ActorUserId,
    string CorrelationId);

public sealed record RequestAccountingProviderSwitchMappingApprovalCommand(
    Guid CompanyId,
    Guid SwitchId,
    Guid MappingDecisionId,
    long ExpectedVersion,
    Guid ActorUserId,
    string CorrelationId);

public sealed record ResolveAccountingProviderSwitchDispositionCommand(
    Guid CompanyId,
    Guid SwitchId,
    Guid StagedRecordId,
    string Disposition,
    string Reason,
    Guid? MappingDecisionId,
    Guid? DuplicateOfStagedRecordId,
    long ExpectedVersion,
    Guid ActorUserId,
    string CorrelationId);

public sealed record GetAccountingProviderSwitchCompletenessQuery(Guid CompanyId, Guid SwitchId);
public sealed record ListAccountingProviderSwitchMappingsQuery(Guid CompanyId, Guid SwitchId,
    bool IncludeSuperseded = false, int Limit = 200);

public sealed record AccountingProviderSwitchStagedRecordDto(
    Guid Id,
    Guid CompanyId,
    Guid SwitchId,
    Guid ExtractionBatchId,
    string SourceEndpoint,
    string Dataset,
    string SourceIdentity,
    string SourceVersion,
    DateTime? ProviderModifiedUtc,
    string SourceHash,
    string NormalizedHash,
    string NormalizedDataJson,
    string EvidenceJson,
    decimal FinancialAmount,
    string? Currency,
    string Disposition,
    string? DispositionReason,
    Guid? MappingDecisionId,
    int? MappingVersion,
    Guid? ApprovalRequestId,
    Guid? DuplicateOfStagedRecordId,
    bool IsCurrent,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    long Version);

public sealed record AccountingProviderSwitchMappingDecisionDto(
    Guid Id,
    Guid MappingSetId,
    int MappingVersion,
    string MappingType,
    string SourceKey,
    string? TargetKey,
    string SuggestionMethod,
    decimal Confidence,
    string EvidenceJson,
    bool IsMaterial,
    long AffectedRecordCount,
    decimal AffectedFinancialTotal,
    string Status,
    Guid? ApprovalRequestId,
    bool IsApprovalCurrent,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    long Version);

public sealed record AccountingProviderSwitchDispositionCountDto(string Disposition, long Count,
    decimal FinancialTotal);
public sealed record AccountingProviderSwitchDatasetCompletenessDto(string Dataset, long ExpectedCount,
    long StagedCount, long ValidDispositionCount, bool IsComplete, string Explanation);
public sealed record AccountingProviderSwitchCompletenessDto(Guid SwitchId, bool IsComplete, long ExpectedCount,
    long StagedCount, long ValidDispositionCount, long BlockingCount,
    IReadOnlyList<AccountingProviderSwitchDispositionCountDto> Dispositions,
    IReadOnlyList<AccountingProviderSwitchDatasetCompletenessDto> Datasets,
    string Explanation);

public interface IAccountingProviderSwitchStagingService
{
    Task<AccountingProviderSwitchStagedRecordDto> StageAsync(StageAccountingProviderSwitchRecordCommand command,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<AccountingProviderSwitchStagedRecordDto>> ListAsync(
        ListAccountingProviderSwitchStagedRecordsQuery query, CancellationToken cancellationToken);
    Task<AccountingProviderSwitchMappingDecisionDto> PreviewMappingAsync(
        PreviewAccountingProviderSwitchMappingCommand command, CancellationToken cancellationToken);
    Task<IReadOnlyList<AccountingProviderSwitchMappingDecisionDto>> ListMappingsAsync(
        ListAccountingProviderSwitchMappingsQuery query, CancellationToken cancellationToken);
    Task<AccountingProviderSwitchMappingDecisionDto> RequestMappingApprovalAsync(
        RequestAccountingProviderSwitchMappingApprovalCommand command, CancellationToken cancellationToken);
    Task<AccountingProviderSwitchStagedRecordDto> ResolveDispositionAsync(
        ResolveAccountingProviderSwitchDispositionCommand command, CancellationToken cancellationToken);
    Task<AccountingProviderSwitchCompletenessDto> GetCompletenessAsync(
        GetAccountingProviderSwitchCompletenessQuery query, CancellationToken cancellationToken);
}
