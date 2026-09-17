using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Sales;

namespace VirtualCompany.Infrastructure.Sales;

public sealed partial class SalesNarrationService
{
    private static ApprovedSpeechProfile SelectVoice(ApprovedSpeechProfile profile, string? requested)
    {
        var voice = requested is null ? profile.Voice : requested.Trim().ToLowerInvariant();
        if (!profile.SupportsVoice(voice))
            throw new SalesNarrationException("Choose an available narration voice.", 400);
        return profile with { Voice = voice };
    }
}
