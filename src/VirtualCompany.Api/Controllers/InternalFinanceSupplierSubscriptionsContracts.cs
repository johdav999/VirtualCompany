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
public static class UpsertSupplierSubscriptionRequestMapping
{
    public static CreateSupplierSubscriptionCommand ToCreateCommand(this UpsertSupplierSubscriptionRequest request, Guid companyId, Guid? actorUserId, string actorDisplayName) => new(companyId, request.CounterpartyId, request.Name, request.Currency, request.ExpectedAmount, request.Cadence, request.BillingDay, request.StartDateUtc, request.NextExpectedBillDateUtc, request.AmountTolerance, request.DateToleranceDays, request.EndDateUtc, request.ContractReference, request.Description, request.NoticePeriodDays, request.AutoRenews, request.ContractDocumentId, actorUserId, actorDisplayName);
    public static UpdateSupplierSubscriptionCommand ToUpdateCommand(this UpsertSupplierSubscriptionRequest request, Guid companyId, Guid subscriptionId, Guid? actorUserId, string actorDisplayName) => new(companyId, subscriptionId, request.CounterpartyId, request.Name, request.Currency, request.ExpectedAmount, request.Cadence, request.BillingDay, request.StartDateUtc, request.NextExpectedBillDateUtc, request.AmountTolerance, request.DateToleranceDays, request.EndDateUtc, request.ContractReference, request.Description, request.NoticePeriodDays, request.AutoRenews, request.ContractDocumentId, actorUserId, actorDisplayName);
}
