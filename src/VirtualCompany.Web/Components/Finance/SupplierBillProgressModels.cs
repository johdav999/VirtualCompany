namespace VirtualCompany.Web.Components.Finance;

public static class SupplierBillProgressStates
{
    public const string Completed = "completed";
    public const string Current = "current";
    public const string Upcoming = "upcoming";
    public const string Blocked = "blocked";
    public const string Failed = "failed";
}

public sealed record SupplierBillProgressStep(
    string Key,
    string Title,
    string Description,
    string State,
    string StatusLabel,
    string? SupportingText = null);

public sealed record SupplierBillProgressViewModel(
    IReadOnlyList<SupplierBillProgressStep> Steps,
    string? CurrentStepKey)
{
    public int CompletedCount => Steps.Count(step => step.State == SupplierBillProgressStates.Completed);
    public bool IsComplete => CompletedCount == Steps.Count;
}
