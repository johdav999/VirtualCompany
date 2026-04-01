using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Companies;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class CompanyQueryService : ICurrentUserCompanyService, ICompanyNoteService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] DefaultStarterGuidance =
    [
        "Invite teammates who need access to payroll, finance, or operations.",
        "Hire your first agents and assign one owner for each workflow.",
        "Upload company knowledge so the workspace can answer questions accurately.",
        "Connect the first systems your team depends on before expanding automation."
    ];

    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICurrentUserAccessor _currentUserAccessor;
    private readonly ICompanyContextAccessor _companyContextAccessor;
    private readonly IExternalUserIdentityAccessor _externalUserIdentityAccessor;
    private readonly IExternalUserIdentityResolver _externalUserIdentityResolver;
    private readonly IHostEnvironment _hostEnvironment;

    public CompanyQueryService(
        VirtualCompanyDbContext dbContext,
        ICurrentUserAccessor currentUserAccessor,
        ICompanyContextAccessor companyContextAccessor,
        IExternalUserIdentityAccessor externalUserIdentityAccessor,
        IExternalUserIdentityResolver externalUserIdentityResolver,
        IHostEnvironment hostEnvironment)
    {
        _dbContext = dbContext;
        _currentUserAccessor = currentUserAccessor;
        _companyContextAccessor = companyContextAccessor;
        _externalUserIdentityAccessor = externalUserIdentityAccessor;
        _externalUserIdentityResolver = externalUserIdentityResolver;
        _hostEnvironment = hostEnvironment;
    }

    public async Task<CurrentUserDto?> GetCurrentUserAsync(CancellationToken cancellationToken)
    {
        var userId = await ResolveCurrentUserIdAsync(cancellationToken);
        if (userId is not Guid resolvedUserId)
        {
            return null;
        }

        return await _dbContext.Users.AsNoTracking()
            .Where(x => x.Id == resolvedUserId)
            .Select(x => new CurrentUserDto(x.Id, x.Email, x.DisplayName, x.AuthProvider, x.AuthSubject))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<CurrentUserContextDto?> GetCurrentUserContextAsync(CancellationToken cancellationToken)
    {
        var userId = await ResolveCurrentUserIdAsync(cancellationToken);
        if (userId is not Guid resolvedUserId)
        {
            return null;
        }

        var currentUser = await GetCurrentUserAsync(cancellationToken);
        if (currentUser is null)
        {
            return null;
        }

        var memberships = await GetMembershipsForUserAsync(resolvedUserId, cancellationToken);
        var activeCompany = _companyContextAccessor.Membership is not null
            ? ToResolvedCompanyContext(_companyContextAccessor.Membership)
            : ResolveActiveCompany(memberships, _companyContextAccessor.CompanyId);
        var activeMembershipCount = memberships.Count(x => x.Status == CompanyMembershipStatus.Active);

        return new CurrentUserContextDto(
            currentUser,
            memberships,
            activeCompany,
            activeCompany is null && activeMembershipCount > 1);
    }

    public async Task<IReadOnlyList<CompanyMembershipDto>> GetMembershipsAsync(CancellationToken cancellationToken)
    {
        var userId = await ResolveCurrentUserIdAsync(cancellationToken);
        if (userId is not Guid resolvedUserId)
        {
            return Array.Empty<CompanyMembershipDto>();
        }

        return await GetMembershipsForUserAsync(resolvedUserId, cancellationToken);
    }

    public async Task<ResolvedCompanyContextDto?> GetResolvedActiveCompanyAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var userId = await ResolveCurrentUserIdAsync(cancellationToken);
        if (userId is not Guid resolvedUserId)
        {
            return null;
        }

        if (GetRequestCompanyContext(companyId) is { } requestContext)
        {
            return ToResolvedCompanyContext(requestContext);
        }

        return await _dbContext.CompanyMemberships.AsNoTracking()
            .Where(x => x.UserId == resolvedUserId)
            .Where(x => x.CompanyId == companyId)
            .Where(x => x.Status == CompanyMembershipStatus.Active)
            .Select(x => new ResolvedCompanyContextDto(
                x.Id,
                x.CompanyId,
                x.Company.Name,
                x.Role,
                x.Status))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<CompanyAccessDto?> GetCompanyAccessAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var userId = await ResolveCurrentUserIdAsync(cancellationToken);
        if (userId is not Guid resolvedUserId)
        {
            return null;
        }

        if (GetRequestCompanyContext(companyId) is { } requestContext)
        {
            return ToCompanyAccess(requestContext);
        }

        return await _dbContext.CompanyMemberships.AsNoTracking()
            .Where(x => x.UserId == resolvedUserId &&
                        x.CompanyId == companyId &&
                        x.Status == CompanyMembershipStatus.Active)
            .Select(x => new CompanyAccessDto(
                x.CompanyId,
                x.Company.Name,
                x.Role,
                x.Status))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<CompanyDashboardEntryDto?> GetDashboardEntryAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var userId = await ResolveCurrentUserIdAsync(cancellationToken);
        if (userId is not Guid resolvedUserId)
        {
            return null;
        }

        var dashboardEntry = await _dbContext.CompanyMemberships.AsNoTracking()
            .Where(x => x.UserId == resolvedUserId &&
                        x.CompanyId == companyId &&
                        x.Status == CompanyMembershipStatus.Active)
            .Select(x => new DashboardEntryProjection(
                x.CompanyId,
                x.Company.Name,
                x.Company.OnboardingStatus,
                x.Company.OnboardingCompletedUtc,
                x.Company.Settings,
                x.Company.OnboardingStateJson,                
                x.Company.Notes.Any()))
            .SingleOrDefaultAsync(cancellationToken);

        if (dashboardEntry is null)
        {
            return null;
        }

        var starterGuidance = ResolveStarterGuidance(dashboardEntry.Settings, dashboardEntry.OnboardingStateJson);
        return new CompanyDashboardEntryDto(
            dashboardEntry.CompanyId,
            dashboardEntry.CompanyName,
            dashboardEntry.OnboardingStatus != CompanyOnboardingStatus.Completed,
            dashboardEntry.OnboardingStatus == CompanyOnboardingStatus.Completed && !dashboardEntry.HasKnowledgeArtifacts,
            dashboardEntry.OnboardingCompletedUtc,
            starterGuidance);
    }

    public async Task<bool> CanAccessCompanyAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var userId = await ResolveCurrentUserIdAsync(cancellationToken);
        if (userId is not Guid resolvedUserId)
        {
            return false;
        }

        if (GetRequestCompanyContext(companyId) is not null)
        {
            return true;
        }

        return await _dbContext.CompanyMemberships.AsNoTracking().AnyAsync(x =>
            x.UserId == resolvedUserId &&
            x.CompanyId == companyId &&
            x.Status == CompanyMembershipStatus.Active,
            cancellationToken);
    }

    public async Task<CompanyNoteDto?> GetNoteAsync(Guid companyId, Guid noteId, CancellationToken cancellationToken)
    {
        var canAccessCompany = await CanAccessCompanyAsync(companyId, cancellationToken);
        if (!canAccessCompany)
        {
            return null;
        }

        if (_companyContextAccessor.CompanyId is Guid requestCompanyId && requestCompanyId != companyId)
        {
            return null;
        }

        return await _dbContext.CompanyNotes.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Id == noteId)
            .Select(x => new CompanyNoteDto(x.Id, x.CompanyId, x.Title, x.Content))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private Task<List<CompanyMembershipDto>> GetMembershipsForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        return _dbContext.CompanyMemberships.AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.Company.Name)
            .ThenBy(x => x.Id)
            .Select(x => new CompanyMembershipDto(
                x.Id,
                x.CompanyId,
                x.Company.Name,
                x.Role,
                x.Status))
            .ToListAsync(cancellationToken);
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

    private static ResolvedCompanyContextDto? ResolveActiveCompany(
        IReadOnlyList<CompanyMembershipDto> memberships,
        Guid? requestedCompanyId)
    {
        if (requestedCompanyId is Guid companyId)
        {
            var requestedMembership = memberships.SingleOrDefault(x =>
                x.CompanyId == companyId &&
                x.Status == CompanyMembershipStatus.Active);

            if (requestedMembership is not null)
            {
                return ToResolvedCompanyContext(requestedMembership);
            }
        }

        var activeMemberships = memberships.Where(x => x.Status == CompanyMembershipStatus.Active).ToList();
        return activeMemberships.Count == 1 ? ToResolvedCompanyContext(activeMemberships[0]) : null;
    }
    private ResolvedCompanyMembershipContext? GetRequestCompanyContext(Guid companyId)
    {
        var requestContext = _companyContextAccessor.Membership;
        return requestContext is not null && requestContext.CompanyId == companyId
            ? requestContext
            : null;
    }

    private static IReadOnlyList<string> ResolveStarterGuidance(CompanySettings? settings, string? onboardingStateJson)
    {
        if (settings?.Onboarding?.StarterGuidance is { Count: > 0 } persistedGuidance)
        {
            return persistedGuidance;
        }

        var state = string.IsNullOrWhiteSpace(onboardingStateJson)
            ? null
            : JsonSerializer.Deserialize<OnboardingStateDocument>(onboardingStateJson, SerializerOptions);

        return state?.StarterGuidance is { Count: > 0 } guidance ? guidance : DefaultStarterGuidance;
    }
    private static CompanyAccessDto ToCompanyAccess(ResolvedCompanyMembershipContext membership) =>
        new(membership.CompanyId, membership.CompanyName, membership.MembershipRole, membership.Status);

    private sealed record DashboardEntryProjection(
        Guid CompanyId,
        string CompanyName,
        CompanyOnboardingStatus OnboardingStatus,
        DateTime? OnboardingCompletedUtc,
        CompanySettings Settings,
        string? OnboardingStateJson,        
        bool HasKnowledgeArtifacts);

    private sealed class OnboardingStateDocument
    {
        public List<string> StarterGuidance { get; set; } = [];
    }

    private static ResolvedCompanyContextDto ToResolvedCompanyContext(CompanyMembershipDto membership) =>
        new(membership.MembershipId, membership.CompanyId, membership.CompanyName, membership.MembershipRole, membership.Status);

    private static ResolvedCompanyContextDto ToResolvedCompanyContext(ResolvedCompanyMembershipContext membership) =>
        new(membership.MembershipId, membership.CompanyId, membership.CompanyName, membership.MembershipRole, membership.Status);
}
