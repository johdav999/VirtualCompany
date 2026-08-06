using VirtualCompany.Domain.Enums;
using Xunit;

namespace VirtualCompany.Infrastructure.Mailbox.Tests;

public sealed class StandardMailboxInboundSyncBackgroundServiceTests
{
    [Theory]
    [InlineData(MailboxProvider.Gmail, MailboxPurpose.Finance)]
    [InlineData(MailboxProvider.Microsoft365, MailboxPurpose.Finance)]
    [InlineData(MailboxProvider.StandardEmail, MailboxPurpose.Finance)]
    [InlineData(MailboxProvider.StandardEmail, MailboxPurpose.Sales)]
    [InlineData(MailboxProvider.StandardEmail, MailboxPurpose.Support)]
    public void Polling_supports_every_active_company_mailbox(
        MailboxProvider provider,
        MailboxPurpose purpose)
    {
        Assert.True(StandardMailboxInboundSyncBackgroundService.IsSupportedPurpose(provider, purpose));
    }
}
