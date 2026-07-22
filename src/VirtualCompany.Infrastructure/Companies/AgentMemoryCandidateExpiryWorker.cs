using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auth;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class AgentMemoryCandidateExpiryOptions
{
    public const string SectionName = "AgentMemoryCandidateExpiry";
    public bool Enabled { get; set; } = true;
    public int IntervalMinutes { get; set; } = 60;
    public int CompanyBatchSize { get; set; } = 100;
}

public sealed class AgentMemoryCandidateExpiryWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory; private readonly AgentMemoryCandidateExpiryOptions _options;
    private readonly ILogger<AgentMemoryCandidateExpiryWorker> _logger;
    public AgentMemoryCandidateExpiryWorker(IServiceScopeFactory scopeFactory, IOptions<AgentMemoryCandidateExpiryOptions> options,
        ILogger<AgentMemoryCandidateExpiryWorker> logger) { _scopeFactory = scopeFactory; _options = options.Value; _logger = logger; }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Clamp(_options.IntervalMinutes, 5, 1440)));
        do { await RunOnceAsync(stoppingToken); } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
    internal async Task RunOnceAsync(CancellationToken ct)
    {
        using var discoveryScope = _scopeFactory.CreateScope();
        var discoveryDb = discoveryScope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var companies = await discoveryDb.AgentMemoryCandidates.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.ExpiresUtc <= DateTime.UtcNow && x.Status != "expired" && x.Status != "activated" && x.Status != "rejected")
            .Select(x => x.CompanyId).Distinct().Take(Math.Clamp(_options.CompanyBatchSize, 1, 500)).ToListAsync(ct);
        foreach (var companyId in companies)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope(); var scopeFactory = scope.ServiceProvider.GetRequiredService<ICompanyExecutionScopeFactory>();
                using var companyScope = scopeFactory.BeginScope(companyId);
                await scope.ServiceProvider.GetRequiredService<IAgentMemoryCandidateService>().ExpireAsync(companyId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { _logger.LogError(ex, "Memory candidate expiry failed for company {CompanyId}.", companyId); }
        }
    }
}
