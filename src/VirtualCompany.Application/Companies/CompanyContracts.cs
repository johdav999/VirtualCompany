using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Collections.ObjectModel;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Application.Companies;

public sealed record CurrentUserDto(
    Guid Id,
    string Email,
    string DisplayName,
    string AuthProvider,
    string AuthSubject);

public sealed record CompanyMembershipDto(
    Guid MembershipId,
    Guid CompanyId,
    string CompanyName,
    [property: JsonPropertyName("membershipRole")] CompanyMembershipRole MembershipRole,
    CompanyMembershipStatus Status);

public sealed record ResolvedCompanyContextDto(
    Guid MembershipId,
    Guid CompanyId,
    string CompanyName,
    [property: JsonPropertyName("membershipRole")] CompanyMembershipRole MembershipRole,
    CompanyMembershipStatus Status);

public sealed record CurrentUserContextDto(
    CurrentUserDto User,
    IReadOnlyList<CompanyMembershipDto> Memberships,
    ResolvedCompanyContextDto? ActiveCompany,
    bool CompanySelectionRequired);

public sealed record CompanyAccessDto(
    Guid CompanyId,
    string CompanyName,
    [property: JsonPropertyName("membershipRole")] CompanyMembershipRole MembershipRole,
    CompanyMembershipStatus Status);

public sealed record CompanyDashboardEntryDto(
    Guid CompanyId,
    string CompanyName,
    bool RequiresOnboarding,
    bool ShowStarterGuidance,
    DateTime? OnboardingCompletedUtc,
    IReadOnlyList<string> StarterGuidance);

public sealed record CompanySelectionDto(
    Guid CompanyId,
    string HeaderName,
    string HeaderValue,
    ResolvedCompanyContextDto ActiveCompany);

public sealed record CompanyNoteDto(Guid Id, Guid CompanyId, string Title, string Content);

public sealed record OnboardingTemplateDto(
    string TemplateId,
    string Name,
    string Description,
    string? Category,
    string? Industry,
    string? BusinessType,
    int SortOrder,
    IReadOnlyDictionary<string, JsonNode?> Defaults,
    IReadOnlyDictionary<string, JsonNode?> Metadata,
    IReadOnlyList<string> StarterGuidance);

public sealed record GetOnboardingTemplateRecommendationRequest(
    string? Industry,
    string? BusinessType);

public sealed record OnboardingTemplateRecommendationDto(
    string TemplateId,
    string Name,
    string Description,
    string MatchKind,
    string? Category,
    string? Industry,
    string? BusinessType,
    IReadOnlyDictionary<string, JsonNode?> Defaults,
    IReadOnlyDictionary<string, JsonNode?> Metadata,
    IReadOnlyList<string> StarterGuidance);

public sealed class CompanyBrandingDto
{
    public string? LogoUrl { get; init; }
    public string? PrimaryColor { get; init; }
    public string? SecondaryColor { get; init; }
    public string? Theme { get; init; }

    [JsonPropertyName("extensions")]
    public JsonObject? Extensions { get; init; }
}

public sealed class CompanyOnboardingSettingsDto
{
    public string? Name { get; init; }
    public string? Industry { get; init; }
    public string? BusinessType { get; init; }
    public string? Timezone { get; init; }
    public string? Currency { get; init; }
    public string? Language { get; init; }
    public string? ComplianceRegion { get; init; }
    public int? CurrentStep { get; init; }
    public string? SelectedTemplateId { get; init; }
    public bool IsCompleted { get; init; }
    public IReadOnlyList<string>? StarterGuidance { get; init; }

    [JsonPropertyName("extensions")]
    public JsonObject? Extensions { get; init; }
}

public sealed class CompanySettingsDto
{
    public string? Locale { get; init; }
    public string? TemplateId { get; init; }
    public CompanyOnboardingSettingsDto? Onboarding { get; init; }
    public IDictionary<string, bool>? FeatureFlags { get; init; }

    [JsonPropertyName("extensions")]
    public JsonObject? Extensions { get; init; }
}

public sealed record CompanyOnboardingProgressDto(
    Guid? CompanyId,
    string Name,
    string Industry,
    string BusinessType,
    string Timezone,
    string Currency,
    string Language,
    string ComplianceRegion,
    int CurrentStep,
    string? SelectedTemplateId,
    string Status,
    bool IsCompleted,
    bool CanResume,
    DateTime? LastSavedUtc,
    DateTime? CompletedUtc,
    DateTime? AbandonedUtc,
    string? DashboardPath,
    IReadOnlyList<string> StarterGuidance,
    CompanyBrandingDto Branding,
    CompanySettingsDto Settings);

public sealed record CreateCompanyCommand(
    string Name,
    string Industry,
    string BusinessType,
    CompanyBrandingDto? Branding,
    CompanySettingsDto? Settings,
    string? Timezone,
    string? Currency,
    string? Language,
    string? ComplianceRegion,
    string? SelectedTemplateId);

public sealed record CreateCompanyResultDto(Guid CompanyId, string CompanyName, string DashboardPath, IReadOnlyList<string> StarterGuidance);

public sealed record CreateCompanyWorkspaceRequest(
    string Name,
    string Industry,
    string BusinessType,
    CompanyBrandingDto? Branding,
    CompanySettingsDto? Settings,
    string? Timezone,
    string? Currency,
    string? Language,
    string? ComplianceRegion,
    int CurrentStep,
    string? SelectedTemplateId);

public sealed record SaveCompanyOnboardingProgressRequest(
    Guid? CompanyId,
    string Name,
    string Industry,
    string BusinessType,
    CompanyBrandingDto? Branding,
    CompanySettingsDto? Settings,
    string? Timezone,
    string? Currency,
    string? Language,
    string? ComplianceRegion,
    int CurrentStep,
    string? SelectedTemplateId);

public sealed record CompleteCompanyOnboardingRequest(
    Guid CompanyId,
    string Name,
    string Industry,
    string BusinessType,
    CompanyBrandingDto? Branding,
    CompanySettingsDto? Settings,
    string? Timezone,
    string? Currency,
    string? Language,
    string? ComplianceRegion,
    string? SelectedTemplateId);

public sealed record AbandonCompanyOnboardingRequest(Guid CompanyId);

public sealed record CompleteCompanyOnboardingResultDto(
    Guid CompanyId,
    string CompanyName,
    string DashboardPath,
    IReadOnlyList<string> StarterGuidance);

public sealed class CompanyOnboardingValidationException : Exception
{
    public CompanyOnboardingValidationException(IDictionary<string, string[]> errors)
        : base("Company onboarding validation failed.")
    {
        Errors = new ReadOnlyDictionary<string, string[]>(new Dictionary<string, string[]>(errors, StringComparer.OrdinalIgnoreCase));
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}

public interface ICurrentUserCompanyService
{
    Task<CurrentUserDto?> GetCurrentUserAsync(CancellationToken cancellationToken);
    Task<CurrentUserContextDto?> GetCurrentUserContextAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<CompanyMembershipDto>> GetMembershipsAsync(CancellationToken cancellationToken);
    Task<ResolvedCompanyContextDto?> GetResolvedActiveCompanyAsync(Guid companyId, CancellationToken cancellationToken);
    Task<CompanyAccessDto?> GetCompanyAccessAsync(Guid companyId, CancellationToken cancellationToken);
    Task<CompanyDashboardEntryDto?> GetDashboardEntryAsync(Guid companyId, CancellationToken cancellationToken);
    Task<bool> CanAccessCompanyAsync(Guid companyId, CancellationToken cancellationToken);
}

public interface ICompanyNoteService
{
    Task<CompanyNoteDto?> GetNoteAsync(Guid companyId, Guid noteId, CancellationToken cancellationToken);
}

public interface ICompanyOnboardingService
{
    Task<CreateCompanyResultDto> CreateCompanyAsync(CreateCompanyCommand command, CancellationToken cancellationToken);
    Task<OnboardingTemplateRecommendationDto?> GetRecommendedDefaultsAsync(GetOnboardingTemplateRecommendationRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<OnboardingTemplateDto>> GetTemplatesAsync(CancellationToken cancellationToken);
    Task<CompanyOnboardingProgressDto?> GetProgressAsync(CancellationToken cancellationToken);
    Task<CompanyOnboardingProgressDto> CreateWorkspaceAsync(CreateCompanyWorkspaceRequest request, CancellationToken cancellationToken);
    Task<CompanyOnboardingProgressDto> SaveProgressAsync(SaveCompanyOnboardingProgressRequest request, CancellationToken cancellationToken);
    Task<CompanyOnboardingProgressDto> AbandonOnboardingAsync(AbandonCompanyOnboardingRequest request, CancellationToken cancellationToken);
    Task<CompleteCompanyOnboardingResultDto> CompleteOnboardingAsync(CompleteCompanyOnboardingRequest request, CancellationToken cancellationToken);
}
