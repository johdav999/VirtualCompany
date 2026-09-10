namespace VirtualCompany.Infrastructure.Sales;

public sealed class SalesPresentationConductorOptions
{
    public const string SectionName = "SalesPresentationConductor";
    public int RenderTimeoutMilliseconds { get; set; } = 3000;
    public int MaximumConsecutiveSlideTransitions { get; set; } = 8;
    public int MinimumSlideDwellSeconds { get; set; } = 1;
}
