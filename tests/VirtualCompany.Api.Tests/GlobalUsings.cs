global using Xunit;
global using VirtualCompany.Application.Auth;
global using VirtualCompany.Application.Tasks;
global using VirtualCompany.Application.Workflows;
global using VirtualCompany.Application.Companies;
global using VirtualCompany.Application.Cockpit;
global using VirtualCompany.Application.Finance;
global using VirtualCompany.Application.Agents;
global using VirtualCompany.Application.Mailbox;
global using VirtualCompany.Infrastructure.Persistence;
global using VirtualCompany.Infrastructure.Companies;
global using VirtualCompany.Infrastructure.Finance;
global using VirtualCompany.Infrastructure.Sales;
global using VirtualCompany.Domain.Entities;
global using VirtualCompany.Domain.Enums;
global using System.Net.Http.Json;
global using System.Text.Json.Nodes;
global using Microsoft.AspNetCore.Mvc;
global using Microsoft.AspNetCore.Http;
global using Microsoft.EntityFrameworkCore;
global using Microsoft.Extensions.DependencyInjection;
global using VirtualCompany.Application.Auditing;
global using VirtualCompany.Application.CustomerMemory;
global using VirtualCompany.Application.Context;
global using VirtualCompany.Application.Orchestration;
global using StartCompanySimulationRequest = VirtualCompany.Api.Controllers.StartCompanySimulationRequest;
global using UpdateCompanySimulationSettingsRequest = VirtualCompany.Api.Controllers.UpdateCompanySimulationSettingsRequest;
global using AdvanceCompanySimulationTimeRequest = VirtualCompany.Api.Controllers.AdvanceCompanySimulationTimeRequest;
global using FinanceInvoiceDetailResponse = VirtualCompany.Api.Controllers.FinanceInvoiceDetailResponse;
global using VirtualCompany.Infrastructure.Context;
global using ActivityEvent = VirtualCompany.Domain.Entities.ActivityEvent;
global using AgentToolExecutionResponse = VirtualCompany.Application.Agents.ExecuteAgentToolResultDto;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace VirtualCompany.Api.Tests;

internal static class FinanceCounterpartyTypes
{
    public const string Supplier = "supplier";
    public const string Customer = "customer";
}

internal static class FinanceAccountTypes
{
    public const string Asset = "asset";
    public const string Liability = "liability";
    public const string Equity = "equity";
    public const string Revenue = "revenue";
    public const string Expense = "expense";
}

internal static class TestHttpClientExtensions
{
    public static HttpClient WithDevUser(this HttpClient client, string subject)
    {
        client.DefaultRequestHeaders.Remove(DevAuthHeaderDefaults.SubjectHeader);
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.SubjectHeader, subject);
        return client;
    }
}

internal static class DevAuthHeaderDefaults
{
    public const string SubjectHeader = VirtualCompany.Infrastructure.Auth.DevHeaderAuthenticationDefaults.SubjectHeader;
    public const string EmailHeader = VirtualCompany.Infrastructure.Auth.DevHeaderAuthenticationDefaults.EmailHeader;
    public const string DisplayNameHeader = VirtualCompany.Infrastructure.Auth.DevHeaderAuthenticationDefaults.DisplayNameHeader;
    public const string ProviderHeader = VirtualCompany.Infrastructure.Auth.DevHeaderAuthenticationDefaults.ProviderHeader;
    public const string SubjectHeaderName = SubjectHeader;
    public const string EmailHeaderName = EmailHeader;
    public const string DisplayNameHeaderName = DisplayNameHeader;
    public const string ProviderHeaderName = ProviderHeader;
}
