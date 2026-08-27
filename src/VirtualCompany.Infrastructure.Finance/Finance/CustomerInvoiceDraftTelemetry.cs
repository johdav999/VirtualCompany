using System.Diagnostics.Metrics;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class CustomerInvoiceDraftTelemetry
{
    private readonly Counter<long> _operations;
    private readonly Counter<long> _blockedPreviews;

    public CustomerInvoiceDraftTelemetry(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create("VirtualCompany.Finance.CustomerInvoiceDraft");
        _operations = meter.CreateCounter<long>("customer_invoice_draft.operations");
        _blockedPreviews = meter.CreateCounter<long>("customer_invoice_draft.blocked_previews");
    }

    public void Record(string operation, bool replay = false) =>
        _operations.Add(1, new KeyValuePair<string, object?>("operation", operation),
            new KeyValuePair<string, object?>("replay", replay));

    public void RecordBlocked(int count) =>
        _blockedPreviews.Add(count, new KeyValuePair<string, object?>("reason", "policy_blocker"));
}
