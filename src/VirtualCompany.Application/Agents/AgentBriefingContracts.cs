namespace VirtualCompany.Application.Agents;

public static class AgentBriefingCategories
{
    public const string CompanyInformation = "company_information";
    public const string ProductsAndServices = "products_and_services";
    public const string Policies = "policies";
    public const string CustomerSupport = "customer_support";
    public const string OtherInstructions = "other_instructions";

    public static IReadOnlyList<string> All { get; } =
    [
        CompanyInformation,
        ProductsAndServices,
        Policies,
        CustomerSupport,
        OtherInstructions
    ];

    public static bool IsSupported(string? category) =>
        !string.IsNullOrWhiteSpace(category) &&
        All.Contains(category.Trim(), StringComparer.OrdinalIgnoreCase);
}

public sealed record GenerateAgentBriefDraftCommand(
    string Category,
    string? ExistingText);

public sealed record UpdateAgentBriefCommand(
    Dictionary<string, string>? Sections);

public sealed record AgentBriefDraftDto(
    string Category,
    string Content,
    string Model,
    DateTime GeneratedUtc);

public interface IAgentBriefDraftService
{
    Task<AgentBriefDraftDto> GenerateAsync(
        Guid companyId,
        Guid agentId,
        GenerateAgentBriefDraftCommand command,
        CancellationToken cancellationToken);
}

public sealed class AgentBriefDraftUnavailableException : Exception
{
    public AgentBriefDraftUnavailableException(string message) : base(message)
    {
    }
}
