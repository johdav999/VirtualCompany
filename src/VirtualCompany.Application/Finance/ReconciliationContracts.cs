using System.Text.Json.Nodes;

using VirtualCompany.Domain.Finance;

namespace VirtualCompany.Application.Finance;

public static class ReconciliationSourceTypes
{
    public const string BankTransaction = "bank_transaction";
    public const string Invoice = "invoice";
    public const string Bill = "bill";
}

public sealed record ReconciliationPaymentCandidate(
    Guid PaymentId,
    decimal Amount,
    string Currency,
    DateTime PaymentDate,
    string CounterpartyReference,
    string? CounterpartyName = null);

public sealed record ScoreBankTransactionPaymentCandidatesQuery(
    Guid CompanyId,
    Guid BankTransactionId,
    decimal Amount,
    string Currency,
    DateTime BookingDate,
    string ReferenceText,
    string Counterparty,
    IReadOnlyList<ReconciliationPaymentCandidate> Candidates);

public sealed record ScoreInvoicePaymentCandidatesQuery(
    Guid CompanyId,
    Guid InvoiceId,
    decimal Amount,
    string Currency,
    DateTime DueUtc,
    string InvoiceNumber,
    string CounterpartyName,
    IReadOnlyList<ReconciliationPaymentCandidate> Candidates);

public sealed record ScoreBillPaymentCandidatesQuery(
    Guid CompanyId,
    Guid BillId,
    decimal Amount,
    string Currency,
    DateTime DueUtc,
    string BillNumber,
    string CounterpartyName,
    IReadOnlyList<ReconciliationPaymentCandidate> Candidates);

public sealed record ReconciliationSuggestionsResult(
    Guid CompanyId,
    string SourceType,
    Guid SourceRecordId,
    ReconciliationScoringSettings Settings,
    IReadOnlyList<ReconciliationSuggestion> Suggestions);

public interface IReconciliationScoringService
{
    Task<ReconciliationSuggestionsResult> ScoreBankTransactionCandidatesAsync(
        ScoreBankTransactionPaymentCandidatesQuery query,
        CancellationToken cancellationToken);

    Task<ReconciliationSuggestionsResult> ScoreInvoiceCandidatesAsync(
        ScoreInvoicePaymentCandidatesQuery query,
        CancellationToken cancellationToken);

    Task<ReconciliationSuggestionsResult> ScoreBillCandidatesAsync(
        ScoreBillPaymentCandidatesQuery query,
        CancellationToken cancellationToken);
}

public interface IReconciliationScoringSettingsProvider
{
    Task<ReconciliationScoringSettings> GetSettingsAsync(
        Guid companyId,
        CancellationToken cancellationToken);
}

public sealed record CreateReconciliationSuggestionCommand(
    Guid CompanyId,
    string SourceRecordType,
    Guid SourceRecordId,
    string TargetRecordType,
    Guid TargetRecordId,
    string MatchType,
    decimal ConfidenceScore,
    IDictionary<string, JsonNode?>? RuleBreakdown,
    Guid ActorUserId);

public sealed record AcceptReconciliationSuggestionCommand(
    Guid CompanyId,
    Guid SuggestionId,
    Guid ActorUserId);

public sealed record RejectReconciliationSuggestionCommand(
    Guid CompanyId,
    Guid SuggestionId,
    Guid ActorUserId);

public sealed record GetOpenReconciliationSuggestionsQuery(
    Guid CompanyId,
    string? SourceRecordType = null,
    Guid? SourceRecordId = null,
    string? TargetRecordType = null,
    Guid? TargetRecordId = null,
    int Limit = 100);

public sealed record GetReconciliationSuggestionsQuery(
    Guid CompanyId,
    string? EntityType = null,
    string? Status = null,
    decimal? MinConfidenceScore = null,
    int Page = 1,
    int PageSize = 50);

public sealed record ReconciliationSuggestionPageDto(
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages,
    IReadOnlyList<ReconciliationSuggestionRecordDto> Items);

public sealed record ReconciliationSuggestionRecordDto(
    Guid Id,
    Guid CompanyId,
    string SourceRecordType,
    Guid SourceRecordId,
    string TargetRecordType,
    Guid TargetRecordId,
    string MatchType,
    decimal ConfidenceScore,
    IReadOnlyDictionary<string, JsonNode?> RuleBreakdown,
    string Status,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    Guid CreatedByUserId,
    Guid UpdatedByUserId,
    DateTime? AcceptedUtc,
    DateTime? RejectedUtc,
    DateTime? SupersededUtc);

public sealed record ReconciliationResultRecordDto(
    Guid Id,
    Guid CompanyId,
    Guid AcceptedSuggestionId,
    string SourceRecordType,
    Guid SourceRecordId,
    string TargetRecordType,
    Guid TargetRecordId,
    string MatchType,
    decimal ConfidenceScore,
    IReadOnlyDictionary<string, JsonNode?> RuleBreakdown,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    Guid CreatedByUserId,
    Guid UpdatedByUserId);

public sealed record AcceptedReconciliationSuggestionDto(
    ReconciliationSuggestionRecordDto Suggestion,
    ReconciliationResultRecordDto Result,
    int SupersededSuggestionCount);

public interface IReconciliationSuggestionReadService
{
    Task<ReconciliationSuggestionPageDto> GetSuggestionsAsync(
        GetReconciliationSuggestionsQuery query,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ReconciliationSuggestionRecordDto>> GetOpenSuggestionsAsync(
        GetOpenReconciliationSuggestionsQuery query,
        CancellationToken cancellationToken);
}

public interface IReconciliationSuggestionCommandService
{
    Task<ReconciliationSuggestionRecordDto> CreateSuggestionAsync(
        CreateReconciliationSuggestionCommand command,
        CancellationToken cancellationToken);

    Task<AcceptedReconciliationSuggestionDto> AcceptSuggestionAsync(
        AcceptReconciliationSuggestionCommand command,
        CancellationToken cancellationToken);

    Task<ReconciliationSuggestionRecordDto> RejectSuggestionAsync(
        RejectReconciliationSuggestionCommand command,
        CancellationToken cancellationToken);
}
