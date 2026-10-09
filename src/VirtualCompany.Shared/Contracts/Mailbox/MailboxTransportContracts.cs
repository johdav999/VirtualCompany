using VirtualCompany.Domain.Enums;
using System.Net;

namespace VirtualCompany.Application.Mailbox;
public sealed record StandardMailboxConnectionResult(Guid? ConnectionId, bool IncomingSucceeded, bool SendingSucceeded, string EmailAddress, MailboxCapability Capabilities, IReadOnlyList<MailboxTransportFolder> Folders, string? FailureCode, string? FailureMessage, DateTime CheckedUtc)
{
    public Guid? ConnectionId { get; set; } = ConnectionId;
    public bool IncomingSucceeded { get; set; } = IncomingSucceeded;
    public bool SendingSucceeded { get; set; } = SendingSucceeded;
    public string EmailAddress { get; set; } = EmailAddress;
    public MailboxCapability Capabilities { get; set; } = Capabilities;
    public IReadOnlyList<MailboxTransportFolder> Folders { get; set; } = Folders;
    public string? FailureCode { get; set; } = FailureCode;
    public string? FailureMessage { get; set; } = FailureMessage;
    public DateTime CheckedUtc { get; set; } = CheckedUtc;

    public StandardMailboxConnectionResult() : this(default !, default !, default !, string.Empty, default !, [], default !, default !, default !)
    {
    }
}

public sealed record MailboxTransportFolder(string FolderId, string DisplayName, bool CanRead, bool CanAppend, bool IsInbox, bool IsDrafts, bool IsSent)
{
    public string FolderId { get; set; } = FolderId;
    public string DisplayName { get; set; } = DisplayName;
    public bool CanRead { get; set; } = CanRead;
    public bool CanAppend { get; set; } = CanAppend;
    public bool IsInbox { get; set; } = IsInbox;
    public bool IsDrafts { get; set; } = IsDrafts;
    public bool IsSent { get; set; } = IsSent;

    public MailboxTransportFolder() : this(string.Empty, string.Empty, default !, default !, default !, default !, default !)
    {
    }
}
