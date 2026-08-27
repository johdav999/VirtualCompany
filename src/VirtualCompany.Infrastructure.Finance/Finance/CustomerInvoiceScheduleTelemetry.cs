using System.Diagnostics.Metrics;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class CustomerInvoiceScheduleTelemetry
{
    private readonly Counter<long> _operations;
    private readonly Counter<long> _occurrences;

    public CustomerInvoiceScheduleTelemetry(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create("VirtualCompany.Finance.CustomerInvoiceSchedule");
        _operations = meter.CreateCounter<long>("customer_invoice_schedule.operations");
        _occurrences = meter.CreateCounter<long>("customer_invoice_schedule.occurrences");
    }

    public void RecordOperation(string operation, bool replay = false) =>
        _operations.Add(1, new("operation", operation), new("replay", replay));

    public void RecordOccurrence(string outcome, string? reasonCode = null) =>
        _occurrences.Add(1, new("outcome", outcome), new("reason_code", reasonCode ?? "none"));
}
