namespace VirtualCompany.Infrastructure.Finance;

public sealed class SupplierApprovalAutomationOptions
{
    public const string SectionName = "SupplierApprovalAutomation";

    public bool Enabled { get; set; }

    public string DisabledMessage { get; set; } =
        "Trusted supplier approvals are disabled on this installation.";
}
