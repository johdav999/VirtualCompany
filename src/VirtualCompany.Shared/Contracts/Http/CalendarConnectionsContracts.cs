using VirtualCompany.Application.Mailbox;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Api.Controllers;

public sealed record StartCalendarConnectionResponse(string AuthorizationUrl);


public sealed record StartCalendarConnectionRequest(string? ReturnUri);
