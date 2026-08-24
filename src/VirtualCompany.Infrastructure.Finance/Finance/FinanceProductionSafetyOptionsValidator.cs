using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace VirtualCompany.Infrastructure.Finance;

/// <summary>
/// Keeps the automatic simulation seed campaign out of production. Seed data is useful for
/// development and Simulation Lab, but it must never become an implicit production data source.
/// </summary>
public sealed class FinanceSeedBackfillWorkerOptionsValidator : IValidateOptions<FinanceSeedBackfillWorkerOptions>
{
    private readonly IHostEnvironment? _hostEnvironment;

    public FinanceSeedBackfillWorkerOptionsValidator(IHostEnvironment? hostEnvironment = null)
    {
        _hostEnvironment = hostEnvironment;
    }

    public ValidateOptionsResult Validate(string? name, FinanceSeedBackfillWorkerOptions options)
    {
        if (!options.Enabled || IsExplicitlySafeEnvironment(_hostEnvironment))
        {
            return ValidateOptionsResult.Success;
        }

        return ValidateOptionsResult.Fail(
            "FinanceSeedBackfill:Enabled is permitted only in Development, Testing, or Simulation environments. Disable automatic finance seeding for production.");
    }

    private static bool IsExplicitlySafeEnvironment(IHostEnvironment? environment) =>
        environment?.IsDevelopment() == true ||
        string.Equals(environment?.EnvironmentName, "Testing", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(environment?.EnvironmentName, "Simulation", StringComparison.OrdinalIgnoreCase);
}
