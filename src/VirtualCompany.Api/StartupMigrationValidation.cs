public static class StartupMigrationValidation
{
    public static bool ShouldFailFastOnPendingMigrations(IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        return environment.IsDevelopment() ||
               environment.IsEnvironment("Test") ||
               environment.IsEnvironment("Testing");
    }

    public static void EnsureNoPendingMigrations(IEnumerable<string> pendingMigrations, ILogger logger, string? environmentName = null)
    {
        ArgumentNullException.ThrowIfNull(pendingMigrations);
        ArgumentNullException.ThrowIfNull(logger);

        var pending = pendingMigrations
            .Where(migration => !string.IsNullOrWhiteSpace(migration))
            .Select(migration => migration.Trim())
            .ToArray();

        if (pending.Length == 0)
        {
            return;
        }

        var normalizedEnvironmentName = string.IsNullOrWhiteSpace(environmentName)
            ? "the current environment"
            : environmentName.Trim();
        var pendingList = string.Join(", ", pending);

        logger.LogCritical(
            "Pending EF Core migrations were detected during application startup in {EnvironmentName}. Pending migrations: {PendingMigrations}. Apply the migrations before starting the API or explicitly enable DatabaseInitialization:ApplyMigrationsOnStartup for this environment.",
            normalizedEnvironmentName,
            pendingList);
        throw new InvalidOperationException(
            $"Pending EF Core migrations were detected during application startup in {normalizedEnvironmentName}. Pending migrations: {pendingList}. Apply the migrations before starting the API or explicitly enable DatabaseInitialization:ApplyMigrationsOnStartup for this environment.");
    }
}