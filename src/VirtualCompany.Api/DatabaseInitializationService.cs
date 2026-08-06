using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Companies;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Infrastructure.Persistence;

public sealed class DatabaseInitializationOptions
{
    public const string SectionName = "DatabaseInitialization";
    public bool Enabled { get; set; } = true;
    public bool ApplyMigrationsOnStartup { get; set; }
    public int SqlReadinessAttempts { get; set; } = 12;
    public int SqlReadinessDelaySeconds { get; set; } = 5;
}

public sealed class DatabaseInitializationService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly CompanySetupTemplateSeeder _templateSeeder;
    private readonly AgentTemplateCatalogSeeder _agentTemplateSeeder;
    private readonly CompanyWorkflowDefinitionSeeder _workflowDefinitionSeeder;
    private readonly IPlanningBaselineService _planningBaselineService;
    private readonly ICoreCompanyAgentSeeder _coreCompanyAgentSeeder;
    private readonly IHostEnvironment _environment;
    private readonly IConfiguration _configuration;
    private readonly DatabaseInitializationOptions _options;
    private readonly ILogger<DatabaseInitializationService> _logger;

    public DatabaseInitializationService(
        VirtualCompanyDbContext dbContext,
        CompanySetupTemplateSeeder templateSeeder,
        AgentTemplateCatalogSeeder agentTemplateSeeder,
        CompanyWorkflowDefinitionSeeder workflowDefinitionSeeder,
        IPlanningBaselineService planningBaselineService,
        ICoreCompanyAgentSeeder coreCompanyAgentSeeder,
        IHostEnvironment environment,
        IConfiguration configuration,
        IOptions<DatabaseInitializationOptions> options,
        ILogger<DatabaseInitializationService> logger)
    {
        _dbContext = dbContext;
        _templateSeeder = templateSeeder;
        _agentTemplateSeeder = agentTemplateSeeder;
        _workflowDefinitionSeeder = workflowDefinitionSeeder;
        _planningBaselineService = planningBaselineService;
        _coreCompanyAgentSeeder = coreCompanyAgentSeeder;
        _environment = environment;
        _configuration = configuration;
        _options = options.Value;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Database initialization is disabled for this host.");
            return;
        }

        if (!_dbContext.Database.IsRelational())
        {
            throw new InvalidOperationException("Virtual Company requires a relational database configured through EF Core migrations.");
        }

        await ExecuteWithSqlReadinessRetryAsync(async () =>
        {
            var pendingMigrations = (await _dbContext.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
            if (_options.ApplyMigrationsOnStartup)
            {
                if (pendingMigrations.Length > 0)
                {
                    _logger.LogInformation("Applying {MigrationCount} pending EF Core migrations.", pendingMigrations.Length);
                    await _dbContext.Database.MigrateAsync(cancellationToken);
                }
            }
            else
            {
                StartupMigrationValidation.EnsureNoPendingMigrations(pendingMigrations, _logger, _environment.EnvironmentName);
            }
        }, cancellationToken);

        if (_configuration.GetValue<bool?>("SimulationStartup:StopRunningSessionsOnStartup") ?? true)
        {
            await StopRunningSimulationSessionsAsync(cancellationToken);
        }

        await _templateSeeder.SeedAsync();
        await _agentTemplateSeeder.SeedAsync(cancellationToken);
        await _coreCompanyAgentSeeder.BackfillAllCompaniesAsync(cancellationToken);
        await _workflowDefinitionSeeder.SeedAsync();
        await _planningBaselineService.BackfillAllCompaniesAsync(cancellationToken);
    }

    private async Task ExecuteWithSqlReadinessRetryAsync(Func<Task> operation, CancellationToken cancellationToken)
    {
        var attempts = Math.Clamp(_options.SqlReadinessAttempts, 1, 60);
        var delay = TimeSpan.FromSeconds(Math.Clamp(_options.SqlReadinessDelaySeconds, 1, 60));
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await operation();
                return;
            }
            catch (Exception ex) when (attempt < attempts && IsTransientSqlStartupException(ex))
            {
                _logger.LogWarning(ex, "SQL Server is not ready. Startup attempt {Attempt} of {Attempts} will be retried.", attempt, attempts);
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private async Task StopRunningSimulationSessionsAsync(CancellationToken cancellationToken)
    {
        var runningStates = await _dbContext.CompanySimulationStates
            .IgnoreQueryFilters()
            .Where(x => x.Status == CompanySimulationStatus.Running)
            .ToListAsync(cancellationToken);
        if (runningStates.Count == 0) return;

        var stoppedUtc = DateTime.UtcNow;
        foreach (var state in runningStates)
        {
            state.Stop(stoppedUtc);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Stopped {SessionCount} running simulation sessions during startup.", runningStates.Count);
    }

    private static bool IsTransientSqlStartupException(Exception exception)
    {
        if (exception is SqlException sqlException && sqlException.Errors.Cast<SqlError>().Any(error => error.Number is 10054 or 233 or 4060 or 258 or 53 or -2))
        {
            return true;
        }

        return exception.InnerException is not null && IsTransientSqlStartupException(exception.InnerException);
    }
}
