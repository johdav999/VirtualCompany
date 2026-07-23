namespace VirtualCompany.Infrastructure.Finance;

public sealed class FinanceTransactionCreationOptions
{
    public const string SectionName = "FinanceTransactionCreation";

    // Temporary kill switch for non-simulation transaction writes such as finance seed/bootstrap.
    public bool AllowNonSimulationTransactionCreation { get; set; } = true;
}
