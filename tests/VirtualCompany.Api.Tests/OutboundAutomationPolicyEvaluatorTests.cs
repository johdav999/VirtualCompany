using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.CustomerMemory;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed class OutboundAutomationPolicyEvaluatorTests
{
    [Fact]
    public async Task OutboundDisabledBlocksSend()
    {
        using var db = CreateDb();
        var companyId = Guid.NewGuid();
        var step = SeedStep(db, companyId);
        db.SalesAutomationPolicies.Add(new SalesAutomationPolicy(Guid.NewGuid(), companyId, SalesAutomationPolicyModes.ManualOnly));
        await db.SaveChangesAsync();

        var service = new OutboundAutomationEnforcementService(db);
        var result = await service.EvaluateSequenceStepAsync(companyId, step.Id, CancellationToken.None);

        Assert.Equal(OutboundPolicyOutcomes.Blocked, result.Outcome);
        Assert.Equal(OutboundPolicyReasonCodes.OutboundDisabled, result.ReasonCode);
    }

    [Fact]
    public async Task FirstContactRequiresApprovalWhenConfigured()
    {
        using var db = CreateDb();
        var companyId = Guid.NewGuid();
        var step = SeedStep(db, companyId);
        var policy = new SalesAutomationPolicy(Guid.NewGuid(), companyId, SalesAutomationPolicyModes.ManualOnly);
        policy.UpdateOutboundSettings(true, 50, true, false, false, false, 10080, null);
        db.SalesAutomationPolicies.Add(policy);
        await db.SaveChangesAsync();

        var service = new OutboundAutomationEnforcementService(db);
        var result = await service.EvaluateSequenceStepAsync(companyId, step.Id, CancellationToken.None);

        Assert.Equal(OutboundPolicyOutcomes.RequiresApproval, result.Outcome);
        Assert.Equal(OutboundPolicyReasonCodes.FirstContactApprovalRequired, result.ReasonCode);
        Assert.NotNull(result.ReviewId);
    }

    [Fact]
    public async Task ValidSendUnderLimitIsAllowed()
    {
        using var db = CreateDb();
        var companyId = Guid.NewGuid();
        var step = SeedStep(db, companyId);
        var policy = new SalesAutomationPolicy(Guid.NewGuid(), companyId, SalesAutomationPolicyModes.ManualOnly);
        policy.UpdateOutboundSettings(true, 50, false, false, false, false, 10080, null);
        db.SalesAutomationPolicies.Add(policy);
        await db.SaveChangesAsync();

        var service = new OutboundAutomationEnforcementService(db);
        var result = await service.EvaluateSequenceStepAsync(companyId, step.Id, CancellationToken.None);

        Assert.Equal(OutboundPolicyOutcomes.Allowed, result.Outcome);
    }

    private static VirtualCompanyDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static SalesSequenceExecutionStep SeedStep(VirtualCompanyDbContext db, Guid companyId)
    {
        var company = new Company(companyId, "Acme");
        company.UpdateWorkspaceProfile("Acme", "Technology", "SaaS", "UTC", "USD", "en", null);
        db.Companies.Add(company);
        var contact = new Contact(Guid.NewGuid(), companyId, "Pat Prospect", "pat@example.com");
        var sequence = new SalesSequence(Guid.NewGuid(), companyId, "Website follow-up");
        var sequenceStep = new SalesSequenceStep(Guid.NewGuid(), companyId, sequence.Id, 1, 0, "Hello {{first_name}}", "Intro", "Following up");
        sequence.Steps.Add(sequenceStep);
        var campaign = new SalesCampaign(Guid.NewGuid(), companyId, sequence.Id, "Campaign", "website");
        var campaignContact = new SalesCampaignContact(Guid.NewGuid(), companyId, campaign.Id, contact.Id);
        var execution = new SalesSequenceExecution(Guid.NewGuid(), companyId, campaign.Id, campaignContact.Id, contact.Id);
        var step = new SalesSequenceExecutionStep(Guid.NewGuid(), companyId, execution.Id, campaign.Id, contact.Id, sequenceStep.Id, 1, DateTime.UtcNow.AddMinutes(-1), Guid.NewGuid().ToString("N"));
        db.Contacts.Add(contact);
        db.SalesSequences.Add(sequence);
        db.SalesCampaigns.Add(campaign);
        db.SalesCampaignContacts.Add(campaignContact);
        db.SalesSequenceExecutions.Add(execution);
        db.SalesSequenceExecutionSteps.Add(step);
        return step;
    }
}
