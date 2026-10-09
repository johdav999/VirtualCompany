using VirtualCompany.Application.Finance;

namespace VirtualCompany.Api.Controllers;
[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record RegisterPaymentBeneficiaryRequest(string PartyType, Guid PartyId, string DisplayName, string Rail, string Destination, string MaskedDestination, string Currency, string VerificationEvidenceReference, string VerificationEvidenceHash)
{
    public string PartyType { get; set; } = PartyType;
    public Guid PartyId { get; set; } = PartyId;
    public string DisplayName { get; set; } = DisplayName;
    public string Rail { get; set; } = Rail;
    public string Destination { get; set; } = Destination;
    public string MaskedDestination { get; set; } = MaskedDestination;
    public string Currency { get; set; } = Currency;
    public string VerificationEvidenceReference { get; set; } = VerificationEvidenceReference;
    public string VerificationEvidenceHash { get; set; } = VerificationEvidenceHash;

    public RegisterPaymentBeneficiaryRequest() : this(string.Empty, default !, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty)
    {
    }
}

[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record CreatePaymentBatchRequest(string Name, DateOnly PlannedExecutionDate, string IdempotencyKey)
{
    public string Name { get; set; } = Name;
    public DateOnly PlannedExecutionDate { get; set; } = PlannedExecutionDate;
    public string IdempotencyKey { get; set; } = IdempotencyKey;

    public CreatePaymentBatchRequest() : this(string.Empty, default !, string.Empty)
    {
    }
}

[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record AddPaymentBatchObligationRequest(string ObligationType, Guid SourceId, long ExpectedVersion, string IdempotencyKey)
{
    public string ObligationType { get; set; } = ObligationType;
    public Guid SourceId { get; set; } = SourceId;
    public long ExpectedVersion { get; set; } = ExpectedVersion;
    public string IdempotencyKey { get; set; } = IdempotencyKey;

    public AddPaymentBatchObligationRequest() : this(string.Empty, default !, default !, string.Empty)
    {
    }
}

[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record PaymentBatchVersionedRequest(long ExpectedVersion, string IdempotencyKey)
{
    public long ExpectedVersion { get; set; } = ExpectedVersion;
    public string IdempotencyKey { get; set; } = IdempotencyKey;

    public PaymentBatchVersionedRequest() : this(default !, string.Empty)
    {
    }
}

[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record DecidePaymentBatchRequest(long ExpectedVersion, string Comment, string IdempotencyKey)
{
    public long ExpectedVersion { get; set; } = ExpectedVersion;
    public string Comment { get; set; } = Comment;
    public string IdempotencyKey { get; set; } = IdempotencyKey;

    public DecidePaymentBatchRequest() : this(default !, string.Empty, default !)
    {
    }
}

[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record CancelPaymentBatchRequest(long ExpectedVersion, string Reason, string IdempotencyKey)
{
    public long ExpectedVersion { get; set; } = ExpectedVersion;
    public string Reason { get; set; } = Reason;
    public string IdempotencyKey { get; set; } = IdempotencyKey;

    public CancelPaymentBatchRequest() : this(default !, string.Empty, default !)
    {
    }
}
