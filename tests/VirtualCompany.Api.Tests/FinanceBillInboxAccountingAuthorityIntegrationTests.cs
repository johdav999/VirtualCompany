using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceBillInboxAccountingAuthorityIntegrationTests : IDisposable
{
    private readonly TestWebApplicationFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Internal_ledger_bill_review_hides_and_blocks_fortnox_registration()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var billId = Guid.NewGuid();
        var subject = $"internal-bill-review-{Guid.NewGuid():N}";
        var email = $"{subject}@example.com";
        var now = new DateTime(2026, 9, 3, 8, 0, 0, DateTimeKind.Utc);

        await _factory.SeedAsync(db =>
        {
            db.Companies.Add(new Company(companyId, "Internal Ledger Company"));
            db.Users.Add(new User(userId, email, "Finance Owner", "dev-header", subject));
            db.CompanyMemberships.Add(new CompanyMembership(
                Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));

            var configuration = new AccountingConfiguration(
                Guid.NewGuid(), companyId, "SEK", 1, 1,
                AccountingPolicyPackDefaults.CountryNeutralPackKey,
                AccountingPolicyPackDefaults.CountryNeutralVersion,
                new DateOnly(2026, 1, 1), 2,
                AccountingRoundingModeValues.MidpointToEven,
                userId, now);
            configuration.SetSetupState(AccountingSetupStateValues.Ready, userId, now);
            db.AccountingConfigurations.Add(configuration);

            db.DetectedBills.Add(new DetectedBill(
                billId, companyId, "Example Supplier AB", "559123-4567", "VC-2026-002",
                now, now.AddDays(30), "SEK", 15_000m, 3_000m, "VC2026002",
                null, null, null, null, 0.75m, "medium", "valid", "completed",
                requiresReview: false, isEligibleForApprovalProposal: true,
                validationStatusPersisted: true, "[]", "email-2", null,
                validationStatusPersistedAtUtc: now, createdUtc: now, updatedUtc: now));
            db.FinanceBillReviewStates.Add(new FinanceBillReviewState(
                Guid.NewGuid(), companyId, billId, FinanceBillInboxStatuses.Approved,
                "Approved supplier bill.", now, now));
            return Task.CompletedTask;
        });

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.DisplayNameHeader, "Finance Owner");

        using var detailResponse = await client.GetAsync(
            $"/internal/companies/{companyId:D}/finance/bill-inbox/{billId:D}");
        var detailBody = await detailResponse.Content.ReadAsStringAsync();
        Assert.True(detailResponse.IsSuccessStatusCode, detailBody);
        using var detail = JsonDocument.Parse(detailBody);
        Assert.True(detail.RootElement.GetProperty("usesInternalAccounting").GetBoolean());
        Assert.False(detail.RootElement.GetProperty("canUseFortnoxAccounting").GetBoolean());
        Assert.Equal(JsonValueKind.Null, detail.RootElement.GetProperty("fortnoxRegistration").ValueKind);
        Assert.Contains("Virtual Company is authoritative", detail.RootElement.GetProperty("accountingGuidance").GetString());

        using var registrationResponse = await client.PostAsJsonAsync(
            $"/internal/companies/{companyId:D}/finance/bill-inbox/{billId:D}/fortnox-registration/request",
            new { rationale = "Send this invoice." });
        var registrationBody = await registrationResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.BadRequest, registrationResponse.StatusCode);
        Assert.Contains("Virtual Company is authoritative", registrationBody, StringComparison.Ordinal);

        var commandCount = await _factory.ExecuteDbContextAsync(db =>
            db.FinanceIntegrationWriteCommands.IgnoreQueryFilters()
                .CountAsync(x => x.CompanyId == companyId));
        Assert.Equal(0, commandCount);
    }
}
