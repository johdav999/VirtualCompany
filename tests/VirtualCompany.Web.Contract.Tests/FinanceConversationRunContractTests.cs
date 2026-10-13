using ApplicationFinance = VirtualCompany.Application.Finance;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class FinanceConversationRunContractTests
{
    [Theory]
    [InlineData(typeof(ApplicationFinance.FinanceConversationRunDto), typeof(FinanceConversationRunViewModel))]
    [InlineData(typeof(ApplicationFinance.FinanceConversationRunRevisionDto), typeof(FinanceConversationRunRevisionViewModel))]
    [InlineData(typeof(ApplicationFinance.FinanceConversationRunStepDto), typeof(FinanceConversationRunStepViewModel))]
    public void Web_contract_tracks_authoritative_application_contract(Type authoritative, Type web)
    {
        var expected = authoritative.GetProperties().Select(x => x.Name).Order(StringComparer.Ordinal).ToArray();
        var actual = web.GetProperties().Select(x => x.Name).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void List_contract_retains_items_and_total_count()
    {
        Assert.Equal(
            typeof(ApplicationFinance.FinanceConversationRunListResult).GetProperties().Select(x => x.Name).Order(StringComparer.Ordinal),
            typeof(FinanceConversationRunListViewModel).GetProperties().Select(x => x.Name).Order(StringComparer.Ordinal));
    }
}
