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



public sealed class CancelAuditPackageRequest { public long ExpectedVersion { get; set; } }




public sealed class ApproveAuditPackageRequest { public long ExpectedVersion { get; set; } public string? Reason { get; set; } }


public sealed class RequestAuditPackageRequest
{
    public Guid FiscalPeriodId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string ScopeKey { get; set; } = AuditPackageScopeValues.PeriodClose;
    public string ScopeVersion { get; set; } = AuditPackageScopeValues.CurrentVersion;
}
