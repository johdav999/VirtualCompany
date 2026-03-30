using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public interface ICompanyOwnedEntity
{
    Guid CompanyId { get; }
}

public sealed class User
{
    private User()
    {
    }

    public User(Guid id, string email, string displayName, string authProvider, string authSubject)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        Email = NormalizeEmail(email);
        DisplayName = NormalizeRequired(displayName, nameof(displayName), 200);
        AuthProvider = NormalizeRequired(authProvider, nameof(authProvider), 100);
        AuthSubject = NormalizeRequired(authSubject, nameof(authSubject), 200);
        CreatedUtc = DateTime.UtcNow;
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public string Email { get; private set; } = null!;
    public string DisplayName { get; private set; } = null!;
    public string AuthProvider { get; private set; } = null!;
    public string AuthSubject { get; private set; } = null!;
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public ICollection<CompanyMembership> Memberships { get; } = new List<CompanyMembership>();

    public void UpdateIdentity(string email, string displayName, string authProvider, string authSubject)
    {
        Email = NormalizeEmail(email);
        DisplayName = NormalizeRequired(displayName, nameof(displayName), 200);
        AuthProvider = NormalizeRequired(authProvider, nameof(authProvider), 100);
        AuthSubject = NormalizeRequired(authSubject, nameof(authSubject), 200);
        UpdatedUtc = DateTime.UtcNow;
    }

    private static string NormalizeEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("Email is required.", nameof(email));
        }

        return email.Trim().ToLowerInvariant();
    }

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }
}

public sealed class Company
{
    private Company()
    {
    }

    public Company(Guid id, string name)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        Name = NormalizeRequired(name, nameof(name), 200);
        CreatedUtc = DateTime.UtcNow;
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Industry { get; private set; }
    public string? BusinessType { get; private set; }
    public string? Timezone { get; private set; }
    public string? Currency { get; private set; }
    public string? Language { get; private set; }
    public string? ComplianceRegion { get; private set; }
    public CompanyBranding Branding { get; private set; } = new();
    public CompanySettings Settings { get; private set; } = new();
    public string? OnboardingStateJson { get; private set; }
    public int? OnboardingCurrentStep { get; private set; }
    public string? OnboardingTemplateId { get; private set; }
    public CompanyOnboardingStatus OnboardingStatus { get; private set; } = CompanyOnboardingStatus.NotStarted;
    public DateTime? OnboardingLastSavedUtc { get; private set; }
    public DateTime? OnboardingCompletedUtc { get; private set; }
    public DateTime? OnboardingAbandonedUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public ICollection<CompanyMembership> Memberships { get; } = new List<CompanyMembership>();
    public ICollection<CompanyOwnedNote> Notes { get; } = new List<CompanyOwnedNote>();

    public void Rename(string name)
    {
        Name = NormalizeRequired(name, nameof(name), 200);
        UpdatedUtc = DateTime.UtcNow;
    }

    public void UpdateWorkspaceProfile(
        string name,
        string? industry,
        string? businessType,
        string? timezone,
        string? currency,
        string? language,
        string? complianceRegion)
    {
        Name = NormalizeRequired(name, nameof(name), 200);
        Industry = NormalizeOptional(industry, nameof(industry), 100);
        BusinessType = NormalizeOptional(businessType, nameof(businessType), 100);
        Timezone = NormalizeOptional(timezone, nameof(timezone), 100);
        Currency = NormalizeOptional(currency, nameof(currency), 16);
        Language = NormalizeOptional(language, nameof(language), 16);
        ComplianceRegion = NormalizeOptional(complianceRegion, nameof(complianceRegion), 50);
        UpdatedUtc = DateTime.UtcNow;
    }

    public void UpdateBrandingAndSettings(CompanyBranding? branding, CompanySettings? settings)
    {
        Branding = branding ?? new CompanyBranding();
        Settings = settings ?? new CompanySettings();
        Settings.Onboarding ??= new CompanyOnboardingSettings();
        UpdatedUtc = DateTime.UtcNow;
    }

    public void SaveOnboardingProgress(int currentStep, string? templateId, string onboardingStateJson)
    {
        if (currentStep < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(currentStep), "Current onboarding step must be greater than zero.");
        }

        OnboardingCurrentStep = currentStep;
        OnboardingTemplateId = NormalizeOptional(templateId, nameof(templateId), 100);
        OnboardingStateJson = string.IsNullOrWhiteSpace(onboardingStateJson) ? null : onboardingStateJson.Trim();
        OnboardingLastSavedUtc = DateTime.UtcNow;
        OnboardingStatus = CompanyOnboardingStatus.InProgress;
        OnboardingAbandonedUtc = null;
        UpdatedUtc = OnboardingLastSavedUtc.Value;
    }

    public void CompleteOnboarding(int currentStep, string? templateId, string onboardingStateJson)
    {
        SaveOnboardingProgress(currentStep, templateId, onboardingStateJson);
        OnboardingCompletedUtc ??= DateTime.UtcNow;
        OnboardingStatus = CompanyOnboardingStatus.Completed;
        OnboardingAbandonedUtc = null;
        UpdatedUtc = OnboardingCompletedUtc.Value;
    }

    public void AbandonOnboarding()
    {
        if (OnboardingStatus == CompanyOnboardingStatus.Completed)
        {
            throw new InvalidOperationException("Completed onboarding cannot be abandoned.");
        }

        OnboardingStatus = CompanyOnboardingStatus.Abandoned;
        OnboardingAbandonedUtc = DateTime.UtcNow;
        UpdatedUtc = OnboardingAbandonedUtc.Value;
    }

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    private static string? NormalizeOptional(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }
}

public sealed class CompanyMembership
{
    private CompanyMembership()
    {
    }

    public CompanyMembership(
        Guid id,
        Guid companyId,
        Guid userId,
        CompanyMembershipRole role,
        CompanyMembershipStatus status,
        string? permissionsJson = null)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        UserId = userId;
        Role = role;
        Status = status;
        PermissionsJson = string.IsNullOrWhiteSpace(permissionsJson) ? null : permissionsJson.Trim();
        CreatedUtc = DateTime.UtcNow;
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid UserId { get; private set; }
    public CompanyMembershipRole Role { get; private set; }
    public CompanyMembershipStatus Status { get; private set; }
    public string? PermissionsJson { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public User User { get; private set; } = null!;

    public bool IsActive => Status == CompanyMembershipStatus.Active;

    public void UpdateRole(CompanyMembershipRole role)
    {
        Role = role;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void UpdateStatus(CompanyMembershipStatus status)
    {
        Status = status;
        UpdatedUtc = DateTime.UtcNow;
    }
}

public sealed class CompanyOwnedNote : ICompanyOwnedEntity
{
    private CompanyOwnedNote()
    {
    }

    public CompanyOwnedNote(Guid id, Guid companyId, string title, string content)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        Title = NormalizeRequired(title, nameof(title), 200);
        Content = string.IsNullOrWhiteSpace(content) ? string.Empty : content.Trim();
        CreatedUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Title { get; private set; } = null!;
    public string Content { get; private set; } = null!;
    public DateTime CreatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }
}