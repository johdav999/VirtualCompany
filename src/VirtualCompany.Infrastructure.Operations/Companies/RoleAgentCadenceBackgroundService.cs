using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Sales;
using VirtualCompany.Application.Marketing;
using VirtualCompany.Application.Support;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class RoleAgentCadenceOptions
{
    public const string SectionName = "RoleAgentCadence";
    public bool Enabled { get; set; } = true;
    public int PollMinutes { get; set; } = 15;
    public int DailyHourUtc { get; set; } = 6;
    public DayOfWeek WeeklyDay { get; set; } = DayOfWeek.Monday;
    public int MonthlyDay { get; set; } = 1;
    public int MaximumAttemptsPerWindow { get; set; } = 3;
}

public sealed class RoleAgentCadenceBackgroundService(
    IServiceScopeFactory scopes,
    IOptions<RoleAgentCadenceOptions> options,
    ILogger<RoleAgentCadenceBackgroundService> logger) : BackgroundService
{
    private readonly RoleAgentCadenceOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled) return;
        await Task.Yield();
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunDueCadencesAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Role agent cadence polling failed safely."); }
            await Task.Delay(TimeSpan.FromMinutes(Math.Clamp(_options.PollMinutes, 5, 1440)), stoppingToken);
        }
    }

    internal async Task RunDueCadencesAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        if (now.Hour < Math.Clamp(_options.DailyHourUtc, 0, 23)) return;
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var agents = await db.Agents.IgnoreQueryFilters().AsNoTracking().Where(x => x.Status == AgentStatus.Active &&
                (x.Department == "Finance" || x.Department == "Sales" || x.Department == "Marketing" ||
                 x.Department == "Support" || x.Department == "Customer Support"))
            .Select(x => new CadenceAgent(x.CompanyId, x.Id, x.Department)).ToListAsync(cancellationToken);
        foreach (var agent in agents)
        {
            await RunIfDueAsync(scope.ServiceProvider, db, agent, "daily", now.Date, 30, cancellationToken);
            if (now.DayOfWeek == _options.WeeklyDay)
                await RunIfDueAsync(scope.ServiceProvider, db, agent, "weekly", now.Date, 90, cancellationToken);
            if ((agent.Department.Equals("Finance", StringComparison.OrdinalIgnoreCase) ||
                 agent.Department.Equals("Marketing", StringComparison.OrdinalIgnoreCase)) &&
                now.Day == Math.Clamp(_options.MonthlyDay, 1, DateTime.DaysInMonth(now.Year, now.Month)))
                await RunIfDueAsync(scope.ServiceProvider, db, agent, "monthly",
                    new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc), 365, cancellationToken);
        }
    }

    private async Task RunIfDueAsync(IServiceProvider services, VirtualCompanyDbContext db, CadenceAgent agent,
        string cadence, DateTime windowStart, int horizon, CancellationToken ct)
    {
        using var tenantScope = services.GetRequiredService<ICompanyExecutionScopeFactory>().BeginScope(agent.CompanyId);
        var promptSuffix = $"role-v1:{cadence}";
        var runs = await db.AgentOrchestrationRuns.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == agent.CompanyId && x.AgentId == agent.AgentId &&
                x.CreatedUtc >= windowStart && x.PromptVersion.EndsWith(promptSuffix))
            .Select(x => x.Status).ToListAsync(ct);
        if (runs.Any(x => x is AgentAiRunStatuses.Completed or AgentAiRunStatuses.NeedsReview) ||
            runs.Count >= Math.Clamp(_options.MaximumAttemptsPerWindow, 1, 5)) return;

        var request = new RoleAgentAnalysisRequest("operating_cadence", null, horizon,
            $"Prepare the {cadence} manager brief and ranked review queue.", DateTime.UtcNow, cadence);
        try
        {
            if (agent.Department.Equals("Finance", StringComparison.OrdinalIgnoreCase))
                await services.GetRequiredService<IFinanceAgentAnalysisService>().AnalyzeAsync(agent.CompanyId, agent.AgentId, null, request, ct);
            else if (agent.Department.Equals("Sales", StringComparison.OrdinalIgnoreCase))
                await services.GetRequiredService<ISalesAgentAnalysisService>().AnalyzeAsync(agent.CompanyId, agent.AgentId, null, request, ct);
            else if (agent.Department.Equals("Marketing", StringComparison.OrdinalIgnoreCase))
            {
                var key = $"marketing-cadence:{agent.CompanyId:N}:{agent.AgentId:N}:{cadence}:{windowStart:yyyyMMdd}";
                await services.GetRequiredService<IMarketingOperatingLoopService>().RunAsync(agent.CompanyId, agent.AgentId,
                    new RequestMarketingOperatingRun("cadence", cadence, key, key, Cadence: cadence), ct);
            }
            else
                await services.GetRequiredService<ISupportAgentAnalysisService>().AnalyzeAsync(agent.CompanyId, agent.AgentId, null, request, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "{Cadence} role analysis failed safely. CompanyId: {CompanyId}; AgentId: {AgentId}.", cadence, agent.CompanyId, agent.AgentId);
        }
    }

    private sealed record CadenceAgent(Guid CompanyId, Guid AgentId, string Department);
}
