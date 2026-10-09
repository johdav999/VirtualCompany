using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Application.Agents;
using VirtualCompany.Shared;

namespace VirtualCompany.Application.Finance;

public sealed record GetFinanceInvoicesQuery(
    Guid CompanyId,
    DateTime? StartUtc = null,
    DateTime? EndUtc = null,
    int Limit = 100,
    string SourceFilter = FinanceDataSources.Operational);

public sealed record GetFinanceInvoiceDetailQuery(
    Guid CompanyId,
    Guid InvoiceId,
    string SourceFilter = FinanceDataSources.Operational);

public sealed record UpdateFinanceInvoiceApprovalStatusCommand(
    Guid CompanyId,
    Guid InvoiceId,
    string Status);

public sealed record ReviewFinanceInvoiceWorkflowCommand(
    Guid CompanyId,
    Guid InvoiceId,
    Guid? WorkflowInstanceId,
    Guid? AgentId,
    Dictionary<string, JsonNode?>? Payload,
    string SourceFilter = FinanceDataSources.Operational);

public sealed record FinanceOverdueInvoiceRecommendationDto(
    int Rank,
    Guid InvoiceId,
    string InvoiceNumber,
    string CustomerName,
    DateTime DueUtc,
    decimal OutstandingAmount,
    string Currency,
    int OverdueDays,
    string Severity,
    string RecommendationCode,
    string RecommendationText,
    string AgingBucket = "current",
    int PaymentPatternScore = 45,
    string PaymentPatternSeverity = "medium_risk",
    string PaymentPatternConfidence = "no_history",
    string RecommendationSeverity = "medium",
    int PriorityScore = 0,
    string RecommendationType = "follow_up",
    string ScoringFactors = "");

public sealed record FinanceOpenReceivableItemDto(
    Guid InvoiceId,
    string InvoiceNumber,
    string CustomerName,
    DateTime DueUtc,
    decimal OutstandingAmount,
    string Currency,
    string Status,
    Guid? CustomerId = null,
    decimal? FunctionalOutstandingAmount = null,
    string? FunctionalCurrency = null,
    decimal? ExchangeRate = null,
    DateOnly? ExchangeRateDate = null,
    string? ExchangeRateIdentity = null);

public sealed record FinanceInvoiceApprovalRecommendationDto(
    Guid InvoiceId,
    string RecommendedStatus,
    decimal Confidence);

public sealed record FinanceInvoiceReviewWorkflowResultDto(
    Guid CompanyId,
    Guid InvoiceId,
    Guid? WorkflowInstanceId,
    Guid TaskId,
    Guid? ApprovalRequestId,
    string InvoiceClassification,
    string RiskLevel,
    string RecommendedAction,
    string Rationale,
    decimal ConfidenceScore,
    bool RequiresHumanApproval,
    FinanceInvoiceDto Invoice,
    FinancePolicyConfigurationDto Policy,
    Dictionary<string, JsonNode?> OutputPayload)
{
    public string ReviewTaskStatus { get; init; } = "new";
    public DateTime LastUpdatedUtc { get; init; } = DateTime.UtcNow;
    public FinanceWorkflowOutputSchemaDto WorkflowOutput { get; init; } =
        FinanceWorkflowOutputSchemas.Create("invoice_review", "low", "no_action", "No workflow output was recorded.", 0m, "invoice_review");
}

public interface IInvoiceReviewWorkflowService
{
    Task<FinanceInvoiceReviewWorkflowResultDto> ExecuteAsync(ReviewFinanceInvoiceWorkflowCommand command, CancellationToken cancellationToken);
    Task<FinanceInvoiceReviewWorkflowResultDto?> GetLatestByInvoiceAsync(
        Guid companyId,
        Guid invoiceId,
        CancellationToken cancellationToken,
        string sourceFilter = FinanceDataSources.Operational);
}

