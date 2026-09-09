namespace VirtualCompany.Infrastructure.Sales;

public sealed class SalesPresentationOptions
{
    public const string SectionName = "SalesPresentations";

    public long MaximumUploadBytes { get; set; } = 25 * 1024 * 1024;
    public int MaximumSlides { get; set; } = 200;
    public int RenderWidthPixels { get; set; } = 1600;
    public int RenderHeightPixels { get; set; } = 900;
    public int BatchSize { get; set; } = 3;
    public int PollIntervalSeconds { get; set; } = 5;
    public int ClaimTimeoutSeconds { get; set; } = 600;
    public int MaximumAttempts { get; set; } = 3;
    public int ReasoningBatchSize { get; set; } = 30;
}
