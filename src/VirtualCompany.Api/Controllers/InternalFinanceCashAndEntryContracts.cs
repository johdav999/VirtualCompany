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




internal static class FinanceSeedingStateResponseMappings
{
    public static FinanceSeedingStateDiagnosticsResponse WithRecordChecks(
        this FinanceSeedingStateDiagnosticsResponse response,
        FinanceSeedingStateDiagnosticsDto diagnostics)
    {
        response.HasAccounts = diagnostics.HasAccounts;
        response.HasCounterparties = diagnostics.HasCounterparties;
        response.HasTransactions = diagnostics.HasTransactions;
        response.HasBalances = diagnostics.HasBalances;
        response.HasPolicyConfiguration = diagnostics.HasPolicyConfiguration;
        response.HasInvoices = diagnostics.HasInvoices;
        response.HasBills = diagnostics.HasBills;

        return response;
    }
}




public sealed record RetryFinanceEntryStateRequest;




internal static partial class InternalFinanceControllerMappings
{
    public static FinanceEntryInitializationResponse MapFinanceEntryState(FinanceEntryStateDto result) =>
        new()
        {
            CompanyId = result.CompanyId,
            InitializationStatus = result.InitializationStatus,
            ProgressState = result.ProgressState,
            SeedingState = result.SeedingState.ToStorageValue(),
            SeedJobEnqueued = result.SeedJobEnqueued,
            SeedJobActive = result.SeedJobActive,
            CanRetry = result.CanRetry,
            CanRefresh = result.CanRefresh,
            Message = result.Message,
            CheckedAtUtc = result.CheckedAtUtc,
            SeededAtUtc = result.SeededAtUtc,
            DataAlreadyExists = result.DataAlreadyExists,
            SeedMode = result.SeedMode,
            SeedOperation = result.SeedOperation,
            ConfirmationRequired = result.ConfirmationRequired,
            FallbackTriggered = result.FallbackTriggered,
            StatusEndpoint = result.StatusEndpoint,
            SeedEndpoint = result.SeedEndpoint,
            IdempotencyKey = result.IdempotencyKey,
            ConfirmationMessage = result.ConfirmationMessage,
            LastAttemptedUtc = result.LastAttemptedUtc,
            LastCompletedUtc = result.LastCompletedUtc,
            LastErrorCode = result.LastErrorCode,
            LastErrorMessage = result.LastErrorMessage,
            JobStatus = result.JobStatus,
            CorrelationId = result.CorrelationId,
            CanGenerate = result.CanGenerate,
            RecommendedAction = result.RecommendedAction,
            SupportedModes = result.SupportedModes
        };
}


