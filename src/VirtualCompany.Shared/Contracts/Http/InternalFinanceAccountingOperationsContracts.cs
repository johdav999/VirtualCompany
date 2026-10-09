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
[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record VerifyAccountingRecoveryRequest(Guid? FiscalPeriodId, bool VerifyObjectContent)
{
    public Guid? FiscalPeriodId { get; set; } = FiscalPeriodId;
    public bool VerifyObjectContent { get; set; } = VerifyObjectContent;

    public VerifyAccountingRecoveryRequest() : this(default !, default !)
    {
    }
}

[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record ResolveAccountingMigrationConflictRequest(string ResolutionSummary, long ExpectedVersion)
{
    public string ResolutionSummary { get; set; } = ResolutionSummary;
    public long ExpectedVersion { get; set; } = ExpectedVersion;

    public ResolveAccountingMigrationConflictRequest() : this(string.Empty, default !)
    {
    }
}

[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record StartAccountingMigrationRequest(string IdempotencyKey)
{
    public string IdempotencyKey { get; set; } = IdempotencyKey;

    public StartAccountingMigrationRequest() : this(string.Empty)
    {
    }
}
