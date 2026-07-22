using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Auth;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Auth;

public sealed class UserPreferenceService : IUserPreferenceService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly ILogger<UserPreferenceService> _logger;

    public UserPreferenceService(
        VirtualCompanyDbContext dbContext,
        ICurrentUserAccessor currentUser,
        ILogger<UserPreferenceService> logger)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<UserPreferenceDto> GetCurrentAsync(CancellationToken cancellationToken)
    {
        var userId = RequireCurrentUserId();
        var preference = await _dbContext.UserPreferences
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);

        return preference is null
            ? new UserPreferenceDto(SupportedUserCultures.Default, null, null)
            : ToDto(preference);
    }

    public async Task<UserPreferenceDto> UpdateCurrentAsync(
        UpdateUserPreferenceCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var userId = RequireCurrentUserId();
        var uiCulture = NormalizeUiCulture(command.UiCulture);
        var formattingCulture = NormalizeFormattingCulture(command.FormattingCulture);
        var preference = await _dbContext.UserPreferences
            .SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);

        var previousUiCulture = preference?.UiCulture;
        var previousFormattingCulture = preference?.FormattingCulture;
        var changed = false;
        if (preference is null)
        {
            preference = new UserPreference(userId, uiCulture, formattingCulture);
            _dbContext.UserPreferences.Add(preference);
            changed = true;
        }
        else
        {
            changed = preference.Update(uiCulture, formattingCulture);
        }

        if (changed)
        {
            _dbContext.UserPreferenceChanges.Add(new UserPreferenceChange(
                userId,
                previousUiCulture,
                preference.UiCulture,
                previousFormattingCulture,
                preference.FormattingCulture));
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Updated interface culture preference for user {UserId} from {PreviousUiCulture} to {UiCulture}.",
                userId,
                previousUiCulture ?? "not-set",
                preference.UiCulture);
        }

        return ToDto(preference);
    }

    private Guid RequireCurrentUserId() => _currentUser.UserId is Guid userId && userId != Guid.Empty
        ? userId
        : throw new UnauthorizedAccessException(UserPreferenceErrorCodes.CurrentUserRequired);

    private static string NormalizeUiCulture(string? value)
    {
        if (SupportedUserCultures.TryNormalize(value, out var normalized))
        {
            return normalized;
        }

        throw new UserPreferenceValidationException(
            UserPreferenceErrorCodes.UnsupportedUiCulture,
            nameof(UpdateUserPreferenceCommand.UiCulture),
            "Select a supported interface language.");
    }

    private static string? NormalizeFormattingCulture(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (SupportedUserCultures.TryNormalize(value, out var normalized))
        {
            return normalized;
        }

        throw new UserPreferenceValidationException(
            UserPreferenceErrorCodes.UnsupportedFormattingCulture,
            nameof(UpdateUserPreferenceCommand.FormattingCulture),
            "Select a supported number and date format.");
    }

    private static UserPreferenceDto ToDto(UserPreference preference) =>
        new(preference.UiCulture, preference.FormattingCulture, preference.UpdatedUtc);
}
