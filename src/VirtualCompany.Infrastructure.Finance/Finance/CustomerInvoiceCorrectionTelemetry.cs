using System.Diagnostics.Metrics;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class CustomerInvoiceCorrectionTelemetry
{
    private readonly Counter<long> _operations;
    private readonly Counter<long> _refundOutcomes;

    public CustomerInvoiceCorrectionTelemetry(IMeterFactory meters)
    {
        var meter = meters.Create("VirtualCompany.Finance.CustomerInvoiceCorrection");
        _operations = meter.CreateCounter<long>("customer_invoice_correction.operations");
        _refundOutcomes = meter.CreateCounter<long>("customer_invoice_refund.outcomes");
    }

    public void Record(string operation, string correctionType, string outcome, bool replay = false) =>
        _operations.Add(1, new("operation", operation), new("correction_type", correctionType),
            new("outcome", outcome), new("replay", replay));

    public void RecordRefund(string outcome, string provider) =>
        _refundOutcomes.Add(1, new("outcome", outcome), new("provider", provider));
}
