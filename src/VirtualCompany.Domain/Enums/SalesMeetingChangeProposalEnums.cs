namespace VirtualCompany.Domain.Enums;

public enum SalesMeetingChangeTargetType { Deal = 1, Lead = 2, Contact = 3, CustomerMinutes = 4, MeetingSession = 5 }
public enum SalesMeetingChangeAction { UpdateField = 1, SendCustomerMinutes = 2, ScheduleNextMeeting = 3 }
public enum SalesMeetingChangeField
{
    DealStage = 1, DealProbability = 2, DealValue = 3, DealNextStep = 4,
    LeadEstimatedValue = 5, LeadNextAction = 6,
    ContactFullName = 7, ContactEmail = 8, ContactTitle = 9, ContactPhone = 10,
    CustomerMinutesRecipient = 11, NextMeeting = 12,
    Discount = 13, PricePromise = 14, ContractTerm = 15
}
public enum SalesMeetingChangeValueKind { String = 1, Decimal = 2, Guid = 3, DateTime = 4, Email = 5, NextMeeting = 6 }
public enum SalesMeetingChangeRiskClass { ConfirmationRequired = 1, ApprovalRequired = 2, AlwaysGated = 3 }
public enum SalesMeetingChangeProposalStatus { Draft = 1, WaitingForApproval = 2, Approved = 3, Rejected = 4, Queued = 5, Executing = 6, Executed = 7, Conflict = 8, Failed = 9, ReconciliationRequired = 10 }

public static class SalesMeetingChangeProposalEnumValues
{
    public static string ToStorageValue(this SalesMeetingChangeTargetType value) => Snake(value);
    public static string ToStorageValue(this SalesMeetingChangeAction value) => Snake(value);
    public static string ToStorageValue(this SalesMeetingChangeField value) => Snake(value);
    public static string ToStorageValue(this SalesMeetingChangeValueKind value) => Snake(value);
    public static string ToStorageValue(this SalesMeetingChangeRiskClass value) => Snake(value);
    public static string ToStorageValue(this SalesMeetingChangeProposalStatus value) => Snake(value);
    public static SalesMeetingChangeTargetType ParseTarget(string value) => Parse<SalesMeetingChangeTargetType>(value);
    public static SalesMeetingChangeAction ParseAction(string value) => Parse<SalesMeetingChangeAction>(value);
    public static SalesMeetingChangeField ParseField(string value) => Parse<SalesMeetingChangeField>(value);
    public static SalesMeetingChangeValueKind ParseValueKind(string value) => Parse<SalesMeetingChangeValueKind>(value);
    public static SalesMeetingChangeRiskClass ParseRisk(string value) => Parse<SalesMeetingChangeRiskClass>(value);
    public static SalesMeetingChangeProposalStatus ParseStatus(string value) => Parse<SalesMeetingChangeProposalStatus>(value);

    private static T Parse<T>(string value) where T : struct, Enum
    {
        var normalized = value?.Trim().Replace("_", string.Empty, StringComparison.Ordinal) ?? string.Empty;
        return Enum.GetValues<T>().FirstOrDefault(x => string.Equals(x.ToString(), normalized, StringComparison.OrdinalIgnoreCase)) is var result && Enum.IsDefined(result)
            ? result : throw new ArgumentOutOfRangeException(nameof(value), value, $"Unsupported {typeof(T).Name} value.");
    }

    private static string Snake<T>(T value) where T : Enum
    {
        var text = value.ToString();
        if (!Enum.IsDefined(typeof(T), value)) throw new ArgumentOutOfRangeException(nameof(value));
        return string.Concat(text.Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
    }
}
