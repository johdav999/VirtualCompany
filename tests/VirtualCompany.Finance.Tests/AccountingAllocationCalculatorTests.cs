using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using AllocationKindValues = VirtualCompany.Domain.Entities.AccountingAllocationKindValues;

namespace VirtualCompany.Finance.Tests;

public sealed class AccountingAllocationCalculatorTests
{
    [Fact]
    public void Percentage_allocation_preserves_totals_with_deterministic_largest_remainder_rounding()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var third = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var lines = new[]
        {
            new AccountingAllocationCalculationLine(1, first, "A", AllocationKindValues.Percentage, 33.333333m),
            new AccountingAllocationCalculationLine(2, second, "B", AllocationKindValues.Percentage, 33.333333m),
            new AccountingAllocationCalculationLine(3, third, "C", AllocationKindValues.Percentage, 33.333334m)
        };

        var result = AccountingAllocationCalculator.Calculate(templateId, versionId, 4, 100m, "sek", 2, 50m, lines);
        var replay = AccountingAllocationCalculator.Calculate(templateId, versionId, 4, 100m, "SEK", 2, 50m, lines);

        Assert.True(result.IsValid);
        Assert.Equal(100m, result.Dto.AllocatedAmount);
        Assert.Equal(0m, result.Dto.Difference);
        Assert.True(result.Dto.RequiresApproval);
        Assert.Equal(new[] { 33.33m, 33.33m, 33.34m }, result.Dto.Lines.Select(line => line.RoundedAmount));
        Assert.Equal(result.Dto.Lines, replay.Dto.Lines);
    }

    [Fact]
    public void Fixed_allocation_handles_negative_sources_and_rejects_non_preserving_templates()
    {
        var templateId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        var valid = AccountingAllocationCalculator.Calculate(templateId, versionId, 1, -10m, "EUR", 2, null,
        [
            new(1, first, "A", AllocationKindValues.Fixed, 4m),
            new(2, second, "B", AllocationKindValues.Fixed, 6m)
        ]);
        var invalid = AccountingAllocationCalculator.Calculate(templateId, versionId, 1, 10m, "EUR", 2, null,
        [
            new(1, first, "A", AllocationKindValues.Fixed, 4m),
            new(2, second, "B", AllocationKindValues.Fixed, 5m)
        ]);

        Assert.True(valid.IsValid);
        Assert.Equal(new[] { -4m, -6m }, valid.Dto.Lines.Select(line => line.RoundedAmount));
        Assert.False(invalid.IsValid);
        Assert.Contains(invalid.Dto.Issues, issue => issue.ReasonCode == AccountingDimensionReasonCodes.AllocationInvalid);
    }

    [Fact]
    public void Dimension_lifecycle_and_hierarchy_invariants_are_explicit()
    {
        var companyId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var typeId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var now = new DateTime(2026, 8, 29, 10, 0, 0, DateTimeKind.Utc);

        Assert.Throws<ArgumentOutOfRangeException>(() => new AccountingDimensionType(typeId, companyId, "cost center",
            "Cost center", null, true, AccountingDimensionStatusValues.Active, new DateOnly(2026, 2, 1),
            new DateOnly(2026, 1, 31), actorId, now));

        var member = new AccountingDimensionMember(memberId, companyId, typeId, null, "se north", "North",
            AccountingDimensionStatusValues.Active, new DateOnly(2026, 1, 1), null, actorId, now);
        Assert.Equal("SE NORTH", member.Code);
        Assert.Throws<ArgumentException>(() => member.Apply(member.Id, "North", AccountingDimensionStatusValues.Active,
            new DateOnly(2026, 1, 1), null, now.AddMinutes(1)));
    }

    [Fact]
    public async Task Dimension_configuration_uses_optimistic_concurrency()
    {
        var companyId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var dimensionId = Guid.NewGuid();
        var now = new DateTime(2026, 8, 29, 10, 0, 0, DateTimeKind.Utc);
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite(connection).Options;
        var accessor = new TestCompanyContextAccessor(companyId, actorId);

        await using (var setup = new VirtualCompanyDbContext(options, accessor))
        {
            await setup.Database.EnsureCreatedAsync();
            setup.Companies.Add(new Company(companyId, "Dimensions company"));
            setup.AccountingDimensionTypes.Add(new AccountingDimensionType(dimensionId, companyId, "cost_center",
                "Cost center", null, true, AccountingDimensionStatusValues.Active, new DateOnly(2026, 1, 1),
                null, actorId, now));
            await setup.SaveChangesAsync();
        }

        await using var firstContext = new VirtualCompanyDbContext(options, accessor);
        await using var secondContext = new VirtualCompanyDbContext(options, accessor);
        var first = await firstContext.AccountingDimensionTypes.SingleAsync(type => type.Id == dimensionId);
        var second = await secondContext.AccountingDimensionTypes.SingleAsync(type => type.Id == dimensionId);
        first.Apply("Cost ownership", null, true, AccountingDimensionStatusValues.Active,
            new DateOnly(2026, 1, 1), null, now.AddMinutes(1));
        second.Apply("Responsibility center", null, true, AccountingDimensionStatusValues.Active,
            new DateOnly(2026, 1, 1), null, now.AddMinutes(2));

        await firstContext.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondContext.SaveChangesAsync());
    }

    private sealed class TestCompanyContextAccessor(Guid companyId, Guid userId) : ICompanyContextAccessor
    {
        public Guid? CompanyId { get; private set; } = companyId;
        public Guid? UserId => userId;
        public bool IsResolved => CompanyId.HasValue;
        public ResolvedCompanyMembershipContext? Membership => null;
        public void SetCompanyId(Guid? resolvedCompanyId) => CompanyId = resolvedCompanyId;
        public void SetCompanyContext(ResolvedCompanyMembershipContext? companyContext) => CompanyId = companyContext?.CompanyId;
    }
}
