using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Finance;
using VirtualCompany.Infrastructure.Finance;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class ReconciliationScoringServiceTests
{
    [Fact]
    public async Task Exact_match_scores_near_one_and_ranks_first()
    {
        var companyId = Guid.NewGuid();
        var service = CreateService(new ReconciliationScoringSettings(5m, 7), companyId);

        var result = await service.ScoreBankTransactionCandidatesAsync(
            new ScoreBankTransactionPaymentCandidatesQuery(
                companyId,
                Guid.NewGuid(),
                1250m,
                "USD",
                Utc(2026, 4, 20),
                "INV-1001",
                "Acme AB",
                [
                    Candidate(1250m, Utc(2026, 4, 20), "INV-1001", "Acme AB"),
                    Candidate(1247m, Utc(2026, 4, 19), "INV-1002", "Other Vendor")
                ]),
            CancellationToken.None);

        var top = Assert.Single(result.Suggestions.Where(x => x.Rank == 1));
        Assert.Equal(1.00m, top.ConfidenceScore);
        Assert.Equal(1.00m, Rule(top, ReconciliationRuleNames.AmountExact).Score);
        Assert.False(Rule(top, ReconciliationRuleNames.AmountNear).Matched);
        Assert.Equal(1.00m, Rule(top, ReconciliationRuleNames.DateProximity).Score);
        Assert.Equal(1.00m, Rule(top, ReconciliationRuleNames.ReferenceSimilarity).Score);
        Assert.Equal(1.00m, Rule(top, ReconciliationRuleNames.CounterpartySimilarity).Score);
    }

    [Fact]
    public async Task Near_amount_match_scores_lower_than_exact_and_above_zero()
    {
        var companyId = Guid.NewGuid();
        var service = CreateService(new ReconciliationScoringSettings(5m, 7), companyId);

        var result = await service.ScoreBankTransactionCandidatesAsync(
            new ScoreBankTransactionPaymentCandidatesQuery(
                companyId,
                Guid.NewGuid(),
                100m,
                "USD",
                Utc(2026, 4, 20),
                "BTX-1",
                "Northwind",
                [
                    Candidate(100m, Utc(2026, 4, 20), "BTX-1", "Northwind"),
                    Candidate(97m, Utc(2026, 4, 20), "BTX-1", "Northwind")
                ]),
            CancellationToken.None);

        Assert.Equal(2, result.Suggestions.Count);
        var exact = result.Suggestions[0];
        var near = result.Suggestions[1];

        Assert.Equal(1.00m, exact.ConfidenceScore);
        Assert.InRange(near.ConfidenceScore, 0.01m, 0.99m);
        Assert.True(near.ConfidenceScore < exact.ConfidenceScore);
        Assert.False(Rule(near, ReconciliationRuleNames.AmountExact).Matched);
        Assert.True(Rule(near, ReconciliationRuleNames.AmountNear).Matched);
        Assert.True(Rule(near, ReconciliationRuleNames.AmountNear).Score < 1.00m);
    }

    [Fact]
    public async Task Date_only_match_returns_date_contribution_only()
    {
        var companyId = Guid.NewGuid();
        var service = CreateService(new ReconciliationScoringSettings(0m, 7), companyId);

        var result = await service.ScoreBankTransactionCandidatesAsync(
            new ScoreBankTransactionPaymentCandidatesQuery(
                companyId,
                Guid.NewGuid(),
                200m,
                "USD",
                Utc(2026, 4, 20),
                "BTX-2",
                "Acme AB",
                [Candidate(250m, Utc(2026, 4, 20), "OTHER-REF", "Other Counterparty")]),
            CancellationToken.None);

        var suggestion = Assert.Single(result.Suggestions);
        Assert.Equal(0.20m, suggestion.ConfidenceScore);
        Assert.Equal(1.00m, Rule(suggestion, ReconciliationRuleNames.DateProximity).Score);
        Assert.Equal(0m, Rule(suggestion, ReconciliationRuleNames.AmountExact).Score);
        Assert.Equal(0m, Rule(suggestion, ReconciliationRuleNames.AmountNear).Score);
        Assert.Equal(0m, Rule(suggestion, ReconciliationRuleNames.ReferenceSimilarity).Score);
        Assert.Equal(0m, Rule(suggestion, ReconciliationRuleNames.CounterpartySimilarity).Score);
    }

    [Fact]
    public async Task Reference_only_match_returns_reference_contribution_only()
    {
        var companyId = Guid.NewGuid();
        var service = CreateService(ReconciliationScoringSettings.Default, companyId);

        var result = await service.ScoreInvoiceCandidatesAsync(
            new ScoreInvoicePaymentCandidatesQuery(
                companyId,
                Guid.NewGuid(),
                300m,
                "USD",
                Utc(2026, 4, 20),
                "INV-2040",
                "Vendor A",
                [Candidate(450m, Utc(2026, 5, 10), "INV 2040", "Different Vendor")]),
            CancellationToken.None);

        var suggestion = Assert.Single(result.Suggestions);
        Assert.Equal(0.25m, suggestion.ConfidenceScore);
        Assert.Equal(1.00m, Rule(suggestion, ReconciliationRuleNames.ReferenceSimilarity).Score);
        Assert.Equal(0m, Rule(suggestion, ReconciliationRuleNames.AmountExact).Score);
        Assert.Equal(0m, Rule(suggestion, ReconciliationRuleNames.AmountNear).Score);
        Assert.Equal(0m, Rule(suggestion, ReconciliationRuleNames.DateProximity).Score);
        Assert.Equal(0m, Rule(suggestion, ReconciliationRuleNames.CounterpartySimilarity).Score);
    }

    [Fact]
    public async Task Counterparty_only_match_returns_counterparty_contribution_only()
    {
        var companyId = Guid.NewGuid();
        var service = CreateService(ReconciliationScoringSettings.Default, companyId);

        var result = await service.ScoreBillCandidatesAsync(
            new ScoreBillPaymentCandidatesQuery(
                companyId,
                Guid.NewGuid(),
                900m,
                "USD",
                Utc(2026, 4, 20),
                "BILL-99",
                "Contoso Logistics",
                [Candidate(850m, Utc(2026, 5, 15), "UNRELATED", "contoso logistics")]),
            CancellationToken.None);

        var suggestion = Assert.Single(result.Suggestions);
        Assert.Equal(0.15m, suggestion.ConfidenceScore);
        Assert.Equal(1.00m, Rule(suggestion, ReconciliationRuleNames.CounterpartySimilarity).Score);
        Assert.Equal(0m, Rule(suggestion, ReconciliationRuleNames.AmountExact).Score);
        Assert.Equal(0m, Rule(suggestion, ReconciliationRuleNames.AmountNear).Score);
        Assert.Equal(0m, Rule(suggestion, ReconciliationRuleNames.DateProximity).Score);
        Assert.Equal(0m, Rule(suggestion, ReconciliationRuleNames.ReferenceSimilarity).Score);
    }

    [Fact]
    public async Task No_match_returns_zero_score()
    {
        var companyId = Guid.NewGuid();
        var service = CreateService(ReconciliationScoringSettings.Default, companyId);

        var result = await service.ScoreBankTransactionCandidatesAsync(
            new ScoreBankTransactionPaymentCandidatesQuery(
                companyId,
                Guid.NewGuid(),
                100m,
                "USD",
                Utc(2026, 4, 20),
                "REF-1",
                "Alpha",
                [Candidate(999m, Utc(2026, 6, 1), "NOPE", "Beta")]),
            CancellationToken.None);

        var suggestion = Assert.Single(result.Suggestions);
        Assert.Equal(0m, suggestion.ConfidenceScore);
        Assert.All(suggestion.RuleDetails, detail => Assert.False(detail.Matched));
    }

    [Fact]
    public async Task Amount_outside_tolerance_gets_no_amount_score()
    {
        var companyId = Guid.NewGuid();
        var service = CreateService(new ReconciliationScoringSettings(2m, 7), companyId);

        var result = await service.ScoreBankTransactionCandidatesAsync(
            new ScoreBankTransactionPaymentCandidatesQuery(
                companyId,
                Guid.NewGuid(),
                100m,
                "USD",
                Utc(2026, 4, 20),
                "REF-2",
                "Gamma",
                [Candidate(103m, Utc(2026, 4, 20), "REF-2", "Gamma")]),
            CancellationToken.None);

        var suggestion = Assert.Single(result.Suggestions);
        Assert.Equal(0m, Rule(suggestion, ReconciliationRuleNames.AmountExact).Score);
        Assert.False(Rule(suggestion, ReconciliationRuleNames.AmountExact).Matched);
        Assert.Equal(0m, Rule(suggestion, ReconciliationRuleNames.AmountNear).Score);
        Assert.False(Rule(suggestion, ReconciliationRuleNames.AmountNear).Matched);
    }

    [Fact]
    public async Task Date_outside_window_gets_no_date_score()
    {
        var companyId = Guid.NewGuid();
        var service = CreateService(new ReconciliationScoringSettings(5m, 3), companyId);

        var result = await service.ScoreBankTransactionCandidatesAsync(
            new ScoreBankTransactionPaymentCandidatesQuery(
                companyId,
                Guid.NewGuid(),
                100m,
                "USD",
                Utc(2026, 4, 20),
                "REF-3",
                "Gamma",
                [Candidate(100m, Utc(2026, 4, 25), "OTHER", "Other")]),
            CancellationToken.None);

        var suggestion = Assert.Single(result.Suggestions);
        Assert.Equal(0m, Rule(suggestion, ReconciliationRuleNames.DateProximity).Score);
        Assert.False(Rule(suggestion, ReconciliationRuleNames.DateProximity).Matched);
    }

    [Fact]
    public async Task Tenant_specific_settings_change_scoring_and_use_requested_company()
    {
        var firstCompanyId = Guid.NewGuid();
        var secondCompanyId = Guid.NewGuid();
        var provider = new FakeReconciliationScoringSettingsProvider(
            new Dictionary<Guid, ReconciliationScoringSettings>
            {
                [firstCompanyId] = new(5m, 2),
                [secondCompanyId] = new(1m, 2)
            });
        var service = new CompanyReconciliationScoringService(provider);
        var candidate = Candidate(103m, Utc(2026, 4, 20), "REF-4", "Vendor");

        var firstResult = await service.ScoreBankTransactionCandidatesAsync(
            new ScoreBankTransactionPaymentCandidatesQuery(
                firstCompanyId,
                Guid.NewGuid(),
                100m,
                "USD",
                Utc(2026, 4, 20),
                "REF-4",
                "Vendor",
                [candidate]),
            CancellationToken.None);
        var secondResult = await service.ScoreBankTransactionCandidatesAsync(
            new ScoreBankTransactionPaymentCandidatesQuery(
                secondCompanyId,
                Guid.NewGuid(),
                100m,
                "USD",
                Utc(2026, 4, 20),
                "REF-4",
                "Vendor",
                [candidate]),
            CancellationToken.None);

        Assert.Equal(new[] { firstCompanyId, secondCompanyId }, provider.RequestedCompanyIds);
        Assert.True(firstResult.Suggestions[0].ConfidenceScore > secondResult.Suggestions[0].ConfidenceScore);
        Assert.True(Rule(firstResult.Suggestions[0], ReconciliationRuleNames.AmountNear).Matched);
        Assert.False(Rule(secondResult.Suggestions[0], ReconciliationRuleNames.AmountNear).Matched);
    }

    [Fact]
    public async Task Tenant_specific_weights_change_confidence_for_same_match_signal()
    {
        var firstCompanyId = Guid.NewGuid();
        var secondCompanyId = Guid.NewGuid();
        var provider = new FakeReconciliationScoringSettingsProvider(
            new Dictionary<Guid, ReconciliationScoringSettings>
            {
                [firstCompanyId] = new(5m, 7, new ReconciliationScoringWeights(0.40m, 0.30m, 0.20m, 0.60m, 0.10m)),
                [secondCompanyId] = new(5m, 7, new ReconciliationScoringWeights(0.40m, 0.30m, 0.20m, 0.05m, 0.10m))
            });
        var service = new CompanyReconciliationScoringService(provider);
        var candidate = Candidate(450m, Utc(2026, 5, 15), "INV 700", "Different Vendor");

        var firstResult = await service.ScoreInvoiceCandidatesAsync(
            new ScoreInvoicePaymentCandidatesQuery(
                firstCompanyId,
                Guid.NewGuid(),
                300m,
                "USD",
                Utc(2026, 4, 20),
                "INV-700",
                "Vendor A",
                [candidate]),
            CancellationToken.None);
        var secondResult = await service.ScoreInvoiceCandidatesAsync(
            new ScoreInvoicePaymentCandidatesQuery(
                secondCompanyId,
                Guid.NewGuid(),
                300m,
                "USD",
                Utc(2026, 4, 20),
                "INV-700",
                "Vendor A",
                [candidate]),
            CancellationToken.None);

        Assert.True(firstResult.Suggestions[0].ConfidenceScore > secondResult.Suggestions[0].ConfidenceScore);
        Assert.True(Rule(firstResult.Suggestions[0], ReconciliationRuleNames.ReferenceSimilarity).Matched);
        Assert.True(Rule(secondResult.Suggestions[0], ReconciliationRuleNames.ReferenceSimilarity).Matched);
    }

    [Fact]
    public async Task Results_are_ranked_descending_and_deterministic_for_ties()
    {
        var companyId = Guid.NewGuid();
        var firstPaymentId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var secondPaymentId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var service = CreateService(ReconciliationScoringSettings.Default, companyId);

        var result = await service.ScoreBankTransactionCandidatesAsync(
            new ScoreBankTransactionPaymentCandidatesQuery(
                companyId,
                Guid.NewGuid(),
                100m,
                "USD",
                Utc(2026, 4, 20),
                "BTX-9",
                "Tie Vendor",
                [
                    Candidate(75m, Utc(2026, 5, 5), "OTHER", "Mismatch", firstPaymentId),
                    Candidate(75m, Utc(2026, 5, 5), "OTHER", "Mismatch", secondPaymentId),
                    Candidate(100m, Utc(2026, 4, 20), "BTX-9", "Tie Vendor")
                ]),
            CancellationToken.None);

        Assert.Equal(3, result.Suggestions.Count);
        Assert.Equal(1, result.Suggestions[0].Rank);
        Assert.Equal(2, result.Suggestions[1].Rank);
        Assert.Equal(3, result.Suggestions[2].Rank);
        Assert.True(result.Suggestions[0].ConfidenceScore >= result.Suggestions[1].ConfidenceScore);
        Assert.True(result.Suggestions[1].ConfidenceScore >= result.Suggestions[2].ConfidenceScore);
        Assert.Equal(firstPaymentId, result.Suggestions[1].CandidatePaymentId);
        Assert.Equal(secondPaymentId, result.Suggestions[2].CandidatePaymentId);
    }

    [Fact]
    public async Task Suggestions_include_rule_breakdown_and_normalized_scores()
    {
        var companyId = Guid.NewGuid();
        var service = CreateService(ReconciliationScoringSettings.Default, companyId);

        var result = await service.ScoreInvoiceCandidatesAsync(
            new ScoreInvoicePaymentCandidatesQuery(
                companyId,
                Guid.NewGuid(),
                500m,
                "USD",
                Utc(2026, 4, 20),
                "INV-500",
                "Acme",
                [Candidate(498m, Utc(2026, 4, 22), "INV 500", "Acme")]),
            CancellationToken.None);

        var suggestion = Assert.Single(result.Suggestions);
        AssertSuggestionShape(suggestion);
        Assert.Equal(1, suggestion.Rank);
        Assert.False(string.IsNullOrWhiteSpace(suggestion.Explanation));
        Assert.All(suggestion.RuleDetails, detail => Assert.False(string.IsNullOrWhiteSpace(detail.Explanation)));
        Assert.All(suggestion.RuleDetails, detail => Assert.False(string.IsNullOrWhiteSpace(detail.SourceValue)));
        Assert.All(suggestion.RuleDetails, detail => Assert.False(string.IsNullOrWhiteSpace(detail.CandidateValue)));
    }

    [Fact]
    public async Task Returns_ranked_payment_matches_for_bank_transaction_using_multiple_signals()
    {
        var companyId = Guid.NewGuid();
        var service = CreateService(new ReconciliationScoringSettings(5m, 7), companyId);
        var exactId = Guid.NewGuid();
        var nearId = Guid.NewGuid();
        var dateOnlyId = Guid.NewGuid();
        var noMatchId = Guid.NewGuid();

        var result = await service.ScoreBankTransactionCandidatesAsync(
            new ScoreBankTransactionPaymentCandidatesQuery(
                companyId,
                Guid.NewGuid(),
                100m,
                "USD",
                Utc(2026, 4, 20),
                "BTX-25",
                "Northwind Traders",
                [
                    Candidate(100m, Utc(2026, 4, 20), "BTX-25", "Northwind Traders", exactId),
                    Candidate(97m, Utc(2026, 4, 20), "BTX 25", "Northwind Traders", nearId),
                    Candidate(250m, Utc(2026, 4, 20), "UNRELATED", "Other Counterparty", dateOnlyId),
                    Candidate(250m, Utc(2026, 5, 20), "UNRELATED", "Other Counterparty", noMatchId)
                ]),
            CancellationToken.None);

        Assert.Equal(ReconciliationSourceTypes.BankTransaction, result.SourceType);
        Assert.Equal(new[] { exactId, nearId, dateOnlyId, noMatchId }, result.Suggestions.Select(x => x.CandidatePaymentId).ToArray());
        Assert.All(result.Suggestions, AssertSuggestionShape);
        Assert.True(result.Suggestions[0].ConfidenceScore > result.Suggestions[1].ConfidenceScore);
        Assert.True(result.Suggestions[1].ConfidenceScore > result.Suggestions[2].ConfidenceScore);
        Assert.True(result.Suggestions[2].ConfidenceScore > result.Suggestions[3].ConfidenceScore);
        Assert.Equal(0m, result.Suggestions[3].ConfidenceScore);
        Assert.Equal("No reconciliation rules matched.", result.Suggestions[3].Explanation);
    }

    [Fact]
    public async Task Returns_ranked_payment_matches_for_invoice_using_multiple_signals()
    {
        var companyId = Guid.NewGuid();
        var service = CreateService(new ReconciliationScoringSettings(5m, 7), companyId);
        var exactId = Guid.NewGuid();
        var nearId = Guid.NewGuid();
        var referenceOnlyId = Guid.NewGuid();
        var noMatchId = Guid.NewGuid();

        var result = await service.ScoreInvoiceCandidatesAsync(
            new ScoreInvoicePaymentCandidatesQuery(
                companyId,
                Guid.NewGuid(),
                300m,
                "USD",
                Utc(2026, 4, 20),
                "INV-2513",
                "Fabrikam Services",
                [
                    Candidate(300m, Utc(2026, 4, 20), "INV-2513", "Fabrikam Services", exactId),
                    Candidate(297m, Utc(2026, 4, 21), "INV 2513", "Fabrikam Services", nearId),
                    Candidate(450m, Utc(2026, 5, 20), "INV 2513", "Different Vendor", referenceOnlyId),
                    Candidate(450m, Utc(2026, 5, 20), "UNRELATED", "Different Vendor", noMatchId)
                ]),
            CancellationToken.None);

        Assert.Equal(ReconciliationSourceTypes.Invoice, result.SourceType);
        Assert.Equal(new[] { exactId, nearId, referenceOnlyId, noMatchId }, result.Suggestions.Select(x => x.CandidatePaymentId).ToArray());
        Assert.All(result.Suggestions, AssertSuggestionShape);
        Assert.True(result.Suggestions[0].ConfidenceScore > result.Suggestions[1].ConfidenceScore);
        Assert.True(result.Suggestions[1].ConfidenceScore > result.Suggestions[2].ConfidenceScore);
        Assert.True(result.Suggestions[2].ConfidenceScore > result.Suggestions[3].ConfidenceScore);
        Assert.True(Rule(result.Suggestions[2], ReconciliationRuleNames.ReferenceSimilarity).Matched);
        Assert.False(Rule(result.Suggestions[2], ReconciliationRuleNames.CounterpartySimilarity).Matched);
    }

    [Fact]
    public async Task Returns_ranked_payment_matches_for_bill_using_multiple_signals()
    {
        var companyId = Guid.NewGuid();
        var service = CreateService(new ReconciliationScoringSettings(5m, 7), companyId);
        var exactId = Guid.NewGuid();
        var nearId = Guid.NewGuid();
        var counterpartyOnlyId = Guid.NewGuid();
        var noMatchId = Guid.NewGuid();

        var result = await service.ScoreBillCandidatesAsync(
            new ScoreBillPaymentCandidatesQuery(
                companyId,
                Guid.NewGuid(),
                910m,
                "USD",
                Utc(2026, 4, 20),
                "BILL-2513",
                "Contoso Logistics",
                [
                    Candidate(910m, Utc(2026, 4, 20), "BILL-2513", "Contoso Logistics", exactId),
                    Candidate(907m, Utc(2026, 4, 22), "BILL 2513", "Contoso Logistics", nearId),
                    Candidate(1200m, Utc(2026, 5, 20), "UNRELATED", "contoso logistics", counterpartyOnlyId),
                    Candidate(1200m, Utc(2026, 5, 20), "UNRELATED", "Different Vendor", noMatchId)
                ]),
            CancellationToken.None);

        Assert.Equal(ReconciliationSourceTypes.Bill, result.SourceType);
        Assert.Equal(new[] { exactId, nearId, counterpartyOnlyId, noMatchId }, result.Suggestions.Select(x => x.CandidatePaymentId).ToArray());
        Assert.All(result.Suggestions, AssertSuggestionShape);
        Assert.True(result.Suggestions[0].ConfidenceScore > result.Suggestions[1].ConfidenceScore);
        Assert.True(result.Suggestions[1].ConfidenceScore > result.Suggestions[2].ConfidenceScore);
        Assert.True(result.Suggestions[2].ConfidenceScore > result.Suggestions[3].ConfidenceScore);
        Assert.True(Rule(result.Suggestions[2], ReconciliationRuleNames.CounterpartySimilarity).Matched);
        Assert.False(Rule(result.Suggestions[2], ReconciliationRuleNames.ReferenceSimilarity).Matched);
    }

    [Fact]
    public async Task Applies_tenant_date_proximity_window_when_scoring_matches()
    {
        var firstCompanyId = Guid.NewGuid();
        var secondCompanyId = Guid.NewGuid();
        var provider = new FakeReconciliationScoringSettingsProvider(
            new Dictionary<Guid, ReconciliationScoringSettings>
            {
                [firstCompanyId] = new(5m, 5),
                [secondCompanyId] = new(5m, 1)
            });
        var service = new CompanyReconciliationScoringService(provider);
        var candidate = Candidate(250m, Utc(2026, 4, 23), "UNRELATED", "Different Vendor");

        var firstResult = await service.ScoreBankTransactionCandidatesAsync(
            new ScoreBankTransactionPaymentCandidatesQuery(
                firstCompanyId,
                Guid.NewGuid(),
                100m,
                "USD",
                Utc(2026, 4, 20),
                "REF-5",
                "Vendor A",
                [candidate]),
            CancellationToken.None);
        var secondResult = await service.ScoreBankTransactionCandidatesAsync(
            new ScoreBankTransactionPaymentCandidatesQuery(
                secondCompanyId,
                Guid.NewGuid(),
                100m,
                "USD",
                Utc(2026, 4, 20),
                "REF-5",
                "Vendor A",
                [candidate]),
            CancellationToken.None);

        Assert.Equal(new[] { firstCompanyId, secondCompanyId }, provider.RequestedCompanyIds);
        Assert.True(firstResult.Suggestions[0].ConfidenceScore > secondResult.Suggestions[0].ConfidenceScore);
        Assert.True(Rule(firstResult.Suggestions[0], ReconciliationRuleNames.DateProximity).Matched);
        Assert.False(Rule(secondResult.Suggestions[0], ReconciliationRuleNames.DateProximity).Matched);
    }

    private static CompanyReconciliationScoringService CreateService(
        ReconciliationScoringSettings settings,
        Guid companyId) =>
        new(new FakeReconciliationScoringSettingsProvider(
            new Dictionary<Guid, ReconciliationScoringSettings>
            {
                [companyId] = settings
            }));

    private static ReconciliationPaymentCandidate Candidate(
        decimal amount,
        DateTime paymentDate,
        string reference,
        string counterparty,
        Guid? paymentId = null,
        string currency = "USD") =>
        new(
            paymentId ?? Guid.NewGuid(),
            amount,
            currency,
            paymentDate,
            reference,
            counterparty);

    private static ReconciliationRuleDetail Rule(
        ReconciliationSuggestion suggestion,
        string ruleName) =>
        Assert.Single(suggestion.RuleDetails.Where(x => x.RuleName == ruleName));

    private static void AssertSuggestionShape(ReconciliationSuggestion suggestion)
    {
        Assert.InRange(suggestion.ConfidenceScore, 0m, 1m);
        Assert.True(suggestion.Rank > 0);
        Assert.Equal(5, suggestion.RuleDetails.Count);
        Assert.Equal(
            new[]
            {
                ReconciliationRuleNames.AmountExact,
                ReconciliationRuleNames.AmountNear,
                ReconciliationRuleNames.DateProximity,
                ReconciliationRuleNames.ReferenceSimilarity,
                ReconciliationRuleNames.CounterpartySimilarity
            },
            suggestion.RuleDetails.Select(x => x.RuleName).ToArray());
        Assert.All(suggestion.RuleDetails, detail =>
        {
            Assert.InRange(detail.Score, 0m, 1m);
            Assert.InRange(detail.AwardedScore, 0m, 1m);
            Assert.True(detail.Weight >= 0m);
            Assert.False(string.IsNullOrWhiteSpace(detail.RuleName));
            Assert.False(string.IsNullOrWhiteSpace(detail.Explanation));
        });
    }

    private static DateTime Utc(int year, int month, int day) =>
        new(year, month, day, 0, 0, 0, DateTimeKind.Utc);

    private sealed class FakeReconciliationScoringSettingsProvider : IReconciliationScoringSettingsProvider
    {
        private readonly IReadOnlyDictionary<Guid, ReconciliationScoringSettings> _settings;

        public FakeReconciliationScoringSettingsProvider(
            IReadOnlyDictionary<Guid, ReconciliationScoringSettings> settings)
        {
            _settings = settings;
        }

        public List<Guid> RequestedCompanyIds { get; } = [];

        public Task<ReconciliationScoringSettings> GetSettingsAsync(
            Guid companyId,
            CancellationToken cancellationToken)
        {
            RequestedCompanyIds.Add(companyId);
            return Task.FromResult(
                _settings.TryGetValue(companyId, out var settings)
                    ? settings
                    : ReconciliationScoringSettings.Default);
        }
    }
}
