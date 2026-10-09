using VirtualCompany.Domain.Entities;
using Xunit;
namespace VirtualCompany.Finance.Tests;
public sealed class StrategicScenarioCalculationTests
{
    private static ScenarioDrivers Drivers=>new(3,"orders",1,10,8,20,10,10,20,.5m,10,5,3,.5m,.75m,10);
    private static ScenarioCashInput[] Cash=>[new(1,20,5,"Additional equipment and assumed founder funding"),new(2,0,0,"No additional investment or funding"),new(3,0,0,"No additional investment or funding")];
    [Fact]public void Hand_checked_capacity_growth_cost_timing_investment_and_negative_cash_roll_forward()
    {
        var r=StrategicScenarioCalculation.Calculate(100,80,Drivers,Cash);
        Assert.Equal(new ScenarioYearResult(1,10,8,8,2,80,72,10,45,57,20,5,-17,40,18,27),r[0]);
        Assert.Equal(new ScenarioYearResult(2,12,9.6m,9.6m,2.4m,105.60m,86.24m,-17,92.8m,82.68m,0,0,-6.88m,52.8m,21.56m,16.88m),r[1]);
        Assert.Equal(r[1].ClosingCash,r[2].OpeningCash);Assert.Equal(r,StrategicScenarioCalculation.Calculate(100,80,Drivers,Cash));
    }
    [Fact]public void Funding_changes_cash_only_and_capacity_limits_revenue()
    {
        var baseline=StrategicScenarioCalculation.Calculate(100,80,Drivers,Cash);
        var alternative=StrategicScenarioCalculation.Calculate(100,80,Drivers,Cash.Select(x=>x.Year==1?x with{Funding=105}:x).ToArray());
        Assert.Equal(100,alternative[0].ClosingCash-baseline[0].ClosingCash);Assert.Equal(baseline[0].Revenue,alternative[0].Revenue);
        Assert.Equal(0,alternative[0].FundingGap);Assert.Equal(2,baseline[0].CapacityShortfall);
    }
    [Fact]public void Explicit_zero_capacity_and_negative_opening_cash_are_supported()
    {
        var r=StrategicScenarioCalculation.Calculate(100,80,Drivers with{CapacityUnits=0,OpeningCash=-10},Cash);
        Assert.Equal(0,r[0].Revenue);Assert.Equal(40,r[0].OperatingCost);Assert.True(r[0].ClosingCash<0);
    }
    [Fact]public void Missing_years_units_invalid_shares_and_numeric_overflow_block()
    {
        Assert.Throws<ArgumentException>(()=>StrategicScenarioCalculation.Calculate(100,80,Drivers,Cash[..2]));
        foreach(var d in new[]{Drivers with{CapacityUnit=""},Drivers with{DemandUnits=0},Drivers with{CollectionShare=1.1m},Drivers with{DemandGrowthPercent=-101},Drivers with{SourceToAnnualScale=0}})
            Assert.Throws<ArgumentException>(()=>StrategicScenarioCalculation.Calculate(100,80,d,Cash));
        Assert.Throws<ArgumentException>(()=>StrategicScenarioCalculation.Calculate(1_000_000_000_000m,80,Drivers with{SourceToAnnualScale=12},Cash));
    }
}
