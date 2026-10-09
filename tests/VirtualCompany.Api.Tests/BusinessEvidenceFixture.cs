using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Api.Tests;

public sealed record BusinessEvidenceFixture(Guid CompanyId, IReadOnlyDictionary<string, Guid> Records,
    IReadOnlyDictionary<string, Guid> Tasks, IReadOnlyDictionary<string, Guid> Approvals)
{
    public static async Task<BusinessEvidenceFixture> SeedAsync(TestWebApplicationFactory factory, Guid company, Guid owner,
        string subject = "p13-owner", bool reuseRecords = false)
    {
        var records = new Dictionary<string, Guid>(); var tasks = new Dictionary<string, Guid>();
        await factory.SeedAsync(async db =>
        {
            async Task<Guid> Existing(IQueryable<Guid> query) => reuseRecords ? await query.FirstOrDefaultAsync() : Guid.Empty;
            records["deal"] = await Existing(db.Deals.IgnoreQueryFilters().Where(x => x.CompanyId == company && !x.IsDeleted).Select(x => x.Id));
            if (records["deal"] == Guid.Empty)
            {
                var stage = new SalesPipelineStage(Guid.NewGuid(), company, "P13 proposal", 10);
                db.Add(stage); var deal = new Deal(Guid.NewGuid(), company, "P13 renewal proposal", stage.Id, 1000, "SEK"); db.Add(deal); records["deal"] = deal.Id;
            }
            records["case"] = await Existing(db.SupportCases.IgnoreQueryFilters().Where(x => x.CompanyId == company).Select(x => x.Id));
            if (records["case"] == Guid.Empty)
            {
                var support = new SupportCase(Guid.NewGuid(), company, "P13-CASE", "P13 renewal question", "Check the retained renewal offer.", "manual");
                db.Add(support); records["case"] = support.Id;
            }
            var customer = new FinanceCounterparty(Guid.NewGuid(), company, "P13 Customer", "customer");
            var supplier = new FinanceCounterparty(Guid.NewGuid(), company, "P13 Supplier", "vendor"); db.AddRange(customer, supplier);
            records["invoice"] = await Existing(db.FinanceInvoices.IgnoreQueryFilters().Where(x => x.CompanyId == company).Select(x => x.Id));
            if (records["invoice"] == Guid.Empty)
            {
                var invoice = new FinanceInvoice(Guid.NewGuid(), company, customer.Id, "P13-INV", DateTime.UtcNow.AddDays(-4), DateTime.UtcNow.AddDays(4), 1000, "SEK", "open");
                db.Add(invoice); records["invoice"] = invoice.Id;
            }
            records["bill"] = await Existing(db.FinanceBills.IgnoreQueryFilters().Where(x => x.CompanyId == company).Select(x => x.Id));
            if (records["bill"] == Guid.Empty)
            {
                var bill = new FinanceBill(Guid.NewGuid(), company, supplier.Id, "P13-BILL", DateTime.UtcNow.AddDays(-4), DateTime.UtcNow.AddDays(4), 400, "SEK", "open");
                db.Add(bill); records["bill"] = bill.Id;
            }
            records["campaign"] = await Existing(db.SalesCampaigns.IgnoreQueryFilters().Where(x => x.CompanyId == company).Select(x => x.Id));
            if (records["campaign"] == Guid.Empty)
            {
                var sequence = new SalesSequence(Guid.NewGuid(), company, "P13 sequence"); db.Add(sequence);
                var campaign = new SalesCampaign(Guid.NewGuid(), company, sequence.Id, "P13 campaign", "b2b"); db.Add(campaign); records["campaign"] = campaign.Id;
            }
            records["brief"] = await Existing(db.MarketingContentBriefs.IgnoreQueryFilters().Where(x => x.CompanyId == company && x.SalesCampaignId == records["campaign"]).Select(x => x.Id));
            if (records["brief"] == Guid.Empty)
            {
                var brief = new MarketingContentBrief(Guid.NewGuid(), company, "P13 renewal content", "Clarify renewal terms", "Current customers", "email", "English", "Clear",
                    "Review terms", records["campaign"], null, null, owner, null); db.Add(brief); records["brief"] = brief.Id;
                db.Add(new MarketingContentVariant(Guid.NewGuid(), company, brief.Id, "P13 draft variant", "Review your retained renewal offer.", "[]", false));
            }
            foreach (var record in records)
            {
                var department = record.Key switch { "deal" => "Sales", "case" => "Support", "invoice" or "bill" => "Finance", _ => "Marketing" };
                var agent = new Agent(Guid.NewGuid(), company, "p13-" + record.Key, "P13 " + department, department + " specialist", department, null, AgentSeniority.Senior, AgentStatus.Active); db.Add(agent);
                var key = record.Key switch { "case" => "caseId", "brief" => "briefId", _ => record.Key + "Id" };
                var task = new WorkTask(Guid.NewGuid(), company, department.ToLowerInvariant() + "_review", "P13 " + record.Key + " internal evidence review",
                    "Synthetic internal review. Approval does not send, publish, post or move money.", WorkTaskPriority.High, agent.Id, null, "user", owner,
                    new Dictionary<string, JsonNode?> { [key] = JsonValue.Create(record.Value), ["proposalVersion"] = JsonValue.Create(1) },
                    rationaleSummary: "The accountable human must review the retained evidence before internal work continues.");
                db.Add(task); tasks[record.Key] = task.Id;
                if (record.Key == "case") db.Add(new SupportReplyDraft(Guid.NewGuid(), company, record.Value,
                    "Please review the renewal terms retained in your agreement.", "Clear", .6m, .6m, "Partial grounding needs a human review.", null, agent.Id, null));
            }
            db.Add(new SalesAgentRecommendation(Guid.NewGuid(), company, "P13 prepare renewal proposal", "Human review of proposed terms is required; customer delivery is separate.", null, records["deal"]));
        });
        using var client = factory.CreateClient(); client.DefaultRequestHeaders.Add("X-Dev-Auth-Subject", subject);
        client.DefaultRequestHeaders.Add("X-Dev-Auth-Email", subject + "@example.com");
        var approvals = new Dictionary<string, Guid>();
        foreach (var task in tasks)
        {
            using var response = await System.Net.Http.Json.HttpClientJsonExtensions.PostAsJsonAsync(client, $"/api/companies/{company}/approvals",
                new CreateApprovalRequestCommand("task", task.Value, "user", owner, "manual_review", new Dictionary<string, JsonNode?> { ["reason"] = JsonValue.Create("Review this retained internal proposal. Delivery is a separate business action.") }, RequiredUserId: owner),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException(await response.Content.ReadAsStringAsync());
            approvals[task.Key] = (await response.Content.ReadFromJsonAsync<ApprovalRequestDto>())!.Id;
        }
        return new(company, records, tasks, approvals);
    }
}
