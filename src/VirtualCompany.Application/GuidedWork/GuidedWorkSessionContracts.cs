using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using VirtualCompany.Application.Companies;

namespace VirtualCompany.Application.GuidedWork;

public static class GuidedArtifactTypes
{
    public const string AgentOperatingBrief = "agent_operating_brief";
    public const string CompanyOnboarding = "company_onboarding";
    public const string MarketingStrategy = "marketing_strategy";
    public const string MarketingSegment = "marketing_segment";
    public const string MarketingPlan = "marketing_plan";
    public const string FinanceBudget = "finance_budget";
    public const string SalesCampaignPlan = "sales_campaign_plan";
    public const string SupportSlaPolicy = "support_sla_policy";
}

public static class GuidedFieldValueTypes
{
    public const string Text = "text";
    public const string Number = "number";
    public const string Boolean = "boolean";
    public const string Date = "date";
    public const string Identifier = "identifier";
    public const string TextList = "text_list";
    public const string Object = "object";
}

public static class GuidedWorkshopFields
{
    public const string InsightsPath = "workshop_insights";
    public static GuidedFieldDefinition Insights { get; } = new(
        InsightsPath,
        "Workshop insights",
        "Keep relevant information that does not yet have a safe destination in the current artifact. Include what was learned, why it matters, and one or more suggested destinations. This field is retained with the workshop but is not committed into the business artifact until it is deliberately mapped.",
        GuidedFieldValueTypes.Text,
        false,
        MaxLength: 8000);

    public static bool IsInsights(string? path) => string.Equals(path, InsightsPath, StringComparison.OrdinalIgnoreCase);
    public static GuidedFieldDefinition? Resolve(IGuidedArtifactDefinition definition, string? path) =>
        definition.Fields.SingleOrDefault(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase)) ??
        (IsInsights(path) ? Insights : null);
}

public sealed record StartGuidedWorkSessionCommand(string ArtifactType, Guid AgentId, Guid? TargetArtifactId = null);
public sealed record AddGuidedWorkTurnCommand(
    string Body,
    Guid ClientRequestId,
    int ExpectedVersion,
    string Modality = "text",
    string? ProviderEventId = null,
    bool Interrupted = false,
    int? DurationMs = null,
    string? TransportVersion = null);
public sealed record RecordGuidedVoiceAgentMessageCommand(string ProviderResponseId, string Body);
public sealed record CorrectGuidedDraftFieldCommand(JsonNode? Value, string Status, Guid ClientRequestId, int ExpectedVersion);
public sealed record ChangeGuidedDraftFieldStatusesCommand(
    IReadOnlyList<string> Paths,
    string? FromStatus,
    string Status,
    string? Explanation,
    Guid ClientRequestId,
    int ExpectedVersion);
public sealed record PrepareGuidedWorkReviewCommand(Guid ClientRequestId, int ExpectedVersion);
public sealed record ConfirmGuidedWorkCommitCommand(string ReviewToken, Guid ClientRequestId, int ExpectedVersion);
public sealed record CancelGuidedWorkSessionCommand(Guid ClientRequestId, int ExpectedVersion);
public sealed record ListGuidedWorkSessionsQuery(string? Status = null, string? ArtifactType = null, int? Skip = null, int? Take = null);

public sealed record GuidedFieldDefinition(
    string Path,
    string Label,
    string Description,
    string ValueType,
    bool IsRequired,
    IReadOnlyList<string>? AllowedValues = null,
    int? MaxLength = null,
    decimal? Minimum = null,
    decimal? Maximum = null,
    bool AllowsEvidence = false);

public sealed record GuidedArtifactCapabilities(
    bool SupportsDocumentAttachments = false,
    IReadOnlyList<string>? AllowedDocumentExtensions = null,
    IReadOnlyList<string>? DocumentDataScopes = null,
    bool SupportsVoiceDocumentSearch = false,
    bool SupportsExternalResearch = false)
{
    public IReadOnlyList<string> EffectiveAllowedDocumentExtensions => AllowedDocumentExtensions ?? [];
    public IReadOnlyList<string> EffectiveDocumentDataScopes => DocumentDataScopes ?? [];
}

public sealed record GuidedDraftFieldDto(
    Guid Id, string Path, string Label, string Description, string ValueType, bool IsRequired,
    JsonNode? Value, string Status, string SourceType, Guid? SourceMessageId,
    IReadOnlyDictionary<string, JsonNode?> SourceMetadata, string? Explanation,
    IReadOnlyList<string> AllowedValues, int Version, DateTime UpdatedAt);

public sealed record GuidedWorkMessageDto(Guid Id, string SenderType, Guid? SenderId, string Body, DateTime CreatedAt);

public sealed record GuidedWorkSessionDto(
    Guid Id, Guid CompanyId, Guid ConversationId, Guid AgentId, string AgentDisplayName,
    string AgentRoleName, string ArtifactType, string ArtifactLabel, string SchemaVersion,
    Guid? TargetArtifactId, string? TargetArtifactVersion, string Status, int Sequence, int Version,
    int RequiredFieldCount, int ReadyFieldCount, string SafeSummary, string? NextQuestion,
    GuidedArtifactCapabilities Capabilities,
    IReadOnlyList<GuidedDraftFieldDto> Fields, IReadOnlyList<GuidedWorkMessageDto> Messages,
    DateTime CreatedAt, DateTime UpdatedAt, DateTime? CompletedAt, DateTime? CancelledAt);

public sealed record GuidedWorkSessionListDto(IReadOnlyList<GuidedWorkSessionDto> Items, int TotalCount, int Skip, int Take);
public sealed record GuidedFieldChangeDto(string Path, string Label, JsonNode? PreviousValue, JsonNode? Value, string Status, string Explanation);
public sealed record GuidedWorkTurnResultDto(GuidedWorkSessionDto Session, GuidedWorkMessageDto UserMessage,
    GuidedWorkMessageDto AgentMessage, IReadOnlyList<GuidedFieldChangeDto> Changes);
public sealed record GuidedWorkReviewDto(GuidedWorkSessionDto Session, string ReviewToken,
    DateTime ExpiresAt, IReadOnlyList<string> MissingFields, IReadOnlyList<string> Conflicts,
    IReadOnlyList<GuidedFieldChangeDto> ProposedChanges, IReadOnlyList<GuidedReviewInsightDto> Insights);
public sealed record GuidedReviewInsightDto(string Label, string Value, string Meaning);
public sealed record GuidedWorkCommitResultDto(GuidedWorkSessionDto Session, string ArtifactType,
    Guid? ArtifactId, string? ArtifactVersion, string Summary);

public sealed record GuidedArtifactInitialization(
    string ArtifactLabel,
    Guid? TargetArtifactId,
    string? TargetArtifactVersion,
    IReadOnlyDictionary<string, JsonNode?> InitialValues,
    IReadOnlyDictionary<string, string>? InitialStatuses = null,
    string? OpeningSummary = null,
    string? OpeningQuestion = null);

public sealed record GuidedArtifactCommitContext(
    Guid CompanyId, Guid SessionId, Guid AgentId, Guid UserId, Guid? TargetArtifactId,
    string? TargetArtifactVersion, IReadOnlyDictionary<string, JsonNode?> Values, string CorrelationId);
public sealed record GuidedArtifactCommitResult(Guid? ArtifactId, string? ArtifactVersion, string Summary);

public interface IGuidedArtifactDefinition
{
    string ArtifactType { get; }
    string SchemaVersion { get; }
    string DisplayName { get; }
    IReadOnlyList<GuidedFieldDefinition> Fields { get; }
    GuidedArtifactCapabilities Capabilities => new();
    bool RequiresTargetArtifact => false;
    IReadOnlyList<string> QuestionPriorities => Fields.Where(x => x.IsRequired).Select(x => x.Path).ToArray();
    Task<GuidedArtifactInitialization> InitializeAsync(Guid companyId, Guid agentId, Guid? targetArtifactId, CancellationToken cancellationToken);
    Task EnsureEligibleAsync(Guid companyId, Guid agentId, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> ValidateAsync(Guid companyId, Guid agentId, Guid? targetArtifactId,
        IReadOnlyDictionary<string, JsonNode?> values, CancellationToken cancellationToken);
    Task<IReadOnlyList<GuidedReviewInsightDto>> BuildReviewInsightsAsync(Guid companyId, Guid agentId, Guid? targetArtifactId,
        IReadOnlyDictionary<string, JsonNode?> values, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<GuidedReviewInsightDto>>([]);
    Task<GuidedArtifactCommitResult> CommitAsync(GuidedArtifactCommitContext context, CancellationToken cancellationToken);
}

public sealed record GuidedCheckpointField(string Path, string Label, string ValueType, bool IsRequired,
    JsonNode? Value, string Status, IReadOnlyList<string> AllowedValues,
    string Description = "", int? MaxLength = null, decimal? Minimum = null, decimal? Maximum = null,
    bool AllowsEvidence = false);
public sealed record GuidedCheckpointConversationTurn(string SenderType, string Body);
public sealed record GuidedCheckpointRequest(Guid CompanyId, Guid SessionId, Guid AgentId, string ArtifactType,
    string SchemaVersion, int ExpectedVersion, string UserMessage, string SafeContext,
    IReadOnlyList<GuidedCheckpointConversationTurn> RecentConversation,
    IReadOnlyList<GuidedCheckpointField> Fields, IReadOnlyList<string> QuestionPriorities,
    string AttachedDocumentContext = "No ready workshop documents are attached.",
    string PublicResearchContext = "Public research has not been performed for this turn.");
public sealed record GuidedPatchOperation(string Path, JsonNode? Value, string Status, string SourceType,
    string Explanation, IReadOnlyDictionary<string, JsonNode?>? SourceMetadata = null);
public sealed record GuidedFieldStatusChangeOperation(string Path, string Status, string Explanation);
public sealed record GuidedCheckpointResult(string AgentMessage, IReadOnlyList<GuidedPatchOperation> Patches,
    IReadOnlyList<string> Confirmations, IReadOnlyList<string> Assumptions, IReadOnlyList<string> Conflicts,
    IReadOnlyList<string> MissingFields, string SafeSummary, string? NextQuestion, bool IsReady,
    string? ResearchQuery = null,
    IReadOnlyList<GuidedFieldStatusChangeOperation>? StatusChanges = null);

public interface IGuidedCheckpointProvider
{
    Task<GuidedCheckpointResult> CreateCheckpointAsync(GuidedCheckpointRequest request, CancellationToken cancellationToken);
}

public sealed record UploadGuidedWorkshopDocumentCommand(
    string Title, string OriginalFileName, string? ContentType, long Length, Stream Content);
public sealed record GuidedWorkshopDocumentDto(
    Guid DocumentId, string Title, string OriginalFileName, long FileSizeBytes, string Status,
    string StatusLabel, bool IsReady, string? FailureMessage, DateTime UpdatedAt);
public interface IGuidedWorkshopDocumentService
{
    Task<IReadOnlyList<GuidedWorkshopDocumentDto>> ListAsync(Guid companyId, Guid sessionId, CancellationToken cancellationToken);
    Task<GuidedWorkshopDocumentDto> UploadAsync(Guid companyId, Guid sessionId, UploadGuidedWorkshopDocumentCommand command, CancellationToken cancellationToken);
    Task<string> SearchAsync(Guid companyId, Guid sessionId, Guid agentId, string query, CancellationToken cancellationToken);
    Task<string> SearchForAuthorizedVoiceSessionAsync(Guid companyId, Guid sessionId, Guid agentId, Guid userId, string query, CancellationToken cancellationToken);
}

public interface IGuidedWorkSessionService
{
    Task<IReadOnlyList<GuidedArtifactOptionDto>> ListArtifactOptionsAsync(Guid companyId, Guid agentId, CancellationToken cancellationToken);
    Task<GuidedWorkSessionDto> StartAsync(Guid companyId, StartGuidedWorkSessionCommand command, CancellationToken cancellationToken);
    Task<GuidedWorkSessionListDto> ListAsync(Guid companyId, ListGuidedWorkSessionsQuery query, CancellationToken cancellationToken);
    Task<GuidedWorkSessionDto> GetAsync(Guid companyId, Guid sessionId, CancellationToken cancellationToken);
    Task<GuidedWorkTurnResultDto> AddTurnAsync(Guid companyId, Guid sessionId, AddGuidedWorkTurnCommand command, CancellationToken cancellationToken);
    Task<GuidedWorkMessageDto> RecordVoiceAgentMessageAsync(Guid companyId, Guid sessionId, RecordGuidedVoiceAgentMessageCommand command, CancellationToken cancellationToken);
    Task<GuidedWorkSessionDto> CorrectFieldAsync(Guid companyId, Guid sessionId, string path, CorrectGuidedDraftFieldCommand command, CancellationToken cancellationToken);
    Task<GuidedWorkSessionDto> ChangeFieldStatusesAsync(Guid companyId, Guid sessionId, ChangeGuidedDraftFieldStatusesCommand command, CancellationToken cancellationToken);
    Task<GuidedWorkReviewDto> PrepareReviewAsync(Guid companyId, Guid sessionId, PrepareGuidedWorkReviewCommand command, CancellationToken cancellationToken);
    Task<GuidedWorkCommitResultDto> ConfirmCommitAsync(Guid companyId, Guid sessionId, ConfirmGuidedWorkCommitCommand command, CancellationToken cancellationToken);
    Task<GuidedWorkSessionDto> CancelAsync(Guid companyId, Guid sessionId, CancelGuidedWorkSessionCommand command, CancellationToken cancellationToken);
}
public sealed record GuidedArtifactOptionDto(string ArtifactType,string DisplayName,string SchemaVersion,bool RequiresTargetArtifact,GuidedArtifactCapabilities Capabilities);

public sealed record GuidedRealtimeCallResult(string AnswerSdp, Guid VoiceBindingId, DateTime ExpiresAt);
public interface IGuidedRealtimeCallService
{
    Task<GuidedRealtimeCallResult> CreateCallAsync(Guid companyId, Guid sessionId, string offerSdp, CancellationToken cancellationToken);
    Task EndCallAsync(Guid companyId, Guid sessionId, Guid voiceBindingId, CancellationToken cancellationToken);
}

public interface IGuidedVoiceToolService
{
    Task<string> ExecuteAsync(string providerCallId, string providerToolCallId, string toolName, string argumentsJson, CancellationToken cancellationToken);
}

public sealed record GuidedEvidenceSource(string Title, string Url);
public sealed record GuidedEvidenceResearchResult(
    bool Available,
    string Summary,
    IReadOnlyList<GuidedEvidenceSource> Sources,
    string? FailureCode = null);

public interface IGuidedEvidenceResearchService
{
    Task<GuidedEvidenceResearchResult> ResearchAsync(
        Guid companyId,
        Guid agentId,
        string query,
        CancellationToken cancellationToken);
}

public interface IGuidedResearchContinuationService
{
    Task ProcessAsync(GuidedResearchContinuationRequestedMessage request, CancellationToken cancellationToken);
}

public sealed class GuidedWorkValidationException : Exception
{
    public GuidedWorkValidationException(IDictionary<string, string[]> errors) : base("Guided work validation failed.") =>
        Errors = new ReadOnlyDictionary<string, string[]>(new Dictionary<string, string[]>(errors, StringComparer.OrdinalIgnoreCase));
    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
public sealed class GuidedWorkConflictException(string message) : Exception(message);
public sealed class GuidedCheckpointUnavailableException(string message) : Exception(message);
public sealed class GuidedRealtimeRateLimitedException(string message, int? retryAfterSeconds = null) : Exception(message)
{
    public int? RetryAfterSeconds { get; } = retryAfterSeconds;
}
public sealed class GuidedArtifactNotEligibleException(string message) : UnauthorizedAccessException(message);
