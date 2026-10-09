using System.Globalization;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Agents;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Shared;
using System.Text.Json.Nodes;
using System.Text.Json;

namespace VirtualCompany.Api.Controllers;





public sealed record RefreshFinanceInsightsSnapshotRequest(
    DateTime? AsOfUtc = null,
    int ExpenseWindowDays = 90,
    int TrendWindowDays = 30,
    int PayableWindowDays = 14,
    string? SnapshotKey = null,
    int RetentionMinutes = 360,
    bool RunInBackground = false,
    bool ResetAttempts = false,
    string? CorrelationId = null);




public sealed record RerunFinanceBootstrapRequest(
    int BatchSize = 250,
    bool RerunPlanningBackfill = true,
    bool RerunApprovalBackfill = true,
    string? CorrelationId = null);



public sealed record BootstrapFinanceSeedRequest(
    int SeedValue,
    DateTime? SeedAnchorUtc = null,
    bool ReplaceExisting = true,
    bool InjectAnomalies = false,
    string? AnomalyScenarioProfile = null);
