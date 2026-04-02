using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace VirtualCompany.Application.Memory;

public sealed record CreateMemoryItemCommand
{
    public Guid? AgentId { get; init; }
    public string MemoryType { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string? SourceEntityType { get; init; }
    public Guid? SourceEntityId { get; init; }
    public decimal Salience { get; init; }
    public Dictionary<string, JsonNode?>? Metadata { get; init; }
    public DateTime? ValidFromUtc { get; init; }
    public DateTime? ValidToUtc { get; init; }

    // Capture unsupported request fields so memory writes can reject hidden-reasoning payloads explicitly.
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; init; }
}

public sealed record MemorySearchFilters(
    Guid? AgentId,
    string? MemoryType,
    DateTime? CreatedAfterUtc,
    DateTime? CreatedBeforeUtc,
    decimal? MinSalience,
    bool OnlyActive = true,
    bool IncludeDeleted = false,
    bool IncludeCompanyWide = true,
    string? QueryText = null,
    int Offset = 0,
    int Limit = 20,
    string? Scope = null,
    IReadOnlyList<string>? MemoryTypes = null,
    DateTime? AsOfUtc = null);

public sealed record MemoryRetrievalRequest(
    Guid CompanyId,
    Guid? AgentId,
    IReadOnlyList<string>? MemoryTypes = null,
    string? QueryText = null,
    IReadOnlyList<float>? QueryEmbedding = null,
    int Top = 10,
    DateTime? AsOfUtc = null,
    bool IncludeCompanyWide = true,
    string? Scope = null);

public static class MemoryDeletionModes
{
    public const string SoftDelete = "soft_delete";
    public const string HardDelete = "hard_delete";
}

// Lifecycle commands carry the tenant/resource identity so mutation paths can stay
// company-scoped while leaving room for future privacy policy inputs.
public sealed record ExpireMemoryItemCommand
{
    public Guid CompanyId { get; init; }
    public Guid MemoryItemId { get; init; }
    public DateTime? ValidToUtc { get; init; }
    public string? Reason { get; init; }
    public string? PolicyContext { get; init; }
}

public sealed record DeleteMemoryItemCommand
{
    public Guid CompanyId { get; init; }
    public Guid MemoryItemId { get; init; }
    public string? Reason { get; init; }
    public string DeletionMode { get; init; } = MemoryDeletionModes.SoftDelete;
    public string? PolicyContext { get; init; }
}

public sealed record MemoryItemDto(
    Guid Id,
    Guid CompanyId,
    Guid? AgentId,
    string Scope,
    string MemoryType,
    string Summary,
    string? SourceEntityType,
    Guid? SourceEntityId,
    decimal Salience,
    IReadOnlyDictionary<string, JsonNode?> Metadata,
    DateTime ValidFromUtc,
    DateTime? ValidToUtc,
    DateTime CreatedUtc,
    double? SemanticScore = null,
    double? RecencyScore = null,
    double? CombinedScore = null);

public sealed record MemorySearchResultDto(
    IReadOnlyList<MemoryItemDto> Items,
    int TotalCount,
    bool SemanticSearchApplied);

public sealed record MemoryRetrievalResultDto(
    IReadOnlyList<MemoryItemDto> Items,
    bool SemanticSearchApplied);

public interface ICompanyMemoryService
{
    Task<MemoryItemDto?> GetAsync(Guid companyId, Guid memoryId, CancellationToken cancellationToken);
    Task<MemoryItemDto> CreateAsync(Guid companyId, CreateMemoryItemCommand command, CancellationToken cancellationToken);
    Task<MemorySearchResultDto> SearchAsync(Guid companyId, MemorySearchFilters filters, CancellationToken cancellationToken);
    Task<MemoryItemDto> ExpireAsync(ExpireMemoryItemCommand command, CancellationToken cancellationToken);
    Task DeleteAsync(DeleteMemoryItemCommand command, CancellationToken cancellationToken);
}

public interface IMemoryRetrievalService
{
    Task<MemoryRetrievalResultDto> RetrieveAsync(MemoryRetrievalRequest request, CancellationToken cancellationToken);
}

public sealed class MemoryValidationException : Exception
{
    public MemoryValidationException(IDictionary<string, string[]> errors)
        : base("Memory validation failed.")
    {
        Errors = new ReadOnlyDictionary<string, string[]>(new Dictionary<string, string[]>(errors, StringComparer.OrdinalIgnoreCase));
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}

public sealed class MemorySearchValidationException : Exception
{
    public MemorySearchValidationException(string message)
        : base(message)
    {
    }
}