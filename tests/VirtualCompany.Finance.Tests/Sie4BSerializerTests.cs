using System.Security.Cryptography;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Finance;

namespace VirtualCompany.Finance.Tests;

public sealed class Sie4BSerializerTests
{
    [Fact]
    public void Serializes_deterministic_pc8_type_4_and_round_trips_with_independent_parser()
    {
        var source = GoldenSource();
        var serializer = new Sie4BSerializer();

        var first = serializer.Serialize(source);
        var second = serializer.Serialize(source);
        var parsed = new Sie4BParser().Parse(first.Content);

        Assert.Equal(first.Content, second.Content);
        Assert.Equal("Övningsbolaget AB", parsed.CompanyName);
        Assert.Equal("556016-0680", parsed.OrganisationNumber);
        Assert.Equal(new DateOnly(2026, 1, 1), parsed.FinancialYearStart);
        Assert.Equal(new DateOnly(2026, 12, 31), parsed.FinancialYearEnd);
        Assert.Equal(3, parsed.Accounts.Count);
        Assert.Single(parsed.Vouchers);
        Assert.Equal(3, parsed.Vouchers[0].Transactions.Count);
        Assert.Equal(0m, parsed.Vouchers[0].Transactions.Sum(x => x.Amount));
        Assert.Equal(1250m, first.DebitTotal);
        Assert.Equal(1250m, first.CreditTotal);
        Assert.Equal(64, Convert.ToHexString(SHA256.HashData(first.Content)).Length);
    }

    [Fact]
    public void Independent_parser_reads_the_official_sie_group_type_4_sample()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Sie4B", "SIE4 Exempelfil.SE");

        var parsed = new Sie4BParser().Parse(File.ReadAllBytes(path));

        Assert.Equal("Övningsbolaget AB", parsed.CompanyName);
        Assert.Equal("555555-5555", parsed.OrganisationNumber);
        Assert.True(parsed.Accounts.Count > 100);
        Assert.NotEmpty(parsed.Vouchers);
        Assert.All(parsed.Vouchers, voucher => Assert.Equal(0m, decimal.Round(voucher.Transactions.Sum(x => x.Amount), 2)));
    }

    [Fact]
    public void Blocks_precision_that_sie_cannot_represent_without_rounding()
    {
        var source = GoldenSource() with
        {
            Vouchers =
            [
                GoldenSource().Vouchers[0] with
                {
                    Transactions =
                    [
                        new("1930", 100.001m, new DateOnly(2026, 1, 15), "Payment", new Dictionary<int, string>()),
                        new("3011", -100.001m, new DateOnly(2026, 1, 15), "Sale", new Dictionary<int, string>())
                    ]
                }
            ]
        };

        var exception = Assert.Throws<Sie4BValidationException>(() => new Sie4BSerializer().Serialize(source));

        Assert.Equal(Sie4BReasonCodes.UnsupportedPrecision, exception.ReasonCode);
    }

    [Fact]
    public void Blocks_unmapped_dimensions_instead_of_dropping_them()
    {
        var source = GoldenSource() with
        {
            Dimensions = [new Sie4BDimension(7, "Employee", [new Sie4BObject("E1", "Employee 1")])]
        };

        var exception = Assert.Throws<Sie4BValidationException>(() => new Sie4BSerializer().Serialize(source));

        Assert.Equal(Sie4BReasonCodes.UnsupportedDimension, exception.ReasonCode);
    }

    [Fact]
    public void Blocks_text_that_pc8_cannot_encode_with_a_stable_capability_gap()
    {
        var source = GoldenSource() with
        {
            Company = GoldenSource().Company with { LegalName = "Unsupported 😀 AB" }
        };

        var exception = Assert.Throws<Sie4BValidationException>(() => new Sie4BSerializer().Serialize(source));

        Assert.Equal(Sie4BReasonCodes.UnsupportedCharacter, exception.ReasonCode);
    }

    private static Sie4BSource GoldenSource()
    {
        var date = new DateOnly(2026, 1, 15);
        return new Sie4BSource(
            new Sie4BCompany("Övningsbolaget AB", "5560160680", "Storgatan 1", "111 22 Stockholm", "SE", "SEK"),
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), new DateOnly(2027, 1, 10),
            [new Sie4BDimension(6, "Project", [new Sie4BObject("P-1", "Launch")])],
            [
                new Sie4BAccount("1930", "Företagskonto", FinanceAccountClassValues.Asset, 10000m, 11250m,
                    new Dictionary<string, decimal> { ["202601"] = 1250m }),
                new Sie4BAccount("2611", "Utgående moms", FinanceAccountClassValues.Liability, 0m, -250m,
                    new Dictionary<string, decimal> { ["202601"] = -250m }),
                new Sie4BAccount("3011", "Försäljning", FinanceAccountClassValues.Income, 0m, -1000m,
                    new Dictionary<string, decimal> { ["202601"] = -1000m })
            ],
            [new Sie4BVoucher("A", 1, date, "Försäljning", date,
            [
                new Sie4BTransaction("1930", 1250m, date, "Betalning", new Dictionary<int, string> { [6] = "P-1" }),
                new Sie4BTransaction("3011", -1000m, date, "Försäljning", new Dictionary<int, string> { [6] = "P-1" }),
                new Sie4BTransaction("2611", -250m, date, "Moms", new Dictionary<int, string> { [6] = "P-1" })
            ])]);
    }
}
