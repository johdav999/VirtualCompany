using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed class SalesMeetingCustomerMinutesDeliveryDispatcherTests
{
    [Fact]
    public async Task Successful_delivery_is_idempotent()
    {
        await using var fixture = await Fixture.CreateAsync(SenderOutcome.Success);

        await fixture.Dispatcher.DispatchAsync(fixture.Message, default);
        await fixture.Dispatcher.DispatchAsync(fixture.Message, default);

        Assert.Equal(1, fixture.Sender.Calls);
        Assert.Equal(SalesMeetingChangeProposalStatus.Executed, fixture.Proposal.Status);
        Assert.Equal("message-1", fixture.Proposal.ProviderReference);
    }

    [Fact]
    public async Task Retryable_failure_can_retry_and_then_succeed()
    {
        await using var fixture = await Fixture.CreateAsync(SenderOutcome.RetryableThenSuccess);

        await Assert.ThrowsAsync<MailboxProviderExecutionException>(() => fixture.Dispatcher.DispatchAsync(fixture.Message, default));
        Assert.Equal(SalesMeetingChangeProposalStatus.Failed, fixture.Proposal.Status);

        await fixture.Dispatcher.DispatchAsync(fixture.Message, default);

        Assert.Equal(2, fixture.Sender.Calls);
        Assert.Equal(SalesMeetingChangeProposalStatus.Executed, fixture.Proposal.Status);
    }

    [Fact]
    public async Task Ambiguous_result_requires_reconciliation_and_is_not_sent_again()
    {
        await using var fixture = await Fixture.CreateAsync(SenderOutcome.Ambiguous);

        await fixture.Dispatcher.DispatchAsync(fixture.Message, default);
        await fixture.Dispatcher.DispatchAsync(fixture.Message, default);

        Assert.Equal(1, fixture.Sender.Calls);
        Assert.Equal(SalesMeetingChangeProposalStatus.ReconciliationRequired, fixture.Proposal.Status);
        Assert.Equal("minutes_delivery_outcome_unknown", fixture.Proposal.LastErrorCode);
    }

    [Fact]
    public async Task Permanent_failure_is_recorded_without_requesting_an_outbox_retry()
    {
        await using var fixture = await Fixture.CreateAsync(SenderOutcome.Permanent);

        await fixture.Dispatcher.DispatchAsync(fixture.Message, default);

        Assert.Equal(1, fixture.Sender.Calls);
        Assert.Equal(SalesMeetingChangeProposalStatus.Failed, fixture.Proposal.Status);
        Assert.Equal("recipient_rejected", fixture.Proposal.LastErrorCode);
    }

    private enum SenderOutcome { Success, RetryableThenSuccess, Ambiguous, Permanent }

    private sealed class Sender(SenderOutcome outcome) : IOutboundEmailSender
    {
        public int Calls { get; private set; }

        public Task<OutboundEmailSendResult> SendSequenceEmailAsync(OutboundEmailSendRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            if (outcome == SenderOutcome.RetryableThenSuccess && Calls == 1)
                throw new MailboxProviderExecutionException("mailbox_throttled", "Try later.", true);
            if (outcome == SenderOutcome.Ambiguous) throw new HttpRequestException("Connection ended after the request was sent.");
            if (outcome == SenderOutcome.Permanent)
                throw new MailboxProviderExecutionException("recipient_rejected", "The recipient was rejected.", false);
            return Task.FromResult(new OutboundEmailSendResult("test", null, "message-1", null, null, "sent"));
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(VirtualCompanyDbContext db, Sender sender, SalesMeetingChangeProposal proposal)
        {
            Db = db;
            Sender = sender;
            Proposal = proposal;
            Dispatcher = new SalesMeetingCustomerMinutesDeliveryDispatcher(db, sender);
            Message = new SalesMeetingCustomerMinutesDeliveryRequestedMessage(proposal.CompanyId, proposal.Id, proposal.IdempotencyKey, "test");
        }

        public VirtualCompanyDbContext Db { get; }
        public Sender Sender { get; }
        public SalesMeetingChangeProposal Proposal { get; }
        public SalesMeetingCustomerMinutesDeliveryDispatcher Dispatcher { get; }
        public SalesMeetingCustomerMinutesDeliveryRequestedMessage Message { get; }

        public static async Task<Fixture> CreateAsync(SenderOutcome outcome)
        {
            var companyId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var sessionId = Guid.NewGuid();
            var now = DateTime.UtcNow;
            var db = new VirtualCompanyDbContext(
                new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseInMemoryDatabase($"minutes-delivery-{Guid.NewGuid():N}").Options,
                new Context(companyId, userId));
            var session = new SalesMeetingSession(sessionId, companyId, Guid.NewGuid(), Guid.NewGuid(), null, null,
                Guid.NewGuid(), "Confirm the rollout", "Buying team", 30, null, "provider-meeting",
                SalesMeetingConsentStatus.Granted, SalesMeetingRetentionPolicy.Standard, 365, now, userId, now);
            var minutes = new SalesMeetingMinutes(Guid.NewGuid(), companyId, sessionId, Guid.NewGuid(), null, 1, 1,
                now, Guid.NewGuid(), null, "test", "test", now.AddDays(30), userId, now);
            minutes.Items.Add(new SalesMeetingMinutesItem(Guid.NewGuid(), companyId, minutes.Id, 0,
                SalesMeetingMinutesItemType.Action, "Send the rollout plan.", "Alex", now.AddDays(2), "minutes:item:1", null, false, now));
            minutes.SubmitForReview(userId, minutes.ConcurrencyVersion, now);
            minutes.Approve(userId, minutes.ConcurrencyVersion, false, now);
            var proposal = new SalesMeetingChangeProposal(Guid.NewGuid(), companyId, sessionId, Guid.NewGuid(),
                SalesMeetingChangeTargetType.CustomerMinutes, minutes.Id, SalesMeetingChangeAction.SendCustomerMinutes,
                SalesMeetingChangeField.CustomerMinutesRecipient, SalesMeetingChangeValueKind.Email,
                JsonSerializer.Serialize("buyer@example.com"), JsonSerializer.Serialize("not_sent"),
                minutes.ConcurrencyVersion.ToString(), .98m, "Send the approved customer minutes.", "[]", "evidence-hash",
                SalesMeetingChangeRiskClass.AlwaysGated, true, SalesMeetingChangePolicy.CurrentVersion, userId, now);
            proposal.Approve(proposal.ConcurrencyVersion, "exact-binding", userId, now);
            proposal.BeginExecution();
            proposal.MarkQueued(JsonSerializer.Serialize("not_sent"), JsonSerializer.Serialize("delivery_queued"), now);
            db.SalesMeetingSessions.Add(session);
            db.SalesMeetingMinutes.Add(minutes);
            db.SalesMeetingChangeProposals.Add(proposal);
            await db.SaveChangesAsync();
            return new Fixture(db, new Sender(outcome), proposal);
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class Context(Guid companyId, Guid userId) : ICompanyContextAccessor
    {
        public Guid? CompanyId { get; private set; } = companyId;
        public Guid? UserId { get; private set; } = userId;
        public bool IsResolved => true;
        public ResolvedCompanyMembershipContext? Membership { get; private set; }
        public void SetCompanyId(Guid? value) => CompanyId = value;
        public void SetCompanyContext(ResolvedCompanyMembershipContext? value) { Membership = value; CompanyId = value?.CompanyId; UserId = value?.UserId; }
    }
}
