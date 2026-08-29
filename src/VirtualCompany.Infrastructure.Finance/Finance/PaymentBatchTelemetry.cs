using System.Diagnostics.Metrics;

namespace VirtualCompany.Infrastructure.Finance;

internal sealed class PaymentBatchTelemetry
{
    private static readonly Meter Meter = new("VirtualCompany.Finance.PaymentBatches", "1.0");
    private readonly Counter<long> _operations = Meter.CreateCounter<long>("finance.payment_batches.operations");
    private readonly Counter<long> _blocked = Meter.CreateCounter<long>("finance.payment_batches.blocked");
    private readonly Histogram<long> _obligations = Meter.CreateHistogram<long>("finance.payment_batches.obligation_count");

    public void Operation(string operation, string status) => _operations.Add(1,
        new KeyValuePair<string, object?>("operation", operation), new("status", status));
    public void Blocked(string operation, string reasonCode) => _blocked.Add(1,
        new KeyValuePair<string, object?>("operation", operation), new("reason_code", reasonCode));
    public void Validated(int obligationCount, bool isValid) => _obligations.Record(obligationCount,
        new KeyValuePair<string, object?>("valid", isValid));
}
