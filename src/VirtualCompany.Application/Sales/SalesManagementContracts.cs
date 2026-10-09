namespace VirtualCompany.Application.Sales;

public sealed record SalesManagementQuery(int Year, int Month, string? Currency = null);
public sealed record SalesOutcomeEvidence(Guid ActivityId, string Outcome, DateTime OccurredUtc, string? RecordedReason, Guid? ActorUserId, Guid? PreviousStageId, Guid? NewStageId);
public sealed record SalesManagementOpportunity(Guid DealId, string Title, string Currency, DateTime CreatedUtc,
    string Cohort, string Outcome, DateTime? OutcomeUtc, string? RecordedReason, decimal? CycleDays,
    bool StageHistoryAvailable, IReadOnlyList<SalesOutcomeEvidence> History, string DeepLink);
public sealed record SalesCohortResult(string Cohort, int Created, int Won, int Lost, int Undecided,
    decimal? WonCreatedPercent, decimal? WinDecidedPercent, decimal? MedianCycleDays, int MissingReasons, int MissingStageHistory);
public sealed record SalesForecastInput(Guid DealId, decimal Amount, string Currency, Guid StageId,
    DateTime ExpectedCloseUtc, decimal Risk, bool RiskRecorded, decimal ExpectedAmount);
public sealed record SalesForecastCapture(DateTime CalculationAsOfUtc, string CalculationVersion, IReadOnlyList<SalesForecastInput> Inputs);
public sealed record SalesForecastEvidence(Guid SnapshotId, DateTime AsOfUtc, string Currency, decimal Expected30,
    decimal Expected60, decimal Expected90, bool InputsAvailable, string Coverage, SalesForecastCapture? Capture);
public sealed record SalesManagementReport(Guid CompanyId, int Year, int Month, string Timezone, DateTime StartUtc,
    DateTime EndUtc, DateTime AsOfUtc, string? Currency, string CalculationVersion, SalesCohortResult Selected,
    SalesCohortResult Prior, decimal? ConversionChangePoints, IReadOnlyList<SalesManagementOpportunity> Opportunities,
    IReadOnlyList<SalesOpportunityEvidence> CurrentForecastOpportunities, IReadOnlyList<SalesForecastCurrencyWindow> CurrentForecastWindows,
    IReadOnlyList<Guid> CurrentOpenOpportunityIds, IReadOnlyList<SalesForecastEvidence> ForecastHistory,
    decimal? ForecastMovement30, string Definitions, string OwnershipCoverage, string Coverage);
public sealed record SalesCapacityAllocation(string Territory, decimal Percent);
public sealed record SalesCapacityAssumptions(decimal AvailableSellingHours, decimal HoursPerOpportunity,
    IReadOnlyList<SalesCapacityAllocation> Allocations, string Notes);
public sealed record SalesCapacityResult(decimal OpportunityCapacity, int OpenOpportunities, decimal ExpectedWorkloadHours,
    decimal CapacityGapHours, IReadOnlyList<SalesCapacityAllocatedHours> Allocations);
public sealed record SalesCapacityAllocatedHours(string Territory, decimal Percent, decimal SellingHours);
public sealed record SaveSalesCapacityProposal(Guid RequestId, SalesManagementQuery Query, SalesCapacityAssumptions Assumptions,
    Guid? PreviousId = null, int? ExpectedRevision = null);
public sealed record SalesCapacityProposalSummary(Guid Id, Guid SeriesId, int Revision, Guid? PreviousId, Guid AccountableUserId,
    DateTime SavedAtUtc, int Year, int Month, string? Currency);
public sealed record SalesCapacityProposal(SalesCapacityProposalSummary Summary, SalesManagementReport Report,
    SalesCapacityAssumptions Assumptions, SalesCapacityResult Result, string Checksum, string Retention);
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
