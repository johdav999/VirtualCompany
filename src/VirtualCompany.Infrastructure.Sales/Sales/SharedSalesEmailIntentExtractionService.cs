using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Application.Sales;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class SharedSalesEmailIntentExtractionService(
    IEmailClassificationService classifier,
    ILogger<SharedSalesEmailIntentExtractionService> logger) : ISalesEmailIntentExtractionService
{
    public async Task<SalesEmailIntentExtractionResult?> ExtractAsync(
        SalesEmailIntentExtractionRequest request, CancellationToken cancellationToken)
    {
        if (request.Messages.Count == 0) return null;

        try
        {
            var classification = await classifier.ClassifyAsync(
                new EmailClassificationRequest(
                    request.CompanyId,
                    VirtualCompany.Domain.Enums.MailboxPurpose.Sales,
                    request.Provider,
                    request.MailboxConnectionId,
                    null,
                    request.Messages),
                cancellationToken);

            if (classification.Intent == EmailClassificationIntents.SalesLead &&
                classification.RecommendedAction == EmailClassificationActions.CreateSalesLeadDraft)
            {
                if (classification.RequiresHumanReview && classification.Confidence < 0.75m)
                {
                    return new SalesEmailIntentExtractionResult(
                        SalesEmailIntentClassifications.Ignore,
                        null,
                        SalesEmailIgnoreReasons.InsufficientSignal,
                        classification.EvidenceSummary,
                        UsedFallback: !classification.UsedAi);
                }

                var latest = request.Messages.Last();
                var email = latest.Sender.Email?.Trim().ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(email) || !email.Contains('@')) return null;

                return new SalesEmailIntentExtractionResult(
                    SalesEmailIntentClassifications.SalesLead,
                    new SalesEmailSignalResult(
                        email,
                        latest.Sender.DisplayName,
                        null,
                        SalesEmailIntents.BuyingInterest,
                        classification.ProductOrServiceInterest,
                        classification.Urgency is SalesEmailUrgencies.Low or SalesEmailUrgencies.High ? classification.Urgency : SalesEmailUrgencies.Medium,
                        classification.Confidence),
                    null,
                    classification.EvidenceSummary,
                    UsedFallback: !classification.UsedAi);
            }

            return new SalesEmailIntentExtractionResult(
                SalesEmailIntentClassifications.Ignore,
                null,
                classification.IgnoreReason ?? ToSalesIgnoreReason(classification.Intent),
                classification.EvidenceSummary,
                UsedFallback: !classification.UsedAi);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Shared Sales email classification failed safely. CompanyId: {CompanyId}.", request.CompanyId);
            return null;
        }
    }

    private static string ToSalesIgnoreReason(string intent) => intent switch
    {
        EmailClassificationIntents.Newsletter => SalesEmailIgnoreReasons.Newsletter,
        EmailClassificationIntents.Receipt => SalesEmailIgnoreReasons.Receipt,
        EmailClassificationIntents.Invoice => SalesEmailIgnoreReasons.Invoice,
        EmailClassificationIntents.SupportRequest => SalesEmailIgnoreReasons.SupportTicket,
        EmailClassificationIntents.Operational => SalesEmailIgnoreReasons.NonSalesOperational,
        _ => SalesEmailIgnoreReasons.InsufficientSignal
    };
}