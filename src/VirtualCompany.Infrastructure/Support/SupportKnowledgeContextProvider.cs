using System.Text.Json.Nodes;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Approvals;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Application.Support;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Mailbox;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Security;

namespace VirtualCompany.Infrastructure.Support;

public sealed class SupportKnowledgeContextProvider : ISupportKnowledgeContextProvider
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyKnowledgeSearchService? _knowledgeSearch;

    public SupportKnowledgeContextProvider(VirtualCompanyDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public SupportKnowledgeContextProvider(
        VirtualCompanyDbContext dbContext,
        ICompanyKnowledgeSearchService knowledgeSearch)
    {
        _dbContext = dbContext;
        _knowledgeSearch = knowledgeSearch;
    }

    public async Task<SupportKnowledgeContext> RetrieveAsync(Guid companyId, Guid supportCaseId, CancellationToken cancellationToken)
    {
        var supportCase = await _dbContext.SupportCases.AsNoTracking()
            .Include(x => x.Messages)
            .FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Id == supportCaseId, cancellationToken);
        if (supportCase is null)
        {
            return new SupportKnowledgeContext(supportCaseId, [], [], [], 0m, "Support case was not found.");
        }

        var queryTerms = BuildQueryTerms(supportCase);
        var sources = new List<SupportKnowledgeSourceReference>
        {
            new("support_case", $"Support case {supportCase.CaseNumber}", supportCase.Id, TrimForExcerpt($"{supportCase.Subject}. {supportCase.Summary} {supportCase.Description}"), 1m)
        };

        var queryText = string.Join(' ', queryTerms);
        if (!string.IsNullOrWhiteSpace(queryText))
        {
            var searchResults = _knowledgeSearch is null
                ? await SearchIndexedKnowledgeForTestsAsync(companyId, queryTerms, cancellationToken)
                : await _knowledgeSearch.SearchAsync(
                    new CompanyKnowledgeSemanticSearchQuery(
                        companyId,
                        queryText,
                        20,
                        new CompanyKnowledgeAccessContext(companyId, DataScopes: ["support", "knowledge"])),
                    cancellationToken);
            sources.AddRange(RankCustomerFacingResults(searchResults, queryTerms)
                .Take(4)
                .Select(x => new SupportKnowledgeSourceReference(
                    "knowledge_chunk",
                    x.Result.DocumentTitle,
                    x.Result.ChunkId,
                    TrimForExcerpt(x.Result.Content),
                    x.Relevance,
                    true,
                    x.Result.DocumentId,
                    x.Result.SourceReference)));
        }

        var similarCases = await _dbContext.SupportCases.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Id != supportCase.Id && x.Category == supportCase.Category && (x.Status == SupportCaseStatuses.Resolved || x.Status == SupportCaseStatuses.Closed))
            .OrderByDescending(x => x.UpdatedUtc)
            .Take(3)
            .Select(x => $"{x.CaseNumber}: {x.Subject} - {x.Summary}")
            .ToListAsync(cancellationToken);

        var memories = new List<string>();
        if (supportCase.ContactId is Guid contactId)
        {
            var profile = await _dbContext.CustomerMemoryProfiles.AsNoTracking()
                .Include(x => x.Preferences)
                .FirstOrDefaultAsync(x => x.CompanyId == companyId && x.ContactId == contactId, cancellationToken);
            if (profile is not null)
            {
                memories.AddRange(profile.Preferences
                    .Where(x => x.PreferenceKey.Contains("support", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(x => x.ObservedUtc)
                    .Take(3)
                    .Select(x => x.PreferenceValue));
            }
        }

        foreach (var similar in similarCases)
        {
            sources.Add(new SupportKnowledgeSourceReference("support_case_history", "Resolved similar support case", null, TrimForExcerpt(similar), 0.65m));
        }

        foreach (var memory in memories)
        {
            sources.Add(new SupportKnowledgeSourceReference("customer_memory", "Customer support memory", supportCase.ContactId, TrimForExcerpt(memory), 0.7m));
        }

        var trustedSources = sources.Where(x => x.IsTrusted).ToList();
        var confidence = trustedSources.Count == 0 ? 0.35m : Math.Min(0.92m, trustedSources.Average(x => x.Relevance));
        var rationale = trustedSources.Count == 0
            ? "No processed, indexed, and accessible company knowledge was found for this question."
            : "Retrieved support case context, customer memory, similar outcomes, and relevant knowledge snippets for grounded drafting.";
        return new SupportKnowledgeContext(supportCase.Id, sources, memories, similarCases, confidence, rationale);
    }

    private async Task<IReadOnlyList<CompanyKnowledgeSearchResultDto>> SearchIndexedKnowledgeForTestsAsync(
        Guid companyId,
        IReadOnlyCollection<string> queryTerms,
        CancellationToken cancellationToken)
    {
        var chunks = await _dbContext.CompanyKnowledgeChunks.AsNoTracking()
            .Include(x => x.Document)
            .Where(x => x.CompanyId == companyId && x.IsActive &&
                x.Document.IngestionStatus == CompanyKnowledgeDocumentIngestionStatus.Processed &&
                x.Document.IndexingStatus == CompanyKnowledgeDocumentIndexingStatus.Indexed)
            .Take(200)
            .ToListAsync(cancellationToken);
        return chunks
            .Select(x => new { Chunk = x, Score = queryTerms.Count(term => (x.Content + " " + x.Document.Title).Contains(term, StringComparison.OrdinalIgnoreCase)) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(6)
            .Select(x => new CompanyKnowledgeSearchResultDto(
                x.Chunk.Id,
                x.Chunk.Content,
                Math.Min(0.95d, 0.45d + x.Score / 10d),
                x.Chunk.DocumentId,
                x.Chunk.Document.Title,
                x.Chunk.ChunkIndex,
                x.Chunk.SourceReference,
                new Dictionary<string, JsonNode?>(),
                new CompanyKnowledgeSourceReferenceDto(x.Chunk.DocumentId, x.Chunk.Document.Title, x.Chunk.Document.DocumentType.ToStorageValue(), x.Chunk.Document.SourceType.ToStorageValue(), null, x.Chunk.Id, x.Chunk.ChunkIndex, x.Chunk.SourceReference),
                new CompanyKnowledgeSourceDocumentDto(x.Chunk.DocumentId, x.Chunk.Document.Title, x.Chunk.Document.DocumentType.ToStorageValue(), x.Chunk.Document.SourceType.ToStorageValue(), null)))
            .ToList();
    }

    private static string[] BuildQueryTerms(SupportCase supportCase)
    {
        var text = $"{supportCase.Subject} {supportCase.Summary} {supportCase.Description} {supportCase.Category} {string.Join(' ', supportCase.Messages.Select(x => x.Body))}";
        return text.Split([' ', '\r', '\n', '\t', '.', ',', ':', ';', '/', '\\', '-', '_', '(', ')'], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim().ToLowerInvariant())
            .Where(x => x.Length > 3 && !QueryStopWords.Contains(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(24)
            .ToArray();
    }

    private static IEnumerable<RankedKnowledgeResult> RankCustomerFacingResults(
        IReadOnlyList<CompanyKnowledgeSearchResultDto> results,
        IReadOnlyCollection<string> queryTerms)
    {
        return results
            .Where(x => !LooksLikeInternalImplementationContent(x.DocumentTitle, x.Content))
            .GroupBy(x => $"{x.DocumentTitle.Trim().ToLowerInvariant()}\n{x.Content.Trim()}", StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(x => x.Score).First())
            .Select(result =>
            {
                var searchable = $"{result.DocumentTitle} {result.Content}";
                var matchingTerms = queryTerms.Count(term => searchable.Contains(term, StringComparison.OrdinalIgnoreCase));
                var lexical = queryTerms.Count == 0 ? 0d : Math.Min(1d, matchingTerms / (double)Math.Min(8, queryTerms.Count));
                var title = result.DocumentTitle.Trim().ToLowerInvariant();
                var titleBoost = title.Contains("product", StringComparison.Ordinal) || title.Contains("catalog", StringComparison.Ordinal)
                    ? 0.2d
                    : title.Contains("faq", StringComparison.Ordinal) || title.Contains("help", StringComparison.Ordinal)
                        ? 0.15d
                        : title.Contains("polic", StringComparison.Ordinal)
                            ? 0.08d
                            : 0d;
                var combined = Math.Clamp((result.Score * 0.45d) + (lexical * 0.55d) + titleBoost, 0d, 0.98d);
                return new RankedKnowledgeResult(result, Convert.ToDecimal(combined), matchingTerms);
            })
            .Where(x => x.MatchingTerms > 0 && x.Relevance >= 0.35m)
            .OrderByDescending(x => x.Relevance)
            .ThenBy(x => x.Result.DocumentTitle, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Result.ChunkIndex);
    }

    private static bool LooksLikeInternalImplementationContent(string title, string content)
    {
        var text = $"{title}\n{content}";
        return InternalImplementationMarkers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static string TrimForExcerpt(string? value)
    {
        var normalized = string.Join(' ', (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= 420 ? normalized : normalized[..420];
    }

    private sealed record RankedKnowledgeResult(
        CompanyKnowledgeSearchResultDto Result,
        decimal Relevance,
        int MatchingTerms);

    private static readonly HashSet<string> QueryStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "about", "after", "also", "been", "completely", "could", "customer", "does", "email", "from",
        "have", "hello", "message", "regards", "that", "their", "there", "these", "they", "this", "what",
        "when", "where", "which", "with", "wonder", "would", "your"
    };

    private static readonly string[] InternalImplementationMarkers =
    [
        "implementation prompt",
        "implementation requirements:",
        "required statuses:",
        "read and follow:",
        "definition of done",
        "deliverable:",
        "depends on:",
        "source: `gap.md`",
        "use these prompts one at a time"
    ];
}
