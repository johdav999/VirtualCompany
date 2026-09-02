using System.Net;
using System.Text;

namespace VirtualCompany.Web.Tests;

public sealed class ResponsibilitySettingsApiClientTests
{
    [Fact]
    public async Task Typed_client_uses_company_transport_for_read_preview_apply_upsert_and_remove()
    {
        var companyId = Guid.NewGuid();
        var assignmentId = Guid.NewGuid();
        var handler = new ResponsibilityHandler(companyId, assignmentId);
        var client = CreateClient(handler);

        var read = await client.GetAsync(companyId);
        var preview = await client.PreviewAsync(companyId, new() { CompanySize = "small", OwnerMembershipId = Guid.NewGuid() });
        var applied = await client.ApplyAsync(companyId, new() { CompanySize = "small", OwnerMembershipId = Guid.NewGuid() });
        var saved = await client.UpsertAsync(companyId, new() { ResponsibilityArea = "sales", AssignedMembershipId = Guid.NewGuid() });
        await client.RemoveAsync(companyId, assignmentId, 4, "handoff");

        Assert.True(read!.CanManage);
        Assert.Single(preview.Changes);
        Assert.Single(applied.Assignments);
        Assert.Equal(assignmentId, saved.Id);
        Assert.Equal(
            [HttpMethod.Get, HttpMethod.Post, HttpMethod.Post, HttpMethod.Put, HttpMethod.Delete],
            handler.Requests.Select(request => request.Method).ToArray());
        Assert.All(handler.Requests, request => Assert.Equal(companyId.ToString(), request.Headers.GetValues("X-Company-Id").Single()));
        Assert.Contains($"expectedVersion=4", handler.Requests[^1].RequestUri!.Query);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task Read_returns_null_for_an_unavailable_company_without_disclosure(HttpStatusCode status)
    {
        var client = CreateClient(new StaticHandler(status, string.Empty));
        Assert.Null(await client.GetAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Mutation_preserves_server_field_errors()
    {
        var json = """{"detail":"Choose an active member.","errors":{"assignedMembershipId":["The member is inactive."]}}""";
        var client = CreateClient(new StaticHandler(HttpStatusCode.BadRequest, json));

        var exception = await Assert.ThrowsAsync<ResponsibilitySettingsApiException>(() => client.UpsertAsync(
            Guid.NewGuid(), new() { ResponsibilityArea = "sales", AssignedMembershipId = Guid.NewGuid() }));

        Assert.Equal("The member is inactive.", exception.Errors["assignedMembershipId"].Single());
    }

    private static ResponsibilitySettingsApiClient CreateClient(HttpMessageHandler handler) => new(
        new CompanyApiTransport(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") }),
        false, new FallbackProblemResolver());

    private sealed class FallbackProblemResolver : IApiProblemMessageResolver
    {
        public string Resolve(ApiProblemResponse? problem, string fallbackMessage) => problem?.Detail ?? fallbackMessage;
    }

    private sealed class ResponsibilityHandler(Guid companyId, Guid assignmentId) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var path = request.RequestUri!.AbsolutePath;
            var json = request.Method == HttpMethod.Get
                ? $$"""{"companyId":"{{companyId}}","companySize":"micro","assignments":[],"availablePresets":[],"canManage":true,"members":[],"agents":[]}"""
                : path.EndsWith("/presets/preview", StringComparison.Ordinal)
                    ? $$"""{"companyId":"{{companyId}}","companySize":"small","mode":"fill_missing","changes":[{"responsibilityArea":"sales","assignmentKind":"primary","changeKind":"add","assignedMembershipId":"{{Guid.NewGuid()}}"}]}"""
                    : path.EndsWith("/presets/apply", StringComparison.Ordinal)
                        ? $$"""{"preview":{"companyId":"{{companyId}}","companySize":"small","mode":"fill_missing","changes":[]},"assignments":[{{AssignmentJson(companyId, assignmentId)}}]}"""
                        : request.Method == HttpMethod.Put ? AssignmentJson(companyId, assignmentId) : string.Empty;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }

        private static string AssignmentJson(Guid companyId, Guid assignmentId) => $$"""{"id":"{{assignmentId}}","companyId":"{{companyId}}","responsibilityArea":"sales","assignmentKind":"primary","assignedMember":{"membershipId":"{{Guid.NewGuid()}}","displayName":"Owner","role":"owner","status":"active"},"authorityLevel":"level_1","version":1}""";
    }

    private sealed class StaticHandler(HttpStatusCode status, string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(content, Encoding.UTF8, "application/json") });
    }
}
