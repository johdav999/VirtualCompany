using System.Text.Json.Nodes;

namespace VirtualCompany.Domain.Entities;
public static class SupportCaseCategories
{
    public const string GeneralQuestion = "general_question";
    public const string Billing = "billing";
    public const string Refund = "refund";
    public const string TechnicalIssue = "technical_issue";
    public const string AccountAccess = "account_access";
    public const string Delivery = "delivery";
    public const string Complaint = "complaint";
    public const string FeatureRequest = "feature_request";
    public const string BugReport = "bug_report";
    public const string ChurnRisk = "churn_risk";
    public static string Normalize(string value) => SupportCaseStatuses.NormalizeKnownForSupport(value, [GeneralQuestion, Billing, Refund, TechnicalIssue, AccountAccess, Delivery, Complaint, FeatureRequest, BugReport, ChurnRisk], nameof(value));
}

