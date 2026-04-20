using VirtualCompany.Web.Services;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class DepartmentDashboardSectionViewModelTests
{
    [Fact]
    public void Visible_signals_hide_zero_values_and_limit_cards_to_three()
    {
        var section = new DepartmentDashboardSectionViewModel
        {
            DepartmentKey = "operations",
            SummaryCounts = new Dictionary<string, int>
            {
                ["open_tasks"] = 12,
                ["blocked_workflows"] = 3,
                ["pending_approvals"] = 2,
                ["zero_metric"] = 0,
                ["overflow_metric"] = 99
            }
        };

        var signals = section.VisibleSignals;

        Assert.Collection(
            signals,
            signal =>
            {
                Assert.Equal("blocked_workflows", signal.Key);
                Assert.Equal("Blocked Workflows", signal.Label);
                Assert.Equal(3, signal.Value);
            },
            signal =>
            {
                Assert.Equal("pending_approvals", signal.Key);
                Assert.Equal("Pending Approvals", signal.Label);
                Assert.Equal(2, signal.Value);
            },
            signal =>
            {
                Assert.Equal("open_tasks", signal.Key);
                Assert.Equal("Open Tasks", signal.Label);
                Assert.Equal(12, signal.Value);
            });
    }

    [Fact]
    public void Visible_signals_returns_empty_when_all_metrics_are_zero()
    {
        var section = new DepartmentDashboardSectionViewModel
        {
            DepartmentKey = "operations",
            SummaryCounts = new Dictionary<string, int>
            {
                ["open_tasks"] = 0,
                ["pending_approvals"] = 0
            }
        };

        Assert.Empty(section.VisibleSignals);
    }

    [Fact]
    public void Visible_signals_are_deterministic_for_unknown_metrics()
    {
        var section = new DepartmentDashboardSectionViewModel
        {
            DepartmentKey = "custom",
            SummaryCounts = new Dictionary<string, int>
            {
                ["custom_b"] = 4,
                ["custom_a"] = 4,
                ["custom_d"] = 1,
                ["custom_c"] = 4
            }
        };

        Assert.Equal(
            new[] { "custom_a", "custom_b", "custom_c" },
            section.VisibleSignals.Select(signal => signal.Key).ToArray());
    }
}