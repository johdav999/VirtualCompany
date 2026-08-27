using System.Net;
using System.Text;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class FinanceApiClientAccountingConfigurationTests
{
    [Fact]
    public async Task Accounting_client_uses_company_transport_and_typed_capability_routes()
    {
        var companyId = Guid.NewGuid();
        var transport = new RecordingTransport();
        var client = new FinanceApiClient(transport, useOfflineMode: false);

        await client.GetCompanyStatutoryProfileAsync(companyId);
        await client.CreateCompanyStatutoryProfileAsync(companyId, new SaveCompanyStatutoryProfileApiRequest());
        await client.UpdateCompanyStatutoryProfileAsync(companyId, new SaveCompanyStatutoryProfileApiRequest { ExpectedVersion = 1 });
        await client.GetAccountingSetupStatusAsync(companyId);
        await client.CreateAccountingConfigurationAsync(companyId, new CreateAccountingConfigurationApiRequest
        {
            BaseCurrency = "USD"
        });
        await client.PreviewAccountingPolicyPackAsync(companyId, new PreviewAccountingPolicyPackApiRequest
        {
            PolicyPackKey = "country-neutral",
            PolicyPackVersion = "1.1.0",
            EffectiveFrom = new DateOnly(2026, 9, 1)
        });
        await client.ApplyAccountingPolicyPackAsync(companyId, new ApplyAccountingPolicyPackApiRequest
        {
            PolicyPackKey = "country-neutral",
            PolicyPackVersion = "1.1.0",
            EffectiveFrom = new DateOnly(2026, 9, 1),
            ExpectedVersion = 2
        });
        await client.ValidateAccountingConfigurationAsync(companyId);
        await client.GetAccountingCapabilityAsync(companyId, "country specific/reporting");

        Assert.Collection(
            transport.Requests,
            request => AssertRequest(request, companyId, HttpMethod.Get, $"internal/companies/{companyId}/finance/accounting/statutory-profile"),
            request => AssertRequest(request, companyId, HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/statutory-profile"),
            request => AssertRequest(request, companyId, HttpMethod.Put, $"internal/companies/{companyId}/finance/accounting/statutory-profile"),
            request => AssertRequest(request, companyId, HttpMethod.Get, $"internal/companies/{companyId}/finance/accounting/setup-status"),
            request => AssertRequest(request, companyId, HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/configuration"),
            request => AssertRequest(request, companyId, HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/policy-pack/preview"),
            request => AssertRequest(request, companyId, HttpMethod.Put, $"internal/companies/{companyId}/finance/accounting/policy-pack"),
            request => AssertRequest(request, companyId, HttpMethod.Get, $"internal/companies/{companyId}/finance/accounting/validation"),
            request => AssertRequest(request, companyId, HttpMethod.Get, $"internal/companies/{companyId}/finance/accounting/capabilities/country%20specific%2Freporting"));
    }

    [Fact]
    public async Task Accounting_mutations_are_blocked_in_explicit_offline_mode()
    {
        var client = new FinanceApiClient(new RecordingTransport(), useOfflineMode: true);

        await Assert.ThrowsAsync<FinanceApiException>(() =>
            client.CreateAccountingConfigurationAsync(Guid.NewGuid(), new CreateAccountingConfigurationApiRequest()));
        await Assert.ThrowsAsync<FinanceApiException>(() =>
            client.CreateCompanyStatutoryProfileAsync(Guid.NewGuid(), new SaveCompanyStatutoryProfileApiRequest()));
    }

    [Fact]
    public async Task Accounting_administration_client_uses_typed_company_scoped_routes()
    {
        var companyId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var transport = new RecordingTransport();
        var client = new FinanceApiClient(transport, useOfflineMode: false);

        await client.GetAccountingPolicyPacksAsync(companyId);
        await client.PreviewAccountingSetupAsync(companyId, new PreviewAccountingSetupApiRequest());
        await client.CompleteAccountingSetupAsync(companyId, new CompleteAccountingSetupApiRequest());
        await client.GetAccountingAccountsAsync(companyId, "cash & bank", "asset", "active");
        await client.GetAccountingAccountAsync(companyId, accountId);
        await client.CreateAccountingAccountAsync(companyId, new CreateAccountingAccountApiRequest());
        await client.GetAccountingChartCatalogAsync(companyId, search: "1510", groupCode: "15", k2Only: true, excludeExisting: true, skip: 25, take: 25);
        await client.CreateAccountingAccountFromChartCatalogAsync(companyId, new CreateAccountingAccountFromChartCatalogApiRequest
        {
            Code = "1510",
            AccountingSemanticsConfirmed = true,
            CompanySuitabilityConfirmed = true
        });
        await client.RenameAccountingAccountAsync(companyId, accountId, new RenameAccountingAccountApiRequest());
        await client.DeactivateAccountingAccountAsync(companyId, accountId, new DeactivateAccountingAccountApiRequest());
        await client.GetAccountingFiscalYearsAsync(companyId);
        await client.GetAccountingPeriodAsync(companyId, periodId);
        await client.PreviewAccountingFiscalYearAsync(companyId, new PreviewAccountingFiscalYearApiRequest());
        await client.CreateAccountingFiscalYearAsync(companyId, new CreateAccountingFiscalYearApiRequest());

        Assert.Collection(
            transport.Requests,
            request => AssertRequest(request, companyId, HttpMethod.Get, $"internal/companies/{companyId}/finance/accounting/policy-packs"),
            request => AssertRequest(request, companyId, HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/setup/preview"),
            request => AssertRequest(request, companyId, HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/setup/complete"),
            request => AssertRequest(request, companyId, HttpMethod.Get, $"internal/companies/{companyId}/finance/accounting/accounts?search=cash%20%26%20bank&accountClass=asset&status=active"),
            request => AssertRequest(request, companyId, HttpMethod.Get, $"internal/companies/{companyId}/finance/accounting/accounts/{accountId:D}"),
            request => AssertRequest(request, companyId, HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/accounts"),
            request => AssertRequest(request, companyId, HttpMethod.Get, $"internal/companies/{companyId}/finance/accounting/chart-catalogs/bas-2026/1.1/accounts?search=1510&groupCode=15&k2Only=true&excludeExisting=true&skip=25&take=25"),
            request => AssertRequest(request, companyId, HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/accounts/from-chart-catalog"),
            request => AssertRequest(request, companyId, HttpMethod.Put, $"internal/companies/{companyId}/finance/accounting/accounts/{accountId:D}/name"),
            request => AssertRequest(request, companyId, HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/accounts/{accountId:D}/deactivate"),
            request => AssertRequest(request, companyId, HttpMethod.Get, $"internal/companies/{companyId}/finance/accounting/fiscal-years"),
            request => AssertRequest(request, companyId, HttpMethod.Get, $"internal/companies/{companyId}/finance/accounting/periods/{periodId:D}"),
            request => AssertRequest(request, companyId, HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/fiscal-years/preview"),
            request => AssertRequest(request, companyId, HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/fiscal-years"));
    }

    [Fact]
    public async Task Accounting_operations_client_uses_typed_company_scoped_routes()
    {
        var companyId = Guid.NewGuid();
        var conflictId = Guid.NewGuid();
        var transport = new RecordingTransport();
        var client = new FinanceApiClient(transport, useOfflineMode: false);

        await client.GetAccountingOperationsAsync(companyId);
        await client.StartAccountingMigrationAsync(companyId, new StartAccountingMigrationApiRequest
        {
            IdempotencyKey = "release:migration:1"
        });
        await client.ResolveAccountingMigrationConflictAsync(companyId, conflictId,
            new ResolveAccountingMigrationConflictApiRequest
            {
                ResolutionSummary = "Reviewed against the source document.",
                ExpectedVersion = 3
            });
        await client.VerifyAccountingRecoveryAsync(companyId, new VerifyAccountingRecoveryApiRequest
        {
            VerifyObjectContent = true
        });

        Assert.Collection(
            transport.Requests,
            request => AssertRequest(request, companyId, HttpMethod.Get,
                $"internal/companies/{companyId}/finance/accounting/operations"),
            request => AssertRequest(request, companyId, HttpMethod.Post,
                $"internal/companies/{companyId}/finance/accounting/operations/migrations"),
            request => AssertRequest(request, companyId, HttpMethod.Put,
                $"internal/companies/{companyId}/finance/accounting/operations/migration-conflicts/{conflictId:D}/resolve"),
            request => AssertRequest(request, companyId, HttpMethod.Post,
                $"internal/companies/{companyId}/finance/accounting/operations/recovery-verification"));
    }

    private static void AssertRequest(
        RecordedRequest request,
        Guid companyId,
        HttpMethod method,
        string uri)
    {
        Assert.Equal(companyId, request.CompanyId);
        Assert.Equal(method, request.Method);
        Assert.Equal(uri, request.Uri);
    }

    private sealed class RecordingTransport : ICompanyApiTransport
    {
        public Uri? BaseAddress => new("https://example.test/");
        public List<RecordedRequest> Requests { get; } = [];

        public Task<HttpResponseMessage> SendAsync(
            Guid companyId,
            HttpMethod method,
            string uri,
            HttpContent? content,
            CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(companyId, method, uri));
            var isList = method == HttpMethod.Get &&
                (uri.EndsWith("/accounting/policy-packs", StringComparison.Ordinal) ||
                 uri.Contains("/accounting/accounts?", StringComparison.Ordinal) ||
                 uri.EndsWith("/accounting/fiscal-years", StringComparison.Ordinal));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(isList ? "[]" : "{}", Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed record RecordedRequest(Guid CompanyId, HttpMethod Method, string Uri);
}
