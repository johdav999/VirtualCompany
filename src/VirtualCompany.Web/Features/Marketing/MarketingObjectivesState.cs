using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Features.Marketing;

public sealed class MarketingObjectivesState
{
    public ObjectiveFormModel ObjectiveForm { get; set; } = new();


    public sealed class ObjectiveFormModel
    {
        public string Name { get; set; } = "";
        public string ObjectiveType { get; set; } = "qualified_demand";
        public decimal TargetValue { get; set; } = 10;
        public string Unit { get; set; } = "leads";
    }
}
