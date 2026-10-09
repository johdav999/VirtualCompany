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
public static class SaveAccountingScheduleRequestMapping
{
    public static AccountingScheduleInput ToInput(this SaveAccountingScheduleRequest request) => new(request.Code, request.Name, request.ScheduleType, request.Cadence, request.AmountBasis, request.ProrationRule, request.StartDate, request.EndDate, request.OccurrenceDay, request.TimeZoneId, request.VoucherSeriesCode, request.Currency, request.ReversalRule, request.Description, request.Lines.Select(x => new AccountingScheduleLineInput(x.FinanceAccountId, x.DebitAmount, x.CreditAmount, x.Description, x.DimensionMemberIds)).ToArray(), request.EvidenceDocumentIds);
}
