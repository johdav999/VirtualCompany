using System.IO;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VirtualCompany.Application.Companies;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class NotificationOutboxDispatcherTests : IClassFixture<TestWebApplicationFactory>
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly TestWebApplicationFactory _factory;

    public NotificationOutboxDispatcherTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task OutboxProcessor_dispatches_pending_notification_and_marks_message_processed()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var outboxMessageId = Guid.NewGuid();
        var sourceEntityId = Guid.NewGuid();
        var dedupeKey = $"approval-requested:{sourceEntityId:N}";

        await SeedNotificationOutboxAsync(
            _factory.Services,
            companyId,
            userId,
            outboxMessageId,
            new NotificationDeliveryRequestedMessage(
                companyId,
                CompanyNotificationType.ApprovalRequested.ToStorageValue(),
                CompanyNotificationPriority.High.ToStorageValue(),
                "Approval requested",
                "Review the approval request.",
                "approval_request",
                sourceEntityId,
                "/inbox",
                userId,
                null,
                null,
                null,
                dedupeKey,
                "notification-success"));

        using (var scope = _factory.Services.CreateScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<ICompanyOutboxProcessor>();
            var handledCount = await processor.DispatchPendingAsync(CancellationToken.None);

            Assert.Equal(1, handledCount);
        }

        using var assertionScope = _factory.Services.CreateScope();
        var dbContext = assertionScope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var notification = await dbContext.CompanyNotifications
            .IgnoreQueryFilters()
            .SingleAsync(x => x.CompanyId == companyId && x.UserId == userId);
        var outboxMessage = await dbContext.CompanyOutboxMessages.SingleAsync(x => x.Id == outboxMessageId);

        Assert.Equal(CompanyNotificationStatus.Unread, notification.Status);
        Assert.Equal(CompanyNotificationType.ApprovalRequested, notification.Type);
        Assert.Equal(CompanyNotificationPriority.High, notification.Priority);
        Assert.Equal(sourceEntityId, notification.RelatedEntityId);
        Assert.Equal($"{dedupeKey}:{userId:N}", notification.DedupeKey);
        Assert.NotNull(outboxMessage.ProcessedUtc);
        Assert.Equal(CompanyOutboxMessageStatus.Dispatched, outboxMessage.Status);
    }

    [Fact]
    public async Task OutboxProcessor_treats_duplicate_notification_delivery_as_success()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var outboxMessageId = Guid.NewGuid();
        var sourceEntityId = Guid.NewGuid();
        var dedupeKey = $"workflow-exception:{sourceEntityId:N}";
        var recipientDedupeKey = $"{dedupeKey}:{userId:N}";

        await SeedNotificationOutboxAsync(
            _factory.Services,
            companyId,
            userId,
            outboxMessageId,
            new NotificationDeliveryRequestedMessage(
                companyId,
                CompanyNotificationType.WorkflowFailure.ToStorageValue(),
                CompanyNotificationPriority.Critical.ToStorageValue(),
                "Workflow failed",
                "A workflow failed.",
                "workflow_exception",
                sourceEntityId,
                "/workflows",
                userId,
                null,
                null,
                null,
                dedupeKey,
                "notification-duplicate"),
            dbContext =>
            {
                dbContext.CompanyNotifications.Add(new CompanyNotification(
                    Guid.NewGuid(),
                    companyId,
                    userId,
                    CompanyNotificationType.WorkflowFailure,
                    CompanyNotificationPriority.Critical,
                    "Workflow failed",
                    "A workflow failed.",
                    "workflow_exception",
                    sourceEntityId,
                    "/workflows",
                    "{}",
                    recipientDedupeKey));

                return Task.CompletedTask;
            });

        using (var scope = _factory.Services.CreateScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<ICompanyOutboxProcessor>();
            var handledCount = await processor.DispatchPendingAsync(CancellationToken.None);

            Assert.Equal(1, handledCount);
        }

        using var assertionScope = _factory.Services.CreateScope();
        var dbContext = assertionScope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var outboxMessage = await dbContext.CompanyOutboxMessages.SingleAsync(x => x.Id == outboxMessageId);

        Assert.Equal(1, await dbContext.CompanyNotifications.IgnoreQueryFilters().CountAsync(x =>
            x.CompanyId == companyId &&
            x.UserId == userId &&
            x.DedupeKey == recipientDedupeKey));
        Assert.NotNull(outboxMessage.ProcessedUtc);
        Assert.Equal(CompanyOutboxMessageStatus.Dispatched, outboxMessage.Status);
    }

    [Fact]
    public async Task OutboxProcessor_leaves_transient_notification_failure_retryable()
    {
        using var retryFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICompanyNotificationDispatcher>();
                services.AddScoped<ICompanyNotificationDispatcher, FailingNotificationDispatcher>();
            });
        });

        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var outboxMessageId = Guid.NewGuid();
        var sourceEntityId = Guid.NewGuid();

        await SeedNotificationOutboxAsync(
            retryFactory.Services,
            companyId,
            userId,
            outboxMessageId,
            new NotificationDeliveryRequestedMessage(
                companyId,
                CompanyNotificationType.Escalation.ToStorageValue(),
                CompanyNotificationPriority.High.ToStorageValue(),
                "Escalation required",
                "A background process needs review.",
                "execution_exception",
                sourceEntityId,
                "/dashboard",
                userId,
                null,
                null,
                null,
                $"execution-exception:{sourceEntityId:N}",
                "notification-retry"));

        using (var scope = retryFactory.Services.CreateScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<ICompanyOutboxProcessor>();
            var handledCount = await processor.DispatchPendingAsync(CancellationToken.None);

            Assert.Equal(0, handledCount);
        }

        using var assertionScope = retryFactory.Services.CreateScope();
        var dbContext = assertionScope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var outboxMessage = await dbContext.CompanyOutboxMessages.SingleAsync(x => x.Id == outboxMessageId);

        Assert.Equal(CompanyOutboxMessageStatus.RetryScheduled, outboxMessage.Status);
        Assert.Equal(1, outboxMessage.AttemptCount);
        Assert.NotNull(outboxMessage.LastAttemptUtc);
        Assert.NotNull(outboxMessage.LastError);
        Assert.Null(outboxMessage.ProcessedUtc);
        Assert.Empty(await dbContext.CompanyNotifications.IgnoreQueryFilters().Where(x => x.CompanyId == companyId).ToListAsync());
    }

    private static async Task SeedNotificationOutboxAsync(
        IServiceProvider services,
        Guid companyId,
        Guid userId,
        Guid outboxMessageId,
        NotificationDeliveryRequestedMessage message,
        Func<VirtualCompanyDbContext, Task>? extraSeed = null)
    {
        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        await dbContext.Database.EnsureCreatedAsync();

        dbContext.Users.Add(new User(userId, "recipient@example.com", "Recipient", "dev-header", $"recipient-{userId:N}"));
        dbContext.Companies.Add(new Company(companyId, "Company A"));
        dbContext.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
        dbContext.CompanyOutboxMessages.Add(new CompanyOutboxMessage(
            outboxMessageId,
            companyId,
            CompanyOutboxTopics.NotificationDeliveryRequested,
            JsonSerializer.Serialize(message, SerializerOptions),
            correlationId: message.CorrelationId,
            messageType: typeof(NotificationDeliveryRequestedMessage).FullName,
            idempotencyKey: $"notification:{message.DedupeKey}"));

        if (extraSeed is not null)
        {
            await extraSeed(dbContext);
        }

        await dbContext.SaveChangesAsync();
    }

    private sealed class FailingNotificationDispatcher : ICompanyNotificationDispatcher
    {
        public Task DispatchAsync(NotificationDeliveryRequestedMessage message, CancellationToken cancellationToken) =>
            throw new IOException("Configured notification dispatcher failure.");
    }
}
