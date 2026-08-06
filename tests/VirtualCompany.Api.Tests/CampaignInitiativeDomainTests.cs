using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Api.Tests;

public sealed class CampaignInitiativeDomainTests
{
    [Fact]
    public void Legacy_campaign_reports_explicit_setup_gaps()
    {
        var campaign = NewCampaign();

        var gaps = campaign.ReadinessGaps();

        Assert.Contains("Complete the campaign objective and schedule.", gaps);
        Assert.Contains("Choose a campaign owner.", gaps);
        Assert.Contains("Add or preview an eligible audience.", gaps);
    }

    [Fact]
    public void Complete_campaign_plan_can_be_scheduled_without_approval()
    {
        var companyId = Guid.NewGuid();
        var campaign = NewCampaign(companyId);
        ConfigureCompletePlan(campaign, companyId);

        campaign.MarkReadyForApproval();

        Assert.Empty(campaign.ReadinessGaps());
        Assert.Equal(CampaignLifecycleStatuses.Scheduled, campaign.LifecycleStatus);
    }

    [Fact]
    public void Approval_policy_holds_ready_campaign_for_human_review()
    {
        var companyId = Guid.NewGuid();
        var campaign = NewCampaign(companyId);
        ConfigureCompletePlan(campaign, companyId);
        campaign.SetPolicy(outboundEnabled: true, maxEmailsPerDay: 25, approvalRequired: true);

        campaign.MarkReadyForApproval();

        Assert.Equal(CampaignLifecycleStatuses.WaitingForApproval, campaign.LifecycleStatus);
    }

    [Fact]
    public void Executable_activity_claim_is_due_bounded_and_idempotent()
    {
        var now = DateTime.UtcNow;
        var activity = new SalesCampaignActivity(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Send launch email", "email", "email",
            CampaignExecutionModes.Executable, now.AddHours(-1), now, "UTC");
        activity.MarkReady();

        var firstClaim = activity.TryClaim("claim-1", now);
        var secondClaim = activity.TryClaim("claim-2", now);

        Assert.True(firstClaim);
        Assert.False(secondClaim);
        Assert.Equal(CampaignActivityStatuses.Ongoing, activity.Status);
        Assert.Equal(1, activity.AttemptCount);
    }

    [Fact]
    public void Segment_change_creates_a_new_definition_version()
    {
        var segment = new SalesCampaignAudienceSegment(Guid.NewGuid(), Guid.NewGuid(), "Nordic SaaS", "b2b");

        segment.Configure("Software", "SE", 5, 250, "Founder", "prospect", "Virtual Company", "sv",
            requireCommunicationPermission: true, excludeOpenCriticalSupportCases: true);

        Assert.Equal(2, segment.Version);
        Assert.True(segment.RequireCommunicationPermission);
        Assert.True(segment.ExcludeOpenCriticalSupportCases);
    }

    private static SalesCampaign NewCampaign(Guid? companyId = null) =>
        new(Guid.NewGuid(), companyId ?? Guid.NewGuid(), Guid.NewGuid(), "Launch campaign", "contacts");

    private static void ConfigureCompletePlan(SalesCampaign campaign, Guid companyId)
    {
        var now = DateTime.UtcNow;
        var ownerId = Guid.NewGuid();
        campaign.ConfigureInitiative(
            CampaignTypes.ProductLaunch,
            "Launch Virtual Company to Nordic SMEs.",
            ownerId,
            null,
            "opportunities",
            10,
            "opportunities",
            now.AddDays(30),
            now,
            now.AddDays(2),
            now.AddDays(30),
            "Europe/Stockholm",
            10_000,
            "SEK");
        campaign.Offers.Add(new SalesCampaignOffer(
            Guid.NewGuid(), companyId, campaign.Id, "Virtual Company", "product_catalog", "product-catalog"));
        campaign.Activities.Add(new SalesCampaignActivity(
            Guid.NewGuid(), companyId, campaign.Id, "Prepare launch", "content", "internal",
            CampaignExecutionModes.Manual, now, now.AddDays(1), "Europe/Stockholm", ownerId));
        campaign.Contacts.Add(new SalesCampaignContact(
            Guid.NewGuid(), companyId, campaign.Id, Guid.NewGuid()));
    }
}
