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
public sealed record CreateCorrectiveAccountingProviderSwitchRequest(Guid EffectiveFiscalPeriodId, long ExpectedVersion, string Reason)
{
    public Guid EffectiveFiscalPeriodId { get; set; } = EffectiveFiscalPeriodId;
    public long ExpectedVersion { get; set; } = ExpectedVersion;
    public string Reason { get; set; } = Reason;

    public CreateCorrectiveAccountingProviderSwitchRequest() : this(default !, default !, string.Empty)
    {
    }
}

public sealed record CloseAccountingProviderSwitchMonitoringRequest(long ExpectedVersion, string Summary);
[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record AcceptAccountingProviderSwitchMonitoringExceptionRequest(long ExpectedVersion, string Explanation, string Scope, decimal FinancialImpact, string EvidenceReference)
{
    public long ExpectedVersion { get; set; } = ExpectedVersion;
    public string Explanation { get; set; } = Explanation;
    public string Scope { get; set; } = Scope;
    public decimal FinancialImpact { get; set; } = FinancialImpact;
    public string EvidenceReference { get; set; } = EvidenceReference;

    public AcceptAccountingProviderSwitchMonitoringExceptionRequest() : this(default !, string.Empty, string.Empty, default !, string.Empty)
    {
    }
}

public sealed record AccountingProviderSwitchMonitoringVersionRequest(long ExpectedVersion);
