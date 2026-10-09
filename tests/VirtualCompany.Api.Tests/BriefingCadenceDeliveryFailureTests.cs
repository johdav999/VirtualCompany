using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Briefings;
using VirtualCompany.Application.Companies;
using VirtualCompany.Infrastructure.Companies;
namespace VirtualCompany.Api.Tests;
public sealed class BriefingCadenceDeliveryFailureTests
{
    private sealed class Fault { public string? Mode; }
    private sealed class Adapter(CompanyNotificationDispatcher inner, Fault fault) : ICompanyNotificationDispatcher
    { public async Task DispatchAsync(NotificationDeliveryRequestedMessage m, CancellationToken ct) { if (fault.Mode == "before") throw new HttpRequestException("Controlled temporary delivery failure"); await inner.DispatchAsync(m, ct); if (fault.Mode == "after") throw new TimeoutException("Controlled ambiguous acknowledgment"); } }
    private sealed class Factory : TestWebApplicationFactory
    {
        public readonly Fault Fault = new(); public Factory() : base(new BriefingCadenceFixture.Clock()) { }
        protected override void ConfigureWebHost(IWebHostBuilder b) { base.ConfigureWebHost(b); b.ConfigureTestServices(s => { s.RemoveAll<ICompanyNotificationDispatcher>(); s.AddScoped<ICompanyNotificationDispatcher>(sp => new Adapter(new CompanyNotificationDispatcher(sp.GetRequiredService<VirtualCompany.Infrastructure.Persistence.VirtualCompanyDbContext>(), NullLogger<CompanyNotificationDispatcher>.Instance), Fault)); }); }
    }
    [Theory][InlineData("before", "retry", 2)][InlineData("after", "uncertain", 1)]
    public async Task Failure_retries_and_ambiguous_acknowledgment_reconciles_without_duplicate(string mode, string status, int attempts)
    {
        using var f = new Factory(); var s = await BriefingCadenceFixture.Seed(f); using var h = s.Client(f); await s.Save(h); await s.Schedule(f, new(2026,10,6,7,0,0,DateTimeKind.Utc)); await s.RunJobs(f);
        Guid id = default; await f.SeedAsync(async db => id = (await db.BriefingCadenceDeliveries.IgnoreQueryFilters().SingleAsync()).Id); f.Fault.Mode = mode;
        async Task Deliver() => await f.ExecuteScopeAsync(async scope => { using var tenant = scope.ServiceProvider.GetRequiredService<ICompanyExecutionScopeFactory>().BeginScope(s.Company); await scope.ServiceProvider.GetRequiredService<IBriefingCadenceService>().DeliverAsync(new(s.Company, id), default); });
        await Assert.ThrowsAnyAsync<Exception>(Deliver); await f.SeedAsync(async db => Assert.Equal(status, (await db.BriefingCadenceDeliveries.IgnoreQueryFilters().SingleAsync()).Status)); f.Fault.Mode = null;
        await Deliver(); await Deliver(); await f.SeedAsync(async db => { var d = await db.BriefingCadenceDeliveries.IgnoreQueryFilters().SingleAsync(); Assert.Equal("sent", d.Status); Assert.Equal(attempts, d.Attempts); Assert.Single(await db.CompanyNotifications.IgnoreQueryFilters().ToArrayAsync()); });
    }
}
