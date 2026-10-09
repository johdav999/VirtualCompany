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
public sealed class ReconcileCustomerInvoiceRefundRequest
{
    public long ExpectedVersion { get; set; }
    public bool ProviderConfirmedSucceeded { get; set; }
    public bool ProviderConfirmedAbsent { get; set; }
    public string EvidenceReference { get; set; } = string.Empty;
    public string? ProviderReference { get; set; }

    public ReconcileCustomerInvoiceRefundRequest()
    {
    }

    public ReconcileCustomerInvoiceRefundRequest(long ExpectedVersion, bool ProviderConfirmedSucceeded, bool ProviderConfirmedAbsent, string EvidenceReference, string? ProviderReference = null)
    {
        this.ExpectedVersion = ExpectedVersion;
        this.ProviderConfirmedSucceeded = ProviderConfirmedSucceeded;
        this.ProviderConfirmedAbsent = ProviderConfirmedAbsent;
        this.EvidenceReference = EvidenceReference;
        this.ProviderReference = ProviderReference;
    }
}

public sealed class ExecuteCustomerInvoiceCorrectionRequest
{
    public long ExpectedVersion { get; set; }
    public string ExpectedSourceHash { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public Guid? SeriesId { get; set; }
    public Guid? FiscalPeriodId { get; set; }
    public DateOnly? AccountingDate { get; set; }
    public string? VoucherSeriesCode { get; set; }
    public Guid? ExpenseAccountId { get; set; }

    public ExecuteCustomerInvoiceCorrectionRequest()
    {
    }

    public ExecuteCustomerInvoiceCorrectionRequest(long ExpectedVersion, string ExpectedSourceHash, string IdempotencyKey, Guid? SeriesId = null, Guid? FiscalPeriodId = null, DateOnly? AccountingDate = null, string? VoucherSeriesCode = null, Guid? ExpenseAccountId = null)
    {
        this.ExpectedVersion = ExpectedVersion;
        this.ExpectedSourceHash = ExpectedSourceHash;
        this.IdempotencyKey = IdempotencyKey;
        this.SeriesId = SeriesId;
        this.FiscalPeriodId = FiscalPeriodId;
        this.AccountingDate = AccountingDate;
        this.VoucherSeriesCode = VoucherSeriesCode;
        this.ExpenseAccountId = ExpenseAccountId;
    }
}

public sealed class ProposeCustomerInvoiceCorrectionRequest
{
    public string CorrectionType { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string EvidenceReference { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? BeneficiaryReference { get; set; }
    public string? PaymentEvidenceReference { get; set; }
    public string? ProviderKey { get; set; }
    public SaveCustomerInvoiceDraftRequest? CreditDraft { get; set; }

    public ProposeCustomerInvoiceCorrectionRequest()
    {
    }

    public ProposeCustomerInvoiceCorrectionRequest(string CorrectionType, decimal Amount, string Currency, string Reason, string EvidenceReference, string IdempotencyKey, string? BeneficiaryReference = null, string? PaymentEvidenceReference = null, string? ProviderKey = null, VirtualCompany.Api.Controllers.SaveCustomerInvoiceDraftRequest? CreditDraft = null)
    {
        this.CorrectionType = CorrectionType;
        this.Amount = Amount;
        this.Currency = Currency;
        this.Reason = Reason;
        this.EvidenceReference = EvidenceReference;
        this.IdempotencyKey = IdempotencyKey;
        this.BeneficiaryReference = BeneficiaryReference;
        this.PaymentEvidenceReference = PaymentEvidenceReference;
        this.ProviderKey = ProviderKey;
        this.CreditDraft = CreditDraft;
    }
}
