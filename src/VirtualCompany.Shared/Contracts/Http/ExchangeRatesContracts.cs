using VirtualCompany.Application.Finance;

namespace VirtualCompany.Api.Controllers;
[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record QueueExchangeRateRefreshRequest(string ProviderKey, DateOnly RequestedDate, IReadOnlyCollection<string>? Currencies, string IdempotencyKey)
{
    public string ProviderKey { get; set; } = ProviderKey;
    public DateOnly RequestedDate { get; set; } = RequestedDate;
    public IReadOnlyCollection<string>? Currencies { get; set; } = Currencies;
    public string IdempotencyKey { get; set; } = IdempotencyKey;

    public QueueExchangeRateRefreshRequest() : this(string.Empty, default !, [], string.Empty)
    {
    }
}
