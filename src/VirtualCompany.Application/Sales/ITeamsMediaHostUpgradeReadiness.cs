namespace VirtualCompany.Application.Sales;

/// <summary>Requires provider-confirmed termination as well as an empty local media runtime.</summary>
public interface ITeamsMediaHostUpgradeReadiness
{
    Task<bool> IsDrainedAsync(CancellationToken ct);
}
