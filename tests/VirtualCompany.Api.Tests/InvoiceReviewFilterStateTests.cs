using System.Web;
using VirtualCompany.Web.Services;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class InvoiceReviewFilterStateTests
{
    [Fact]
    public void Query_string_round_trip_preserves_invoice_review_filters()
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        query["status"] = " pending_approval ";
        query["supplier"] = " Contoso Supplies ";
        query["riskLevel"] = " high ";
        query["outcome"] = " request_human_approval ";

        var state = InvoiceReviewFilterState.FromQuery(query);
        var parameters = state.ToQueryParameters(Guid.Parse("df0ff3c3-1f38-40cf-a56e-557379b2b298"));

        Assert.Equal("pending_approval", state.Status);
        Assert.Equal("Contoso Supplies", state.Supplier);
        Assert.Equal("high", state.RiskLevel);
        Assert.Equal("request_human_approval", state.RecommendationOutcome);
        Assert.Equal("pending_approval", parameters["status"]);
        Assert.Equal("Contoso Supplies", parameters["supplier"]);
        Assert.Equal("high", parameters["riskLevel"]);
        Assert.Equal("request_human_approval", parameters["outcome"]);
    }

    [Fact]
    public void Clearing_filters_omits_empty_query_values()
    {
        var state = new InvoiceReviewFilterState
        {
            Status = " ",
            Supplier = "",
            RiskLevel = null,
            RecommendationOutcome = " "
        };

        var parameters = state.ToQueryParameters(Guid.Parse("df0ff3c3-1f38-40cf-a56e-557379b2b298"));

        Assert.Null(parameters["status"]);
        Assert.Null(parameters["supplier"]);
        Assert.Null(parameters["riskLevel"]);
        Assert.Null(parameters["outcome"]);
    }

    [Fact]
    public void Query_string_serialization_is_shareable_and_company_scoped()
    {
        var companyId = Guid.Parse("df0ff3c3-1f38-40cf-a56e-557379b2b298");
        var state = new InvoiceReviewFilterState
        {
            Status = "pending_approval",
            Supplier = "Northwind",
            RiskLevel = "high",
            RecommendationOutcome = "request_human_approval"
        };

        var query = state.ToQueryString(companyId);

        Assert.Contains($"companyId={companyId:D}", query);
        Assert.Contains("status=pending_approval", query);
        Assert.Contains("supplier=Northwind", query);
        Assert.Contains("riskLevel=high", query);
        Assert.Contains("outcome=request_human_approval", query);
    }
}