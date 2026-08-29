using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class BankConnectionTelemetry
{
    internal const string MeterName = "VirtualCompany.BankConnectivity";
    private static readonly Meter Meter = new(MeterName, "1.0.0");
    private static readonly Counter<long> Operations = Meter.CreateCounter<long>("bank.connection.operations");
    private static readonly Counter<long> Blocks = Meter.CreateCounter<long>("bank.connection.blocks");
    private readonly ILogger<BankConnectionTelemetry> _logger;
    public BankConnectionTelemetry(ILogger<BankConnectionTelemetry> logger) => _logger = logger;
    public void Operation(Guid companyId, Guid? connectionId, string operation, string outcome, string? reasonCode, string? correlationId)
    {
        Operations.Add(1, new("operation", operation), new("outcome", outcome), new("reason_code", reasonCode));
        if (outcome == "blocked" || outcome == "failed") Blocks.Add(1, new("operation", operation), new("reason_code", reasonCode));
        _logger.LogInformation("Bank connection operation {Operation} completed with {Outcome} for company {CompanyId}, connection {ConnectionId}. ReasonCode={ReasonCode}. CorrelationId={CorrelationId}.", operation, outcome, companyId, connectionId, reasonCode, correlationId);
    }
}
