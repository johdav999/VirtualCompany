using VirtualCompany.Application.Finance;

namespace VirtualCompany.Api.Controllers;

public sealed class UpdateReportDefinitionRequest : ReportDefinitionRevisionRequest
{
    public string Name { get; set; } = string.Empty;
    public IReadOnlyList<ReportDefinitionSectionInput> Sections { get; set; } = [];
    public ReportDefinitionComparisonDto Comparison { get; set; } = new("none", 1, false, false);
}

public class ReportDefinitionRevisionRequest { public int ExpectedRevision { get; set; } public string IdempotencyKey { get; set; } = string.Empty; }
