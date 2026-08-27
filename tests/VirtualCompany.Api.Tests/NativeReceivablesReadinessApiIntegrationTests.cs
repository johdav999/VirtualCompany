using System.Net;
using System.Text.Json;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;

namespace VirtualCompany.Api.Tests;

public sealed class NativeReceivablesReadinessApiIntegrationTests : IDisposable
{
    private readonly TestWebApplicationFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Readiness_is_bounded_company_scoped_and_blocks_when_ar_control_is_unavailable()
    {
        var seed = await SeedAsync();
        using var client = Client(seed.Subject, seed.Email);

        using var response = await client.GetAsync(Route(seed.CompanyId));
        using var crossCompany = await client.GetAsync(Route(seed.UnownedCompanyId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, crossCompany.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal(seed.CompanyId, root.GetProperty("companyId").GetGuid());
        Assert.False(root.GetProperty("isReady").GetBoolean());
        Assert.Equal("blocking", root.GetProperty("status").GetString());

        var signals = root.GetProperty("signals").EnumerateArray().ToArray();
        Assert.Equal(10, signals.Length);
        var numbering = Signal(signals, "numbering_gaps");
        Assert.Equal(1, numbering.GetProperty("count").GetInt32());
        Assert.Equal(seed.OwnedGapId, Assert.Single(numbering.GetProperty("subjectIds").EnumerateArray()).GetGuid());
        Assert.NotEqual(seed.UnownedGapId, seed.OwnedGapId);
        Assert.Equal("blocking", Signal(signals, "receivables_control").GetProperty("status").GetString());
        Assert.All(signals, signal => Assert.InRange(signal.GetProperty("subjectIds").GetArrayLength(), 0, 25));
    }

    private async Task<Seed> SeedAsync()
    {
        var companyId = Guid.NewGuid();
        var unownedCompanyId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var ownedGapId = Guid.NewGuid();
        var unownedGapId = Guid.NewGuid();
        const string subject = "receivables-readiness-owner";
        const string email = "receivables-readiness-owner@example.test";
        var now = new DateTime(2026, 8, 27, 8, 0, 0, DateTimeKind.Utc);

        await _factory.SeedAsync(db =>
        {
            db.Users.Add(new User(ownerId, email, "Receivables readiness owner", "dev-header", subject));
            db.Companies.AddRange(new Company(companyId, "Readiness company"),
                new Company(unownedCompanyId, "Unowned readiness company"));
            db.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, ownerId,
                CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));

            var ownedSeries = new StatutoryDocumentSeries(Guid.NewGuid(), companyId, "INV", "invoice",
                new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "INV-", 6, 1, ownerId, now);
            var series = new StatutoryDocumentSeries(Guid.NewGuid(), unownedCompanyId, "INV", "invoice",
                new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "INV-", 6, 1, ownerId, now);
            db.StatutoryDocumentSeries.AddRange(ownedSeries, series);
            db.StatutoryDocumentNumberAllocations.Add(new StatutoryDocumentNumberAllocation(ownedGapId,
                companyId, ownedSeries.Id, ownedSeries.FiscalYearKey, 1, "INV-000001",
                StatutoryDocumentAllocationStatuses.Gap, "Posting transaction rolled back.",
                "owned-gap", 1, null, ownerId, now));
            db.StatutoryDocumentNumberAllocations.Add(new StatutoryDocumentNumberAllocation(unownedGapId,
                unownedCompanyId, series.Id, series.FiscalYearKey, 1, "INV-000001",
                StatutoryDocumentAllocationStatuses.Gap, "Posting transaction rolled back.",
                "unowned-gap", 1, null, ownerId, now));
            return Task.CompletedTask;
        });

        return new Seed(companyId, unownedCompanyId, ownedGapId, unownedGapId, subject, email);
    }

    private HttpClient Client(string subject, string email)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.DisplayNameHeader, subject);
        return client;
    }

    private static JsonElement Signal(IEnumerable<JsonElement> signals, string key) =>
        signals.Single(x => x.GetProperty("key").GetString() == key);

    private static string Route(Guid companyId) =>
        $"/api/companies/{companyId:D}/finance/receivables/readiness";

    private sealed record Seed(Guid CompanyId, Guid UnownedCompanyId, Guid OwnedGapId, Guid UnownedGapId,
        string Subject, string Email);
}
