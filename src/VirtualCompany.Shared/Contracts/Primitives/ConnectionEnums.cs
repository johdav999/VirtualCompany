using System.Text.Json.Serialization;
using VirtualCompany.Shared;

namespace VirtualCompany.Domain.Enums;

[JsonConverter(typeof(WireEnumJsonConverter))]
public enum ExternalAccountProvider
{
    Google = 1,
    Microsoft365 = 2
}

[JsonConverter(typeof(WireEnumJsonConverter))]
public enum ExternalConnectionStatus
{
    Pending = 1,
    Active = 2,
    TokenExpired = 3,
    Revoked = 4,
    Failed = 5,
    Disconnected = 6
}

[Flags]
[JsonConverter(typeof(WireEnumJsonConverter))]
public enum CalendarCapability
{
    None = 0,
    ReadAvailability = 1 << 0,
    CreateEvents = 1 << 1,
    UpdateEvents = 1 << 2,
    CancelEvents = 1 << 3,
    CreateConferenceLinks = 1 << 4
}

[Flags]
[JsonConverter(typeof(WireEnumJsonConverter))]
public enum MailboxCapability
{
    None = 0,
    ReadMessages = 1 << 0,
    ReadAttachments = 1 << 1,
    ListFolders = 1 << 2,
    ThreadCorrelation = 1 << 3,
    CreateDrafts = 1 << 4,
    SendMessages = 1 << 5,
    IncrementalSync = 1 << 6
}
