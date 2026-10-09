using VirtualCompany.Application.Finance;
namespace VirtualCompany.Api.Controllers;

public sealed record RequestBankFeedSynchronizationRequest(Guid? CheckpointId);

public sealed record RequestBankFeedBackfillRequest(DateOnly DateFrom, DateOnly DateTo,
    long ExpectedCheckpointVersion, string Reason);
