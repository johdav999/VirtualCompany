namespace VirtualCompany.Application.Sales;

public interface ILeadGenerationService
{
    Task<IReadOnlyList<IcpProfileDto>> ListProfilesAsync(Guid companyId, CancellationToken ct);
    Task<IcpProfileDto> CreateProfileAsync(Guid companyId, Guid userId, SaveIcpProfileRequest request, CancellationToken ct);
    Task<IcpProfileDto> UpdateProfileAsync(Guid companyId, Guid userId, Guid id, SaveIcpProfileRequest request, CancellationToken ct);
    Task<IcpProfileDto> ActivateProfileAsync(Guid companyId, Guid userId, Guid id, CancellationToken ct);
    Task<IcpProfileDto> CloneProfileAsync(Guid companyId, Guid userId, Guid id, CancellationToken ct);
    Task ArchiveProfileAsync(Guid companyId, Guid userId, Guid id, CancellationToken ct);
    Task<IcpPreviewDto> PreviewProfileAsync(Guid companyId, Guid id, ProspectAccountInput request, CancellationToken ct);
    Task<SourcePolicyDto> GetSourcePolicyAsync(Guid companyId, CancellationToken ct);
    Task<SourcePolicyDto> UpdateSourcePolicyAsync(Guid companyId, Guid userId, SaveSourcePolicyRequest request, CancellationToken ct);
    Task<IReadOnlyList<ProspectingRunDto>> ListRunsAsync(Guid companyId, CancellationToken ct);
    Task<ProspectingRunDto> CreateRunAsync(Guid companyId, Guid userId, CreateProspectingRunRequest request, CancellationToken ct);
    Task<ProspectingRunDto> StartRunAsync(Guid companyId, Guid userId, Guid id, CancellationToken ct);
    Task<ProspectingRunDto> ChangeRunAsync(Guid companyId, Guid userId, Guid id, string action, CancellationToken ct);
    Task<ImportResultDto> ImportCsvAsync(Guid companyId, Guid userId, Guid runId, Stream content, string fileName, CancellationToken ct);
    Task<ProspectPageDto> ListAccountsAsync(Guid companyId, ProspectQuery query, CancellationToken ct);
    Task<ProspectAccountDto?> GetAccountAsync(Guid companyId, Guid id, CancellationToken ct);
    Task<ProspectAccountDto> AddAccountAsync(Guid companyId, Guid userId, Guid runId, ProspectAccountInput request, CancellationToken ct);
    Task<ProspectAccountDto> ReviewAccountAsync(Guid companyId, Guid userId, Guid id, ReviewProspectRequest request, CancellationToken ct);
    Task<ProspectAccountDto> MergeAccountAsync(Guid companyId, Guid userId, Guid sourceId, Guid targetId, CancellationToken ct);
    Task<ProspectContactDto> AddContactAsync(Guid companyId, Guid userId, Guid accountId, SaveProspectContactRequest request, CancellationToken ct);
    Task<ProspectContactDto> ReviewContactAsync(Guid companyId, Guid userId, Guid id, ReviewProspectRequest request, CancellationToken ct);
    Task<ProspectContactDto> MergeContactAsync(Guid companyId, Guid userId, Guid sourceId, Guid targetId, CancellationToken ct);
    Task<ProspectSignalDto> AddSignalAsync(Guid companyId, Guid userId, Guid accountId, SaveProspectSignalRequest request, CancellationToken ct);
    Task<ProspectSignalDto> ReviewSignalAsync(Guid companyId, Guid userId, Guid id, string action, CancellationToken ct);
    Task<ProspectAccountDto> RefreshResearchAndScoreAsync(Guid companyId, Guid userId, Guid accountId, CancellationToken ct);
    Task<LeadConversionDto> ConvertAsync(Guid companyId, Guid userId, Guid accountId, Guid? contactId, CancellationToken ct);
    Task<IReadOnlyList<SuppressionDto>> ListSuppressionsAsync(Guid companyId, CancellationToken ct);
    Task<SuppressionDto> AddSuppressionAsync(Guid companyId, Guid userId, SaveSuppressionRequest request, CancellationToken ct);
    Task RemoveSuppressionAsync(Guid companyId, Guid userId, Guid id, CancellationToken ct);
    Task<LeadGenerationMetricsDto> GetMetricsAsync(Guid companyId, CancellationToken ct);
    Task<byte[]> ExportCsvAsync(Guid companyId, CancellationToken ct);
    Task<CrmDeliveryStatusDto> GetCrmStatusAsync(Guid companyId, CancellationToken ct);
    Task<CrmSyncResultDto> SyncLeadAsync(Guid companyId, Guid userId, Guid accountId, string providerKey, CancellationToken ct);
}

public interface IIcpSuggestionService
{
    Task<IcpSuggestionDto> SuggestAsync(
        Guid companyId,
        Guid userId,
        SuggestIcpRequest request,
        CancellationToken cancellationToken);
}
public interface IProspectDataProvider
{
    string Key { get; }
    ProspectProviderCapabilities Capabilities { get; }
    Task<ProspectProviderPage> SearchAccountsAsync(Guid companyId, ProspectProviderSearch request, CancellationToken ct);
}

public interface IProspectDataProviderRegistry
{
    IReadOnlyList<ProspectProviderDescriptor> List();
    IProspectDataProvider Resolve(string key);
}

public interface ICrmLeadAdapter
{
    string Key { get; }
    Task<CrmAdapterStatus> GetStatusAsync(Guid companyId, CancellationToken ct);
    Task<CrmSyncResultDto> UpsertLeadAsync(Guid companyId, Guid leadId, string idempotencyKey, CancellationToken ct);
}

public interface ICrmLeadAdapterRegistry
{
    IReadOnlyList<string> Keys { get; }
    ICrmLeadAdapter Resolve(string key);
}
public sealed record ProspectProviderSearch(Guid ProfileId, int Limit, string? Cursor, string Countries, string Industries);
public sealed record ProspectProviderPage(IReadOnlyList<ProspectAccountInput> Accounts, string? NextCursor, decimal Cost, bool Complete);
public sealed record IcpCriterionDto(string Criterion, string Outcome, string Explanation);
public sealed record IcpPreviewDto(string Outcome, decimal FitScore, IReadOnlyList<IcpCriterionDto> Criteria);
public sealed record ProspectAccountInput(string Name, string? Domain, string? Country, string? Industry, int? Employees, decimal? Revenue, string? Technologies, string SourceKey, string SourceReference, DateTime? ObservedUtc = null);
public sealed record ProspectQuery(string? Search, string? Status, string? Country, string? Source, int Page = 1, int PageSize = 50, string Sort = "score");
public sealed record MergeProspectRequest(Guid TargetId);
public sealed record SignalReviewRequest(string Action);
public sealed record CrmAdapterStatus(string Key, string Label, bool Connected, string Health, DateTime? LastSyncUtc);
public sealed record CrmDeliveryStatusDto(bool InternalWorkspaceActive, IReadOnlyList<CrmAdapterStatus> Providers, bool SpreadsheetExportAvailable);
public sealed record CrmSyncResultDto(string ProviderKey, Guid LeadId, string ExternalReference, string Status, bool ExistingRecord);

public sealed class LeadGenerationValidationException : Exception
{
    public LeadGenerationValidationException(string message) : base(message) { }
}
