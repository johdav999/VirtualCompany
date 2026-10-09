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

    protected async Task<ActionResult<T>> ExecuteReadAsync<T>(Func<Task<T>> read)
    {
        try
        {
            return Ok(await read());
        }
        catch (UnauthorizedAccessException)
        {
            LogHandledFinanceException("read_forbidden", null);
            return Forbid();
        }
        catch (FinanceNotInitializedException ex)
        {
            LogHandledFinanceException("read_not_initialized", ex);
            return await CreateFinanceNotInitializedResultAsync<T>(ex);
        }
        catch (KeyNotFoundException ex)
        {
            LogHandledFinanceException("read_not_found", ex);
            return NotFound(CreateProblemDetails(ex.Message, "Finance record was not found.", StatusCodes.Status404NotFound));
        }
        catch (AccountingConfigurationException ex)
        {
            LogHandledFinanceException("read_accounting_configuration", ex);
            return CreateAccountingConfigurationErrorResult<T>(ex);
        }
        catch (AccountingExportException ex)
        {
            LogHandledFinanceException("read_accounting_export", ex);
            var status = ex.IsConflict ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest;
            return new ObjectResult(StableProblemDetails.Create(HttpContext, status, ex.ReasonCode,
                ex.IsConflict ? "Accounting export conflict" : "Accounting export request was rejected", ex.Message))
            { StatusCode = status };
        }
        catch (CompanyStatutoryProfileException ex)
        {
            LogHandledFinanceException("read_company_statutory_profile", ex);
            return CreateCompanyStatutoryProfileErrorResult<T>(ex);
        }
        catch (StatutoryDocumentException ex)
        {
            LogHandledFinanceException("read_statutory_document", ex);
            return CreateStatutoryDocumentErrorResult<T>(ex);
        }
        catch (VatReturnOperationException ex)
        {
            LogHandledFinanceException("read_vat_return", ex);
            return Conflict(CreateVatReturnProblemDetails(ex));
        }
        catch (ComplianceObligationException ex)
        {
            return Conflict(StableProblemDetails.Create(HttpContext, StatusCodes.Status409Conflict, ex.Code,
                "Compliance obligation request was rejected", ex.Message));
        }
        catch (AuditPackageException ex)
        {
            LogHandledFinanceException("read_audit_package", ex);
            var status = ex.IsConflict ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest;
            return new ObjectResult(StableProblemDetails.Create(HttpContext, status, ex.ReasonCode,
                "Audit package request was rejected", ex.Message)) { StatusCode = status };
        }
        catch (CurrencyRevaluationException ex)
        {
            LogHandledFinanceException("read_currency_revaluation", ex);
            return CreateCurrencyRevaluationErrorResult<T>(ex);
        }
        catch (AccountingScheduleException ex)
        {
            LogHandledFinanceException("read_accounting_schedule", ex);
            return CreateAccountingScheduleErrorResult<T>(ex);
        }
        catch (FixedAssetException ex)
        {
            LogHandledFinanceException("read_fixed_asset", ex);
            return CreateFixedAssetErrorResult<T>(ex);
        }
        catch (AccountingAuthorityException ex)
        {
            LogHandledFinanceException("read_accounting_authority", ex);
            return CreateAccountingAuthorityErrorResult<T>(ex);
        }
        catch (AccountingPostingException ex)
        {
            LogHandledFinanceException("read_accounting_posting", ex);
            return CreateAccountingPostingErrorResult<T>(ex);
        }
        catch (AccountingDimensionException ex)
        {
            LogHandledFinanceException("read_accounting_dimension", ex);
            return CreateAccountingDimensionErrorResult<T>(ex);
        }
        catch (ManualJournalException ex)
        {
            LogHandledFinanceException("read_manual_journal", ex);
            return CreateManualJournalErrorResult<T>(ex);
        }
        catch (CustomerInvoiceDraftException ex)
        {
            LogHandledFinanceException("read_customer_invoice_draft", ex);
            return CreateCustomerInvoiceDraftErrorResult<T>(ex);
        }
        catch (CustomerInvoiceScheduleException ex)
        {
            LogHandledFinanceException("read_customer_invoice_schedule", ex);
            return CreateCustomerInvoiceScheduleErrorResult<T>(ex);
        }
        catch (CustomerInvoiceAccountingException ex)
        {
            LogHandledFinanceException("read_customer_invoice_accounting", ex);
            return CreateCustomerInvoiceAccountingErrorResult<T>(ex);
        }
        catch (CustomerInvoiceCorrectionException ex)
        {
            LogHandledFinanceException("read_customer_invoice_correction", ex);
            return CreateCustomerInvoiceCorrectionErrorResult<T>(ex);
        }
        catch (CustomerCollectionException ex)
        {
            LogHandledFinanceException("read_customer_collections", ex);
            return CreateCustomerCollectionErrorResult<T>(ex);
        }
        catch (SupplierBillAccountingException ex)
        {
            LogHandledFinanceException("read_supplier_bill_accounting", ex);
            return CreateSupplierBillAccountingErrorResult<T>(ex);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            LogHandledFinanceException("read_argument_out_of_range", ex);
            return BadRequest(CreateProblemDetails(ex.Message));
        }
        catch (ArgumentException ex)
        {
            LogHandledFinanceException("read_argument", ex);
            return BadRequest(CreateProblemDetails(ex.Message));
        }
    }

    protected async Task<ActionResult<T>> ExecuteReadOptionalAsync<T>(Func<Task<T?>> read, string notFoundDetail)
        where T : class
    {
        try
        {
            var result = await read();
            return result is null
                ? NotFound(CreateProblemDetails(notFoundDetail, "Finance record was not found.", StatusCodes.Status404NotFound))
                : Ok(result);
        }
        catch (UnauthorizedAccessException)
        {
            LogHandledFinanceException("read_optional_forbidden", null);
            return Forbid();
        }
        catch (FinanceNotInitializedException ex)
        {
            LogHandledFinanceException("read_optional_not_initialized", ex);
            return await CreateFinanceNotInitializedResultAsync<T>(ex);
        }
        catch (KeyNotFoundException ex)
        {
            LogHandledFinanceException("read_optional_not_found", ex);
            return NotFound(CreateProblemDetails(ex.Message, "Finance record was not found.", StatusCodes.Status404NotFound));
        }
        catch (ArgumentOutOfRangeException ex)
        {
            LogHandledFinanceException("read_optional_argument_out_of_range", ex);
            return BadRequest(CreateProblemDetails(ex.Message));
        }
        catch (ArgumentException ex)
        {
            LogHandledFinanceException("read_optional_argument", ex);
            return BadRequest(CreateProblemDetails(ex.Message));
        }
    }

    protected async Task<ActionResult<T>> ExecuteWriteAsync<T>(Func<Task<T>> write)
    {
        try
        {
            return Ok(await write());
        }
        catch (UnauthorizedAccessException)
        {
            LogHandledFinanceException("write_forbidden", null);
            return Forbid();
        }
        catch (FinanceNotInitializedException ex)
        {
            LogHandledFinanceException("write_not_initialized", ex);
            return await CreateFinanceNotInitializedResultAsync<T>(ex);
        }
        catch (SimulationBackendDisabledException ex)
        {
            LogHandledFinanceException("simulation_execution_disabled", ex);
            return Conflict(CreateSimulationExecutionDisabledProblemDetails(ex.Message));
        }
        catch (FinanceValidationException ex)
        {
            LogHandledFinanceException("write_validation", ex);
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>(ex.Errors))
            {
                Title = "Finance validation failed",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest,
                Instance = HttpContext.Request.Path
            });
        }
        catch (AccountingConfigurationException ex)
        {
            LogHandledFinanceException("write_accounting_configuration", ex);
            return CreateAccountingConfigurationErrorResult<T>(ex);
        }
        catch (AccountingExportException ex)
        {
            LogHandledFinanceException("write_accounting_export", ex);
            var status = ex.IsConflict ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest;
            return new ObjectResult(StableProblemDetails.Create(HttpContext, status, ex.ReasonCode,
                ex.IsConflict ? "Accounting export conflict" : "Accounting export request was rejected", ex.Message))
            { StatusCode = status };
        }
        catch (CompanyStatutoryProfileException ex)
        {
            LogHandledFinanceException("write_company_statutory_profile", ex);
            return CreateCompanyStatutoryProfileErrorResult<T>(ex);
        }
        catch (StatutoryDocumentException ex)
        {
            LogHandledFinanceException("write_statutory_document", ex);
            return CreateStatutoryDocumentErrorResult<T>(ex);
        }
        catch (VatReturnOperationException ex)
        {
            LogHandledFinanceException("write_vat_return", ex);
            return Conflict(CreateVatReturnProblemDetails(ex));
        }
        catch (ComplianceObligationException ex)
        {
            return Conflict(StableProblemDetails.Create(HttpContext, StatusCodes.Status409Conflict, ex.Code,
                "Compliance obligation request was rejected", ex.Message));
        }
        catch (AuditPackageException ex)
        {
            LogHandledFinanceException("write_audit_package", ex);
            var status = ex.IsConflict ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest;
            return new ObjectResult(StableProblemDetails.Create(HttpContext, status, ex.ReasonCode,
                "Audit package request was rejected", ex.Message)) { StatusCode = status };
        }
        catch (CurrencyRevaluationException ex)
        {
            LogHandledFinanceException("write_currency_revaluation", ex);
            return CreateCurrencyRevaluationErrorResult<T>(ex);
        }
        catch (AccountingScheduleException ex)
        {
            LogHandledFinanceException("write_accounting_schedule", ex);
            return CreateAccountingScheduleErrorResult<T>(ex);
        }
        catch (FixedAssetException ex)
        {
            LogHandledFinanceException("write_fixed_asset", ex);
            return CreateFixedAssetErrorResult<T>(ex);
        }
        catch (AccountingAuthorityException ex)
        {
            LogHandledFinanceException("write_accounting_authority", ex);
            return CreateAccountingAuthorityErrorResult<T>(ex);
        }
        catch (AccountingPostingException ex)
        {
            LogHandledFinanceException("write_accounting_posting", ex);
            return CreateAccountingPostingErrorResult<T>(ex);
        }
        catch (AccountingDimensionException ex)
        {
            LogHandledFinanceException("write_accounting_dimension", ex);
            return CreateAccountingDimensionErrorResult<T>(ex);
        }
        catch (ManualJournalException ex)
        {
            LogHandledFinanceException("write_manual_journal", ex);
            return CreateManualJournalErrorResult<T>(ex);
        }
        catch (CustomerInvoiceDraftException ex)
        {
            LogHandledFinanceException("write_customer_invoice_draft", ex);
            return CreateCustomerInvoiceDraftErrorResult<T>(ex);
        }
        catch (CustomerInvoiceScheduleException ex)
        {
            LogHandledFinanceException("write_customer_invoice_schedule", ex);
            return CreateCustomerInvoiceScheduleErrorResult<T>(ex);
        }
        catch (CustomerInvoiceAccountingException ex)
        {
            LogHandledFinanceException("write_customer_invoice_accounting", ex);
            return CreateCustomerInvoiceAccountingErrorResult<T>(ex);
        }
        catch (CustomerInvoiceCorrectionException ex)
        {
            LogHandledFinanceException("write_customer_invoice_correction", ex);
            return CreateCustomerInvoiceCorrectionErrorResult<T>(ex);
        }
        catch (CustomerCollectionException ex)
        {
            LogHandledFinanceException("write_customer_collections", ex);
            return CreateCustomerCollectionErrorResult<T>(ex);
        }
        catch (SupplierBillAccountingException ex)
        {
            LogHandledFinanceException("write_supplier_bill_accounting", ex);
            return CreateSupplierBillAccountingErrorResult<T>(ex);
        }
        catch (KeyNotFoundException ex)
        {
            LogHandledFinanceException("write_not_found", ex);
            return NotFound(CreateProblemDetails(ex.Message, "Finance record was not found.", StatusCodes.Status404NotFound));
        }
        catch (ArgumentOutOfRangeException ex)
        {
            LogHandledFinanceException("write_argument_out_of_range", ex);
            return BadRequest(CreateProblemDetails(ex.Message, "Invalid finance write request.", StatusCodes.Status400BadRequest));
        }
        catch (ArgumentException ex)
        {
            LogHandledFinanceException("write_argument", ex);
            return BadRequest(CreateProblemDetails(ex.Message, "Invalid finance write request.", StatusCodes.Status400BadRequest));
        }
        catch (InvalidOperationException ex)
        {
            LogHandledFinanceException("write_invalid_operation", ex);
            return BadRequest(CreateProblemDetails(ex.Message, "Invalid finance write request.", StatusCodes.Status400BadRequest));
        }
    }

    protected void LogHandledFinanceException(string category, Exception? exception)
    {
        if (exception is null)
        {
            _logger.LogWarning(
                "Finance request {Category} for HTTP {Method} {Path}.",
                category,
                HttpContext.Request.Method,
                HttpContext.Request.Path);
            return;
        }

        _logger.LogWarning(
            exception,
            "Finance request {Category} for HTTP {Method} {Path}: {Message}",
            category,
            HttpContext.Request.Method,
            HttpContext.Request.Path,
            exception.Message);
    }

}
