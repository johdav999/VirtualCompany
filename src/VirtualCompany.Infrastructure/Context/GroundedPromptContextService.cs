using VirtualCompany.Application.Context;

namespace VirtualCompany.Infrastructure.Context;

public sealed class GroundedContextPromptBuilder : IGroundedContextPromptBuilder
{
    public GroundedPromptContextDto Build(GroundedContextRetrievalResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var normalizedContext = GroundedContextPromptReadyMapper.Normalize(
            result.GeneratedAtUtc,
            result.KnowledgeSection,
            result.MemorySection,
            result.RecentTaskSection,
            result.RelevantRecordsSection,
            result.SourceReferences);

        return new GroundedPromptContextDto(
            result.RetrievalId,
            result.GeneratedAtUtc,
            result.CompanyContextSection,
            normalizedContext,
            result.AppliedFilters);
    }
}

public sealed class GroundedPromptContextService : IGroundedPromptContextService
{
    private readonly IGroundedContextRetrievalService _retrievalService;
    private readonly IGroundedContextPromptBuilder _promptBuilder;

    public GroundedPromptContextService(
        IGroundedContextRetrievalService retrievalService,
        IGroundedContextPromptBuilder promptBuilder)
    {
        _retrievalService = retrievalService;
        _promptBuilder = promptBuilder;
    }

    public async Task<GroundedPromptContextDto> PrepareAsync(
        GroundedPromptContextRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Prompt-ready shaping stays in orchestration so callers consume structured sections.
        var retrievalResult = await _retrievalService.RetrieveAsync(
            request.ToRetrievalRequest(),
            cancellationToken);

        return _promptBuilder.Build(retrievalResult);
    }
}
