using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Features.Agents;

public sealed class HireAgentFormModel : IValidatableObject
{
    [Required, StringLength(100)]
    public string TemplateId { get; set; } = string.Empty;

    [Required, StringLength(200)]
    public string DisplayName { get; set; } = string.Empty;

    [StringLength(2048)]
    public string? AvatarUrl { get; set; }

    [StringLength(100)]
    public string? Department { get; set; }

    [StringLength(100)]
    public string? RoleName { get; set; }

    [StringLength(1000)]
    public string? Personality { get; set; }

    [StringLength(32)]
    public string? Seniority { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(AvatarUrl))
        {
            yield break;
        }

        var trimmed = AvatarUrl.Trim();
        if (trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("blob:", StringComparison.OrdinalIgnoreCase) ||
            (trimmed.Length >= 256 &&
             !trimmed.Contains("/", StringComparison.Ordinal) &&
             !trimmed.Contains("\\", StringComparison.Ordinal) &&
             !trimmed.Contains(".", StringComparison.Ordinal) &&
             (trimmed.StartsWith("iVBORw0KGgo", StringComparison.Ordinal) ||
              trimmed.StartsWith("/9j/", StringComparison.Ordinal) ||
              trimmed.StartsWith("R0lGOD", StringComparison.Ordinal) ||
              trimmed.StartsWith("PHN2Zy", StringComparison.Ordinal))))
        {
            yield return new ValidationResult("AvatarUrl must be an external http/https URL or a file/storage reference, not inline image data.", [nameof(AvatarUrl)]);
            yield break;
        }

        if ((trimmed.Contains("://", StringComparison.Ordinal) || trimmed.StartsWith("http:", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("https:", StringComparison.OrdinalIgnoreCase)) &&
            (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
        {
            yield return new ValidationResult("AvatarUrl must be a valid absolute http/https URL or a file/storage reference.", [nameof(AvatarUrl)]);
        }
    }

    public CreateAgentFromTemplateRequest ToRequest() =>
        new()
        {
            TemplateId = TemplateId,
            DisplayName = DisplayName,
            AvatarUrl = AvatarUrl,
            Department = Department,
            RoleName = RoleName,
            Personality = Personality,
            Seniority = Seniority
        };
}

