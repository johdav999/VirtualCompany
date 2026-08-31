using System.Diagnostics.Metrics;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class ForeignCurrencySettlementTelemetry
{
    private readonly Counter<long> _settlements;
    private readonly Counter<long> _reversals;
    private readonly Counter<long> _blocks;

    public ForeignCurrencySettlementTelemetry(IMeterFactory meters)
    {
        var meter = meters.Create("VirtualCompany.Finance.CurrencySettlements");
        _settlements = meter.CreateCounter<long>("finance.currency_settlements");
        _reversals = meter.CreateCounter<long>("finance.currency_settlement_reversals");
        _blocks = meter.CreateCounter<long>("finance.currency_settlement_blocks");
    }

    public void Settled(string paymentType, string documentCurrency, string functionalCurrency,
        bool finalSettlement, decimal realizedGainLossAmount) =>
        _settlements.Add(1,
            new("payment_type", paymentType),
            new("document_currency", documentCurrency),
            new("functional_currency", functionalCurrency),
            new("final_settlement", finalSettlement),
            new("realized_outcome", realizedGainLossAmount > 0m ? "gain" : realizedGainLossAmount < 0m ? "loss" : "none"));

    public void Reversed(string documentCurrency, string functionalCurrency) =>
        _reversals.Add(1,
            new("document_currency", documentCurrency),
            new("functional_currency", functionalCurrency));

    public void Blocked(string reasonCode, string? documentCurrency, string? functionalCurrency) =>
        _blocks.Add(1,
            new("reason_code", reasonCode),
            new("document_currency", documentCurrency),
            new("functional_currency", functionalCurrency));
}
