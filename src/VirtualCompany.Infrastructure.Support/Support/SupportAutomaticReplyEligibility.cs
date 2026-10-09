using VirtualCompany.Domain.Entities;
namespace VirtualCompany.Infrastructure.Support;

internal static class SupportAutomaticReplyEligibility
{
    public static bool Allows(SupportCase supportCase,SupportReplyDraft draft)
        => supportCase.Category is SupportCaseCategories.GeneralQuestion or SupportCaseCategories.AccountAccess or SupportCaseCategories.BugReport
            && draft.Confidence>=0.8m && draft.Answerability>=0.75m;
}
