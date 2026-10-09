using System.Security.Claims;
using VirtualCompany.Application.Finance;
namespace VirtualCompany.Api.Controllers;

public sealed record FinanceWorkerOperatorActionRequest(long ExpectedVersion, string Reason, string? CorrelationId = null);
