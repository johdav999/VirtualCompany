namespace VirtualCompany.Application.Sales;
public interface ISalesManagementService
{
    Task<SalesManagementReport> ReportAsync(Guid company, SalesManagementQuery query, CancellationToken ct);
    Task<SalesCapacityProposal> SaveAsync(Guid company, SaveSalesCapacityProposal command, CancellationToken ct);
    Task<SalesCapacityProposal> OpenAsync(Guid company, Guid id, CancellationToken ct);
    Task<IReadOnlyList<SalesCapacityProposalSummary>> ListAsync(Guid company, int skip, CancellationToken ct);
}

public static class SalesCapacityCalculation
{
    public static SalesCapacityResult Calculate(SalesCapacityAssumptions value, int open)
    {
        if(value.AvailableSellingHours is < 0 or > 100000 || value.HoursPerOpportunity is <= 0 or > 10000 || open<0 ||
            value.Notes is null || value.Notes.Length>2000 || value.Allocations is null || value.Allocations.Count is < 1 or > 20 ||
            value.Allocations.Any(x=>x is null || string.IsNullOrWhiteSpace(x.Territory) || x.Territory.Length>80 || x.Percent is < 0 or > 100) ||
            value.Allocations.Select(x=>x.Territory.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=value.Allocations.Count ||
            value.Allocations.Sum(x=>x.Percent)!=100m) throw new ArgumentException("Supply bounded hours, notes and unique allocations totaling 100%.");
        decimal capacity;
        try
        {
            capacity = Math.Floor(value.AvailableSellingHours / value.HoursPerOpportunity);
        }
        catch (OverflowException ex)
        {
            throw new ArgumentException("The hours per opportunity are too small to calculate the available capacity.", nameof(value), ex);
        }
        return new(capacity,open,open*value.HoursPerOpportunity,
            value.AvailableSellingHours-open*value.HoursPerOpportunity,
            value.Allocations.Select(x=>new SalesCapacityAllocatedHours(x.Territory.Trim(),x.Percent,value.AvailableSellingHours*x.Percent/100m)).ToArray());
    }
}
