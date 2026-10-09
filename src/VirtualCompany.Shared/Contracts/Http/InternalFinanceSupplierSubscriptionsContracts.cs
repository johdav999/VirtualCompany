using System.Globalization;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Agents;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Shared;
using System.Text.Json.Nodes;
using System.Text.Json;

namespace VirtualCompany.Api.Controllers;
public sealed record RejectSupplierSubscriptionProposalRequest(string Reason);
public sealed record AcceptSupplierSubscriptionProposalRequest(SupplierSubscriptionProposalTermsDto Terms, string? DecisionReason);
public sealed record LinkSupplierSubscriptionReceiptEvidenceRequest(Guid BillId, string? EvidenceSummary);
public sealed record SupplierSubscriptionStatusRequest(string Action);
public sealed record UpsertSupplierSubscriptionRequest(Guid CounterpartyId, string Name, string Currency, decimal ExpectedAmount, string Cadence, int BillingDay, DateTime StartDateUtc, DateTime NextExpectedBillDateUtc, decimal AmountTolerance, int DateToleranceDays, DateTime? EndDateUtc, string? ContractReference, string? Description, int NoticePeriodDays, bool AutoRenews, Guid? ContractDocumentId)
{
}
