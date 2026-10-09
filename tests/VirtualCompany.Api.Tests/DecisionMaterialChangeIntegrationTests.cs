using System.Net.Http.Json;
using System.Text.Json.Nodes;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Api.Tests;

public sealed class DecisionMaterialChangeIntegrationTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Editing_pending_or_approved_work_invalidates_its_original_review(bool approve)
    {
        using var factory = new TestWebApplicationFactory(); var company = Guid.NewGuid(); var human = Guid.NewGuid(); var task = Guid.NewGuid();
        await CollaborationEvidenceIntegrationTests.SeedOwner(factory, company, human);
        await factory.SeedAsync(db => { db.Add(new WorkTask(task, company, "sales_review", "Revised terms", "Review discount",
            WorkTaskPriority.High, null, null, "user", human)); return Task.CompletedTask; });
        using var client = CollaborationEvidenceIntegrationTests.Client(factory);
        var response = await client.PostReviewAsync($"/api/companies/{company}/approvals", new CreateApprovalRequestCommand("task", task, "user", human,
            "terms", new() { ["reason"] = JsonValue.Create("Review proposed terms") }, RequiredUserId: human));
        response.EnsureSuccessStatusCode(); var review = (await response.Content.ReadFromJsonAsync<ApprovalRequestDto>())!;
        var route = $"/api/companies/{company}/approvals/{review.Id}";
        if (approve) (await client.PostReviewAsync(route + "/decisions", new ApprovalDecisionCommand(review.Id, "approve", review.CurrentStep!.Id, ReviewToken: review.Review!.Token))).EnsureSuccessStatusCode();
        (await client.PatchAsync($"/api/companies/{company}/tasks/{task}/status", JsonContent.Create(new { status = "in_progress", outputPayload = new { discount = "10%" } }))).EnsureSuccessStatusCode();
        var changed = (await client.GetFromJsonAsync<ApprovalRequestDto>(route))!;
        Assert.Equal("stale", changed.Status); Assert.False(changed.Review!.CanDecide);
    }
}
