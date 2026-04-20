namespace VirtualCompany.Shared;

public sealed class FinanceInvoiceReviewActionAvailabilityResponse
{
    public bool IsActionable { get; set; }
    public bool CanApprove { get; set; }
    public bool CanReject { get; set; }
    public bool CanSendForFollowUp { get; set; }
}
