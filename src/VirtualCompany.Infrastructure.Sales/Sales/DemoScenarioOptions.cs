namespace VirtualCompany.Infrastructure.Sales;

public sealed class DemoScenarioOptions
{
    public const string SectionName = "DemoScenarios";

    public bool Enabled { get; set; }
    public bool ProvisioningEnabled { get; set; }
    public bool ResetEnabled { get; set; }
}

