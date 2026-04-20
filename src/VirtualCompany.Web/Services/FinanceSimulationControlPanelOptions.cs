namespace VirtualCompany.Web.Services;

public sealed class FinanceSimulationControlPanelOptions
{
    public const string SectionName = "SimulationFeatures";

    public bool UiVisible { get; set; } = true;
    public int PollIntervalMilliseconds { get; set; } = 1000;

    public bool Enabled
    {
        get => UiVisible;
        set => UiVisible = value;
    }
}