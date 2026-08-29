using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace VirtualCompany.Infrastructure.Finance;

internal sealed class PaymentExecutionTelemetry
{
    private readonly Counter<long> _queued;
    private readonly Counter<long> _providerOperations;
    private readonly Counter<long> _ambiguousOutcomes;
    private readonly Counter<long> _settlements;
    private readonly Counter<long> _remittances;

    public PaymentExecutionTelemetry(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create("VirtualCompany.Finance.PaymentExecution");
        _queued = meter.CreateCounter<long>("finance_payment_execution_queued_total");
        _providerOperations = meter.CreateCounter<long>("finance_payment_provider_operations_total");
        _ambiguousOutcomes = meter.CreateCounter<long>("finance_payment_execution_ambiguous_total");
        _settlements = meter.CreateCounter<long>("finance_payment_execution_settled_total");
        _remittances = meter.CreateCounter<long>("finance_payment_remittance_attempts_total");
    }

    public void Queued(string provider)
    { var tags = new TagList { { "provider", provider } }; _queued.Add(1, tags); }
    public void ProviderOperation(string provider, string operation, string outcome)
    { var tags = new TagList { { "provider", provider }, { "operation", operation }, { "outcome", outcome } }; _providerOperations.Add(1, tags); }
    public void Ambiguous(string provider, string operation)
    { var tags = new TagList { { "provider", provider }, { "operation", operation } }; _ambiguousOutcomes.Add(1, tags); }
    public void Settled(string provider)
    { var tags = new TagList { { "provider", provider } }; _settlements.Add(1, tags); }
    public void Remittance(string outcome)
    { var tags = new TagList { { "outcome", outcome } }; _remittances.Add(1, tags); }
}
