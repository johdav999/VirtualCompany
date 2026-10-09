using VirtualCompany.Application.Finance;
namespace VirtualCompany.Finance.Tests;

public sealed class FinanceRollingPlanningCalculationTests
{
    [Theory][InlineData("-1000.10","-1100","99.90","-9.08")][InlineData("200","0","200",null)][InlineData("10","20","-10","-50")]
    public void Signed_variance_and_zero_baseline_percentage(string actual,string budget,string difference,string? percent){
        static decimal D(string s)=>decimal.Parse(s,System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(D(difference),FinanceRollingPlanningCalculation.Difference(D(actual),D(budget)));Assert.Equal(percent==null?(decimal?)null:D(percent),FinanceRollingPlanningCalculation.Percentage(D(difference),D(budget)));}
    [Fact]public void Missing_evidence_is_not_zero(){Assert.Null(FinanceRollingPlanningCalculation.Difference(null,100));Assert.Null(FinanceRollingPlanningCalculation.Difference(0,null));Assert.Null(FinanceRollingPlanningCalculation.Percentage(null,100));}
}
