using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Companies;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class CompanyOnboardingService : ICompanyOnboardingService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private const int CompanyNameMaxLength = 200;
    private const int LastWizardStep = 3;
    private const int CompletedWizardStep = 4;
    private const int IndustryMaxLength = 100;
    private const int BusinessTypeMaxLength = 100;
    private const int TimezoneMaxLength = 100;
    private const int CurrencyMaxLength = 16;
    private const int LanguageMaxLength = 16;
    private const int ComplianceRegionMaxLength = 50;
    private const int TemplateIdMaxLength = 100;
    private const int LogoUrlMaxLength = 2048;
    private const int ThemeMaxLength = 50;
    private const int LocaleMaxLength = 16;
    private const int HexColorMaxLength = 16;

    private static readonly string[] DefaultStarterGuidance =
    [
        "Invite teammates who need access to payroll, finance, or operations.",
        "Hire your first agents and assign one owner for each workflow.",
        "Upload company knowledge so the workspace can answer questions accurately."
    ];

    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICurrentUserAccessor _currentUserAccessor;
    private readonly IExternalUserIdentityAccessor _externalUserIdentityAccessor;
    private readonly IExternalUserIdentityResolver _externalUserIdentityResolver;
    private readonly IHostEnvironment _hostEnvironment;

    public CompanyOnboardingService(
        VirtualCompanyDbContext dbContext,
        ICurrentUserAccessor currentUserAccessor,
        IExternalUserIdentityAccessor externalUserIdentityAccessor,
        IExternalUserIdentityResolver externalUserIdentityResolver,
        IHostEnvironment hostEnvironment)
    {
        _dbContext = dbContext;
        _currentUserAccessor = currentUserAccessor;
        _externalUserIdentityAccessor = externalUserIdentityAccessor;
        _externalUserIdentityResolver = externalUserIdentityResolver;
        _hostEnvironment = hostEnvironment;
    }

    public async Task<CreateCompanyResultDto> CreateCompanyAsync(
        CreateCompanyCommand command,
        CancellationToken cancellationToken)
    {
        var userId = await RequireCurrentUserIdAsync(cancellationToken);
        var selectedTemplateId = NormalizeOptional(command.SelectedTemplateId);
        var resolvedTemplate = await FindTemplateAsync(selectedTemplateId, cancellationToken);
        EnsureTemplateExists(selectedTemplateId, resolvedTemplate);
        var guidance = ResolveGuidance(resolvedTemplate);

        var merged = MergeValues(
            command.Name,
            command.Industry,
            command.BusinessType,
            command.Timezone,
            command.Currency,
            command.Language,
            command.ComplianceRegion,
            resolvedTemplate);
        ValidateFlexibleConfiguration(command.Branding, command.Settings);

        ValidateCompanyCreation(merged, selectedTemplateId);

        var company = new Company(Guid.NewGuid(), merged.Name);
        company.UpdateWorkspaceProfile(
            merged.Name,
            merged.Industry,
            merged.BusinessType,
            merged.Timezone,
            merged.Currency,
            merged.Language,
            merged.ComplianceRegion);
        company.UpdateBrandingAndSettings(
            MergeBranding(null, command.Branding),
            BuildSettings(null, command.Settings, selectedTemplateId, resolvedTemplate, merged, CompletedWizardStep, true, guidance));

        company.CompleteOnboarding(
            CompletedWizardStep,
            selectedTemplateId,
            SerializeState(
                merged,
                selectedTemplateId,
                CompletedWizardStep,
                true,
                guidance));
        _dbContext.Companies.Add(company);
        _dbContext.CompanyMemberships.Add(new CompanyMembership(
            Guid.NewGuid(),
            company.Id,
            userId,
            CompanyMembershipRole.Owner,
            CompanyMembershipStatus.Active));

        await _dbContext.SaveChangesAsync(cancellationToken);
        return new CreateCompanyResultDto(company.Id, company.Name, BuildDashboardPath(company.Id, includeStarterGuidance: true), guidance);
    }

    public async Task<IReadOnlyList<OnboardingTemplateDto>> GetTemplatesAsync(CancellationToken cancellationToken)
    {
        var templates = await _dbContext.CompanySetupTemplates
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return templates
            .Select(ToDto)
            .ToList();
    }

    public async Task<OnboardingTemplateRecommendationDto?> GetRecommendedDefaultsAsync(
        GetOnboardingTemplateRecommendationRequest request,
        CancellationToken cancellationToken)
    {
        var recommendation = await FindRecommendedTemplateAsync(request.Industry, request.BusinessType, cancellationToken);
        if (recommendation is null)
        {
            return null;
        }

        return ToRecommendationDto(recommendation.Template, recommendation.MatchKind);
    }

    public async Task<CompanyOnboardingProgressDto?> GetProgressAsync(CancellationToken cancellationToken)
    {
        var userId = await ResolveCurrentUserIdAsync(cancellationToken);
        if (userId is not Guid resolvedUserId)
        {
            return null;
        }

        var company = await GetLatestOwnedOnboardingAsync(resolvedUserId, cancellationToken);
        return company is null ? null : MapProgress(company);
    }

    public async Task<CompanyOnboardingProgressDto> CreateWorkspaceAsync(
        CreateCompanyWorkspaceRequest request,
        CancellationToken cancellationToken)
    {
        var userId = await RequireCurrentUserIdAsync(cancellationToken);

        var company = await GetLatestOwnedDraftAsync(userId, cancellationToken);
        if (company is null)
        {
            company = new Company(Guid.NewGuid(), request.Name);
            _dbContext.Companies.Add(company);
            _dbContext.CompanyMemberships.Add(new CompanyMembership(
                Guid.NewGuid(),
                company.Id,
                userId,
                CompanyMembershipRole.Owner,
                CompanyMembershipStatus.Active));
        }

        var selectedTemplateId = NormalizeOptional(request.SelectedTemplateId);
        var resolvedTemplate = await FindTemplateAsync(selectedTemplateId, cancellationToken);
        EnsureTemplateExists(selectedTemplateId, resolvedTemplate);
        var guidance = ResolveGuidance(resolvedTemplate);
        var merged = MergeValues(
            request.Name,
            request.Industry,
            request.BusinessType,
            request.Timezone,
            request.Currency,
            request.Language,
            request.ComplianceRegion,
            resolvedTemplate);
        ValidateDraft(
            request.Name,
            request.Industry,
            request.BusinessType,
            request.Branding,
            request.Settings,
            request.Timezone,
            request.Currency,
            request.Language,
            request.ComplianceRegion,
            request.SelectedTemplateId,
            request.CurrentStep);

        company.UpdateBrandingAndSettings(
            MergeBranding(company.Branding, request.Branding),
            BuildSettings(company.Settings, request.Settings, selectedTemplateId, resolvedTemplate, merged, NormalizeDraftStep(request.CurrentStep), false, guidance));

        company.UpdateWorkspaceProfile(
            merged.Name,
            merged.Industry,
            merged.BusinessType,
            merged.Timezone,
            merged.Currency,
            merged.Language,
            merged.ComplianceRegion);

        company.SaveOnboardingProgress(NormalizeDraftStep(request.CurrentStep), selectedTemplateId, SerializeState(
                merged,
                selectedTemplateId,
                NormalizeDraftStep(request.CurrentStep),
                false,
                guidance));

        await _dbContext.SaveChangesAsync(cancellationToken);
        return MapProgress(company);
    }

    public async Task<CompanyOnboardingProgressDto> SaveProgressAsync(
        SaveCompanyOnboardingProgressRequest request,
        CancellationToken cancellationToken)
    {
        if (request.CompanyId is null)
        {
            return await CreateWorkspaceAsync(
                new CreateCompanyWorkspaceRequest(
                    request.Name,
                    request.Industry,
                    request.BusinessType,
                    request.Branding,
                    request.Settings,
                    request.Timezone,
                    request.Currency,
                    request.Language,
                    request.ComplianceRegion,
                    request.CurrentStep,
                    request.SelectedTemplateId),
                cancellationToken);
        }

        var userId = await RequireCurrentUserIdAsync(cancellationToken);
        var company = await GetOwnedCompanyAsync(userId, request.CompanyId.Value, cancellationToken);
        if (company is null)
        {
            throw new UnauthorizedAccessException("The current user cannot update this company onboarding flow.");
        }

        EnsureSessionIsMutable(company);

        ValidateDraft(
            request.Name,
            request.Industry,
            request.BusinessType,
            request.Branding,
            request.Settings,
            request.Timezone,
            request.Currency,
            request.Language,
            request.ComplianceRegion,
            request.SelectedTemplateId ?? company.OnboardingTemplateId,
            request.CurrentStep);

        var selectedTemplateId = NormalizeOptional(request.SelectedTemplateId) ?? company.OnboardingTemplateId;
        var resolvedTemplate = await FindTemplateAsync(selectedTemplateId, cancellationToken);
        EnsureTemplateExists(selectedTemplateId, resolvedTemplate);
        var guidance = ResolveGuidance(resolvedTemplate);
        var merged = MergeValues(
            request.Name,
            request.Industry,
            request.BusinessType,
            request.Timezone,
            request.Currency,
            request.Language,
            request.ComplianceRegion,
            resolvedTemplate);
        company.UpdateBrandingAndSettings(
            MergeBranding(company.Branding, request.Branding),
            BuildSettings(company.Settings, request.Settings, selectedTemplateId, resolvedTemplate, merged, NormalizeDraftStep(request.CurrentStep), false, guidance));


        company.UpdateWorkspaceProfile(
            merged.Name,
            merged.Industry,
            merged.BusinessType,
            merged.Timezone,
            merged.Currency,
            merged.Language,
            merged.ComplianceRegion);

        company.SaveOnboardingProgress(
            NormalizeDraftStep(request.CurrentStep),
            selectedTemplateId,
            SerializeState(
                merged,
                selectedTemplateId,
                NormalizeDraftStep(request.CurrentStep),
                false,
                guidance));

        await _dbContext.SaveChangesAsync(cancellationToken);
        return MapProgress(company);
    }

    public async Task<CompanyOnboardingProgressDto> AbandonOnboardingAsync(
        AbandonCompanyOnboardingRequest request,
        CancellationToken cancellationToken)
    {
        var userId = await RequireCurrentUserIdAsync(cancellationToken);
        var company = await GetOwnedCompanyAsync(userId, request.CompanyId, cancellationToken);
        if (company is null)
        {
            throw new UnauthorizedAccessException("The current user cannot abandon this company onboarding flow.");
        }

        if (company.OnboardingStatus == CompanyOnboardingStatus.Completed)
        {
            throw BuildValidationException("CompanyId", "Completed onboarding cannot be discarded.");
        }

        company.AbandonOnboarding();

        var onboarding = ResolveOnboardingSettings(company);
        var resolvedTemplate = await FindTemplateAsync(company.OnboardingTemplateId, cancellationToken);
        var guidance = ResolveGuidance(resolvedTemplate);
        company.SaveOnboardingProgress(
            company.OnboardingCurrentStep ?? 1,
            company.OnboardingTemplateId,
            SerializeState(
                new MergedOnboardingValues(
                    onboarding.Name ?? company.Name,
                    onboarding.Industry ?? company.Industry,
                    onboarding.BusinessType ?? company.BusinessType,
                    onboarding.Timezone ?? company.Timezone,
                    onboarding.Currency ?? company.Currency,
                    onboarding.Language ?? company.Language,
                    onboarding.ComplianceRegion ?? company.ComplianceRegion),
                company.OnboardingTemplateId,
                company.OnboardingCurrentStep ?? 1,
                false,
                guidance));
        company.UpdateBrandingAndSettings(
            company.Branding,
            BuildSettings(company.Settings, null, company.OnboardingTemplateId, resolvedTemplate, new MergedOnboardingValues(company.Name, company.Industry, company.BusinessType, company.Timezone, company.Currency, company.Language, company.ComplianceRegion), company.OnboardingCurrentStep ?? 1, false, guidance));
        company.AbandonOnboarding();

        await _dbContext.SaveChangesAsync(cancellationToken);
        return MapProgress(company);
    }

    public async Task<CompleteCompanyOnboardingResultDto> CompleteOnboardingAsync(
        CompleteCompanyOnboardingRequest request,
        CancellationToken cancellationToken)
    {
        var userId = await RequireCurrentUserIdAsync(cancellationToken);
        var company = await GetOwnedCompanyAsync(userId, request.CompanyId, cancellationToken);
        if (company is null)
        {
            throw new UnauthorizedAccessException("The current user cannot complete this company onboarding flow.");
        }

        if (company.OnboardingStatus == CompanyOnboardingStatus.Completed)
        {
            return new CompleteCompanyOnboardingResultDto(company.Id, company.Name, BuildDashboardPath(company.Id, includeStarterGuidance: true), ResolveGuidance(company.OnboardingTemplateId));
        }

        EnsureSessionIsMutable(company);

        var selectedTemplateId = NormalizeOptional(request.SelectedTemplateId) ?? company.OnboardingTemplateId;
        var resolvedTemplate = await FindTemplateAsync(selectedTemplateId, cancellationToken);
        EnsureTemplateExists(selectedTemplateId, resolvedTemplate);
        var guidance = ResolveGuidance(resolvedTemplate);
        var merged = MergeValues(
            company,
            request.Name,
            request.Industry,
            request.BusinessType,
            request.Timezone,
            request.Currency,
            request.Language,
            request.ComplianceRegion,
            resolvedTemplate);
        ValidateFlexibleConfiguration(request.Branding, request.Settings);

        ValidateCompletion(merged);

        company.UpdateWorkspaceProfile(
            merged.Name,
            merged.Industry,
            merged.BusinessType,
            merged.Timezone,
            merged.Currency,
            merged.Language,
            merged.ComplianceRegion);

        company.UpdateBrandingAndSettings(
            MergeBranding(company.Branding, request.Branding),
            BuildSettings(company.Settings, request.Settings, selectedTemplateId, resolvedTemplate, merged, CompletedWizardStep, true, guidance));

        company.CompleteOnboarding(
            CompletedWizardStep,
            selectedTemplateId,
            SerializeState(merged, selectedTemplateId, CompletedWizardStep, true, guidance));

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new CompleteCompanyOnboardingResultDto(
            company.Id,
            company.Name,
            BuildDashboardPath(company.Id, includeStarterGuidance: true),
            guidance);
    }

    private async Task<Company?> GetLatestOwnedOnboardingAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await _dbContext.Companies
            .Where(x => x.Memberships.Any(m =>
                m.UserId == userId &&
                m.Role == CompanyMembershipRole.Owner &&
                m.Status == CompanyMembershipStatus.Active))
            .Where(x =>
                x.OnboardingStatus != CompanyOnboardingStatus.NotStarted ||
                x.OnboardingCurrentStep != null ||
                x.OnboardingLastSavedUtc != null ||
                x.OnboardingCompletedUtc != null ||
                x.OnboardingAbandonedUtc != null)
            .OrderByDescending(x => x.OnboardingStatus == CompanyOnboardingStatus.InProgress)
            .ThenByDescending(x => x.OnboardingLastSavedUtc ?? x.OnboardingCompletedUtc ?? x.OnboardingAbandonedUtc ?? x.UpdatedUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<Company?> GetLatestOwnedDraftAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await _dbContext.Companies
            .Where(x => x.OnboardingStatus == CompanyOnboardingStatus.InProgress)
            .Where(x => x.Memberships.Any(m =>
                m.UserId == userId &&
                m.Role == CompanyMembershipRole.Owner &&
                m.Status == CompanyMembershipStatus.Active))
            .OrderByDescending(x => x.OnboardingLastSavedUtc ?? x.UpdatedUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<Company?> GetOwnedCompanyAsync(Guid userId, Guid companyId, CancellationToken cancellationToken)
    {
        return await _dbContext.Companies
            .Where(x => x.Id == companyId)
            .Where(x => x.Memberships.Any(m =>
                m.UserId == userId &&
                m.Role == CompanyMembershipRole.Owner &&
                m.Status == CompanyMembershipStatus.Active))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<Guid?> ResolveCurrentUserIdAsync(CancellationToken cancellationToken)
    {
        if (_currentUserAccessor.UserId is Guid userId)
        {
            return userId;
        }

        var externalIdentity = _externalUserIdentityAccessor.GetCurrentIdentity();
        if (externalIdentity is null && !_hostEnvironment.IsDevelopment())
        {
            return null;
        }

        externalIdentity ??= new ExternalUserIdentity(
            new ExternalIdentityKey("dev-header", "alice"),
            "alice@example.com",
            "Alice Admin");

        var resolvedUser = await _externalUserIdentityResolver.ResolveAsync(externalIdentity, cancellationToken);
        return resolvedUser.UserId;
    }

    private async Task<Guid> RequireCurrentUserIdAsync(CancellationToken cancellationToken)
    {
        var userId = await ResolveCurrentUserIdAsync(cancellationToken);
        if (userId is not Guid resolvedUserId)
        {
            throw new UnauthorizedAccessException("An authenticated user is required.");
        }

        return resolvedUserId;
    }

    private static OnboardingTemplateDto ToDto(CompanySetupTemplate template) =>
        new(
            template.TemplateId,
            template.Name,
            template.Description ?? string.Empty,
            template.Category,
            template.IndustryTag,
            template.BusinessTypeTag,
            template.SortOrder,
            CloneNodes(template.Defaults),
            CloneNodes(template.Metadata),
            ResolveStarterGuidance(template.Metadata));

    private static OnboardingTemplateRecommendationDto ToRecommendationDto(
        CompanySetupTemplate template,
        string matchKind) =>
        new(
            template.TemplateId,
            template.Name,
            template.Description ?? string.Empty,
            matchKind,
            template.Category,
            template.IndustryTag,
            template.BusinessTypeTag,
            CloneNodes(template.Defaults),
            CloneNodes(template.Metadata),
            ResolveStarterGuidance(template.Metadata));

    private async Task<TemplateMatch?> FindRecommendedTemplateAsync(string? industry, string? businessType, CancellationToken cancellationToken)
    {
        var normalizedIndustry = NormalizeOptional(industry);
        var normalizedBusinessType = NormalizeOptional(businessType);
        if (normalizedIndustry is null && normalizedBusinessType is null)
        {
            return null;
        }

        var templates = await _dbContext.CompanySetupTemplates
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var exactMatch = templates.FirstOrDefault(template =>
            MatchesValue(template.IndustryTag, normalizedIndustry) &&
            MatchesValue(template.BusinessTypeTag, normalizedBusinessType) &&
            !string.IsNullOrWhiteSpace(template.IndustryTag) &&
            !string.IsNullOrWhiteSpace(template.BusinessTypeTag));
        if (exactMatch is not null)
        {
            return new TemplateMatch(exactMatch, "industry_business_type");
        }

        var industryOnlyMatch = templates.FirstOrDefault(template =>
            MatchesValue(template.IndustryTag, normalizedIndustry) &&
            string.IsNullOrWhiteSpace(template.BusinessTypeTag));
        if (industryOnlyMatch is not null)
        {
            return new TemplateMatch(industryOnlyMatch, "industry");
        }

        var businessTypeOnlyMatch = templates.FirstOrDefault(template =>
            MatchesValue(template.BusinessTypeTag, normalizedBusinessType) &&
            string.IsNullOrWhiteSpace(template.IndustryTag));
        if (businessTypeOnlyMatch is not null)
        {
            return new TemplateMatch(businessTypeOnlyMatch, "business_type");
        }

        return null;
    }

    private CompanySetupTemplate? FindTemplate(string? templateId)
    {
        if (string.IsNullOrWhiteSpace(templateId))
        {
            return null;
        }

        return _dbContext.CompanySetupTemplates
            .AsNoTracking()
            .FirstOrDefault(x => string.Equals(x.TemplateId, templateId, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<CompanySetupTemplate?> FindTemplateAsync(string? templateId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(templateId))
        {
            return null;
        }

        return await _dbContext.CompanySetupTemplates
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.TemplateId == templateId, cancellationToken);
    }

    private static MergedOnboardingValues MergeValues(
        string name,
        string industry,
        string businessType,
        string? timezone,
        string? currency,
        string? language,
        string? complianceRegion,
        CompanySetupTemplate? template) =>
        new(
            NormalizeRequired(name),
            FirstNonEmpty(industry, template?.IndustryTag),
            FirstNonEmpty(businessType, template?.BusinessTypeTag),
            FirstNonEmpty(timezone, GetTemplateDefault(template, "timezone")),
            FirstNonEmpty(currency, GetTemplateDefault(template, "currency")),
            FirstNonEmpty(language, GetTemplateDefault(template, "language")),
            FirstNonEmpty(complianceRegion, GetTemplateDefault(template, "complianceRegion")));

    private static MergedOnboardingValues MergeValues(
        Company company,
        string name,
        string industry,
        string businessType,
        string? timezone,
        string? currency,
        string? language,
        string? complianceRegion,
        CompanySetupTemplate? template) =>
        new(
            FirstNonEmpty(name, company.Name) ?? string.Empty,
            FirstNonEmpty(industry, company.Industry, template?.IndustryTag),
            FirstNonEmpty(businessType, company.BusinessType, template?.BusinessTypeTag),
            FirstNonEmpty(timezone, company.Timezone, GetTemplateDefault(template, "timezone")),
            FirstNonEmpty(currency, company.Currency, GetTemplateDefault(template, "currency")),
            FirstNonEmpty(language, company.Language, GetTemplateDefault(template, "language")),
            FirstNonEmpty(complianceRegion, company.ComplianceRegion, GetTemplateDefault(template, "complianceRegion")));

    private static string? GetTemplateDefault(CompanySetupTemplate? template, string key)
    {
        if (template is null || !template.Defaults.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        if (value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var stringValue))
        {
            return NormalizeOptional(stringValue);
        }

        return null;
    }

    private static IReadOnlyList<string> ResolveStarterGuidance(IDictionary<string, JsonNode?>? metadata)
    {
        if (metadata is null ||
            !metadata.TryGetValue("starterGuidance", out var node) ||
            node is not JsonArray guidanceArray)
        {
            return [];
        }

        return guidanceArray
            .Select(item => item is JsonValue value && value.TryGetValue<string>(out var text) ? NormalizeOptional(text) : null)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!)
            .ToList();
    }

    private static void EnsureTemplateExists(string? templateId, CompanySetupTemplate? template)
    {
        if (!string.IsNullOrWhiteSpace(templateId) && template is null)
        {
            throw BuildValidationException("SelectedTemplateId", "The selected template does not exist.");
        }
    }

    private static OnboardingStateDocument? DeserializeState(string? stateJson) =>
        string.IsNullOrWhiteSpace(stateJson)
            ? null
            : JsonSerializer.Deserialize<OnboardingStateDocument>(stateJson, SerializerOptions);

    private static string SerializeState(
        MergedOnboardingValues values,
        string? templateId,
        int currentStep,
        bool isCompleted,
        IReadOnlyList<string> starterGuidance)
    {
        return JsonSerializer.Serialize(new OnboardingStateDocument
        {
            CurrentStep = currentStep,
            SelectedTemplateId = templateId,
            IsCompleted = isCompleted,
            Name = values.Name,
            Industry = values.Industry,
            BusinessType = values.BusinessType,
            Timezone = values.Timezone,
            Currency = values.Currency,
            Language = values.Language,
            ComplianceRegion = values.ComplianceRegion,
            StarterGuidance = starterGuidance.ToList()
        }, SerializerOptions);
    }

    private CompanyOnboardingProgressDto MapProgress(Company company)
    {
        var state = DeserializeState(company.OnboardingStateJson);
        var onboarding = ResolveOnboardingSettings(company, state);

        var selectedTemplateId = company.OnboardingTemplateId ?? onboarding.SelectedTemplateId ?? state?.SelectedTemplateId;
        var guidance = onboarding.StarterGuidance.Count > 0
            ? onboarding.StarterGuidance
            : ResolveGuidance(selectedTemplateId);
        var status = company.OnboardingStatus.ToStorageValue();

        return new CompanyOnboardingProgressDto(
            company.Id,
            onboarding.Name ?? state?.Name ?? company.Name,
            onboarding.Industry ?? state?.Industry ?? company.Industry ?? string.Empty,
            onboarding.BusinessType ?? state?.BusinessType ?? company.BusinessType ?? string.Empty,
            onboarding.Timezone ?? state?.Timezone ?? company.Timezone ?? string.Empty,
            onboarding.Currency ?? state?.Currency ?? company.Currency ?? string.Empty,
            onboarding.Language ?? state?.Language ?? company.Language ?? string.Empty,
            onboarding.ComplianceRegion ?? state?.ComplianceRegion ?? company.ComplianceRegion ?? string.Empty,
            company.OnboardingCurrentStep ?? onboarding.CurrentStep ?? state?.CurrentStep ?? 1,
            selectedTemplateId,
            status,
            company.OnboardingCompletedUtc.HasValue,
            company.OnboardingStatus == CompanyOnboardingStatus.InProgress,
            company.OnboardingLastSavedUtc,
            company.OnboardingCompletedUtc,
            company.OnboardingAbandonedUtc,
            company.OnboardingStatus == CompanyOnboardingStatus.Completed
                ? BuildDashboardPath(company.Id)
                : null,
            guidance,
            ToBrandingDto(company.Branding),
            ToSettingsDto(company.Settings, onboarding));
    }

    private static CompanyBrandingDto ToBrandingDto(CompanyBranding branding) =>
        new()
        {
            LogoUrl = branding.LogoUrl,
            PrimaryColor = branding.PrimaryColor,
            SecondaryColor = branding.SecondaryColor,
            Theme = branding.Theme,
            Extensions = BuildJsonObject(CloneNodes(branding.Extensions))
        };

    private static CompanySettingsDto ToSettingsDto(CompanySettings settings, CompanyOnboardingSettings onboarding) =>
        new()
        {
            Locale = settings.Locale,
            TemplateId = settings.TemplateId,
            Onboarding = new CompanyOnboardingSettingsDto
            {
                Name = onboarding.Name,
                Industry = onboarding.Industry,
                BusinessType = onboarding.BusinessType,
                Timezone = onboarding.Timezone,
                Currency = onboarding.Currency,
                Language = onboarding.Language,
                ComplianceRegion = onboarding.ComplianceRegion,
                CurrentStep = onboarding.CurrentStep,
                SelectedTemplateId = onboarding.SelectedTemplateId,
                IsCompleted = onboarding.IsCompleted,
                StarterGuidance = onboarding.StarterGuidance,
                Extensions = BuildJsonObject(CloneNodes(onboarding.Extensions))
            },
            FeatureFlags = new Dictionary<string, bool>(settings.FeatureFlags, StringComparer.OrdinalIgnoreCase),
            Extensions = BuildJsonObject(CloneNodes(settings.Extensions))
        };

    private static CompanyOnboardingSettings ResolveOnboardingSettings(Company company, OnboardingStateDocument? state = null)
    {
        state ??= DeserializeState(company.OnboardingStateJson);
        var settings = company.Settings ?? new CompanySettings();
        var onboarding = settings.Onboarding ?? new CompanyOnboardingSettings();

        return new CompanyOnboardingSettings
        {
            Name = onboarding.Name ?? state?.Name ?? company.Name,
            Industry = onboarding.Industry ?? state?.Industry ?? company.Industry,
            BusinessType = onboarding.BusinessType ?? state?.BusinessType ?? company.BusinessType,
            Timezone = onboarding.Timezone ?? state?.Timezone ?? company.Timezone,
            Currency = onboarding.Currency ?? state?.Currency ?? company.Currency,
            Language = onboarding.Language ?? state?.Language ?? company.Language,
            ComplianceRegion = onboarding.ComplianceRegion ?? state?.ComplianceRegion ?? company.ComplianceRegion,
            CurrentStep = onboarding.CurrentStep ?? company.OnboardingCurrentStep ?? state?.CurrentStep,
            SelectedTemplateId = onboarding.SelectedTemplateId ?? company.OnboardingTemplateId ?? state?.SelectedTemplateId,
            IsCompleted = onboarding.IsCompleted || state?.IsCompleted == true || company.OnboardingCompletedUtc.HasValue,
            StarterGuidance = onboarding.StarterGuidance.Count > 0
                ? onboarding.StarterGuidance.ToList()
                : state?.StarterGuidance?.ToList() ?? [],
            Extensions = CloneNodes(onboarding.Extensions)
        };
    }

    private static CompanyBranding MergeBranding(CompanyBranding? current, CompanyBrandingDto? incoming)
    {
        current ??= new CompanyBranding();
        if (incoming is null)
        {
            return CloneBranding(current);
        }

        return new CompanyBranding
        {
            LogoUrl = NormalizeOptional(incoming.LogoUrl),
            PrimaryColor = NormalizeOptional(incoming.PrimaryColor),
            SecondaryColor = NormalizeOptional(incoming.SecondaryColor),
            Theme = NormalizeOptional(incoming.Theme),
            Extensions = CloneNodes(incoming.Extensions)
        };
    }

    private static CompanySettings BuildSettings(
        CompanySettings? current,
        CompanySettingsDto? incoming,
        string? selectedTemplateId,
        CompanySetupTemplate? template,
        MergedOnboardingValues values,
        int currentStep,
        bool isCompleted,
        IReadOnlyList<string> starterGuidance)
    {
        current ??= new CompanySettings();
        var incomingOnboarding = incoming?.Onboarding;
        current.Onboarding ??= new CompanyOnboardingSettings();
        var extensions = incoming?.Extensions is null ? CloneNodes(current.Extensions) : CloneNodes(incoming.Extensions);
        ApplyTemplateSnapshot(extensions, template);

        return new CompanySettings
        {
            Locale = NormalizeOptional(incoming?.Locale) ?? current.Locale,
            TemplateId = selectedTemplateId ?? NormalizeOptional(incoming?.TemplateId) ?? current.TemplateId,
            FeatureFlags = incoming?.FeatureFlags is null
                ? new Dictionary<string, bool>(current.FeatureFlags, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, bool>(incoming.FeatureFlags, StringComparer.OrdinalIgnoreCase),
            Extensions = extensions,
            Onboarding = new CompanyOnboardingSettings
            {
                Name = values.Name,
                Industry = values.Industry,
                BusinessType = values.BusinessType,
                Timezone = values.Timezone,
                Currency = values.Currency,
                Language = values.Language,
                ComplianceRegion = values.ComplianceRegion,
                CurrentStep = currentStep,
                SelectedTemplateId = selectedTemplateId,
                IsCompleted = isCompleted,
                StarterGuidance = starterGuidance.ToList(),
                Extensions = BuildOnboardingExtensions(current.Onboarding, incomingOnboarding, template)
            }
        };
    }

    private static Dictionary<string, JsonNode?> BuildOnboardingExtensions(
        CompanyOnboardingSettings current,
        CompanyOnboardingSettingsDto? incoming,
        CompanySetupTemplate? template)
    {
        var extensions = incoming?.Extensions is null
            ? CloneNodes(current.Extensions)
            : CloneNodes(incoming.Extensions);

        if (template is null)
        {
            extensions.Remove("templateDefaults");
            extensions.Remove("templateMetadata");
            extensions.Remove("templateCategory");
            return extensions;
        }

        extensions["templateDefaults"] = BuildJsonObject(template.Defaults);
        extensions["templateMetadata"] = BuildJsonObject(template.Metadata);

        if (string.IsNullOrWhiteSpace(template.Category))
        {
            extensions.Remove("templateCategory");
        }
        else
        {
            extensions["templateCategory"] = JsonValue.Create(template.Category);
        }

        return extensions;
    }

    private static void ApplyTemplateSnapshot(IDictionary<string, JsonNode?> extensions, CompanySetupTemplate? template)
    {
        if (template is null)
        {
            extensions.Remove("companySetupTemplate");
            return;
        }

        extensions["companySetupTemplate"] = new JsonObject
        {
            ["templateId"] = template.TemplateId,
            ["name"] = template.Name,
            ["category"] = template.Category,
            ["industry"] = template.IndustryTag,
            ["businessType"] = template.BusinessTypeTag,
            ["defaults"] = BuildJsonObject(template.Defaults),
            ["metadata"] = BuildJsonObject(template.Metadata)
        };
    }

    private static JsonObject BuildJsonObject(IDictionary<string, JsonNode?> values)
    {
        var json = new JsonObject();
        foreach (var pair in values)
        {
            json[pair.Key] = pair.Value?.DeepClone();
        }

        return json;
    }

    private static CompanyBranding CloneBranding(CompanyBranding source) =>
        new()
        {
            LogoUrl = source.LogoUrl,
            PrimaryColor = source.PrimaryColor,
            SecondaryColor = source.SecondaryColor,
            Theme = source.Theme,
            Extensions = CloneNodes(source.Extensions)
        };

    private static Dictionary<string, JsonNode?> CloneNodes(IDictionary<string, JsonNode?>? nodes)
    {
        if (nodes is null || nodes.Count == 0)
        {
            return new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        }

        return nodes.ToDictionary(
            pair => pair.Key,
            pair => pair.Value?.DeepClone(),
            StringComparer.OrdinalIgnoreCase);
    }

    private static void ValidateFlexibleConfiguration(CompanyBrandingDto? branding, CompanySettingsDto? settings)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        AddOptionalLength(errors, "Branding.LogoUrl", branding?.LogoUrl, LogoUrlMaxLength);
        AddOptionalLength(errors, "Branding.Theme", branding?.Theme, ThemeMaxLength);
        AddOptionalLength(errors, "Settings.Locale", settings?.Locale, LocaleMaxLength);
        AddOptionalLength(errors, "Settings.TemplateId", settings?.TemplateId, TemplateIdMaxLength);
        AddOptionalLength(errors, "Settings.Onboarding.SelectedTemplateId", settings?.Onboarding?.SelectedTemplateId, TemplateIdMaxLength);

        ValidateHexColor(errors, "Branding.PrimaryColor", branding?.PrimaryColor);
        ValidateHexColor(errors, "Branding.SecondaryColor", branding?.SecondaryColor);

        ThrowIfAny(errors);
    }

    private static void ValidateHexColor(IDictionary<string, List<string>> errors, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var candidate = value.Trim();
        if (candidate.Length > HexColorMaxLength)
        {
            AddError(errors, key, $"{key} must be {HexColorMaxLength} characters or fewer.");
            return;
        }

        if (!(candidate.Length is 4 or 7 or 9) || candidate[0] != '#' || !candidate.Skip(1).All(Uri.IsHexDigit))
        {
            AddError(errors, key, $"{key} must be a valid hex color.");
        }
    }

    private IReadOnlyList<string> ResolveGuidance(CompanySetupTemplate? template) =>
        ResolveStarterGuidance(template?.Metadata).Count > 0 ? ResolveStarterGuidance(template?.Metadata) : DefaultStarterGuidance;

    private IReadOnlyList<string> ResolveGuidance(string? templateId)
    {
        var template = FindTemplate(templateId);
        return ResolveGuidance(template);
    }
    private static string BuildDashboardPath(Guid companyId, bool includeStarterGuidance = false)
    {
        return includeStarterGuidance ? $"/dashboard?companyId={companyId}&welcome=onboarding" : $"/dashboard?companyId={companyId}";
    }

    private static void ValidateDraft(
        string name,
        string industry,
        string businessType,
        CompanyBrandingDto? branding,
        CompanySettingsDto? settings,
        string? timezone,
        string? currency,
        string? language,
        string? complianceRegion,
        string? selectedTemplateId,
        int currentStep)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        AddRequired(errors, "Name", name, "Company name is required.", CompanyNameMaxLength);
        AddOptionalLength(errors, "Industry", industry, IndustryMaxLength);
        AddOptionalLength(errors, "BusinessType", businessType, BusinessTypeMaxLength);
        AddOptionalLength(errors, "Timezone", timezone, TimezoneMaxLength);
        AddOptionalLength(errors, "Currency", currency, CurrencyMaxLength);
        AddOptionalLength(errors, "Language", language, LanguageMaxLength);
        AddOptionalLength(errors, "ComplianceRegion", complianceRegion, ComplianceRegionMaxLength);
        AddOptionalLength(errors, "SelectedTemplateId", selectedTemplateId, TemplateIdMaxLength);
        if (currentStep < 1 || currentStep > LastWizardStep)
        {
            AddError(errors, "CurrentStep", $"Current onboarding step must be between 1 and {LastWizardStep}.");
        }
        ValidateFlexibleConfiguration(branding, settings);

        ThrowIfAny(errors);
    }

    private static void ValidateCompletion(MergedOnboardingValues values) =>
        ValidateCompanyCreation(values, null);

    private static void ValidateCompanyCreation(MergedOnboardingValues values, string? selectedTemplateId)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        AddRequired(errors, "Name", values.Name, "Company name is required.", CompanyNameMaxLength);
        AddRequired(errors, "Industry", values.Industry, "Industry is required.", IndustryMaxLength);
        AddRequired(errors, "BusinessType", values.BusinessType, "Business type is required.", BusinessTypeMaxLength);
        AddRequired(errors, "Timezone", values.Timezone, "Timezone is required.", TimezoneMaxLength);
        AddRequired(errors, "Currency", values.Currency, "Currency is required.", CurrencyMaxLength);
        AddRequired(errors, "Language", values.Language, "Language is required.", LanguageMaxLength);
        AddRequired(errors, "ComplianceRegion", values.ComplianceRegion, "Compliance region is required.", ComplianceRegionMaxLength);
        AddOptionalLength(errors, "SelectedTemplateId", selectedTemplateId, TemplateIdMaxLength);

        ThrowIfAny(errors);
    }

    private static int NormalizeDraftStep(int currentStep) =>
        Math.Min(LastWizardStep, Math.Max(1, currentStep));

    private static void EnsureSessionIsMutable(Company company)
    {
        if (company.OnboardingStatus == CompanyOnboardingStatus.Completed)
        {
            throw BuildValidationException("CompanyId", "This onboarding session is already completed.");
        }

        if (company.OnboardingStatus == CompanyOnboardingStatus.Abandoned)
        {
            throw BuildValidationException("CompanyId", "This onboarding draft was discarded. Start a new setup session.");
        }
    }

    private static void AddRequired(
        IDictionary<string, List<string>> errors,
        string key,
        string? value,
        string requiredMessage,
        int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            AddError(errors, key, requiredMessage);
            return;
        }

        if (value.Trim().Length > maxLength)
        {
            AddError(errors, key, $"{key} must be {maxLength} characters or fewer.");
        }
    }

    private static void AddOptionalLength(
        IDictionary<string, List<string>> errors,
        string key,
        string? value,
        int maxLength)
    {
        if (!string.IsNullOrWhiteSpace(value) && value.Trim().Length > maxLength)
        {
            AddError(errors, key, $"{key} must be {maxLength} characters or fewer.");
        }
    }

    private static void AddError(IDictionary<string, List<string>> errors, string key, string message)
    {
        if (!errors.TryGetValue(key, out var list))
        {
            list = [];
            errors[key] = list;
        }

        list.Add(message);
    }

    private static void ThrowIfAny(Dictionary<string, List<string>> errors)
    {
        if (errors.Count > 0)
        {
            throw new CompanyOnboardingValidationException(errors.ToDictionary(x => x.Key, x => x.Value.ToArray(), StringComparer.OrdinalIgnoreCase));
        }
    }

    private static CompanyOnboardingValidationException BuildValidationException(string key, string message) =>
        new(new Dictionary<string, string[]>
        {
            [key] = [message]
        });

    private static string NormalizeRequired(string value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool MatchesValue(string? templateValue, string? inputValue) =>
        !string.IsNullOrWhiteSpace(templateValue) &&
        !string.IsNullOrWhiteSpace(inputValue) &&
        string.Equals(templateValue.Trim(), inputValue.Trim(), StringComparison.OrdinalIgnoreCase);
    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim();

    private sealed record MergedOnboardingValues(
        string Name,
        string? Industry,
        string? BusinessType,
        string? Timezone,
        string? Currency,
        string? Language,
        string? ComplianceRegion);

    private sealed class OnboardingStateDocument
    {
        public string Name { get; set; } = string.Empty;
        public string? Industry { get; set; }
        public string? BusinessType { get; set; }
        public string? Timezone { get; set; }
        public string? Currency { get; set; }
        public string? Language { get; set; }
        public string? ComplianceRegion { get; set; }
        public int CurrentStep { get; set; }
        public string? SelectedTemplateId { get; set; }
        public bool IsCompleted { get; set; }
        public List<string> StarterGuidance { get; set; } = [];
    }

    private sealed record TemplateMatch(CompanySetupTemplate Template, string MatchKind);
}
