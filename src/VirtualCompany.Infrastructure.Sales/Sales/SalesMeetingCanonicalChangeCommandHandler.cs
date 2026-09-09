using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class SalesMeetingCanonicalChangeCommandHandler(VirtualCompanyDbContext db) : ISalesMeetingCanonicalChangeCommandHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task<ApplySalesMeetingCanonicalChangeResult> ApplyAsync(ApplySalesMeetingCanonicalChangeCommand command, CancellationToken ct)
    {
        var target = SalesMeetingChangeProposalEnumValues.ParseTarget(command.TargetType);
        var field = SalesMeetingChangeProposalEnumValues.ParseField(command.Field);
        return target switch
        {
            SalesMeetingChangeTargetType.Deal => await ApplyDealAsync(command, field, ct),
            SalesMeetingChangeTargetType.Lead => await ApplyLeadAsync(command, field, ct),
            SalesMeetingChangeTargetType.Contact => await ApplyContactAsync(command, field, ct),
            _ => throw new InvalidOperationException("This proposal is not a canonical sales-field command.")
        };
    }

    private async Task<ApplySalesMeetingCanonicalChangeResult> ApplyDealAsync(ApplySalesMeetingCanonicalChangeCommand c, SalesMeetingChangeField field, CancellationToken ct)
    {
        var x = await db.Deals.SingleOrDefaultAsync(x => x.CompanyId == c.CompanyId && x.Id == c.TargetId, ct) ?? throw new KeyNotFoundException("Deal not found.");
        EnsureVersion(x.UpdatedUtc, c.ExpectedTargetVersion);
        object? before = field switch { SalesMeetingChangeField.DealStage => x.PipelineStageId, SalesMeetingChangeField.DealProbability => x.Probability, SalesMeetingChangeField.DealValue => x.Amount, SalesMeetingChangeField.DealNextStep => x.NextStep, SalesMeetingChangeField.Discount or SalesMeetingChangeField.PricePromise or SalesMeetingChangeField.ContractTerm => null, _ => throw NotAllowed() };
        switch (field)
        {
            case SalesMeetingChangeField.DealStage: x.ChangeStage(RequiredGuid(c.ProposedValue)); break;
            case SalesMeetingChangeField.DealProbability: x.SetProbability(RequiredDecimal(c.ProposedValue)); break;
            case SalesMeetingChangeField.DealValue: x.SetAmount(RequiredDecimal(c.ProposedValue)); break;
            case SalesMeetingChangeField.DealNextStep: x.SetNextStep(c.ProposedValue.StringValue); break;
            case SalesMeetingChangeField.Discount:
            case SalesMeetingChangeField.PricePromise:
            case SalesMeetingChangeField.ContractTerm:
                db.SalesActivities.Add(new Domain.Entities.SalesActivity(Guid.NewGuid(), c.CompanyId, "approved commercial commitment", c.ProposedValue.StringValue ?? throw new ArgumentException("A commitment description is required."), DateTime.UtcNow, dealId: x.Id));
                break;
            default: throw NotAllowed();
        }
        object? after = field switch { SalesMeetingChangeField.DealStage => x.PipelineStageId, SalesMeetingChangeField.DealProbability => x.Probability, SalesMeetingChangeField.DealValue => x.Amount, SalesMeetingChangeField.DealNextStep => x.NextStep, _ => c.ProposedValue.StringValue };
        return Result(before, after, x.UpdatedUtc);
    }

    private async Task<ApplySalesMeetingCanonicalChangeResult> ApplyLeadAsync(ApplySalesMeetingCanonicalChangeCommand c, SalesMeetingChangeField field, CancellationToken ct)
    {
        var x = await db.Leads.SingleOrDefaultAsync(x => x.CompanyId == c.CompanyId && x.Id == c.TargetId, ct) ?? throw new KeyNotFoundException("Lead not found.");
        EnsureVersion(x.UpdatedUtc, c.ExpectedTargetVersion); object? before;
        switch (field)
        {
            case SalesMeetingChangeField.LeadEstimatedValue: before = x.EstimatedValue; x.SetEstimatedValue(RequiredDecimal(c.ProposedValue)); break;
            case SalesMeetingChangeField.LeadNextAction: before = x.SuggestedNextAction; x.SetSuggestedNextAction(c.ProposedValue.StringValue); break;
            default: throw NotAllowed();
        }
        var after = field == SalesMeetingChangeField.LeadEstimatedValue ? (object?)x.EstimatedValue : x.SuggestedNextAction;
        return Result(before, after, x.UpdatedUtc);
    }

    private async Task<ApplySalesMeetingCanonicalChangeResult> ApplyContactAsync(ApplySalesMeetingCanonicalChangeCommand c, SalesMeetingChangeField field, CancellationToken ct)
    {
        var x = await db.Contacts.SingleOrDefaultAsync(x => x.CompanyId == c.CompanyId && x.Id == c.TargetId, ct) ?? throw new KeyNotFoundException("Contact not found.");
        EnsureVersion(x.UpdatedUtc, c.ExpectedTargetVersion); var value = c.ProposedValue.StringValue ?? throw new ArgumentException("A string value is required.");
        object? before = field switch { SalesMeetingChangeField.ContactFullName => x.FullName, SalesMeetingChangeField.ContactEmail => x.Email, SalesMeetingChangeField.ContactTitle => x.Title, SalesMeetingChangeField.ContactPhone => x.Phone, _ => throw NotAllowed() };
        x.UpdateIdentity(field == SalesMeetingChangeField.ContactFullName ? value : x.FullName, field == SalesMeetingChangeField.ContactEmail ? value : x.Email, field == SalesMeetingChangeField.ContactTitle ? value : x.Title, field == SalesMeetingChangeField.ContactPhone ? value : x.Phone);
        object? after = field switch { SalesMeetingChangeField.ContactFullName => x.FullName, SalesMeetingChangeField.ContactEmail => x.Email, SalesMeetingChangeField.ContactTitle => x.Title, _ => x.Phone };
        return Result(before, after, x.UpdatedUtc);
    }

    private static ApplySalesMeetingCanonicalChangeResult Result(object? before, object? after, DateTime updated) => new(JsonSerializer.Serialize(before, Json), JsonSerializer.Serialize(after, Json), Version(updated));
    internal static string Version(DateTime updated) => updated.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture);
    private static void EnsureVersion(DateTime updated, string expected) { if (!string.Equals(Version(updated), expected, StringComparison.Ordinal)) throw new SalesMeetingChangeProposalConflictException(SalesMeetingChangeProposalProblemCodes.TargetChanged, "The target changed after the proposal was captured. Review the current value before applying it."); }
    private static decimal RequiredDecimal(SalesMeetingProposedValue x) => x.DecimalValue ?? throw new ArgumentException("A decimal value is required.");
    private static Guid RequiredGuid(SalesMeetingProposedValue x) => x.GuidValue is { } id && id != Guid.Empty ? id : throw new ArgumentException("A target identifier is required.");
    private static InvalidOperationException NotAllowed() => new("The requested canonical field is not in the command allowlist.");
}
