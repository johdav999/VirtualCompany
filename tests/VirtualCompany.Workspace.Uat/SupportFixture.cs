using System.Text.Json.Nodes;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Api.Tests;

internal static class SupportFixture
{
    public static Task SeedAsync(TestWebApplicationFactory factory) => factory.SeedAsync(db => {
        var company = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var contact = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var customer = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var ben = Guid.Parse("08080808-0808-0808-0808-080808080801");
        var now = DateTime.UtcNow;
        db.Agents.Add(new Agent(ben, company, "ben_support", "Ben", "Support manager", "Support", null, AgentSeniority.Senior));
        var c = new SupportCase(Guid.Parse("08080808-0808-0808-0808-080808080802"), company, "P08-ACCESS", "Customer account access",
            "How can I review account access? Please verify our customer account before advising.", "manual", contact, customer, ben, createdUtc:now.AddDays(-2));
        c.SetSla(now.AddMinutes(90), now.AddHours(8)); c.MarkCustomerMessage(now.AddHours(-1));
        c.Messages.Add(new SupportMessage(Guid.NewGuid(),company,c.Id,SupportMessageDirections.Inbound,"manual","p08-buyer@example.test",null,c.Description!,now.AddHours(-1)));
        db.SupportCases.Add(c);
        var history = new SupportCase(Guid.Parse("08080808-0808-0808-0808-080808080803"), company, "P08-HISTORY", "Previous account question", "Previous internal verification completed.","manual",contact,customer, createdUtc:now.AddDays(-35));
        history.SetStatus(SupportCaseStatuses.Resolved); history.SetStatus(SupportCaseStatuses.Reopened); history.SetSla(now.AddDays(-3),now.AddDays(-1));
        var waiting = new SupportCase(Guid.Parse("08080808-0808-0808-0808-080808080804"),company,"P08-WAITING","Waiting for specialist evidence",null,"manual", createdUtc:now.AddDays(-10));
        waiting.SetStatus(SupportCaseStatuses.WaitingInternal); waiting.SetSla(now.AddDays(-2),now.AddHours(20));
        var missing = new SupportCase(Guid.Parse("08080808-0808-0808-0808-080808080805"),company,"P08-GAP","Unsupported product question","Is an unrecorded product feature available?","manual",createdUtc:now.AddDays(-1));
        db.SupportCases.AddRange(history,waiting,missing);
        var docId = Guid.Parse("08080808-0808-0808-0808-080808080806");
        var content = "Customer account access: verify the customer account reference with a support specialist. Never ask for a password. Account ownership requires internal review before changing access.";
        var doc = new CompanyKnowledgeDocument(docId,company,"Customer account access guide",CompanyKnowledgeDocumentType.Reference,"fixture/p08/access.md",null,"access.md","text/markdown",".md",content.Length,
            accessScope:new CompanyKnowledgeDocumentAccessScope(company,CompanyKnowledgeDocumentAccessScope.CompanyVisibility));
        doc.MarkScanClean();doc.MarkProcessing();doc.MarkProcessed();doc.MarkIndexed(content,1,1,"deterministic","fixture","v1",256,"p08-access-v1");
        db.CompanyKnowledgeDocuments.Add(doc);
        db.CompanyKnowledgeChunks.Add(new CompanyKnowledgeChunk(Guid.NewGuid(),company,docId,1,0,content,
            "["+string.Join(',',Enumerable.Repeat("0.0625",256))+"]","deterministic","fixture","v1",256,sourceReference:"access.md:1"));
        var protectedDoc = new CompanyKnowledgeDocument(Guid.NewGuid(),company,"Private account credentials",CompanyKnowledgeDocumentType.Reference,"fixture/p08/private.md",null,"private.md","text/markdown",".md",64,
            accessScope:new CompanyKnowledgeDocumentAccessScope(company,CompanyKnowledgeDocumentAccessScope.CompanyVisibility,new Dictionary<string,JsonNode?> { ["restricted"]=JsonValue.Create(true) }));
        protectedDoc.MarkScanClean();protectedDoc.MarkProcessing();protectedDoc.MarkProcessed();protectedDoc.MarkIndexed("Private account access fixture secret",1,1,"deterministic","fixture","v1",256,"p08-private-v1");
        db.CompanyKnowledgeDocuments.Add(protectedDoc);
        db.CompanyKnowledgeChunks.Add(new CompanyKnowledgeChunk(Guid.NewGuid(),company,protectedDoc.Id,1,0,"Private account access fixture secret","["+string.Join(',',Enumerable.Repeat("0.0625",256))+"]","deterministic","fixture","v1",256,sourceReference:"private.md:1"));
        return Task.CompletedTask;
    });
}
