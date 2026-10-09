using System.Security.Claims;
using VirtualCompany.Application.Finance;
namespace VirtualCompany.Api.Controllers;

public sealed record UpsertCustomerBillingProfileRequest(CustomerBillingProfileInputDto Profile, long? ExpectedVersion);

public sealed record ResolveCustomerBillingSourceConflictRequest(long ExpectedConflictVersion, long ExpectedProfileVersion,
    bool UseIncomingValues, string Reason);

public sealed record DecideCustomerDuplicateRequest(long ExpectedVersion, string Decision, Guid? MergeSourceCounterpartyId,
    Guid? MergeTargetCounterpartyId, string Reason);
