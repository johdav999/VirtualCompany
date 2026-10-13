using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Features.Marketing;

public sealed class MarketingAudiencesState
{
    public QualificationFormModel QualificationForm { get; set; } = new();


    public sealed class QualificationFormModel
    {
        public string Name { get; set; } = "";
        public string AudienceType { get; set; } = "b2b";
        public string Channel { get; set; } = "email";
        public decimal MinimumScore { get; set; } = 70;
        public int FreshnessDays { get; set; } = 30;
        public bool RequiresCustomerCompany { get; set; } = true;
    }
}
