using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Tests;

public sealed class RoleAgentCadenceBackgroundServiceTests
{
    [Fact]
    public async Task Startup_daily_run_enters_agent_company_scope_and_does_not_run_weekly_or_monthly()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var companyId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var companyContext = new RequestCompanyContextAccessor();
        var analysis = new CapturingFinanceAgentAnalysisService(companyContext);
        var services = new ServiceCollection();
        services.AddSingleton(connection);
        services.AddSingleton<ICompanyContextAccessor>(companyContext);
        services.AddScoped<ICompanyExecutionScopeFactory, CompanyExecutionScopeFactory>();
        services.AddDbContext<VirtualCompanyDbContext>((_, options) => options.UseSqlite(connection));
        services.AddSingleton(analysis);
        services.AddScoped<IFinanceAgentAnalysisService>(provider =>
            provider.GetRequiredService<CapturingFinanceAgentAnalysisService>());

        await using var provider = services.BuildServiceProvider();
        await using (var seedScope = provider.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Companies.Add(new Company(companyId, "Cadence Test Company"));
            db.Agents.Add(new Agent(
                agentId,
                companyId,
                "finance-manager",
                "Laura",
                "Finance Manager",
                "Finance",
                null,
                AgentSeniority.Lead,
                AgentStatus.Active,
                AgentAutonomyLevel.Assisted));
            await db.SaveChangesAsync();
        }

        var worker = new RoleAgentCadenceBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new RoleAgentCadenceOptions
            {
                Enabled = true,
                DailyHourUtc = 23,
                WeeklyDay = DateTime.UtcNow.DayOfWeek,
                MonthlyDay = DateTime.UtcNow.Day
            }),
            NullLogger<RoleAgentCadenceBackgroundService>.Instance);

        await worker.RunDueCadencesAsync(CancellationToken.None, ignoreDailyHour: true, dailyOnly: true);

        Assert.Equal(1, analysis.InvocationCount);
        Assert.Equal(companyId, analysis.ObservedCompanyId);
        Assert.Equal(companyId, analysis.ObservedContextCompanyId);
        Assert.Equal(agentId, analysis.ObservedAgentId);
        Assert.Equal("daily", analysis.ObservedCadence);
        Assert.Null(companyContext.CompanyId);
    }

    private sealed class CapturingFinanceAgentAnalysisService(ICompanyContextAccessor companyContext)
        : IFinanceAgentAnalysisService
    {
        public int InvocationCount { get; private set; }
        public Guid? ObservedCompanyId { get; private set; }
        public Guid? ObservedAgentId { get; private set; }
        public Guid? ObservedContextCompanyId { get; private set; }
        public string? ObservedCadence { get; private set; }

        public Task<RoleAgentAnalysisResult> AnalyzeAsync(
            Guid companyId,
            Guid agentId,
            Guid? actorUserId,
            RoleAgentAnalysisRequest request,
            CancellationToken cancellationToken)
        {
            InvocationCount++;
            ObservedCompanyId = companyId;
            ObservedAgentId = agentId;
            ObservedContextCompanyId = companyContext.CompanyId;
            ObservedCadence = request.Cadence;

            return Task.FromResult(new RoleAgentAnalysisResult(
                Guid.NewGuid(),
                AgentCapabilityIds.FinanceOperatingCadence,
                AgentAiRunStatuses.Completed,
                "Completed.",
                1m,
                DateTime.UtcNow,
                [],
                [],
                [],
                [],
                [],
                [],
                false));
        }
    }
}
