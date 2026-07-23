using System.Text.Json.Nodes;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Approvals;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Application.Support;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Security;

namespace VirtualCompany.Infrastructure.Support;

public sealed class SupportOperationsBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<SupportOperationsWorkerOptions> _options;
    private readonly ILogger<SupportOperationsBackgroundService> _logger;

    public SupportOperationsBackgroundService(IServiceScopeFactory scopeFactory, IOptionsMonitor<SupportOperationsWorkerOptions> options, ILogger<SupportOperationsBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var options = _options.CurrentValue;
            if (options.Enabled)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var router = scope.ServiceProvider.GetRequiredService<ISupportMailboxRoutingService>();
                    var sla = scope.ServiceProvider.GetRequiredService<ISupportSlaMonitor>();
                    var routed = await router.RouteUnlinkedInboundMessagesAsync(DateTime.UtcNow.AddMinutes(-Math.Clamp(options.MailboxLookbackMinutes, 5, 1440)), options.MailboxBatchSize, stoppingToken);
                    var monitored = await sla.RunAsync(DateTime.UtcNow, stoppingToken);
                    _logger.LogInformation("Support operations worker routed {Routed} messages and scanned {Cases} SLA cases.", routed.MessagesRouted, monitored.CasesScanned);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Support operations worker failed while routing mailbox messages or monitoring SLA.");
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(options.PollSeconds, 10, 600)), stoppingToken);
        }
    }
}
