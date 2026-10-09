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

namespace VirtualCompany.Api.ProblemHandling;

using VirtualCompany.Api.Controllers;

public sealed class FinanceInitializationProblemHandler
{
    private readonly IFinanceEntryService _financeEntryService;
    private readonly IOptions<FinanceInitializationOptions> _financeInitializationOptions;
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ILogger<FinanceInitializationProblemHandler> _logger;
    private const string FinanceRequestNotInitializedAction = "finance.request.not_initialized";

    public FinanceInitializationProblemHandler(
        IFinanceEntryService financeEntryService,
        IOptions<FinanceInitializationOptions> financeInitializationOptions,
        IAuditEventWriter auditEventWriter,
        VirtualCompanyDbContext dbContext,
        ILogger<FinanceInitializationProblemHandler> logger)
    {
        _financeEntryService = financeEntryService;
        _financeInitializationOptions = financeInitializationOptions;
        _auditEventWriter = auditEventWriter;
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<ObjectResult> CreateAsync(FinanceNotInitializedException exception, HttpContext httpContext, string correlationId)
    {
        var shouldTriggerFallback = exception.CanTriggerSeed && _financeInitializationOptions.Value.ShouldTriggerSeedFallback();
        var entryState = shouldTriggerFallback
            ? await _financeEntryService.RequestEntryStateAsync(
                new GetFinanceEntryStateQuery(
                    exception.CompanyId,
                    Source: FinanceEntrySources.FallbackRead,
                    SeedMode: FinanceSeedRequestModes.Replace),
                httpContext.RequestAborted)
            : await _financeEntryService.GetEntryStateAsync(
                new GetFinanceEntryStateQuery(exception.CompanyId, Source: FinanceEntrySources.FallbackRead),
                httpContext.RequestAborted);

        var response = new FinanceInitializationProblemResponse
        {
            Title = "Finance data is not initialized.",
            Detail = exception.Message,
            Status = StatusCodes.Status409Conflict,
            Code = FinanceInitializationProblemCodeValues.NotInitialized,
            Message = entryState.Message,
            Domain = exception.Domain,
            Module = FinanceInitializationDomainValues.Finance,
            CompanyId = exception.CompanyId,
            CanTriggerSeed = exception.CanTriggerSeed,
            CanGenerate = entryState.CanGenerate,
            RecommendedAction = entryState.RecommendedAction,
            SupportedModes = entryState.SupportedModes,
            FallbackTriggered = shouldTriggerFallback,
            SeedRequested = entryState.SeedJobEnqueued,
            SeedJobActive = entryState.SeedJobActive,
            ProgressState = entryState.ProgressState,
            SeedingState = entryState.SeedingState.ToStorageValue(),
            InitializationStatus = entryState.InitializationStatus,
            JobStatus = entryState.JobStatus,
            CorrelationId = entryState.CorrelationId ?? correlationId,
            StatusEndpoint = entryState.StatusEndpoint,
            SeedEndpoint = entryState.SeedEndpoint,
            ConfirmationRequired = entryState.ConfirmationRequired,
            ConfirmationMessage = entryState.ConfirmationMessage
        };

        _logger.LogInformation(
            "Finance request for company {CompanyId} returned not_initialized on {RequestPath}. TriggerSource={TriggerSource}, CorrelationId={CorrelationId}, FallbackTriggered={FallbackTriggered}, SeedRequested={SeedRequested}, ProgressState={ProgressState}.",
            exception.CompanyId,
            httpContext.Request.Path,
            response.FallbackTriggered ? FinanceEntrySources.FallbackRead : FinanceEntrySources.FinanceEntry,
            response.CorrelationId,
            response.FallbackTriggered,
            response.SeedRequested,
            response.ProgressState);

        await _auditEventWriter.WriteAsync(
            new AuditEventWriteRequest(
                exception.CompanyId,
                AuditActorTypes.System,
                null,
                FinanceRequestNotInitializedAction,
                BackgroundExecutionRelatedEntityTypes.FinanceSeed,
                exception.CompanyId.ToString("D"),
                AuditEventOutcomes.Failed,
                "A finance API request returned a structured not_initialized response because the finance dataset is unavailable.",
                Metadata: new Dictionary<string, string?>
                {
                    ["triggerSource"] = response.FallbackTriggered ? FinanceEntrySources.FallbackRead : FinanceEntrySources.FinanceEntry,
                    ["requestPath"] = httpContext.Request.Path,
                    ["requestMethod"] = httpContext.Request.Method,
                    ["code"] = response.Code,
                    ["domain"] = response.Domain,
                    ["module"] = response.Module,
                    ["fallbackTriggered"] = response.FallbackTriggered ? "true" : "false",
                    ["seedRequested"] = response.SeedRequested ? "true" : "false",
                    ["seedJobActive"] = response.SeedJobActive ? "true" : "false",
                    ["progressState"] = response.ProgressState,
                    ["seedingState"] = response.SeedingState,
                    ["recommendedAction"] = response.RecommendedAction,
                    ["statusEndpoint"] = response.StatusEndpoint,
                    ["seedEndpoint"] = response.SeedEndpoint
                },
                CorrelationId: response.CorrelationId,
                OccurredUtc: DateTime.UtcNow),
            httpContext.RequestAborted);
        await _dbContext.SaveChangesAsync(httpContext.RequestAborted);

        return new ObjectResult(response) { StatusCode = response.Status };
    }

}
