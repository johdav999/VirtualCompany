using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Enums;
namespace VirtualCompany.Api.Controllers;

public sealed record StartBankConnectionRequest(string ProviderKey, string InstitutionId, string? ReturnUri,
    IReadOnlyCollection<string>? RequestedCapabilities);

public sealed record RenewBankConnectionRequest(string ProviderKey, long ExpectedVersion, string? ReturnUri);

public sealed record MapBankAccountRequest(Guid CompanyBankAccountId, long ExpectedConnectionVersion, string Reason);

public sealed record BankConnectionVersionRequest(long ExpectedVersion);

public sealed record ChangeBankConnectionStateRequest(long ExpectedVersion, string? Reason);
