using VirtualCompany.Application.Finance;

namespace VirtualCompany.Api.Controllers;


public sealed record FinanceBillReviewActionRequest(string Rationale);


public sealed record SetSupplierApprovalAutomationRequest(bool Enabled);
