namespace VirtualCompany.Domain.Enums;

public enum CompanyOnboardingStatus
{
    NotStarted = 1,
    InProgress = 2,
    Completed = 3,
    Abandoned = 4
}

public static class CompanyOnboardingStatusValues
{
    public static string ToStorageValue(this CompanyOnboardingStatus status) =>
        status switch
        {
            CompanyOnboardingStatus.NotStarted => "not_started",
            CompanyOnboardingStatus.InProgress => "in_progress",
            CompanyOnboardingStatus.Completed => "completed",
            CompanyOnboardingStatus.Abandoned => "abandoned",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported company onboarding status.")
        };

    public static CompanyOnboardingStatus Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return CompanyOnboardingStatus.NotStarted;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "not_started" => CompanyOnboardingStatus.NotStarted,
            "in_progress" => CompanyOnboardingStatus.InProgress,
            "completed" => CompanyOnboardingStatus.Completed,
            "abandoned" => CompanyOnboardingStatus.Abandoned,
            _ when Enum.TryParse<CompanyOnboardingStatus>(value.Trim(), ignoreCase: true, out var legacyStatus) => legacyStatus,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported company onboarding status value.")
        };
    }
}