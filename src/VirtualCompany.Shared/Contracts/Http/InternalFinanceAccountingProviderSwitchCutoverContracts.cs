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



public sealed record AccountingProviderSwitchCutoverRecoveryRequest(string Reason, long ExpectedVersion);



public sealed record AccountingProviderSwitchCutoverVersionRequest(long ExpectedVersion);


public sealed record ScheduleAccountingProviderSwitchCutoverRequest(Guid PlanId, long ExpectedSwitchVersion,
    string IdempotencyKey);
