using System.Text.Json.Nodes;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Approvals;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Application.Support;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Security;

namespace VirtualCompany.Infrastructure.Support;

internal static class SupportMemorySafetyPolicy
{
    private static readonly string[] BlockedTerms = ["password", "passcode", "secret", "api key", "token", "credit card", "card number", "cvv", "iban", "bank account", "social security", "personnummer"];
    private static readonly string[] ReviewTerms = ["maybe", "probably", "seems", "appears", "angry", "upset", "temporary", "for now", "until"];
    public const string PolicyVersion = "support-memory-v1";

    public static SupportMemoryPolicyDecision Evaluate(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 1000)
        {
            return new SupportMemoryPolicyDecision(SupportMemoryObservationStatuses.Rejected, "Candidate was blank or too long.", 0m, null);
        }

        var normalized = value.ToLowerInvariant();
        if (BlockedTerms.Any(normalized.Contains) || System.Text.RegularExpressions.Regex.IsMatch(value, @"\b(?:\d[ -]*?){13,19}\b"))
        {
            return new SupportMemoryPolicyDecision(SupportMemoryObservationStatuses.Rejected, "Candidate contained sensitive information blocked by policy.", 0m, null);
        }

        if (ReviewTerms.Any(normalized.Contains))
        {
            return new SupportMemoryPolicyDecision(SupportMemoryObservationStatuses.Review, "Candidate may be temporary or inferred and needs review.", 0.65m, DateTime.UtcNow.AddDays(90));
        }

        return new SupportMemoryPolicyDecision(SupportMemoryObservationStatuses.Approved, "Explicit support preference passed deterministic safety checks.", 0.85m, null);
    }

    public sealed record SupportMemoryPolicyDecision(string Status, string EvidenceSummary, decimal Confidence, DateTime? ValidUntilUtc);
}

internal sealed class NoopAuditEventWriter : IAuditEventWriter
{
    public Task WriteAsync(AuditEventWriteRequest auditEvent, CancellationToken cancellationToken) => Task.CompletedTask;
}
