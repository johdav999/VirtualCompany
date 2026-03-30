using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VirtualCompany.Application.Companies;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Api.Tests;

public sealed class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configurationBuilder) =>
        {
            configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{CompanyOutboxDispatcherOptions.SectionName}:Enabled"] = "false",
                [$"{CompanyOutboxDispatcherOptions.SectionName}:RetryDelaySeconds"] = "0"
            });
        });

        builder.ConfigureServices(services =>
    {
            services.RemoveAll<DbContextOptions<VirtualCompanyDbContext>>();
            services.RemoveAll<VirtualCompanyDbContext>();

            services.AddDbContext<VirtualCompanyDbContext>(options =>
                options.UseInMemoryDatabase($"virtual-company-tests-{Guid.NewGuid()}"));

            services.RemoveAll<ICompanyInvitationSender>();
            services.AddSingleton<TestCompanyInvitationSender>();
            services.AddSingleton<ICompanyInvitationSender>(provider => provider.GetRequiredService<TestCompanyInvitationSender>());
        });
    }

    public TestCompanyInvitationSender InvitationSender =>
        Services.GetRequiredService<TestCompanyInvitationSender>();

    public async Task SeedAsync(Func<VirtualCompanyDbContext, Task> seed)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        await dbContext.Database.EnsureCreatedAsync();
        await seed(dbContext);
        await dbContext.SaveChangesAsync();
    }

    public sealed class TestCompanyInvitationSender : ICompanyInvitationSender
    {
        private readonly ConcurrentQueue<CompanyInvitationDeliveryRequestedMessage> _sent = new();
        private int _attemptCount;
        private int _remainingFailures;

        public IReadOnlyList<CompanyInvitationDeliveryRequestedMessage> Sent => _sent.ToArray();
        public int AttemptCount => Volatile.Read(ref _attemptCount);

        public void Reset()
        {
            while (_sent.TryDequeue(out _))
            {
            }

            Interlocked.Exchange(ref _attemptCount, 0);
            Interlocked.Exchange(ref _remainingFailures, 0);
        }

        public void FailNext(int count = 1) =>
            Interlocked.Exchange(ref _remainingFailures, Math.Max(0, count));

        public Task<CompanyInvitationSendResult> SendAsync(CompanyInvitationDeliveryRequestedMessage invitation, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _attemptCount);

            if (TryConsumeFailure())
            {
                throw new InvalidOperationException("Configured invitation delivery failure.");
            }

            _sent.Enqueue(invitation);
            return Task.FromResult(new CompanyInvitationSendResult($"test:{invitation.InvitationId:N}:{AttemptCount}"));
        }

        private bool TryConsumeFailure()
        {
            while (true)
            {
                var remaining = Volatile.Read(ref _remainingFailures);
                if (remaining <= 0)
                {
                    return false;
                }

                if (Interlocked.CompareExchange(ref _remainingFailures, remaining - 1, remaining) == remaining)
                {
                    return true;
                }
            }
        }
    }
}