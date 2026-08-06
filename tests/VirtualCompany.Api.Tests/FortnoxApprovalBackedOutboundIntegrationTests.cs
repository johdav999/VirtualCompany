using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FortnoxApprovalBackedOutboundIntegrationTests
{
    private static readonly DateTime Now = new(2026, 5, 4, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task OAuth_callback_success_persists_active_tenant_connection_without_exposing_tokens()
    {
        var fortnoxHttp = new ScriptedFortnoxHttpHandler();
        fortnoxHttp.EnqueueJson(HttpStatusCode.OK, new
        {
            access_token = "fortnox-access-token",
            refresh_token = "fortnox-refresh-token",
            expires_in = 3600,
            scope = "bookkeeping invoice",
            tenant_id = "fortnox-tenant-1"
        });

        await using var factory = CreateFactory(fortnoxHttp);
        var seed = await SeedTenantAsync(factory, CompanyMembershipRole.Owner);
        using var client = CreateAuthenticatedClient(factory, seed);

        var start = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId:D}/finance/integrations/fortnox/connect",
            new { returnUri = $"http://localhost/finance/settings/integrations/fortnox?companyId={seed.CompanyId:D}" });
        start.EnsureSuccessStatusCode();
        var startPayload = await start.Content.ReadFromJsonAsync<StartConnectionResponse>();
        var state = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(startPayload!.AuthorizationUrl).Query)["state"].ToString();

        var callback = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId:D}/finance/integrations/fortnox/callback",
            new { code = "authorization-code", state });

        callback.EnsureSuccessStatusCode();
        var completion = await callback.Content.ReadFromJsonAsync<OAuthCompletionResponse>();
        Assert.NotNull(completion);
        Assert.Equal(seed.CompanyId, completion!.CompanyId);
        Assert.Equal(FortnoxConnectionStatusValues.Connected, completion.Status);

        var persisted = await ExecuteDbAsync(factory, db => db.FortnoxConnections.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == seed.CompanyId));
        Assert.Equal(FortnoxConnectionStatus.Connected, persisted.Status);
        Assert.Equal("fortnox-tenant-1", persisted.ProviderTenantId);
        Assert.DoesNotContain("fortnox-access-token", persisted.EncryptedAccessToken!, StringComparison.Ordinal);
        Assert.DoesNotContain("fortnox-refresh-token", persisted.EncryptedRefreshToken!, StringComparison.Ordinal);
        Assert.Null(persisted.LastErrorSummary);
        Assert.Single(fortnoxHttp.Requests, request => request.Path.EndsWith("/oauth-v1/token", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task OAuth_callback_failure_records_safe_failure_and_does_not_create_connection()
    {
        await using var factory = CreateFactory();
        var seed = await SeedTenantAsync(factory, CompanyMembershipRole.Owner);
        using var client = CreateAuthenticatedClient(factory, seed);

        var start = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId:D}/finance/integrations/fortnox/connect",
            new { returnUri = $"http://localhost/finance/settings/integrations/fortnox?companyId={seed.CompanyId:D}" });
        var startPayload = await start.Content.ReadFromJsonAsync<StartConnectionResponse>();
        var state = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(startPayload!.AuthorizationUrl).Query)["state"].ToString();

        var callback = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId:D}/finance/integrations/fortnox/callback",
            new { state, providerError = "access_denied" });

        Assert.Equal(HttpStatusCode.BadRequest, callback.StatusCode);
        var problem = await callback.Content.ReadFromJsonAsync<ProblemResponse>();
        Assert.Contains("cancelled or denied", problem!.Detail, StringComparison.OrdinalIgnoreCase);

        var snapshot = await ExecuteDbAsync(factory, async db => new
        {
            Connections = await db.FortnoxConnections.IgnoreQueryFilters().CountAsync(x => x.CompanyId == seed.CompanyId),
            State = await db.FortnoxOAuthStates.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == seed.CompanyId)
        });

        Assert.Equal(0, snapshot.Connections);
        Assert.Contains("cancelled or denied", snapshot.State.FailureReason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("access_denied", snapshot.State.FailureReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Token_refresh_success_updates_token_snapshot_and_allows_followup_access()
    {
        var fortnoxHttp = new ScriptedFortnoxHttpHandler();
        fortnoxHttp.EnqueueJson(HttpStatusCode.OK, new
        {
            access_token = "new-access-token",
            refresh_token = "new-refresh-token",
            expires_in = 3600,
            scope = "bookkeeping invoice",
            tenant_id = "tenant-refresh"
        });

        await using var factory = CreateFactory(fortnoxHttp);
        var seed = await SeedTenantAsync(factory, CompanyMembershipRole.Owner);
        var connectionId = await SeedFortnoxConnectionAsync(factory, seed, accessTokenExpiresUtc: Now.AddMinutes(-10));

        var result = await ExecuteScopedAsync(factory, seed.CompanyId, provider =>
            provider.GetRequiredService<IFortnoxOAuthService>().GetValidAccessTokenAsync(
                new RefreshFortnoxAccessTokenCommand(seed.CompanyId, connectionId, ForceRefresh: true),
                CancellationToken.None));

        Assert.True(result.Succeeded);
        Assert.Equal("new-access-token", result.AccessToken);
        Assert.Single(fortnoxHttp.Requests, request => request.Body.Contains("grant_type=refresh_token", StringComparison.Ordinal));

        var persisted = await ExecuteDbAsync(factory, db => db.FortnoxConnections.IgnoreQueryFilters().SingleAsync(x => x.Id == connectionId));
        Assert.Equal(FortnoxConnectionStatus.Connected, persisted.Status);
        Assert.NotNull(persisted.LastRefreshAttemptUtc);
        Assert.NotNull(persisted.LastSuccessfulRefreshUtc);
        Assert.DoesNotContain("new-access-token", persisted.EncryptedAccessToken!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Token_refresh_failure_marks_reconnect_and_prevents_outbound_execution()
    {
        var fortnoxHttp = new ScriptedFortnoxHttpHandler();
        fortnoxHttp.EnqueueJson(HttpStatusCode.BadRequest, new
        {
            error = "invalid_grant",
            error_description = "raw refresh token rejected"
        });

        await using var factory = CreateFactory(fortnoxHttp);
        var seed = await SeedTenantAsync(factory, CompanyMembershipRole.Owner);
        var connectionId = await SeedFortnoxConnectionAsync(factory, seed, accessTokenExpiresUtc: Now.AddMinutes(-10));

        var result = await ExecuteScopedAsync(factory, seed.CompanyId, provider =>
            provider.GetRequiredService<IFortnoxOAuthService>().GetValidAccessTokenAsync(
                new RefreshFortnoxAccessTokenCommand(seed.CompanyId, connectionId, ForceRefresh: true),
                CancellationToken.None));

        Assert.False(result.Succeeded);
        Assert.True(result.NeedsReconnect);
        Assert.DoesNotContain("raw refresh token", result.SafeFailureMessage!, StringComparison.OrdinalIgnoreCase);

        var persisted = await ExecuteDbAsync(factory, db => db.FortnoxConnections.IgnoreQueryFilters().SingleAsync(x => x.Id == connectionId));
        Assert.Equal(FortnoxConnectionStatus.NeedsReconnect, persisted.Status);
        Assert.Contains("Reconnect Fortnox", persisted.LastErrorSummary!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Sync_is_idempotent_for_repeated_fortnox_external_ids()
    {
        var fakeApi = new FakeFortnoxApiClient
        {
            Customers =
            [
                new FortnoxCustomer
                {
                    CustomerNumber = "1001",
                    Name = "Acme AB",
                    OrganisationNumber = "556000-0001",
                    Email = "finance@acme.example",
                    Active = true,
                    LastModified = "2026-05-04T09:00:00Z"
                }
            ]
        };

        await using var factory = CreateFactory(apiClient: fakeApi);
        var seed = await SeedTenantAsync(factory, CompanyMembershipRole.Owner);
        var connectionId = await SeedFinanceIntegrationConnectionAsync(factory, seed);

        var first = await ExecuteScopedAsync(factory, seed.CompanyId, provider =>
            provider.GetRequiredService<IFortnoxSyncService>().SyncAsync(
                new RunFortnoxSyncCommand(seed.CompanyId, connectionId, "sync-idempotency", seed.UserId),
                CancellationToken.None));
        var second = await ExecuteScopedAsync(factory, seed.CompanyId, provider =>
            provider.GetRequiredService<IFortnoxSyncService>().SyncAsync(
                new RunFortnoxSyncCommand(seed.CompanyId, connectionId, "sync-idempotency", seed.UserId),
                CancellationToken.None));

        Assert.True(first.Created >= 1);
        Assert.True(second.Updated >= 1 || second.Skipped >= 1);

        var counts = await ExecuteDbAsync(factory, async db => new
        {
            Counterparties = await db.FinanceCounterparties.IgnoreQueryFilters().CountAsync(x => x.CompanyId == seed.CompanyId),
            References = await db.FinanceExternalReferences.IgnoreQueryFilters().CountAsync(x =>
                x.CompanyId == seed.CompanyId &&
                x.ProviderKey == FinanceIntegrationProviderKeys.Fortnox &&
                x.ExternalId == "1001"),
            Histories = await db.FinanceIntegrationAuditEvents.IgnoreQueryFilters().CountAsync(x =>
                x.CompanyId == seed.CompanyId &&
                x.ProviderKey == FinanceIntegrationProviderKeys.Fortnox &&
                x.EventType == "manual_sync_completed")
        });

        Assert.Equal(1, counts.Counterparties);
        Assert.Equal(1, counts.References);
        Assert.Equal(2, counts.Histories);
    }

    [Fact]
    public async Task Sync_reconciles_fortnox_accounts_with_existing_finance_account_codes()
    {
        var fakeApi = new FakeFortnoxApiClient
        {
            Accounts =
            [
                new FortnoxAccount
                {
                    Number = 6100,
                    Description = "Office supplies",
                    Type = "COST",
                    Active = true,
                    LastModified = "2026-05-04T09:00:00Z"
                },
                new FortnoxAccount
                {
                    Number = 6100,
                    Description = "Office supplies",
                    Type = "COST",
                    Active = true,
                    LastModified = "2026-05-04T09:00:00Z"
                }
            ]
        };

        await using var factory = CreateFactory(apiClient: fakeApi);
        var seed = await SeedTenantAsync(factory, CompanyMembershipRole.Owner);
        var connectionId = await SeedFinanceIntegrationConnectionAsync(factory, seed);

        await ExecuteDbAsync(factory, async db =>
        {
            db.FinanceAccounts.Add(new FinanceAccount(
                Guid.NewGuid(),
                seed.CompanyId,
                "6100",
                "Existing expense account",
                FinanceAccountTypes.Expense,
                "SEK",
                0m,
                Now.AddDays(-1)));
            await db.SaveChangesAsync();
            return true;
        });

        var result = await ExecuteScopedAsync(factory, seed.CompanyId, provider =>
            provider.GetRequiredService<IFortnoxSyncService>().SyncAsync(
                new RunFortnoxSyncCommand(seed.CompanyId, connectionId, "sync-account-reconcile", seed.UserId),
                CancellationToken.None));

        Assert.True(result.Updated >= 1);
        Assert.True(result.Skipped >= 1);

        var persisted = await ExecuteDbAsync(factory, async db => new
        {
            Accounts = await db.FinanceAccounts.IgnoreQueryFilters().CountAsync(x => x.CompanyId == seed.CompanyId && x.Code == "6100"),
            Account = await db.FinanceAccounts.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == seed.CompanyId && x.Code == "6100"),
            References = await db.FinanceExternalReferences.IgnoreQueryFilters().CountAsync(x =>
                x.CompanyId == seed.CompanyId &&
                x.ProviderKey == FinanceIntegrationProviderKeys.Fortnox &&
                x.EntityType == "account" &&
                x.ExternalId == "6100")
        });

        Assert.Equal(1, persisted.Accounts);
        Assert.Equal("Office supplies", persisted.Account.Name);
        Assert.Equal(1, persisted.References);
    }

    [Fact]
    public async Task Sync_refreshes_existing_supplier_invoice_lifecycle_statuses_from_fortnox()
    {
        var fakeApi = new FakeFortnoxApiClient
        {
            SupplierInvoices =
            [
                new FortnoxSupplierInvoice
                {
                    GivenNumber = "42",
                    SupplierNumber = "S-42",
                    SupplierName = "Nordic IT Solutions AB",
                    InvoiceDate = "2026-05-01",
                    DueDate = "2026-05-31",
                    Total = 2410m,
                    Balance = 2410m,
                    Currency = "SEK",
                    Booked = true,
                    FullyPaid = false,
                    LastModified = "2026-05-01T10:00:00Z"
                }
            ]
        };

        await using var factory = CreateFactory(apiClient: fakeApi);
        var seed = await SeedTenantAsync(factory, CompanyMembershipRole.Owner);
        var connectionId = await SeedFinanceIntegrationConnectionAsync(factory, seed);

        await ExecuteScopedAsync(factory, seed.CompanyId, provider =>
            provider.GetRequiredService<IFortnoxSyncService>().SyncAsync(
                new RunFortnoxSyncCommand(seed.CompanyId, connectionId, "supplier-invoice-initial", seed.UserId, FullSync: true),
                CancellationToken.None));

        fakeApi.SupplierInvoices =
        [
            new FortnoxSupplierInvoice
            {
                GivenNumber = "42",
                SupplierNumber = "S-42",
                SupplierName = "Nordic IT Solutions AB",
                InvoiceDate = "2026-05-01",
                DueDate = "2026-05-31",
                Total = 2410m,
                Balance = 0m,
                Currency = "SEK",
                Booked = true,
                FullyPaid = true,
                PaymentPending = false,
                LastModified = "2026-05-02T10:00:00Z"
            }
        ];

        var refresh = await ExecuteScopedAsync(factory, seed.CompanyId, provider =>
            provider.GetRequiredService<IFortnoxSyncService>().SyncAsync(
                new RunFortnoxSyncCommand(seed.CompanyId, connectionId, "supplier-invoice-refresh", seed.UserId, FullSync: true),
                CancellationToken.None));

        Assert.True(refresh.Updated >= 1);
        var persisted = await ExecuteDbAsync(factory, async db => new
        {
            Bills = await db.FinanceBills.IgnoreQueryFilters().CountAsync(x => x.CompanyId == seed.CompanyId),
            Bill = await db.FinanceBills.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == seed.CompanyId && x.BillNumber == "42"),
            Reference = await db.FinanceExternalReferences.IgnoreQueryFilters().SingleAsync(x =>
                x.CompanyId == seed.CompanyId &&
                x.ProviderKey == FinanceIntegrationProviderKeys.Fortnox &&
                x.EntityType == "supplier_invoice" &&
                x.ExternalId == "42")
        });

        Assert.Equal(1, persisted.Bills);
        Assert.Equal("paid", persisted.Bill.Status);
        Assert.Equal(FinanceDocumentPostingStatuses.Booked, persisted.Bill.PostingStatus);
        Assert.Equal(FinanceSettlementStatuses.Paid, persisted.Bill.SettlementStatus);
        Assert.Equal(FinanceDocumentDueStatuses.NotDue, persisted.Bill.DueStatus);
        Assert.Equal(FinanceDocumentKinds.SupplierInvoice, persisted.Bill.DocumentKind);
        Assert.Equal(2410m, persisted.Bill.PaidAmount);
        Assert.Contains("fullyPaid=true", persisted.Bill.ProviderStatus);
        Assert.Equal(true, persisted.Reference.Metadata["rawFullyPaid"]?.GetValue<bool>());
        Assert.Equal(0m, persisted.Reference.Metadata["rawBalance"]?.GetValue<decimal>());
    }

    [Fact]
    public async Task Full_sync_repairs_a_dangling_supplier_invoice_reference()
    {
        var fakeApi = new FakeFortnoxApiClient
        {
            SupplierInvoices =
            [
                new FortnoxSupplierInvoice
                {
                    GivenNumber = "84",
                    SupplierNumber = "S-84",
                    SupplierName = "Repair Supplier AB",
                    InvoiceDate = "2026-06-01",
                    DueDate = "2026-06-30",
                    Total = 1200m,
                    Balance = 1200m,
                    Currency = "SEK",
                    Booked = true,
                    FullyPaid = false,
                    LastModified = "2026-06-01T10:00:00Z"
                }
            ]
        };

        await using var factory = CreateFactory(apiClient: fakeApi);
        var seed = await SeedTenantAsync(factory, CompanyMembershipRole.Owner);
        var connectionId = await SeedFinanceIntegrationConnectionAsync(factory, seed);

        await ExecuteScopedAsync(factory, seed.CompanyId, provider =>
            provider.GetRequiredService<IFortnoxSyncService>().SyncAsync(
                new RunFortnoxSyncCommand(seed.CompanyId, connectionId, "supplier-invoice-create", seed.UserId, FullSync: true),
                CancellationToken.None));

        var original = await ExecuteDbAsync(factory, async db => new
        {
            BillId = await db.FinanceBills.IgnoreQueryFilters()
                .Where(x => x.CompanyId == seed.CompanyId && x.BillNumber == "84")
                .Select(x => x.Id)
                .SingleAsync(),
            ReferenceId = await db.FinanceExternalReferences.IgnoreQueryFilters()
                .Where(x =>
                    x.CompanyId == seed.CompanyId &&
                    x.ProviderKey == FinanceIntegrationProviderKeys.Fortnox &&
                    x.EntityType == "supplier_invoice" &&
                    x.ExternalId == "84")
                .Select(x => x.Id)
                .SingleAsync()
        });

        await ExecuteDbAsync(factory, async db =>
        {
            await db.FinanceBills.IgnoreQueryFilters()
                .Where(x => x.Id == original.BillId)
                .ExecuteDeleteAsync();
            return true;
        });

        var repair = await ExecuteScopedAsync(factory, seed.CompanyId, provider =>
            provider.GetRequiredService<IFortnoxSyncService>().SyncAsync(
                new RunFortnoxSyncCommand(seed.CompanyId, connectionId, "supplier-invoice-repair", seed.UserId, FullSync: true),
                CancellationToken.None));

        var repaired = await ExecuteDbAsync(factory, async db => new
        {
            Bill = await db.FinanceBills.IgnoreQueryFilters()
                .SingleAsync(x => x.CompanyId == seed.CompanyId && x.BillNumber == "84"),
            Reference = await db.FinanceExternalReferences.IgnoreQueryFilters()
                .SingleAsync(x => x.Id == original.ReferenceId),
            SourceType = await db.FinanceBills.IgnoreQueryFilters()
                .Where(x => x.CompanyId == seed.CompanyId && x.BillNumber == "84")
                .Select(x => EF.Property<string>(x, "SourceType"))
                .SingleAsync()
        });

        Assert.True(repair.Created >= 1);
        Assert.NotEqual(original.BillId, repaired.Bill.Id);
        Assert.Equal(repaired.Bill.Id, repaired.Reference.InternalRecordId);
        Assert.Equal(FinanceRecordSourceTypes.Fortnox, repaired.SourceType);
    }

    [Fact]
    public async Task Offline_recovery_repairs_only_supplier_invoices_with_retained_fortnox_references()
    {
        var supplierInvoice = new FortnoxSupplierInvoice
        {
            GivenNumber = "85",
            SupplierNumber = "S-85",
            SupplierName = "Offline Recovery Supplier AB",
            InvoiceDate = "2026-06-02",
            DueDate = "2026-07-02",
            Total = 1500m,
            Balance = 1500m,
            Currency = "SEK",
            Booked = true,
            FullyPaid = false
        };
        var fakeApi = new FakeFortnoxApiClient { SupplierInvoices = [supplierInvoice] };
        await using var factory = CreateFactory(apiClient: fakeApi);
        var seed = await SeedTenantAsync(factory, CompanyMembershipRole.Owner);
        var connectionId = await SeedFinanceIntegrationConnectionAsync(factory, seed);

        await ExecuteScopedAsync(factory, seed.CompanyId, provider =>
            provider.GetRequiredService<IFortnoxSyncService>().SyncAsync(
                new RunFortnoxSyncCommand(seed.CompanyId, connectionId, "offline-recovery-create", seed.UserId, FullSync: true),
                CancellationToken.None));

        var originalBillId = await ExecuteDbAsync(factory, db =>
            db.FinanceBills.IgnoreQueryFilters()
                .Where(x => x.CompanyId == seed.CompanyId && x.BillNumber == "85")
                .Select(x => x.Id)
                .SingleAsync());
        await ExecuteDbAsync(factory, async db =>
        {
            await db.FinanceBills.IgnoreQueryFilters()
                .Where(x => x.Id == originalBillId)
                .ExecuteDeleteAsync();
            return true;
        });

        var result = await ExecuteScopedAsync(factory, seed.CompanyId, async provider =>
        {
            var mapper = provider.GetRequiredService<IFortnoxMappingService>();
            var valid = mapper.MapSupplierInvoice(supplierInvoice);
            var unknown = valid with { ExternalId = "unknown", ExternalNumber = "unknown" };
            return await provider.GetRequiredService<IFortnoxSyncService>().RepairDanglingSupplierInvoicesAsync(
                new RepairFortnoxSupplierInvoicesCommand(
                    seed.CompanyId,
                    connectionId,
                    [valid, unknown],
                    "offline-recovery",
                    seed.UserId),
                CancellationToken.None);
        });

        Assert.Equal(1, result.Repaired);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(1, result.Rejected);
        Assert.Contains(result.Items, x => x.ExternalId == "85" && x.Outcome == "repaired");
        Assert.Contains(result.Items, x => x.ExternalId == "unknown" && x.Outcome == "rejected");

        var repaired = await ExecuteDbAsync(factory, async db => new
        {
            Bills = await db.FinanceBills.IgnoreQueryFilters()
                .CountAsync(x => x.CompanyId == seed.CompanyId && x.BillNumber == "85"),
            Reference = await db.FinanceExternalReferences.IgnoreQueryFilters()
                .SingleAsync(x =>
                    x.CompanyId == seed.CompanyId &&
                    x.EntityType == "supplier_invoice" &&
                    x.ExternalId == "85")
        });
        Assert.Equal(1, repaired.Bills);
        Assert.NotEqual(originalBillId, repaired.Reference.InternalRecordId);
    }

    [Fact]
    public async Task Sync_skips_entities_when_granted_fortnox_scopes_are_missing()
    {
        var fakeApi = new FakeFortnoxApiClient();
        await using var factory = CreateFactory(apiClient: fakeApi);
        var seed = await SeedTenantAsync(factory, CompanyMembershipRole.Owner);
        var connectionId = await SeedFortnoxConnectionAsync(factory, seed, accessTokenExpiresUtc: Now.AddHours(1));

        var result = await ExecuteScopedAsync(factory, seed.CompanyId, provider =>
            provider.GetRequiredService<IFortnoxSyncService>().SyncAsync(
                new RunFortnoxSyncCommand(seed.CompanyId, connectionId, "sync-missing-scopes", seed.UserId),
                CancellationToken.None));

        Assert.Equal(FinanceIntegrationSyncStatuses.Partial, result.Status);
        Assert.Contains("accounts", fakeApi.PageCalls);
        Assert.Contains("invoices", fakeApi.PageCalls);
        Assert.DoesNotContain("projects", fakeApi.PageCalls);
        Assert.DoesNotContain("supplier_invoices", fakeApi.PageCalls);
        Assert.Contains(result.Entities, entity =>
            entity.EntityType == "projects" &&
            entity.Errors == 1 &&
            entity.ErrorSummary!.Contains("project permission", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Entities, entity =>
            entity.EntityType == "supplier_invoices" &&
            entity.Errors == 1 &&
            entity.ErrorSummary!.Contains("supplierinvoice permission", StringComparison.OrdinalIgnoreCase));

        var syncStates = await ExecuteDbAsync(factory, async db =>
            await db.FinanceIntegrationSyncStates
                .IgnoreQueryFilters()
                .Where(x => x.CompanyId == seed.CompanyId && (x.EntityType == "projects" || x.EntityType == "supplier_invoices"))
                .Select(x => new { x.EntityType, x.Status, x.LastErrorSummary })
                .ToListAsync());
        var connectionStatus = await ExecuteDbAsync(factory, async db =>
            await db.FortnoxConnections
                .IgnoreQueryFilters()
                .Where(x => x.CompanyId == seed.CompanyId && x.Id == connectionId)
                .Select(x => x.Status)
                .SingleAsync());

        Assert.All(syncStates, state =>
        {
            Assert.Equal(FinanceIntegrationSyncStatuses.Failed, state.Status);
            Assert.Contains("Enable the scope", state.LastErrorSummary, StringComparison.OrdinalIgnoreCase);
        });
        Assert.Equal(FortnoxConnectionStatus.NeedsReconnect, connectionStatus);

        fakeApi.PageCalls.Clear();
        var blockedResult = await ExecuteScopedAsync(factory, seed.CompanyId, provider =>
            provider.GetRequiredService<IFortnoxSyncService>().SyncAsync(
                new RunFortnoxSyncCommand(seed.CompanyId, connectionId, "sync-reconnect-blocked", seed.UserId),
                CancellationToken.None));

        Assert.Equal(FinanceIntegrationSyncStatuses.Failed, blockedResult.Status);
        Assert.Equal(1, blockedResult.Errors);
        Assert.Empty(fakeApi.PageCalls);
    }

    [Fact]
    public async Task Tenant_isolation_blocks_cross_tenant_settings_and_outbound_execution()
    {
        var fakeApi = new FakeFortnoxApiClient();
        await using var factory = CreateFactory(apiClient: fakeApi);
        var tenantA = await SeedTenantAsync(factory, CompanyMembershipRole.Owner, "tenant-a");
        var tenantB = await SeedTenantAsync(factory, CompanyMembershipRole.Owner, "tenant-b");
        var connectionA = await SeedFinanceIntegrationConnectionAsync(factory, tenantA);
        var connectionB = await SeedFinanceIntegrationConnectionAsync(factory, tenantB);
        var actionB = await SeedApprovedOutboundActionAsync(factory, tenantB, connectionB);

        using var tenantAClient = CreateAuthenticatedClient(factory, tenantA);
        var crossTenantStatus = await tenantAClient.GetAsync($"/api/companies/{tenantB.CompanyId:D}/finance/integrations/fortnox/status");
        var crossTenantExecute = await tenantAClient.PostAsync(
            $"/api/companies/{tenantA.CompanyId:D}/finance/integrations/fortnox/outbound-actions/{actionB.WriteRequestId:D}/execute",
            null);

        Assert.Equal(HttpStatusCode.BadRequest, crossTenantStatus.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, crossTenantExecute.StatusCode);
        Assert.Empty(fakeApi.WriteRequests);

        await ExecuteScopedAsync(factory, tenantB.CompanyId, provider =>
            provider.GetRequiredService<IFortnoxOutboundActionExecutor>().ExecuteApprovedAsync(tenantB.CompanyId, actionB.WriteRequestId, CancellationToken.None));

        var write = Assert.Single(fakeApi.WriteRequests);
        Assert.Equal(tenantB.CompanyId, write.Context.CompanyId);
        Assert.Equal(connectionB, write.Context.ConnectionId);
        Assert.NotEqual(connectionA, write.Context.ConnectionId);
    }

    [Fact]
    public async Task Sensitive_outbound_write_is_persisted_for_approval_and_not_sent_before_approval()
    {
        var fakeApi = new FakeFortnoxApiClient();
        await using var factory = CreateFactory(apiClient: fakeApi);
        var seed = await SeedTenantAsync(factory, CompanyMembershipRole.Owner);
        var connectionId = await SeedFinanceIntegrationConnectionAsync(factory, seed);
        using var client = CreateAuthenticatedClient(factory, seed);
        var writeRequestId = Guid.NewGuid();
        var payload = new JsonObject
        {
            ["Invoice"] = new JsonObject
            {
                ["CustomerNumber"] = "1001",
                ["Total"] = 1200,
                ["access_token"] = "must-not-be-stored"
            }
        };

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId:D}/finance/integrations/fortnox/outbound-actions",
            new
            {
                commandType = "invoice_export",
                httpMethod = "POST",
                path = "invoices",
                targetCompany = "Acme AB",
                payload,
                connectionId,
                writeRequestId,
                correlationId = "outbound-approval-gate"
            });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Empty(fakeApi.WriteRequests);

        var persisted = await ExecuteDbAsync(factory, async db => new
        {
            Command = await db.FinanceIntegrationWriteCommands.IgnoreQueryFilters().SingleAsync(x => x.Id == writeRequestId),
            Approval = await db.ApprovalRequests.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == seed.CompanyId),
            AuditCount = await db.FinanceIntegrationAuditEvents.IgnoreQueryFilters().CountAsync(x => x.CompanyId == seed.CompanyId)
        });

        Assert.Equal(FinanceIntegrationWriteCommandRecordStatuses.AwaitingApproval, persisted.Command.Status);
        Assert.Equal(persisted.Approval.Id, persisted.Command.ApprovalId);
        Assert.Equal(ApprovalRequestStatus.Pending, persisted.Approval.Status);
        Assert.DoesNotContain("must-not-be-stored", persisted.Command.PayloadSummary, StringComparison.Ordinal);
        Assert.DoesNotContain("must-not-be-stored", persisted.Command.SanitizedPayloadJson, StringComparison.Ordinal);
        Assert.Equal(0, persisted.AuditCount);
    }

    [Fact]
    public async Task Approved_outbound_action_executes_once_and_records_request_response_and_audit()
    {
        var fakeApi = new FakeFortnoxApiClient
        {
            NextPostResponse = JsonNode.Parse("""{"Invoice":{"DocumentNumber":"INV-1001","Total":1200}}""")
        };
        await using var factory = CreateFactory(apiClient: fakeApi);
        var seed = await SeedTenantAsync(factory, CompanyMembershipRole.Owner);
        var connectionId = await SeedFinanceIntegrationConnectionAsync(factory, seed);
        var action = await SeedPendingOutboundActionAsync(factory, seed, connectionId);
        using var client = CreateAuthenticatedClient(factory, seed);

        var decision = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId:D}/approvals/{action.ApprovalId:D}/decisions",
            new { decision = "approve", stepId = action.StepId, comment = "Approved for export." });

        decision.EnsureSuccessStatusCode();

        var write = Assert.Single(fakeApi.WriteRequests);
        Assert.Equal(seed.CompanyId, write.Context.CompanyId);
        Assert.Equal(connectionId, write.Context.ConnectionId);
        Assert.Equal(action.ApprovalId, write.Context.ApprovedApprovalId);
        Assert.Equal(action.WriteRequestId, write.Context.WriteRequestId);
        Assert.Equal("invoices", write.Path);

        var persisted = await ExecuteDbAsync(factory, async db => new
        {
            Command = await db.FinanceIntegrationWriteCommands.IgnoreQueryFilters().SingleAsync(x => x.Id == action.WriteRequestId),
            AuditTypes = await db.FinanceIntegrationAuditEvents.IgnoreQueryFilters()
                .Where(x => x.CompanyId == seed.CompanyId && x.InternalRecordId == action.WriteRequestId)
                .Select(x => x.EventType)
                .ToListAsync()
        });

        Assert.Equal(FinanceIntegrationWriteCommandRecordStatuses.Executed, persisted.Command.Status);
        Assert.Equal(200, persisted.Command.ResponseStatusCode);
        Assert.Contains("INV-1001", persisted.Command.SafeResponseSummary);
        Assert.Contains("approved_write", persisted.AuditTypes);
        Assert.Contains("write_execution_started", persisted.AuditTypes);
        Assert.Contains("write_execution_succeeded", persisted.AuditTypes);
    }

    [Theory]
    [InlineData("reject", FinanceIntegrationWriteCommandRecordStatuses.Rejected)]
    [InlineData("expire", FinanceIntegrationWriteCommandRecordStatuses.Expired)]
    public async Task Rejected_or_expired_outbound_approval_never_sends_fortnox_request(string terminalAction, string expectedStatus)
    {
        var fakeApi = new FakeFortnoxApiClient();
        await using var factory = CreateFactory(apiClient: fakeApi);
        var seed = await SeedTenantAsync(factory, CompanyMembershipRole.Owner);
        var connectionId = await SeedFinanceIntegrationConnectionAsync(factory, seed);
        var action = await SeedPendingOutboundActionAsync(factory, seed, connectionId);
        using var client = CreateAuthenticatedClient(factory, seed);

        if (terminalAction == "reject")
        {
            var decision = await client.PostAsJsonAsync(
                $"/api/companies/{seed.CompanyId:D}/approvals/{action.ApprovalId:D}/decisions",
                new { decision = "reject", stepId = action.StepId, comment = "Do not send." });
            decision.EnsureSuccessStatusCode();
        }
        else
        {
            await ExecuteDbAsync(factory, async db =>
            {
                var approval = await db.ApprovalRequests.IgnoreQueryFilters().Include(x => x.Steps).SingleAsync(x => x.Id == action.ApprovalId);
                var command = await db.FinanceIntegrationWriteCommands.IgnoreQueryFilters().SingleAsync(x => x.Id == action.WriteRequestId);
                approval.MarkExpired("Approval request expired.");
                command.MarkExpired(Now.AddHours(1));
                await db.SaveChangesAsync();
                return true;
            });
        }

        var execute = await client.PostAsync(
            $"/api/companies/{seed.CompanyId:D}/finance/integrations/fortnox/outbound-actions/{action.WriteRequestId:D}/execute",
            null);

        Assert.Equal(HttpStatusCode.OK, execute.StatusCode);
        Assert.Empty(fakeApi.WriteRequests);

        var persisted = await ExecuteDbAsync(factory, async db => new
        {
            Command = await db.FinanceIntegrationWriteCommands.IgnoreQueryFilters().SingleAsync(x => x.Id == action.WriteRequestId),
            Audit = await db.FinanceIntegrationAuditEvents.IgnoreQueryFilters().SingleAsync(x =>
                x.CompanyId == seed.CompanyId &&
                x.InternalRecordId == action.WriteRequestId &&
                x.EventType == "write_skipped")
        });

        Assert.Equal(expectedStatus, persisted.Command.Status);
        Assert.Equal(FinanceIntegrationAuditOutcomes.Skipped, persisted.Audit.Outcome);
        Assert.Contains("not approved", persisted.Audit.Summary!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Successful_supplier_invoice_registration_completes_the_bill_review()
    {
        var fakeApi = new FakeFortnoxApiClient
        {
            NextPostResponse = JsonNode.Parse("""{"SupplierInvoice":{"GivenNumber":"77"}}""")
        };
        await using var factory = CreateFactory(apiClient: fakeApi);
        var seed = await SeedTenantAsync(factory, CompanyMembershipRole.Owner);
        var connectionId = await SeedFinanceIntegrationConnectionAsync(factory, seed);
        var billId = Guid.NewGuid();
        var writeRequestId = Guid.NewGuid();
        var approvalId = Guid.NewGuid();
        var payload = new JsonObject
        {
            ["SupplierInvoice"] = new JsonObject
            {
                ["SupplierNumber"] = "S-77",
                ["InvoiceNumber"] = "VC-TEST-77",
                ["Total"] = 10_000m
            }
        };

        await ExecuteDbAsync(factory, async db =>
        {
            db.DetectedBills.Add(new DetectedBill(
                billId,
                seed.CompanyId,
                "Prosa Test Services AB",
                "559123-4567",
                "VC-TEST-77",
                Now.Date,
                Now.Date.AddDays(30),
                "SEK",
                10_000m,
                2_000m,
                "VC2026077",
                null,
                null,
                null,
                null,
                0.95m,
                "high",
                "valid",
                "completed",
                requiresReview: false,
                isEligibleForApprovalProposal: true,
                validationStatusPersisted: true,
                "[]",
                "email-77",
                null,
                validationStatusPersistedAtUtc: Now,
                createdUtc: Now,
                updatedUtc: Now));
            db.FinanceBillReviewStates.Add(new FinanceBillReviewState(
                Guid.NewGuid(),
                seed.CompanyId,
                billId,
                FinanceBillInboxStatuses.Approved,
                "Approved supplier bill.",
                Now,
                Now));

            var command = new FinanceIntegrationWriteCommandRecord(
                writeRequestId,
                seed.CompanyId,
                connectionId,
                seed.UserId,
                FinanceIntegrationWriteCommandTypes.InvoiceExport,
                "POST",
                "supplierinvoices",
                "Prosa Test Services AB",
                FortnoxWritePayloadSanitizer.CreateSummary(payload),
                FortnoxWritePayloadSanitizer.CreatePayloadHash(payload),
                FortnoxWritePayloadSanitizer.CreateSanitizedJson(payload),
                $"finance-bill-inbox:{billId:N}:fortnox-registration",
                Now);
            var approval = ApprovalRequest.CreateForTarget(
                approvalId,
                seed.CompanyId,
                ApprovalTargetEntityType.FinanceIntegrationWrite,
                writeRequestId,
                "user",
                seed.UserId,
                "finance_integration_write",
                new Dictionary<string, JsonNode?>
                {
                    ["provider"] = JsonValue.Create(FinanceIntegrationProviderKeys.Fortnox),
                    ["direction"] = JsonValue.Create("outbound"),
                    ["payloadSummary"] = JsonValue.Create(command.PayloadSummary)
                },
                CompanyMembershipRole.Owner.ToStorageValue(),
                null,
                []);
            var stepId = approval.CurrentActionableStep!.Id;
            approval.ApproveCurrentStep(stepId, seed.UserId, "Approved.");
            command.AttachApproval(approval.Id, Now);
            command.MarkApproved(approval.Id, seed.UserId, Now);
            db.FinanceIntegrationWriteCommands.Add(command);
            db.ApprovalRequests.Add(approval);
            await db.SaveChangesAsync();
            return true;
        });

        var result = await ExecuteScopedAsync(factory, seed.CompanyId, provider =>
            provider.GetRequiredService<IFortnoxOutboundActionExecutor>().ExecuteApprovedAsync(
                seed.CompanyId,
                writeRequestId,
                CancellationToken.None));

        Assert.True(result.Executed);
        var persisted = await ExecuteDbAsync(factory, async db => new
        {
            State = await db.FinanceBillReviewStates
                .IgnoreQueryFilters()
                .SingleAsync(x => x.CompanyId == seed.CompanyId && x.DetectedBillId == billId),
            Action = await db.FinanceBillReviewActions
                .IgnoreQueryFilters()
                .SingleAsync(x => x.CompanyId == seed.CompanyId && x.DetectedBillId == billId && x.Action == "fortnox_registered"),
            AuditCount = await db.AuditEvents
                .IgnoreQueryFilters()
                .CountAsync(x =>
                    x.CompanyId == seed.CompanyId &&
                    x.Action == "finance.bill_inbox.fortnox_registered" &&
                    x.TargetId == billId.ToString("D"))
        });

        Assert.Equal(FinanceBillInboxStatuses.SentToPaymentExported, persisted.State.Status);
        Assert.Equal(FinanceBillInboxStatuses.Approved, persisted.Action.PriorStatus);
        Assert.Equal(FinanceBillInboxStatuses.SentToPaymentExported, persisted.Action.NewStatus);
        Assert.Equal(1, persisted.AuditCount);
    }

    [Fact]
    public async Task Outbound_execution_failure_persists_safe_summary_and_retry_metadata()
    {
        var fakeApi = new FakeFortnoxApiClient
        {
            NextException = new FortnoxApiException(
                "Fortnox rejected the approved accounting-system action.",
                HttpStatusCode.BadRequest,
                "validation",
                "200031",
                "raw provider payload contained access_token secret")
        };
        await using var factory = CreateFactory(apiClient: fakeApi);
        var seed = await SeedTenantAsync(factory, CompanyMembershipRole.Owner);
        var connectionId = await SeedFinanceIntegrationConnectionAsync(factory, seed);
        var action = await SeedApprovedOutboundActionAsync(factory, seed, connectionId, commandType: FinanceIntegrationWriteCommandTypes.Payment);

        var result = await ExecuteScopedAsync(factory, seed.CompanyId, provider =>
            provider.GetRequiredService<IFortnoxOutboundActionExecutor>().ExecuteApprovedAsync(seed.CompanyId, action.WriteRequestId, CancellationToken.None));

        Assert.False(result.Executed);
        Assert.Contains("rejected", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("access_token", result.Summary, StringComparison.OrdinalIgnoreCase);

        var persisted = await ExecuteDbAsync(factory, async db => new
        {
            Command = await db.FinanceIntegrationWriteCommands.IgnoreQueryFilters().SingleAsync(x => x.Id == action.WriteRequestId),
            FailedAudit = await db.FinanceIntegrationAuditEvents.IgnoreQueryFilters().SingleAsync(x =>
                x.CompanyId == seed.CompanyId &&
                x.InternalRecordId == action.WriteRequestId &&
                x.EventType == "write_execution_failed")
        });

        Assert.Equal(FinanceIntegrationWriteCommandRecordStatuses.Failed, persisted.Command.Status);
        Assert.Equal("validation", persisted.Command.FailureCategory);
        Assert.Equal(400, persisted.Command.ResponseStatusCode);
        Assert.False(persisted.Command.RetrySupported);
        Assert.Equal(FinanceIntegrationWriteRetryPolicyValues.None, persisted.Command.RetryPolicy);
        Assert.DoesNotContain("access_token", persisted.Command.SafeFailureSummary!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(FinanceIntegrationAuditOutcomes.Failed, persisted.FailedAudit.Outcome);
    }

    [Fact]
    public async Task Approved_supplier_write_reopens_after_reconnect_preflight_failure()
    {
        await using var factory = CreateFactory(apiClient: new FakeFortnoxApiClient());
        var seed = await SeedTenantAsync(factory, CompanyMembershipRole.Owner);
        var connectionId = await SeedFinanceIntegrationConnectionAsync(factory, seed);
        var action = await SeedApprovedOutboundActionAsync(
            factory,
            seed,
            connectionId,
            commandType: FinanceIntegrationWriteCommandTypes.SupplierMasterData);

        await ExecuteDbAsync(factory, async db =>
        {
            var command = await db.FinanceIntegrationWriteCommands
                .IgnoreQueryFilters()
                .SingleAsync(x => x.Id == action.WriteRequestId);
            command.MarkExecutionStarted(Now.AddMinutes(1));
            command.MarkFailed(
                "not_retryable",
                "Fortnox needs to be reconnected.",
                null,
                Now.AddMinutes(2));
            await db.SaveChangesAsync();
            return true;
        });

        var result = await ExecuteScopedAsync(factory, seed.CompanyId, async provider =>
        {
            SetCurrentUser(provider, seed.UserId);
            var db = provider.GetRequiredService<VirtualCompanyDbContext>();
            var persisted = await db.FinanceIntegrationWriteCommands
                .IgnoreQueryFilters()
                .AsNoTracking()
                .SingleAsync(x => x.Id == action.WriteRequestId);
            var service = provider.GetRequiredService<IFinanceIntegrationWriteCommandService>();
            return await service.EnsureApprovedForExecutionAsync(
                ToWriteCommand(persisted, action.ApprovalId),
                CancellationToken.None);
        });

        Assert.True(result.CanExecute);
        Assert.Equal(FinanceIntegrationWriteCommandRecordStatuses.Executing, result.Status);

        var recovered = await ExecuteDbAsync(factory, async db => new
        {
            Command = await db.FinanceIntegrationWriteCommands
                .IgnoreQueryFilters()
                .SingleAsync(x => x.Id == action.WriteRequestId),
            RecoveryAudit = await db.FinanceIntegrationAuditEvents
                .IgnoreQueryFilters()
                .SingleAsync(x =>
                    x.CompanyId == seed.CompanyId &&
                    x.InternalRecordId == action.WriteRequestId &&
                    x.EventType == "write_retry_after_reconnect")
        });
        Assert.Equal(FinanceIntegrationWriteCommandRecordStatuses.Executing, recovered.Command.Status);
        Assert.Equal(2, recovered.Command.ExecutionAttemptCount);
        Assert.Null(recovered.Command.FailureCategory);
        Assert.Null(recovered.Command.SafeFailureSummary);
        Assert.Equal(FinanceIntegrationAuditOutcomes.Succeeded, recovered.RecoveryAudit.Outcome);
    }

    [Fact]
    public async Task Approved_supplier_write_does_not_reopen_after_provider_validation_failure()
    {
        await using var factory = CreateFactory(apiClient: new FakeFortnoxApiClient());
        var seed = await SeedTenantAsync(factory, CompanyMembershipRole.Owner);
        var connectionId = await SeedFinanceIntegrationConnectionAsync(factory, seed);
        var action = await SeedApprovedOutboundActionAsync(
            factory,
            seed,
            connectionId,
            commandType: FinanceIntegrationWriteCommandTypes.SupplierMasterData);

        await ExecuteDbAsync(factory, async db =>
        {
            var command = await db.FinanceIntegrationWriteCommands
                .IgnoreQueryFilters()
                .SingleAsync(x => x.Id == action.WriteRequestId);
            command.MarkExecutionStarted(Now.AddMinutes(1));
            command.MarkFailed(
                "validation",
                "Fortnox rejected the supplier.",
                400,
                Now.AddMinutes(2));
            await db.SaveChangesAsync();
            return true;
        });

        var exception = await Assert.ThrowsAsync<FortnoxApiException>(() =>
            ExecuteScopedAsync(factory, seed.CompanyId, async provider =>
            {
                SetCurrentUser(provider, seed.UserId);
                var db = provider.GetRequiredService<VirtualCompanyDbContext>();
                var persisted = await db.FinanceIntegrationWriteCommands
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .SingleAsync(x => x.Id == action.WriteRequestId);
                var service = provider.GetRequiredService<IFinanceIntegrationWriteCommandService>();
                return await service.EnsureApprovedForExecutionAsync(
                    ToWriteCommand(persisted, action.ApprovalId),
                    CancellationToken.None);
            }));

        Assert.Equal("not_retryable", exception.Category);
        Assert.Contains("rejected", exception.SafeMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unexpected_outbound_failure_does_not_leave_command_executing()
    {
        var fakeApi = new FakeFortnoxApiClient
        {
            NextException = new InvalidOperationException("Local token protection key is unavailable.")
        };
        await using var factory = CreateFactory(apiClient: fakeApi);
        var seed = await SeedTenantAsync(factory, CompanyMembershipRole.Owner);
        var connectionId = await SeedFinanceIntegrationConnectionAsync(factory, seed);
        var action = await SeedApprovedOutboundActionAsync(
            factory,
            seed,
            connectionId,
            commandType: FinanceIntegrationWriteCommandTypes.SupplierMasterData);

        var result = await ExecuteScopedAsync(factory, seed.CompanyId, provider =>
            provider.GetRequiredService<IFortnoxOutboundActionExecutor>().ExecuteApprovedAsync(
                seed.CompanyId,
                action.WriteRequestId,
                CancellationToken.None));

        Assert.False(result.Executed);
        Assert.DoesNotContain("token protection", result.Summary, StringComparison.OrdinalIgnoreCase);

        var persisted = await ExecuteDbAsync(factory, async db =>
            await db.FinanceIntegrationWriteCommands
                .IgnoreQueryFilters()
                .SingleAsync(x => x.Id == action.WriteRequestId));

        Assert.Equal(FinanceIntegrationWriteCommandRecordStatuses.Failed, persisted.Status);
        Assert.Equal(nameof(InvalidOperationException), persisted.FailureCategory);
        Assert.NotNull(persisted.FailedUtc);
        Assert.DoesNotContain("token protection", persisted.SafeFailureSummary!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "validation", false, "could not process")]
    [InlineData(HttpStatusCode.Unauthorized, "authorization", true, "Reconnect Fortnox")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "transient", false, "temporarily unavailable")]
    public void Api_failure_translation_returns_safe_plain_english_messages(
        HttpStatusCode statusCode,
        string category,
        bool requiresReconnect,
        string expectedFragment)
    {
        var translator = new DefaultFortnoxErrorTranslator();
        var safeMessage = translator.Translate(new FortnoxErrorTranslationContext(
            statusCode,
            category,
            "raw-error-code",
            "provider payload access_token=secret refresh_token=secret"));

        Assert.Contains(expectedFragment, safeMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("access_token", safeMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refresh_token", safeMessage, StringComparison.OrdinalIgnoreCase);

        var exception = new FortnoxApiException(
            safeMessage,
            statusCode,
            category,
            requiresReconnect: requiresReconnect,
            isTransient: statusCode == HttpStatusCode.ServiceUnavailable);

        Assert.Equal(requiresReconnect, exception.RequiresReconnect);
        Assert.Equal(statusCode == HttpStatusCode.ServiceUnavailable, exception.IsTransient);
    }

    [Fact]
    public async Task Finance_settings_api_reflects_connection_approval_and_outbound_status_without_internal_labels()
    {
        await using var factory = CreateFactory();
        var seed = await SeedTenantAsync(factory, CompanyMembershipRole.Owner);
        var connectionId = await SeedFortnoxConnectionAsync(factory, seed, accessTokenExpiresUtc: Now.AddHours(1));
        var financeConnectionId = await SeedFinanceIntegrationConnectionAsync(factory, seed);
        var pending = await SeedPendingOutboundActionAsync(factory, seed, financeConnectionId);
        using var client = CreateAuthenticatedClient(factory, seed);

        var status = await client.GetFromJsonAsync<FinanceIntegrationConnectionStatusResult>(
            $"/api/companies/{seed.CompanyId:D}/finance/integrations/fortnox/status");
        var history = await client.GetFromJsonAsync<FinanceIntegrationSyncHistoryResult>(
            $"/api/companies/{seed.CompanyId:D}/finance/integrations/fortnox/sync-history");

        Assert.NotNull(status);
        Assert.True(status!.IsConnected);
        Assert.Equal(connectionId, status.ConnectionId);
        Assert.Equal(FortnoxConnectionStatusValues.Connected, status.ConnectionStatus);
        Assert.Null(status.LastErrorSummary);
        Assert.NotNull(history);
        Assert.Equal(seed.CompanyId, history!.CompanyId);

        var approval = await ExecuteDbAsync(factory, db => db.ApprovalRequests.IgnoreQueryFilters().SingleAsync(x => x.Id == pending.ApprovalId));
        Assert.Equal(ApprovalRequestStatus.Pending, approval.Status);
        Assert.Equal(ApprovalTargetEntityType.FinanceIntegrationWrite.ToStorageValue(), approval.TargetEntityType);
        Assert.Equal(pending.WriteRequestId, approval.TargetEntityId);
    }

    [Fact]
    public async Task Ef_core_model_creates_new_fortnox_outbound_schema_in_sqlite()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var dbContext = new VirtualCompanyDbContext(
            new DbContextOptionsBuilder<VirtualCompanyDbContext>()
                .UseSqlite(connection)
                .Options);

        await dbContext.Database.EnsureCreatedAsync();

        Assert.True(await TableExistsAsync(connection, "fortnox_write_commands"));
        Assert.True(await TableExistsAsync(connection, "finance_integration_audit_events"));
        Assert.True(await TableExistsAsync(connection, "finance_integration_connections"));
        Assert.True(await ColumnExistsAsync(connection, "fortnox_write_commands", "approval_id"));
        Assert.True(await ColumnExistsAsync(connection, "fortnox_write_commands", "safe_failure_summary"));
        Assert.True(await ColumnExistsAsync(connection, "fortnox_write_commands", "retry_policy"));
        Assert.True(await ColumnExistsAsync(connection, "fortnox_write_commands", "execution_attempt_count"));
    }

    private static WebApplicationFactory<Program> CreateFactory(
        ScriptedFortnoxHttpHandler? fortnoxHttp = null,
        FakeFortnoxApiClient? apiClient = null)
    {
        var configuration = new Dictionary<string, string?>
        {
            [$"{FortnoxOptions.SectionName}:Enabled"] = "true",
            [$"{FortnoxOptions.SectionName}:ClientId"] = "client-id",
            [$"{FortnoxOptions.SectionName}:ClientSecret"] = "client-secret",
            [$"{FortnoxOptions.SectionName}:RedirectUri"] = "http://localhost/finance/integrations/fortnox/callback",
            [$"{FortnoxOptions.SectionName}:AuthorizationUrl"] = "https://apps.fortnox.test/oauth-v1/auth",
            [$"{FortnoxOptions.SectionName}:TokenUrl"] = "https://apps.fortnox.test/oauth-v1/token",
            [$"{FortnoxOptions.SectionName}:ApiBaseUrl"] = "https://api.fortnox.test/3"
        };

        var baseFactory = new TestWebApplicationFactory(configuration);
        return baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                if (fortnoxHttp is not null)
                {
                    services.AddHttpClient(FortnoxOAuthClient.ClientName)
                        .ConfigurePrimaryHttpMessageHandler(() => fortnoxHttp);
                }

                if (apiClient is not null)
                {
                    services.RemoveAll<IFortnoxApiClient>();
                    services.AddSingleton<IFortnoxApiClient>(apiClient);
                }
            });
        });
    }

    private static async Task<TenantSeed> SeedTenantAsync(
        WebApplicationFactory<Program> factory,
        CompanyMembershipRole role,
        string suffix = "owner")
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var subject = $"fortnox-{suffix}-{Guid.NewGuid():N}";
        var email = $"{subject}@example.com";
        var displayName = "Fortnox Test User";

        await ExecuteDbAsync(factory, async db =>
        {
            db.Users.Add(new User(userId, email, displayName, "dev-header", subject));
            db.Companies.Add(new Company(companyId, $"Fortnox {suffix} company"));
            db.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, userId, role, CompanyMembershipStatus.Active));
            await db.SaveChangesAsync();
            return true;
        });

        return new TenantSeed(companyId, userId, subject, email, displayName);
    }

    private static async Task<Guid> SeedFortnoxConnectionAsync(
        WebApplicationFactory<Program> factory,
        TenantSeed seed,
        DateTime accessTokenExpiresUtc)
    {
        return await ExecuteScopedAsync(factory, seed.CompanyId, async provider =>
        {
            var store = provider.GetRequiredService<IFortnoxTokenStore>();
            var snapshot = await store.UpsertConnectedAsync(
                seed.CompanyId,
                seed.UserId,
                new FortnoxOAuthTokenResult(
                    "stored-access-token",
                    "stored-refresh-token",
                    accessTokenExpiresUtc,
                    ["bookkeeping", "invoice"],
                    "provider-tenant"),
                Now,
                CancellationToken.None);
            return snapshot.ConnectionId;
        });
    }

    private static async Task<Guid> SeedFinanceIntegrationConnectionAsync(WebApplicationFactory<Program> factory, TenantSeed seed)
    {
        var connectionId = Guid.NewGuid();
        await ExecuteDbAsync(factory, async db =>
        {
            if (!await db.FortnoxConnections.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == seed.CompanyId))
            {
                var fortnoxConnection = new FortnoxConnection(connectionId, seed.CompanyId, seed.UserId, Now);
                fortnoxConnection.StoreEncryptedTokens(
                    "test-access-token",
                    "test-refresh-token",
                    Now.AddHours(1),
                    ["article", "bookkeeping", "companyinformation", "customer", "invoice", "payment", "project", "supplier", "supplierinvoice"],
                    "test-provider-tenant",
                    Now);
                db.FortnoxConnections.Add(fortnoxConnection);
            }
            db.FinanceIntegrationConnections.Add(new FinanceIntegrationConnection(
                connectionId,
                seed.CompanyId,
                FinanceIntegrationProviderKeys.Fortnox,
                FinanceIntegrationConnectionStatuses.Connected,
                seed.UserId,
                Now));
            await db.SaveChangesAsync();
            return true;
        });

        return connectionId;
    }

    private static async Task<OutboundActionSeed> SeedPendingOutboundActionAsync(
        WebApplicationFactory<Program> factory,
        TenantSeed seed,
        Guid connectionId,
        string commandType = FinanceIntegrationWriteCommandTypes.InvoiceExport)
    {
        var writeRequestId = Guid.NewGuid();
        var approvalId = Guid.NewGuid();
        Guid stepId = Guid.Empty;

        await ExecuteDbAsync(factory, async db =>
        {
            var command = CreateWriteCommand(writeRequestId, seed.CompanyId, connectionId, seed.UserId, commandType);
            var approval = ApprovalRequest.CreateForTarget(
                approvalId,
                seed.CompanyId,
                ApprovalTargetEntityType.FinanceIntegrationWrite,
                writeRequestId,
                "user",
                seed.UserId,
                "finance_integration_write",
                new Dictionary<string, JsonNode?>
                {
                    ["provider"] = JsonValue.Create(FinanceIntegrationProviderKeys.Fortnox),
                    ["direction"] = JsonValue.Create("outbound"),
                    ["payloadSummary"] = JsonValue.Create(command.PayloadSummary)
                },
                CompanyMembershipRole.Owner.ToStorageValue(),
                null,
                []);
            stepId = approval.CurrentActionableStep!.Id;
            command.AttachApproval(approval.Id, Now);

            db.FinanceIntegrationWriteCommands.Add(command);
            db.ApprovalRequests.Add(approval);
            await db.SaveChangesAsync();
            return true;
        });

        return new OutboundActionSeed(writeRequestId, approvalId, stepId);
    }

    private static async Task<OutboundActionSeed> SeedApprovedOutboundActionAsync(
        WebApplicationFactory<Program> factory,
        TenantSeed seed,
        Guid connectionId,
        string commandType = FinanceIntegrationWriteCommandTypes.InvoiceExport)
    {
        var action = await SeedPendingOutboundActionAsync(factory, seed, connectionId, commandType);
        await ExecuteDbAsync(factory, async db =>
        {
            var approval = await db.ApprovalRequests.IgnoreQueryFilters().Include(x => x.Steps).SingleAsync(x => x.Id == action.ApprovalId);
            var command = await db.FinanceIntegrationWriteCommands.IgnoreQueryFilters().SingleAsync(x => x.Id == action.WriteRequestId);
            approval.ApproveCurrentStep(action.StepId, seed.UserId, "Approved.");
            command.MarkApproved(approval.Id, seed.UserId, Now);
            await db.SaveChangesAsync();
            return true;
        });

        return action;
    }

    private static FinanceIntegrationWriteCommandRecord CreateWriteCommand(
        Guid writeRequestId,
        Guid companyId,
        Guid connectionId,
        Guid actorUserId,
        string commandType)
    {
        var payload = new JsonObject
        {
            ["Invoice"] = new JsonObject
            {
                ["CustomerNumber"] = "1001",
                ["Total"] = 1200
            }
        };

        return new FinanceIntegrationWriteCommandRecord(
            writeRequestId,
            companyId,
            connectionId,
            actorUserId,
            commandType,
            "POST",
            "invoices",
            "Fortnox test company",
            FortnoxWritePayloadSanitizer.CreateSummary(payload),
            FortnoxWritePayloadSanitizer.CreatePayloadHash(payload),
            FortnoxWritePayloadSanitizer.CreateSanitizedJson(payload),
            "fortnox-outbound-test",
            Now);
    }

    private static FinanceIntegrationWriteCommand ToWriteCommand(
        FinanceIntegrationWriteCommandRecord command,
        Guid approvalId) =>
        new(
            FinanceIntegrationProviderKeys.Fortnox,
            command.CompanyId,
            command.ConnectionId,
            command.ActorUserId,
            command.CommandType,
            command.HttpMethod,
            command.Path,
            command.TargetCompany,
            command.PayloadSummary,
            command.PayloadHash,
            new FinanceIntegrationWritePayload(command.SanitizedPayloadJson),
            command.Id,
            command.CorrelationId,
            approvalId);

    private static void SetCurrentUser(IServiceProvider provider, Guid userId)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(CurrentUserClaimTypes.UserId, userId.ToString("D"))
        ], "tests");
        provider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity)
        };
    }

    private static HttpClient CreateAuthenticatedClient(WebApplicationFactory<Program> factory, TenantSeed seed)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, seed.Subject);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, seed.Email);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, seed.DisplayName);
        client.DefaultRequestHeaders.Add(CompanyContextResolutionMiddleware.CompanyHeaderName, seed.CompanyId.ToString("D"));
        return client;
    }

    private static async Task<T> ExecuteScopedAsync<T>(
        WebApplicationFactory<Program> factory,
        Guid companyId,
        Func<IServiceProvider, Task<T>> callback)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>().SetCompanyId(companyId);
        return await callback(scope.ServiceProvider);
    }

    private static async Task<T> ExecuteDbAsync<T>(WebApplicationFactory<Program> factory, Func<VirtualCompanyDbContext, Task<T>> callback)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        await db.Database.EnsureCreatedAsync();
        return await callback(db);
    }

    private static async Task<bool> TableExistsAsync(SqliteConnection connection, string tableName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $tableName;";
        command.Parameters.AddWithValue("$tableName", tableName);
        return Convert.ToInt64(await command.ExecuteScalarAsync()) > 0;
    }

    private static async Task<bool> ColumnExistsAsync(SqliteConnection connection, string tableName, string columnName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private sealed record TenantSeed(Guid CompanyId, Guid UserId, string Subject, string Email, string DisplayName);
    private sealed record OutboundActionSeed(Guid WriteRequestId, Guid ApprovalId, Guid StepId);
    private sealed record StartConnectionResponse(string AuthorizationUrl, DateTime ExpiresUtc);
    private sealed record OAuthCompletionResponse(string ProviderKey, Guid ConnectionId, Guid CompanyId, string Status, Uri? ReturnUri);
    private sealed record ProblemResponse(string Title, string Detail, int? Status);

    private sealed class ScriptedFortnoxHttpHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new();

        public List<CapturedHttpRequest> Requests { get; } = [];

        public void EnqueueJson(HttpStatusCode statusCode, object payload)
        {
            _responses.Enqueue(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            });
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new CapturedHttpRequest(
                request.Method,
                request.RequestUri?.AbsolutePath ?? string.Empty,
                request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));
            return _responses.Count == 0 ? new HttpResponseMessage(HttpStatusCode.OK) : _responses.Dequeue();
        }
    }

    private sealed record CapturedHttpRequest(HttpMethod Method, string Path, string Body);

    private sealed class FakeFortnoxApiClient : IFortnoxApiClient
    {
        public List<FortnoxAccount> Accounts { get; init; } = [];
        public List<FortnoxCustomer> Customers { get; init; } = [];
        public List<FortnoxSupplierInvoice> SupplierInvoices { get; set; } = [];
        public JsonNode? NextPostResponse { get; set; } = JsonNode.Parse("""{"Result":{"id":"ok"}}""");
        public Exception? NextException { get; set; }
        public List<string> PageCalls { get; } = [];
        public List<CapturedFortnoxWrite> WriteRequests { get; } = [];

        public Task<FortnoxCompanyInformation> GetCompanyInformationAsync(FortnoxRequestContext context, CancellationToken cancellationToken) =>
            Task.FromResult(new FortnoxCompanyInformation { CompanyName = "Fortnox test company" });

        public Task<FortnoxPagedResponse<FortnoxCustomer>> GetCustomersAsync(FortnoxRequestContext context, FortnoxPageOptions? options, CancellationToken cancellationToken)
        {
            PageCalls.Add("customers");
            return Task.FromResult(Page(Customers));
        }

        public Task<FortnoxPagedResponse<FortnoxSupplier>> GetSuppliersAsync(FortnoxRequestContext context, FortnoxPageOptions? options, CancellationToken cancellationToken)
        {
            PageCalls.Add("suppliers");
            return Task.FromResult(Page(Array.Empty<FortnoxSupplier>()));
        }

        public Task<FortnoxPagedResponse<FortnoxInvoice>> GetInvoicesAsync(FortnoxRequestContext context, FortnoxPageOptions? options, CancellationToken cancellationToken)
        {
            PageCalls.Add("invoices");
            return Task.FromResult(Page(Array.Empty<FortnoxInvoice>()));
        }

        public Task<FortnoxPagedResponse<FortnoxInvoicePayment>> GetInvoicePaymentsAsync(FortnoxRequestContext context, FortnoxPageOptions? options, CancellationToken cancellationToken)
        {
            PageCalls.Add("invoice_payments");
            return Task.FromResult(Page(Array.Empty<FortnoxInvoicePayment>()));
        }

        public Task<FortnoxPagedResponse<FortnoxSupplierInvoice>> GetSupplierInvoicesAsync(FortnoxRequestContext context, FortnoxPageOptions? options, CancellationToken cancellationToken)
        {
            PageCalls.Add("supplier_invoices");
            return Task.FromResult(Page(SupplierInvoices));
        }

        public Task<FortnoxPagedResponse<FortnoxSupplierInvoicePayment>> GetSupplierInvoicePaymentsAsync(FortnoxRequestContext context, FortnoxPageOptions? options, CancellationToken cancellationToken)
        {
            PageCalls.Add("supplier_invoice_payments");
            return Task.FromResult(Page(Array.Empty<FortnoxSupplierInvoicePayment>()));
        }

        public Task<FortnoxPagedResponse<FortnoxVoucher>> GetVouchersAsync(FortnoxRequestContext context, FortnoxPageOptions? options, CancellationToken cancellationToken)
        {
            PageCalls.Add("vouchers");
            return Task.FromResult(Page(Array.Empty<FortnoxVoucher>()));
        }

        public Task<FortnoxPagedResponse<FortnoxAccount>> GetAccountsAsync(FortnoxRequestContext context, FortnoxPageOptions? options, CancellationToken cancellationToken)
        {
            PageCalls.Add("accounts");
            return Task.FromResult(Page(Accounts));
        }

        public Task<FortnoxPagedResponse<FortnoxArticle>> GetArticlesAsync(FortnoxRequestContext context, FortnoxPageOptions? options, CancellationToken cancellationToken)
        {
            PageCalls.Add("articles");
            return Task.FromResult(Page(Array.Empty<FortnoxArticle>()));
        }

        public Task<FortnoxPagedResponse<FortnoxProject>> GetProjectsAsync(FortnoxRequestContext context, FortnoxPageOptions? options, CancellationToken cancellationToken)
        {
            PageCalls.Add("projects");
            return Task.FromResult(Page(Array.Empty<FortnoxProject>()));
        }

        public Task<TResponse?> GetAsync<TResponse>(FortnoxRequestContext context, string path, FortnoxPageOptions? options, CancellationToken cancellationToken) =>
            Task.FromResult(default(TResponse));

        public Task<TResponse?> PostAsync<TRequest, TResponse>(FortnoxRequestContext context, string path, TRequest payload, CancellationToken cancellationToken)
        {
            if (NextException is not null)
            {
                var exception = NextException;
                NextException = null;
                throw exception;
            }

            WriteRequests.Add(new CapturedFortnoxWrite("POST", path, context, JsonSerializer.SerializeToNode(payload)));
            return Task.FromResult(NextPostResponse is null ? default : NextPostResponse.Deserialize<TResponse>());
        }

        public Task<TResponse?> PostDirectAsync<TRequest, TResponse>(FortnoxRequestContext context, string path, TRequest payload, CancellationToken cancellationToken) =>
            PostAsync<TRequest, TResponse>(context, path, payload, cancellationToken);

        public Task<TResponse?> PutAsync<TRequest, TResponse>(FortnoxRequestContext context, string path, TRequest payload, CancellationToken cancellationToken)
        {
            WriteRequests.Add(new CapturedFortnoxWrite("PUT", path, context, JsonSerializer.SerializeToNode(payload)));
            return Task.FromResult(default(TResponse));
        }

        public Task DeleteAsync(FortnoxRequestContext context, string path, CancellationToken cancellationToken)
        {
            WriteRequests.Add(new CapturedFortnoxWrite("DELETE", path, context, null));
            return Task.CompletedTask;
        }

        private static FortnoxPagedResponse<T> Page<T>(IReadOnlyCollection<T> items) =>
            new(items.ToList(), new FortnoxPageMetadata(1, 1, items.Count, 100));
    }

    private sealed record CapturedFortnoxWrite(string Method, string Path, FortnoxRequestContext Context, JsonNode? Payload);
}
