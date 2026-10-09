namespace VirtualCompany.Domain.Entities;

public sealed class FinanceVarianceExplanation : ICompanyOwnedEntity
{
    private FinanceVarianceExplanation() { }
    public FinanceVarianceExplanation(Guid company, Guid author, Guid request, DateTime month, Guid account, Guid? costCenter,
        string currency, string? budgetVersion, string text, string fingerprint, DateTime saved)
    {
        if (company == Guid.Empty || author == Guid.Empty || request == Guid.Empty || account == Guid.Empty ||
            string.IsNullOrWhiteSpace(text) || text.Length > 2000 || currency.Length != 3 || fingerprint.Length != 64)
            throw new ArgumentException("Invalid variance explanation.");
        Id = Guid.NewGuid(); CompanyId = company; AuthorId = author; RequestId = request; MonthUtc = month; AccountId = account;
        CostCenterId = costCenter; Currency = currency; BudgetVersion = budgetVersion; Text = text; SourceFingerprint = fingerprint; SavedUtc = saved;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid AuthorId { get; private set; }
    public Guid RequestId { get; private set; }
    public DateTime MonthUtc { get; private set; }
    public Guid AccountId { get; private set; }
    public Guid? CostCenterId { get; private set; }
    public string Currency { get; private set; } = null!;
    public string? BudgetVersion { get; private set; }
    public string Text { get; private set; } = null!;
    public string SourceFingerprint { get; private set; } = null!;
    public DateTime SavedUtc { get; private set; }
}
