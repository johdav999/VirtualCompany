using System.Security.Claims;
using VirtualCompany.Application.Finance;
namespace VirtualCompany.Api.Controllers;

public sealed record AccountingRetentionPreviewRequest(int BatchSize = 100);

public sealed record AccountingRetentionCleanupRequest(
    string PreviewToken,
    int BatchSize,
    string Reason,
    string? CorrelationId = null);
