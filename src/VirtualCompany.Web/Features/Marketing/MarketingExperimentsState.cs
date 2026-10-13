using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Features.Marketing;

public sealed class MarketingExperimentsState
{
    public ExperimentFormModel ExperimentForm { get; set; } = new();


    public sealed class ExperimentFormModel
    {
        public string Name { get; set; } = "";
        public string Hypothesis { get; set; } = "";
        public string PrimaryMetric { get; set; } = "conversion_rate";
        public string GuardrailMetric { get; set; } = "unsubscribe_rate";
        public int MinimumSampleSize { get; set; } = 100;
    }
}
