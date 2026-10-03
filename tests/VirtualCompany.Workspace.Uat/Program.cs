using System.Net;
using VirtualCompany.Api.Tests;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

// Browser adapter for the real composed API test host. No production configuration is changed.
using var factory = new BrowserApiFactory();
var companyA = Guid.Parse("11111111-1111-1111-1111-111111111111");
var companyB = Guid.Parse("22222222-2222-2222-2222-222222222222");
var companyC = Guid.Parse("33333333-3333-3333-3333-333333333333");
await factory.SeedAsync(db =>
{
    var owner = new User(Guid.NewGuid(), "p01-owner@example.com", "P01 Owner", "dev-header", "p01-owner");
    var dual = new User(Guid.NewGuid(), "p01-dual@example.com", "P01 Sales and Marketing", "dev-header", "p01-dual");
    var member = new User(Guid.NewGuid(), "p01-member@example.com", "P01 Member", "dev-header", "p01-member");
    db.Users.AddRange(owner, dual, member);
    foreach (var (id, name) in new[] { (companyA, "P01 North Company"), (companyB, "P01 South Company"), (companyC, "P01 Restricted Company") })
    {
        var company = new Company(id, name);
        company.UpdateWorkspaceProfile(name, null, null, "Europe/Stockholm", "SEK", "en-GB", "SE");
        company.CompleteOnboarding(1, null, "{}");
        if (id == companyA || id == companyB) company.SetFinanceSeedStatus(FinanceSeedingState.Seeded, DateTime.UtcNow, DateTime.UtcNow);
        db.Companies.Add(company);
    }
    var ownerA = new CompanyMembership(Guid.NewGuid(), companyA, owner.Id, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active);
    var ownerB = new CompanyMembership(Guid.NewGuid(), companyB, owner.Id, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active);
    var dualA = new CompanyMembership(Guid.NewGuid(), companyA, dual.Id, CompanyMembershipRole.Manager, CompanyMembershipStatus.Active);
    db.CompanyMemberships.AddRange(ownerA, ownerB, dualA,
        new CompanyMembership(Guid.NewGuid(), companyC, owner.Id, CompanyMembershipRole.Owner, CompanyMembershipStatus.Revoked),
        new CompanyMembership(Guid.NewGuid(), companyA, member.Id, CompanyMembershipRole.Employee, CompanyMembershipStatus.Active));
    // Explicitly configured areas exercise the actual resolver, including a member with no assignment.
    foreach (var area in new[] { ResponsibilityArea.CompanyPerformance, ResponsibilityArea.CashAndAccounting, ResponsibilityArea.Sales,
        ResponsibilityArea.Marketing, ResponsibilityArea.CustomerSupport })
        db.CompanyResponsibilityAssignments.Add(new CompanyResponsibilityAssignment(Guid.NewGuid(), companyA, area,
            ResponsibilityAssignmentKind.ExecutiveOversight, ownerA.Id, null, AgentAutonomyLevel.Level1, null, null));
    foreach (var area in new[] { ResponsibilityArea.Sales, ResponsibilityArea.Marketing })
        db.CompanyResponsibilityAssignments.Add(new CompanyResponsibilityAssignment(Guid.NewGuid(), companyA, area,
            ResponsibilityAssignmentKind.Primary, dualA.Id, null, AgentAutonomyLevel.Level1, null, ownerA.Id));
    var dealA = Guid.Parse("44444444-4444-4444-4444-444444444444");
    var dealB = Guid.Parse("55555555-5555-5555-5555-555555555555");
    var contact = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    var customer = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    var lead = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    db.CustomerCompanies.Add(new CustomerCompany(customer, companyA, "P04 North account"));
    db.Contacts.Add(new Contact(contact, companyA, "P04 Test buyer", "p04-buyer@example.com", customer));
    db.Leads.Add(new Lead(lead, companyA, "P04 North source lead", SalesPipelineStage.QualifiedStageId,
        SalesStatuses.Converted, contact, customer, 12000, "SEK"));
    db.Deals.AddRange(
        new Deal(dealA, companyA, "P01 North renewal", SalesPipelineStage.QualifiedStageId, 12000m, "SEK", sourceLeadId: lead, primaryContactId: contact, customerCompanyId: customer, expectedCloseUtc: DateTime.UtcNow.AddDays(1)),
        new Deal(Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"), companyA, "P04 USD expansion", SalesPipelineStage.QualifiedStageId, 1000m, "USD", expectedCloseUtc: DateTime.UtcNow.AddDays(45)),
        new Deal(dealB, companyB, "P01 South private deal", SalesPipelineStage.QualifiedStageId, 34000m, "SEK"));
    db.SalesActivities.Add(new SalesActivity(Guid.NewGuid(), companyA, "customer_context", "P04 customer requested a proposal review.", DateTime.UtcNow.AddDays(-8), dealId: dealA, contactId: contact, customerCompanyId: customer));
    var external = Guid.NewGuid(); var calendar = Guid.NewGuid();
    db.ExternalAccountConnections.Add(new ExternalAccountConnection(external, companyA, owner.Id, ExternalAccountProvider.Google,
        "p01-owner@example.com", "P04 disconnected test calendar", null, "p04-fixture"));
    db.CalendarConnections.Add(new CalendarConnection(calendar, companyA, owner.Id, external, ExternalAccountProvider.Google,
        "p01-owner@example.com", "P04 disconnected test calendar"));
    db.SalesMeetingInvitations.Add(new SalesMeetingInvitation(Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"), companyA, lead, dealA,
        contact, calendar, ExternalAccountProvider.Google, "p01-owner@example.com", "p04-buyer@example.com", "P04 Test buyer",
        "P04 renewal preparation", "Internal fixture meeting; no delivery has been requested.", DateTime.UtcNow.AddHours(1),
        DateTime.UtcNow.AddHours(1.5), "Europe/Stockholm", null, true, owner.Id));
    db.Entry(db.Deals.Local.Single(x => x.Id == dealA)).Property(x => x.UpdatedUtc).CurrentValue = DateTime.UtcNow.AddDays(-8);
    db.SalesAgentRecommendations.Add(new SalesAgentRecommendation(Guid.NewGuid(), companyA,
        "Review P01 North renewal", "Review the next step before tomorrow's expected close.", null, dealA, requiresApproval: false));
    // P02 records are durable domain fixtures, not mocked priority responses.
    var manual = new WorkTask(Guid.Parse("66666666-6666-6666-6666-666666666666"), companyA, "follow_up",
        "P02 verify renewal terms", "Check the recorded contract terms and record the follow-up as complete.",
        WorkTaskPriority.High, null, null, "user", owner.Id);
    manual.SetDueDate(DateTime.UtcNow.AddHours(-2));
    db.WorkTasks.Add(manual);
    db.Entry(manual).Property(x => x.UpdatedUtc).CurrentValue = DateTime.UtcNow.AddDays(-2);
    var approvalTask = new WorkTask(Guid.Parse("77777777-7777-7777-7777-777777777777"), companyA, "review",
        "P02 review proposed follow-up", "Review this internal follow-up proposal.", WorkTaskPriority.High,
        null, null, "user", owner.Id, status: WorkTaskStatus.AwaitingApproval);
    db.WorkTasks.Add(approvalTask);
    db.ApprovalRequests.Add(ApprovalRequest.CreateForTarget(Guid.Parse("88888888-8888-8888-8888-888888888888"),
        companyA, ApprovalTargetEntityType.Task, approvalTask.Id, "user", owner.Id, "manual_review",
        new Dictionary<string, System.Text.Json.Nodes.JsonNode?> { ["reason"] = System.Text.Json.Nodes.JsonValue.Create("Review the internal proposal") }, null, owner.Id, []));
    var experiment = new MarketingExperiment(Guid.Parse("99999999-9999-9999-9999-999999999999"), companyA,
        "P02 renewal message experiment", "A clearer renewal message improves engagement", "engagement", "unsubscribe_rate",
        25, DateTime.UtcNow.AddDays(-7), DateTime.UtcNow.AddHours(-1), null);
    db.MarketingExperiments.Add(experiment);
    // P05 campaign/content/measurement fixtures extend the earlier journeys without dispatching.
    var campaign = Guid.Parse("05050505-0505-0505-0505-050505050505");
    var briefId = Guid.Parse("05050505-0505-0505-0505-050505050506");
    var variantId = Guid.Parse("05050505-0505-0505-0505-050505050507");
    var sequence = Guid.NewGuid(); db.SalesSequences.Add(new SalesSequence(sequence, companyA, "P05 fixture sequence"));
    var launch = new SalesCampaign(campaign, companyA, sequence, "P05 renewal launch", "b2b");
    var observed = DateTime.UtcNow;
    launch.ConfigureInitiative(CampaignTypes.ProductLaunch, "Internal test campaign", owner.Id, null, "leads", 5, "count",
        observed.AddDays(5), observed.AddDays(-2), observed.AddMinutes(-1), observed.AddDays(7), "Europe/Stockholm", 100, "SEK");
    db.SalesCampaigns.Add(launch);
    var brief = new MarketingContentBrief(briefId, companyA, "P05 renewal content", "Help customers review renewal options", "Existing renewal customers",
        "email", "English", "Clear", "Review renewal options", campaign, null, observed.AddMinutes(-1), owner.Id, null);
    brief.Submit(); db.MarketingContentBriefs.Add(brief);
    db.MarketingContentVariants.Add(new MarketingContentVariant(variantId, companyA, briefId, "P05 original message", "Review your current renewal options with your account contact.", "[\"approved-renewal-offer\"]", false));
    db.MarketingAttributionTouches.AddRange(new MarketingAttributionTouch(Guid.NewGuid(), companyA, "campaign", campaign, "view", "email", "p05-known-cost", 1, observed.AddHours(-2), 120, "SEK", "{}", "p05-known-cost"),
        new MarketingAttributionTouch(Guid.NewGuid(), companyA, "campaign", campaign, "view", "email", "p05-cost-unavailable", 1, observed.AddHours(-1), null, null, "{}", "p05-cost-unavailable"),
        new MarketingAttributionTouch(Guid.NewGuid(), companyA, "campaign", campaign, "view", "email", "p05-usd-cost", 1, observed.AddHours(-1), 9, "USD", "{}", "p05-usd-cost"));
    db.MarketingChannelObservations.Add(new MarketingChannelObservation(Guid.NewGuid(), companyA, "fixture", "leads", 4, "count", observed.AddDays(-1), observed, campaign, null, "p05-observed-leads", "p05-observed-leads"));
    var marketingConnection = Guid.NewGuid(); db.MarketingChannelConnections.Add(new MarketingChannelConnection(marketingConnection, companyA, "linkedin", "p05", "P05 disconnected fixture channel", "{}", "p05-fixture-reference", owner.Id));
    var failed = new MarketingChannelAction(Guid.Parse("05050505-0505-0505-0505-050505050508"), companyA, marketingConnection, campaign, briefId, "p05-fixture-destination", "publish", "{}", null, "p05-failed", brief.Version);
    failed.Submit(Guid.NewGuid()); failed.Queue(); failed.ClaimForDispatch(); failed.RecordFailure("provider_unavailable", true); db.MarketingChannelActions.Add(failed);
    db.SalesCampaigns.Add(new SalesCampaign(Guid.Parse("05050505-0505-0505-0505-050505050509"), companyA, sequence, "P05 budget not recorded", "b2b"));
    var rejectedAsset = new MarketingCreativeAsset(Guid.Parse("05050505-0505-0505-0505-050505050510"), companyA, briefId, campaign,
        "P05 rejected creative", "image/png", "1024x1024", "en", "Synthetic review fixture", "v1", "fixture", "brand-v1", "unverified",
        "Fixture illustration requiring revision", "fixture/p05-asset", "p05-fixture-checksum", owner.Id, "p05-asset", contentVariantId: variantId,
        provenanceJson: "{\"origin\":\"synthetic_fixture\",\"rights\":\"unverified\"}");
    rejectedAsset.Submit(); rejectedAsset.Review(false); db.MarketingCreativeAssets.Add(rejectedAsset);
    var supportCase = new SupportCase(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), companyA,
        "P02-SUP-1", "P02 renewal question", "The customer needs clarification of renewal terms.", "manual");
    supportCase.SetSla(DateTime.UtcNow.AddHours(-3), DateTime.UtcNow.AddHours(-1));
    supportCase.MarkSlaState(true, true);
    db.SupportCases.Add(supportCase);
    // P06 operational Finance fixtures: retained evidence only, no provider instruction or customer message.
    var financeNow = DateTime.UtcNow; var financeCustomer = Guid.Parse("06060606-0606-0606-0606-060606060601"); var financeVendor = Guid.Parse("06060606-0606-0606-0606-060606060602");
    var sekCash = Guid.Parse("06060606-0606-0606-0606-060606060603"); var usdCash = Guid.Parse("06060606-0606-0606-0606-060606060604");
    var financeInvoice = Guid.Parse("06060606-0606-0606-0606-060606060605"); var financeBill = Guid.Parse("06060606-0606-0606-0606-060606060606");
    db.FinanceCounterparties.AddRange(new FinanceCounterparty(financeCustomer, companyA, "P06 Customer", "customer"), new FinanceCounterparty(financeVendor, companyA, "P06 Supplier", "vendor"));
    db.FinanceAccounts.AddRange(new FinanceAccount(sekCash, companyA, "1930", "P06 Cash SEK", "asset", "SEK", 0, financeNow.AddDays(-100)), new FinanceAccount(usdCash, companyA, "1931", "P06 Cash USD", "asset", "USD", 0, financeNow.AddDays(-100)));
    db.FinanceBalances.AddRange(new FinanceBalance(Guid.NewGuid(), companyA, sekCash, financeNow.Date, 10000, "SEK"), new FinanceBalance(Guid.NewGuid(), companyA, usdCash, financeNow.Date, 500, "USD"));
    db.FinanceInvoices.AddRange(new FinanceInvoice(financeInvoice, companyA, financeCustomer, "P06 overdue invoice", financeNow.AddDays(-40), financeNow.Date.AddDays(-31), 1000, "SEK", "open", paidAmount: 200),
        new FinanceInvoice(Guid.Parse("06060606-0606-0606-0606-060606060607"), companyA, financeCustomer, "P06 USD receivable", financeNow.AddDays(-10), financeNow.Date.AddDays(7), 100, "USD", "open"));
    db.FinanceBills.Add(new FinanceBill(financeBill, companyA, financeVendor, "P06 due supplier bill", financeNow.AddDays(-20), financeNow.Date.AddDays(2), 400, "SEK", "open", paidAmount: 100));
    db.FinanceTransactions.Add(new FinanceTransaction(Guid.Parse("06060606-0606-0606-0606-060606060608"), companyA, sekCash, financeCustomer, financeInvoice, null, financeNow.AddDays(-2), "customer_payment", 200, "SEK", "P06 recorded installment", "P06-payment-reference"));
    var financeBank = Guid.Parse("06060606-0606-0606-0606-060606060609"); db.CompanyBankAccounts.Add(new CompanyBankAccount(financeBank, companyA, sekCash, "P06 Bank account", "Fixture bank", "•••• 0606", "SEK"));
    db.BankTransactions.Add(new BankTransaction(Guid.Parse("06060606-0606-0606-0606-060606060610"), companyA, financeBank, financeNow.AddDays(-2), financeNow.AddDays(-2), 200, "SEK", "P06 bank installment", "P06 Customer", "p06-bank-row", "fixture"));
    db.Payments.Add(new Payment(Guid.Parse("06060606-0606-0606-0606-060606060611"), companyA, "outgoing", 300, "SEK", financeNow.AddDays(2), "bank_transfer", "pending", "P06 Supplier"));
    var p06Batch = new PaymentBatch(Guid.Parse("06060606-0606-0606-0606-060606060612"), companyA, "P06-UNCERTAIN", "P06 uncertain instruction", DateOnly.FromDateTime(financeNow), "p06-batch", new string('a',64), owner.Id, financeNow);
    var p06Approval = ApprovalRequest.CreateForTarget(Guid.NewGuid(), companyA, ApprovalTargetEntityType.PaymentBatch, p06Batch.Id, "user", owner.Id, "payment_batch", new Dictionary<string, System.Text.Json.Nodes.JsonNode?> { ["reason"] = System.Text.Json.Nodes.JsonValue.Create("Retained controlled payment fixture") }, null, owner.Id, []);
    var p06Binding = new PaymentBatchApprovalBinding(Guid.NewGuid(), companyA, p06Batch.Id, p06Approval.Id, 1, new string('a',64), owner.Id, financeNow);
    p06Approval.ApproveCurrentStep(p06Approval.CurrentActionableStep!.Id, owner.Id, "Retained fixture approval; no provider dispatch.");
    p06Binding.MarkApproved(owner.Id, "Retained fixture approval; no provider dispatch.", financeNow);
    var p06Connection = new BankConnection(Guid.NewGuid(), companyA, "fixture", "p06-bank", "Disconnected fixture bank", owner.Id, financeNow);
    var p06Execution = new PaymentBatchExecution(Guid.Parse("06060606-0606-0606-0606-060606060613"), companyA, p06Batch.Id, 1, p06Binding.Id, p06Connection.Id, financeBank, "fixture", new string('b',64), "p06-execution", owner.Id, "p06", financeNow);
    p06Execution.RequireReconciliation("provider_timeout", "Fixture uncertain outcome. Reconcile before any retry.", financeNow); db.AddRange(p06Batch, p06Approval, p06Binding, p06Connection, p06Execution);
    return Task.CompletedTask;
});
using var api = factory.CreateClient(new() { AllowAutoRedirect = false });
await AccountingFixture.InitializeAsync(factory);
await SupportFixture.SeedAsync(factory);
await AgentWorkFixture.SeedAsync(factory);
var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
var app = builder.Build();
app.Run(async context =>
{
    if (context.Request.Path == "/_uat/health")
    {
        await Results.Ok(new { fixture = "P01", companyA, companyB, companyC }).ExecuteAsync(context);
        return;
    }
    using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), context.Request.Path + context.Request.QueryString);
    if (context.Request.ContentLength > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding"))
        request.Content = new StreamContent(context.Request.Body);
    foreach (var header in context.Request.Headers.Where(x => !string.Equals(x.Key, "Host", StringComparison.OrdinalIgnoreCase)))
        if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()) && request.Content is not null)
            request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
    using var response = await api.SendAsync(request, context.RequestAborted);
    context.Response.StatusCode = (int)response.StatusCode;
    foreach (var header in response.Headers.Concat(response.Content.Headers))
        context.Response.Headers[header.Key] = header.Value.ToArray();
    context.Response.Headers.Remove("transfer-encoding");
    await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
});
await app.RunAsync();

sealed class BrowserApiFactory : TestWebApplicationFactory
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseContentRoot(Path.GetFullPath("src/VirtualCompany.Api"));
    }
}
