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

public abstract partial class InternalFinanceControllerBase : ControllerBase
{
    private readonly FinanceInitializationProblemHandler _initializationProblems;
    protected readonly ILogger _logger;

    protected InternalFinanceControllerBase(FinanceInitializationProblemHandler initializationProblems, ILogger logger)
    {
        _initializationProblems = initializationProblems;
        _logger = logger;
    }

    protected async Task<ActionResult<T>> CreateFinanceNotInitializedResultAsync<T>(FinanceNotInitializedException exception) =>
        await _initializationProblems.CreateAsync(exception, HttpContext, ResolveCorrelationId());

    protected Guid RequiredActor() => ResolveActorId() ?? throw new UnauthorizedAccessException("A resolved company user is required.");

    protected Guid ResolveRequiredAccountingActorId() => ResolveActorId()
        ?? throw new UnauthorizedAccessException("An authenticated company member is required.");

    protected static string? NormalizeReviewToken(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().Replace(" ", "_", StringComparison.Ordinal).Replace("-", "_", StringComparison.Ordinal).ToLowerInvariant();

    protected static string HumanizeReviewToken(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "System"
            : string.Join(" ", value
                .Trim()
                .Replace("-", "_", StringComparison.Ordinal)
                .Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    protected Guid? ResolveActorId()
    {
        var value = User.FindFirstValue(CurrentUserClaimTypes.UserId) ??
            User.FindFirstValue(ClaimTypes.NameIdentifier) ??
            User.FindFirstValue("sub");
        return Guid.TryParse(value, out var userId) ? userId : null;
    }

    protected string ResolveActorDisplayName() =>
        User.Identity?.Name ??
        User.FindFirstValue("name") ??
        User.FindFirstValue(ClaimTypes.Email) ??
        "Finance user";

    protected string ResolveCorrelationId()
    {
        if (Request.Headers.TryGetValue("X-Correlation-ID", out var headerValue) && !string.IsNullOrWhiteSpace(headerValue))
        {
            return headerValue.ToString();
        }

        return HttpContext.TraceIdentifier;
    }

}
