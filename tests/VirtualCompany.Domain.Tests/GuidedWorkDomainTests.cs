using System.Text.Json.Nodes;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Api.Tests;

public sealed class GuidedWorkDomainTests
{
    [Fact]
    public void Session_requires_complete_review_before_completion()
    {
        var session=CreateSession();
        session.Advance("Draft updated.","What is missing?",2,1);
        Assert.Throws<InvalidOperationException>(()=>session.PrepareReview("hash",DateTime.UtcNow.AddMinutes(15),2,1,"Review"));
        session.PrepareReview("hash",DateTime.UtcNow.AddMinutes(15),2,2,"Review");
        session.Complete("version-2");
        Assert.Equal(GuidedWorkSessionStatuses.Completed,session.Status);
        Assert.NotNull(session.CompletedUtc);
        Assert.Null(session.ReviewTokenHash);
        Assert.Throws<InvalidOperationException>(()=>session.Advance("late",null,2,2));
    }

    [Fact]
    public void Cancellation_is_terminal_and_does_not_set_completion()
    {
        var session=CreateSession();session.Cancel();
        Assert.Equal(GuidedWorkSessionStatuses.Cancelled,session.Status);Assert.NotNull(session.CancelledUtc);Assert.Null(session.CompletedUtc);
        Assert.Throws<InvalidOperationException>(()=>session.PrepareReview("hash",DateTime.UtcNow.AddMinutes(5),0,0,"Review"));
    }

    [Fact]
    public void Draft_field_records_bounded_provenance_and_clones_metadata()
    {
        var session=CreateSession();var metadata=new Dictionary<string,JsonNode?>{{"source_id",JsonValue.Create("doc-1")}};
        var field=new GuidedDraftField(Guid.NewGuid(),session.CompanyId,session.Id,"summary","Summary","text",true);
        field.Set(JsonValue.Create("A grounded summary").ToJsonString(),GuidedDraftFieldStatuses.Confirmed,"evidence",Guid.NewGuid(),metadata,"Supported by the selected source.");
        metadata["source_id"]=JsonValue.Create("changed");
        Assert.Equal("doc-1",field.SourceMetadata["source_id"]!.GetValue<string>());Assert.Equal(GuidedDraftFieldStatuses.Confirmed,field.Status);Assert.Equal(2,field.Version);
    }

    [Fact]
    public void Review_state_can_return_to_active_when_user_edits()
    {
        var session=CreateSession();session.PrepareReview("hash",DateTime.UtcNow.AddMinutes(15),1,1,"Review");session.ReturnToActive();
        Assert.Equal(GuidedWorkSessionStatuses.Active,session.Status);Assert.Null(session.ReviewTokenHash);Assert.Null(session.ReviewTokenExpiresUtc);
    }

    private static GuidedWorkSession CreateSession()=>new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"agent_operating_brief","1.0",null,"correlation");
}
