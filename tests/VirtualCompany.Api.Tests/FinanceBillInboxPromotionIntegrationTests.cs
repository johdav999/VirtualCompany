using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceBillInboxPromotionIntegrationTests : IDisposable
{
    private readonly TestWebApplicationFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Approval_promotes_detected_bill_once_and_returns_operational_bill_id()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var detectedBillId = Guid.NewGuid();
        var subject = $"bill-promotion-{Guid.NewGuid():N}";
        var email = $"{subject}@example.com";
        var now = new DateTime(2026, 9, 3, 9, 0, 0, DateTimeKind.Utc);

        await _factory.SeedAsync(db =>
        {
            db.Companies.Add(new Company(companyId, "Bill Promotion Company"));
            db.Users.Add(new User(userId, email, "Finance Owner", "dev-header", subject));
            db.CompanyMemberships.Add(new CompanyMembership(
                Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            db.DetectedBills.Add(new DetectedBill(
                detectedBillId, companyId, "Example Company AB", "559123-4567", "VC-EX-2026-002",
                now, now.AddDays(30), "SEK", 15_000m, 3_000m, "VC-EX-2026-002",
                null, null, "SE3550000000054910000003", "ESSESESS", 0.75m, "medium", "valid", "completed",
                requiresReview: false, isEligibleForApprovalProposal: true,
                validationStatusPersisted: true, "[]", "email-promotion", "attachment-promotion",
                validationStatusPersistedAtUtc: now, createdUtc: now, updatedUtc: now));
            return Task.CompletedTask;
        });

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.DisplayNameHeader, "Finance Owner");

        var route = $"/internal/companies/{companyId:D}/finance/bill-inbox/{detectedBillId:D}/approve";
        using var firstResponse = await client.PostAsJsonAsync(route, new { rationale = "Approved after review." });
        var firstBody = await firstResponse.Content.ReadAsStringAsync();
        Assert.True(firstResponse.IsSuccessStatusCode, firstBody);
        using var firstResult = JsonDocument.Parse(firstBody);
        var operationalBillId = firstResult.RootElement.GetProperty("operationalBillId").GetGuid();
        Assert.NotEqual(Guid.Empty, operationalBillId);

        using var secondResponse = await client.PostAsJsonAsync(route, new { rationale = "Safe repeat." });
        var secondBody = await secondResponse.Content.ReadAsStringAsync();
        Assert.True(secondResponse.IsSuccessStatusCode, secondBody);
        using var secondResult = JsonDocument.Parse(secondBody);
        Assert.Equal(operationalBillId, secondResult.RootElement.GetProperty("operationalBillId").GetGuid());

        await _factory.ExecuteDbContextAsync(async db =>
        {
            var promotedBill = await db.FinanceBills
                .IgnoreQueryFilters()
                .SingleAsync(x => x.CompanyId == companyId && x.SourceDetectedBillId == detectedBillId);
            Assert.Equal(operationalBillId, promotedBill.Id);
            Assert.Equal("VC-EX-2026-002", promotedBill.BillNumber);
            Assert.Equal(15_000m, promotedBill.Amount);
            Assert.Equal("SEK", promotedBill.Currency);
            Assert.Equal("approved", promotedBill.Status);
            Assert.Equal(FinanceDocumentPostingStatuses.Draft, promotedBill.PostingStatus);
            Assert.Equal(FinanceSettlementStatuses.Unpaid, promotedBill.SettlementStatus);

            Assert.Equal(1, await db.FinanceBills.IgnoreQueryFilters()
                .CountAsync(x => x.CompanyId == companyId && x.SourceDetectedBillId == detectedBillId));
            Assert.Equal(1, await db.FinanceBillReviewActions.IgnoreQueryFilters()
                .CountAsync(x => x.CompanyId == companyId && x.DetectedBillId == detectedBillId && x.Action == "approve"));
        });

        using var detailResponse = await client.GetAsync(
            $"/internal/companies/{companyId:D}/finance/bill-inbox/{detectedBillId:D}");
        var detailBody = await detailResponse.Content.ReadAsStringAsync();
        Assert.True(detailResponse.IsSuccessStatusCode, detailBody);
        using var detail = JsonDocument.Parse(detailBody);
        Assert.Equal(operationalBillId, detail.RootElement.GetProperty("operationalBillId").GetGuid());
    }

    [Fact]
    public async Task Repeating_approval_repairs_an_approved_detected_bill_without_an_operational_bill()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var detectedBillId = Guid.NewGuid();
        var subject = $"bill-repair-{Guid.NewGuid():N}";
        var email = $"{subject}@example.com";
        var now = new DateTime(2026, 9, 3, 10, 0, 0, DateTimeKind.Utc);

        await _factory.SeedAsync(db =>
        {
            db.Companies.Add(new Company(companyId, "Bill Repair Company"));
            db.Users.Add(new User(userId, email, "Finance Owner", "dev-header", subject));
            db.CompanyMemberships.Add(new CompanyMembership(
                Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            db.DetectedBills.Add(new DetectedBill(
                detectedBillId, companyId, "Historical Supplier AB", null, "HIST-2026-001",
                now, now.AddDays(30), "SEK", 2_500m, 500m, "HIST-2026-001",
                null, null, null, null, 0.95m, "high", "valid", "completed",
                requiresReview: false, isEligibleForApprovalProposal: true,
                validationStatusPersisted: true, "[]", "email-historical", "attachment-historical",
                validationStatusPersistedAtUtc: now, createdUtc: now, updatedUtc: now));
            db.FinanceBillReviewStates.Add(new FinanceBillReviewState(
                Guid.NewGuid(), companyId, detectedBillId, FinanceBillInboxStatuses.Approved,
                "Previously approved supplier bill.", now, now));
            return Task.CompletedTask;
        });

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.DisplayNameHeader, "Finance Owner");

        using var response = await client.PostAsJsonAsync(
            $"/internal/companies/{companyId:D}/finance/bill-inbox/{detectedBillId:D}/approve",
            new { rationale = "Repair approved bill linkage." });
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        using var result = JsonDocument.Parse(body);
        var operationalBillId = result.RootElement.GetProperty("operationalBillId").GetGuid();

        await _factory.ExecuteDbContextAsync(async db =>
        {
            Assert.Equal(1, await db.FinanceBills.IgnoreQueryFilters()
                .CountAsync(x => x.CompanyId == companyId && x.SourceDetectedBillId == detectedBillId));
            Assert.Equal(operationalBillId, await db.FinanceBills.IgnoreQueryFilters()
                .Where(x => x.CompanyId == companyId && x.SourceDetectedBillId == detectedBillId)
                .Select(x => x.Id)
                .SingleAsync());
            Assert.Equal("approved", await db.FinanceBills.IgnoreQueryFilters()
                .Where(x => x.CompanyId == companyId && x.SourceDetectedBillId == detectedBillId)
                .Select(x => x.Status)
                .SingleAsync());
            Assert.Equal(0, await db.FinanceBillReviewActions.IgnoreQueryFilters()
                .CountAsync(x => x.CompanyId == companyId && x.DetectedBillId == detectedBillId));
        });
    }
}
