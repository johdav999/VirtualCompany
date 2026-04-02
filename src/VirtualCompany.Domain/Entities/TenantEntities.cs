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
    public ICollection<CompanyKnowledgeDocument> Documents { get; } = new List<CompanyKnowledgeDocument>();
    public ICollection<CompanyKnowledgeChunk> KnowledgeChunks { get; } = new List<CompanyKnowledgeChunk>();

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
        Guid? userId,
        CompanyMembershipRole role,
        CompanyMembershipStatus status,
        string? membershipAccessConfigurationJson = null,
        string? invitedEmail = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (userId.HasValue && userId.Value == Guid.Empty)
        {
            throw new ArgumentException("UserId cannot be empty.", nameof(userId));
        }

        var normalizedInvitedEmail = NormalizeOptionalEmail(invitedEmail);
        if (!userId.HasValue && normalizedInvitedEmail is null)
        {
            throw new ArgumentException("Either UserId or invitedEmail is required.", nameof(invitedEmail));
        }

        if (status == CompanyMembershipStatus.Active && !userId.HasValue)
        {
            throw new InvalidOperationException("Active memberships must be bound to a user.");
        }

        CompanyMembershipRoles.EnsureSupported(role, nameof(role));

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        UserId = userId;
        InvitedEmail = status == CompanyMembershipStatus.Active ? null : normalizedInvitedEmail;
        Role = role;
        Status = status;
        MembershipAccessConfigurationJson = string.IsNullOrWhiteSpace(membershipAccessConfigurationJson) ? null : membershipAccessConfigurationJson.Trim();
        CreatedUtc = DateTime.UtcNow;
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid? UserId { get; private set; }
    public string? InvitedEmail { get; private set; }
    public CompanyMembershipRole Role { get; private set; }
    public CompanyMembershipStatus Status { get; private set; }
    // Membership access configuration is tenant-scoped human authorization metadata.
    // It must never be interpreted as agent execution policy or agent tool permissions.
    public string? MembershipAccessConfigurationJson { get; private set; }

    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public User? User { get; private set; }

    public bool IsActive => Status == CompanyMembershipStatus.Active;

    public void UpdateRole(CompanyMembershipRole role)
    {
        CompanyMembershipRoles.EnsureSupported(role, nameof(role));

        Role = role;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void RefreshInvitation(CompanyMembershipRole role, string invitedEmail)
    {
        CompanyMembershipRoles.EnsureSupported(role, nameof(role));

        if (Status == CompanyMembershipStatus.Active)
        {
            throw new InvalidOperationException("Active memberships cannot be converted back to pending invitations.");
        }

        Role = role;
        Status = CompanyMembershipStatus.Pending;
        InvitedEmail = NormalizeEmail(invitedEmail);
        UpdatedUtc = DateTime.UtcNow;
    }

    public void Accept(Guid userId)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("UserId is required.", nameof(userId));
        }

        if (Status != CompanyMembershipStatus.Pending)
        {
            throw new InvalidOperationException("Only pending memberships can be accepted.");
        }

        if (UserId.HasValue && UserId.Value != userId)
        {
            throw new InvalidOperationException("Pending membership is already associated with a different user.");
        }

        UserId = userId;
        InvitedEmail = null;
        Status = CompanyMembershipStatus.Active;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void UpdateStatus(CompanyMembershipStatus status)
    {
        if (status == CompanyMembershipStatus.Active && !UserId.HasValue)
        {
            throw new InvalidOperationException("Active memberships must be bound to a user.");
        }

        if (status == CompanyMembershipStatus.Active)
        {
            InvitedEmail = null;
        }

        Status = status;
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

    private static string? NormalizeOptionalEmail(string? email) =>
        string.IsNullOrWhiteSpace(email) ? null : NormalizeEmail(email);
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