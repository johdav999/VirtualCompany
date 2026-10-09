using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auth;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Shared;
using VirtualCompany.Infrastructure.Tenancy;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Api.ProblemHandling;
using System.Text.Json.Nodes;
using System.Text.Json;

namespace VirtualCompany.Api.Controllers;

public abstract partial class InternalFinanceControllerBase
{

    protected ProblemDetails CreateProblemDetails(string detail) =>
        CreateProblemDetails(detail, "Invalid finance read request.", StatusCodes.Status400BadRequest);

    protected ProblemDetails CreateProblemDetails(string detail, string title, int status) =>
        new()
        {
            Title = title,
            Detail = detail,
            Status = status,
            Instance = HttpContext.Request.Path
        };

    protected ProblemDetails CreateSimulationExecutionDisabledProblemDetails(string detail) =>
        CreateProblemDetails(detail, "Simulation execution is disabled.", StatusCodes.Status409Conflict);

    protected ProblemDetails CreateVatReturnProblemDetails(VatReturnOperationException exception)
    {
        var problem = CreateProblemDetails(exception.Message, "VAT return operation blocked.", StatusCodes.Status409Conflict);
        problem.Extensions["code"] = exception.Code;
        return problem;
    }

    protected ActionResult<T> CreateAccountingConfigurationErrorResult<T>(AccountingConfigurationException exception)
    {
        var status = exception.ReasonCode is AccountingConfigurationReasonCodes.ConfigurationNotFound
            or AccountingConfigurationReasonCodes.AccountNotFound
            or AccountingConfigurationReasonCodes.ChartCatalogNotFound
            or AccountingConfigurationReasonCodes.ChartCatalogAccountNotFound
            or AccountingConfigurationReasonCodes.PeriodNotFound
            ? StatusCodes.Status404NotFound
            : exception.IsConflict
                ? StatusCodes.Status409Conflict
                : StatusCodes.Status400BadRequest;
        var problem = StableProblemDetails.Create(
            HttpContext,
            status,
            exception.ReasonCode,
            status == StatusCodes.Status409Conflict
                ? "Accounting configuration conflict"
                : status == StatusCodes.Status404NotFound
                    ? "Accounting configuration was not found"
                    : "Accounting configuration request was rejected",
            exception.Message);
        return new ObjectResult(problem) { StatusCode = status };
    }

    protected ActionResult<T> CreateCompanyStatutoryProfileErrorResult<T>(CompanyStatutoryProfileException exception)
    {
        var status = exception.ReasonCode == CompanyStatutoryProfileReasonCodes.NotFound
            ? StatusCodes.Status404NotFound
            : exception.IsConflict
                ? StatusCodes.Status409Conflict
                : StatusCodes.Status400BadRequest;
        var problem = StableProblemDetails.Create(
            HttpContext,
            status,
            exception.ReasonCode,
            status == StatusCodes.Status409Conflict
                ? "Statutory profile conflict"
                : status == StatusCodes.Status404NotFound
                    ? "Statutory profile was not found"
                    : "Statutory profile request was rejected",
            exception.Message);
        return new ObjectResult(problem) { StatusCode = status };
    }

    protected ActionResult<T> CreateStatutoryDocumentErrorResult<T>(StatutoryDocumentException exception)
    {
        var status = exception.ReasonCode is StatutoryDocumentReasonCodes.SeriesNotFound or StatutoryDocumentReasonCodes.SourceNotFound
            ? StatusCodes.Status404NotFound
            : exception.IsConflict ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest;
        var problem = StableProblemDetails.Create(HttpContext, status, exception.ReasonCode,
            status == StatusCodes.Status409Conflict ? "Statutory document conflict" :
            status == StatusCodes.Status404NotFound ? "Statutory document record was not found" : "Statutory document request was rejected",
            exception.Message);
        return new ObjectResult(problem) { StatusCode = status };
    }

    protected ActionResult<T> CreateAccountingPostingErrorResult<T>(AccountingPostingException exception)
    {
        var status = exception.ReasonCode == AccountingPostingReasonCodes.JournalNotFound
            ? StatusCodes.Status404NotFound
            : exception.IsConflict
                ? StatusCodes.Status409Conflict
                : StatusCodes.Status400BadRequest;
        var problem = StableProblemDetails.Create(
            HttpContext,
            status,
            exception.ReasonCode,
            status == StatusCodes.Status409Conflict
                ? "Accounting posting conflict"
                : status == StatusCodes.Status404NotFound
                    ? "Journal entry was not found"
                    : "Accounting posting was rejected",
            exception.Message);
        return new ObjectResult(problem) { StatusCode = status };
    }

    protected ActionResult<T> CreateAccountingAuthorityErrorResult<T>(AccountingAuthorityException exception)
    {
        var status = exception.ReasonCode is AccountingAuthorityReasonCodes.AuthorityPeriodNotFound
            or AccountingAuthorityReasonCodes.ExportNotFound
            or AccountingProviderSwitchReasonCodes.NotFound
            or AccountingProviderSwitchReasonCodes.AssessmentNotFound
            or AccountingProviderSwitchCutoverReasonCodes.NotFound
            or AccountingProviderSwitchMonitoringReasonCodes.NotFound
            ? StatusCodes.Status404NotFound
            : exception.IsConflict
                ? StatusCodes.Status409Conflict
                : StatusCodes.Status400BadRequest;
        var problem = StableProblemDetails.Create(
            HttpContext,
            status,
            exception.ReasonCode,
            status == StatusCodes.Status409Conflict
                ? "Accounting authority conflict"
                : status == StatusCodes.Status404NotFound
                    ? "Accounting authority record was not found"
                    : "Accounting authority request was rejected",
            exception.Message);
        return new ObjectResult(problem) { StatusCode = status };
    }

    protected ActionResult<T> CreateManualJournalErrorResult<T>(ManualJournalException exception)
    {
        var status = exception.ReasonCode == ManualJournalReasonCodes.NotFound
            ? StatusCodes.Status404NotFound
            : exception.IsConflict
                ? StatusCodes.Status409Conflict
                : StatusCodes.Status400BadRequest;
        var problem = StableProblemDetails.Create(HttpContext, status, exception.ReasonCode,
            status == StatusCodes.Status409Conflict ? "Manual journal conflict" :
            status == StatusCodes.Status404NotFound ? "Manual journal was not found" : "Manual journal request was rejected",
            exception.Message);
        if (exception.CurrentVersion.HasValue) problem.Extensions["currentVersion"] = exception.CurrentVersion.Value;
        return new ObjectResult(problem) { StatusCode = status };
    }

    protected ActionResult<T> CreateAccountingDimensionErrorResult<T>(AccountingDimensionException exception)
    {
        var status = exception.ReasonCode == AccountingDimensionReasonCodes.NotFound
            ? StatusCodes.Status404NotFound
            : exception.IsConflict ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest;
        var problem = StableProblemDetails.Create(HttpContext, status, exception.ReasonCode,
            status == StatusCodes.Status409Conflict ? "Accounting dimension conflict" :
            status == StatusCodes.Status404NotFound ? "Accounting dimension record was not found" :
            "Accounting dimension request was rejected", exception.Message);
        if (exception.CurrentVersion.HasValue) problem.Extensions["currentVersion"] = exception.CurrentVersion.Value;
        return new ObjectResult(problem) { StatusCode = status };
    }

    protected ActionResult<T> CreateCurrencyRevaluationErrorResult<T>(CurrencyRevaluationException exception)
    {
        var status = exception.ReasonCode == CurrencyRevaluationReasonCodes.NotFound
            ? StatusCodes.Status404NotFound
            : exception.IsConflict ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest;
        var problem = StableProblemDetails.Create(HttpContext, status, exception.ReasonCode,
            status == StatusCodes.Status409Conflict ? "Currency revaluation conflict" :
            status == StatusCodes.Status404NotFound ? "Currency revaluation run was not found" :
            "Currency revaluation request was rejected", exception.Message);
        if (exception.CurrentVersion.HasValue) problem.Extensions["currentVersion"] = exception.CurrentVersion.Value;
        return new ObjectResult(problem) { StatusCode = status };
    }

    protected ActionResult<T> CreateAccountingScheduleErrorResult<T>(AccountingScheduleException exception)
    {
        var status = exception.ReasonCode == AccountingScheduleReasonCodes.NotFound
            ? StatusCodes.Status404NotFound
            : exception.IsConflict ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest;
        var problem = StableProblemDetails.Create(HttpContext, status, exception.ReasonCode,
            status == StatusCodes.Status409Conflict ? "Accounting schedule conflict" :
            status == StatusCodes.Status404NotFound ? "Accounting schedule was not found" :
            "Accounting schedule request was rejected", exception.Message);
        if (exception.CurrentVersion.HasValue) problem.Extensions["currentVersion"] = exception.CurrentVersion.Value;
        return new ObjectResult(problem) { StatusCode = status };
    }

    protected ActionResult<T> CreateFixedAssetErrorResult<T>(FixedAssetException exception)
    {
        var status = exception.ReasonCode is FixedAssetReasonCodes.NotFound or FixedAssetReasonCodes.ClassNotFound
            ? StatusCodes.Status404NotFound
            : exception.IsConflict ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest;
        return new ObjectResult(StableProblemDetails.Create(HttpContext, status, exception.ReasonCode,
            status == StatusCodes.Status409Conflict ? "Fixed-asset conflict" :
            status == StatusCodes.Status404NotFound ? "Fixed-asset record was not found" :
            "Fixed-asset request was rejected", exception.Message)) { StatusCode = status };
    }

    protected ActionResult<T> CreateCustomerInvoiceScheduleErrorResult<T>(CustomerInvoiceScheduleException exception)
    {
        var status = exception.ReasonCode == CustomerInvoiceScheduleReasonCodes.NotFound
            ? StatusCodes.Status404NotFound
            : exception.IsConflict ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest;
        return new ObjectResult(StableProblemDetails.Create(HttpContext, status, exception.ReasonCode,
            exception.IsConflict ? "Invoice schedule conflict" : "Invoice schedule request was rejected", exception.Message))
        { StatusCode = status };
    }

    protected ActionResult<T> CreateCustomerInvoiceDraftErrorResult<T>(CustomerInvoiceDraftException exception)
    {
        var status = exception.ReasonCode is CustomerInvoiceDraftReasonCodes.NotFound
            or CustomerInvoiceDraftReasonCodes.CustomerNotFound
            or CustomerInvoiceDraftReasonCodes.EvidenceNotFound
            ? StatusCodes.Status404NotFound
            : exception.IsConflict ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest;
        var problem = StableProblemDetails.Create(HttpContext, status, exception.ReasonCode,
            status == StatusCodes.Status409Conflict ? "Customer invoice draft conflict" :
            status == StatusCodes.Status404NotFound ? "Customer invoice draft record was not found" :
            "Customer invoice draft request was rejected", exception.Message);
        if (exception.CurrentVersion.HasValue) problem.Extensions["currentVersion"] = exception.CurrentVersion.Value;
        return new ObjectResult(problem) { StatusCode = status };
    }

    protected ActionResult<T> CreateCustomerInvoiceAccountingErrorResult<T>(CustomerInvoiceAccountingException exception)
    {
        var status = exception.ReasonCode == CustomerInvoiceAccountingReasonCodes.InvoiceNotFound
            ? StatusCodes.Status404NotFound
            : exception.IsConflict ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest;
        var problem = StableProblemDetails.Create(HttpContext, status, exception.ReasonCode,
            status == StatusCodes.Status409Conflict ? "Customer invoice accounting conflict" :
            status == StatusCodes.Status404NotFound ? "Customer invoice was not found" : "Customer invoice accounting was rejected",
            exception.Message);
        return new ObjectResult(problem) { StatusCode = status };
    }

    protected ActionResult<T> CreateCustomerInvoiceCorrectionErrorResult<T>(CustomerInvoiceCorrectionException exception)
    {
        var status = exception.ReasonCode == CustomerInvoiceCorrectionReasonCodes.InvoiceNotFound
            ? StatusCodes.Status404NotFound
            : exception.IsConflict ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest;
        var problem = StableProblemDetails.Create(HttpContext, status, exception.ReasonCode,
            status == StatusCodes.Status409Conflict ? "Customer invoice correction conflict" :
            status == StatusCodes.Status404NotFound ? "Customer invoice correction was not found" :
            "Customer invoice correction was rejected", exception.Message);
        if (exception.CurrentVersion.HasValue) problem.Extensions["currentVersion"] = exception.CurrentVersion.Value;
        return new ObjectResult(problem) { StatusCode = status };
    }

    protected ActionResult<T> CreateCustomerCollectionErrorResult<T>(CustomerCollectionException exception)
    {
        var status = exception.ReasonCode is CustomerCollectionReasonCodes.NotFound or
            CustomerCollectionReasonCodes.CustomerNotFound or CustomerCollectionReasonCodes.InvoiceNotFound
            ? StatusCodes.Status404NotFound
            : exception.IsConflict ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest;
        var problem = StableProblemDetails.Create(HttpContext, status, exception.ReasonCode,
            exception.IsConflict ? "Customer collection conflict" :
            status == StatusCodes.Status404NotFound ? "Customer collection record was not found" :
            "Customer collection request was rejected", exception.Message);
        if (exception.CurrentVersion.HasValue) problem.Extensions["currentVersion"] = exception.CurrentVersion.Value;
        return new ObjectResult(problem) { StatusCode = status };
    }

    protected ActionResult<T> CreateSupplierBillAccountingErrorResult<T>(SupplierBillAccountingException exception)
    {
        var status = exception.ReasonCode == SupplierBillAccountingReasonCodes.BillNotFound
            ? StatusCodes.Status404NotFound
            : exception.IsConflict ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest;
        var problem = StableProblemDetails.Create(HttpContext, status, exception.ReasonCode,
            status == StatusCodes.Status409Conflict ? "Supplier bill accounting conflict" :
            status == StatusCodes.Status404NotFound ? "Supplier bill was not found" : "Supplier bill accounting was rejected",
            exception.Message);
        return new ObjectResult(problem) { StatusCode = status };
    }

}
