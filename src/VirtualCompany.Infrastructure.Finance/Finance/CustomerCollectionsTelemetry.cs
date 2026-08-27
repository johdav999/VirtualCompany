using System.Diagnostics.Metrics;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class CustomerCollectionsTelemetry
{
    private readonly Counter<long> _operations;
    private readonly Counter<long> _deliveryOutcomes;
    private readonly Counter<long> _workerOutcomes;
    public CustomerCollectionsTelemetry(IMeterFactory meters)
    {
        var meter = meters.Create("VirtualCompany.Finance.CustomerCollections");
        _operations = meter.CreateCounter<long>("customer_collections.operations");
        _deliveryOutcomes = meter.CreateCounter<long>("customer_collections.delivery_outcomes");
        _workerOutcomes = meter.CreateCounter<long>("customer_collections.worker_outcomes");
    }
    public void Operation(string operation, string outcome, bool replay = false) =>
        _operations.Add(1, new("operation", operation), new("outcome", outcome), new("replay", replay));
    public void Delivery(string outcome) => _deliveryOutcomes.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
    public void Worker(string outcome) => _workerOutcomes.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
}
