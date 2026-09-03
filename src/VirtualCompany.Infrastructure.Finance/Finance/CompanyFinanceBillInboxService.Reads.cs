using System.Text.Json;
using System.Text.Json.Nodes;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Security;

namespace VirtualCompany.Infrastructure.Finance;


public sealed partial class CompanyFinanceBillInboxService
{
    public async Task<IReadOnlyList<FinanceBillInboxRowDto>> GetInboxAsync(GetFinanceBillInboxQuery query, CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        var limit = Math.Clamp(query.Limit <= 0 ? 100 : query.Limit, 1, 500);

        var rows = await _dbContext.DetectedBills
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId)
            .GroupJoin(
                _dbContext.FinanceBillReviewStates.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == query.CompanyId),
                bill => bill.Id,
                state => state.DetectedBillId,
                (bill, states) => new { Bill = bill, State = states.FirstOrDefault() })
            // The review queue represents bill arrival order. Reviewing or rejecting an older
            // item must not move it above a bill that was received more recently.
            .OrderByDescending(x => x.Bill.CreatedUtc)
            .ThenByDescending(x => x.Bill.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return rows
            .Select(x =>
            {
                var status = ResolveInboxStatus(x.Bill, x.State);
                return new FinanceBillInboxRowDto(
                    x.Bill.Id,
                    x.Bill.SupplierName ?? "Unknown supplier",
                    x.Bill.InvoiceNumber ?? x.Bill.SourceAttachmentId ?? x.Bill.Id.ToString("D"),
                    x.Bill.TotalAmount,
                    x.Bill.Currency,
                    x.Bill.CreatedUtc,
                    FormatBillStatus(status),
                    FormatStatus(x.Bill.ConfidenceLevel),
                    CountValidationWarnings(x.Bill),
                    x.Bill.DuplicateCheck?.IsDuplicate == true ? 1 : 0);
            })
            .Where(x => IsAllowedDisplayStatus(x.Status))
            .ToList();
    }

    public async Task<FinanceBillInboxDetailDto?> GetDetailAsync(GetFinanceBillInboxDetailQuery query, CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);

        var bill = await _dbContext.DetectedBills
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(x => x.Fields)
            .Include(x => x.DuplicateCheck)
            .SingleOrDefaultAsync(x => x.CompanyId == query.CompanyId && x.Id == query.BillId, cancellationToken);

        if (bill is null)
        {
            return null;
        }

        var state = await _dbContext.FinanceBillReviewStates
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(x => x.Actions)
            .SingleOrDefaultAsync(x => x.CompanyId == query.CompanyId && x.DetectedBillId == query.BillId, cancellationToken);

        var validationWarnings = ParseValidationWarnings(bill);
        var duplicateWarnings = BuildDuplicateWarnings(bill);
        var hasUnresolvedValidationFailures = HasUnresolvedValidationFailures(bill, validationWarnings);
        var status = ResolveInboxStatus(bill, state);
        var proposalSummary = BuildProposalSummary(bill, validationWarnings, duplicateWarnings, state?.ProposalSummary);
        var sourcePreview = await LoadSourcePreviewAsync(query.CompanyId, bill, cancellationToken);
        var canRequestFortnoxRegistration = !hasUnresolvedValidationFailures &&
            !string.Equals(status, FinanceBillInboxStatuses.Rejected, StringComparison.OrdinalIgnoreCase);
        var accountingAuthority = await EvaluateFortnoxAuthorityAsync(bill, cancellationToken);
        var authorityIsConfigured = accountingAuthority.ReasonCode != AccountingAuthorityReasonCodes.AuthorityNotConfigured;
        var usesInternalAccounting = authorityIsConfigured &&
            accountingAuthority.Authority == AccountingAuthorityValues.InternalLedger;
        var canUseFortnoxAccounting = accountingAuthority.IsAllowed || !authorityIsConfigured;
        var operationalBillId = await _dbContext.FinanceBills
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.SourceDetectedBillId == bill.Id)
            .Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);
        var fortnoxRegistration = canUseFortnoxAccounting
            ? await BuildFortnoxRegistrationStateAsync(
                query.CompanyId,
                bill,
                canRequestFortnoxRegistration,
                BuildFortnoxRegistrationBlockedMessage(validationWarnings),
                cancellationToken)
            : null;

        return new FinanceBillInboxDetailDto(
            bill.Id,
            bill.SupplierName ?? "Unknown supplier",
            bill.SupplierOrgNumber,
            bill.InvoiceNumber ?? bill.SourceAttachmentId ?? bill.Id.ToString("D"),
            bill.InvoiceDateUtc,
            bill.DueDateUtc,
            bill.TotalAmount,
            bill.VatAmount,
            bill.Currency,
            FormatBillStatus(status),
            bill.Confidence,
            FormatStatus(bill.ConfidenceLevel),
            bill.Fields.OrderBy(x => x.FieldName).Select(MapField).ToList(),
            validationWarnings,
            duplicateWarnings,
            proposalSummary,
            sourcePreview,
            state?.Actions.OrderByDescending(x => x.OccurredUtc).Select(MapAction).ToList() ?? [],
            !hasUnresolvedValidationFailures && !string.Equals(status, FinanceBillInboxStatuses.Approved, StringComparison.OrdinalIgnoreCase),
            hasUnresolvedValidationFailures ? "Resolve validation failures before approving this bill." : null,
            usesInternalAccounting,
            canUseFortnoxAccounting,
            accountingAuthority.Explanation,
            operationalBillId,
            fortnoxRegistration);
    }

    private async Task<FinanceBillSourcePreviewDto?> LoadSourcePreviewAsync(
        Guid companyId,
        DetectedBill bill,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(bill.SourceEmailId))
        {
            return null;
        }

        var sourceEmailId = bill.SourceEmailId.Trim();
        var hasSnapshotId = Guid.TryParse(sourceEmailId, out var snapshotId);
        var snapshot = await _dbContext.EmailMessageSnapshots
            .IgnoreQueryFilters()
            .Include(x => x.Attachments)
            .Where(x => x.CompanyId == companyId &&
                ((hasSnapshotId && x.Id == snapshotId) || x.ExternalMessageId == sourceEmailId))
            .OrderByDescending(x => x.CreatedUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (snapshot is null)
        {
            return null;
        }

        var attachment = ResolveSourceAttachment(snapshot, bill.SourceAttachmentId);
        if (!HasPreviewContent(snapshot, attachment))
        {
            await RehydrateSourceSnapshotAsync(companyId, snapshot, attachment, bill.SourceAttachmentId, cancellationToken);
            attachment = ResolveSourceAttachment(snapshot, bill.SourceAttachmentId);
        }

        var title = string.IsNullOrWhiteSpace(snapshot.Subject) ? "Email invoice" : snapshot.Subject.Trim();
        var from = FormatEmailSender(snapshot.FromDisplayName, snapshot.FromAddress);
        if (attachment is not null && !string.IsNullOrWhiteSpace(attachment.UntrustedExtractedText))
        {
            return new FinanceBillSourcePreviewDto(
                title,
                from,
                snapshot.ReceivedUtc,
                attachment.UntrustedExtractedText.Trim(),
                FormatSourceLabel(attachment.SourceType),
                attachment.FileName);
        }

        return new FinanceBillSourcePreviewDto(
            title,
            from,
            snapshot.ReceivedUtc,
            string.IsNullOrWhiteSpace(snapshot.UntrustedBodyText) ? null : snapshot.UntrustedBodyText.Trim());
    }

    private static bool HasPreviewContent(EmailMessageSnapshot snapshot, EmailAttachmentSnapshot? attachment) =>
        attachment is not null && !string.IsNullOrWhiteSpace(attachment.UntrustedExtractedText) ||
        !string.IsNullOrWhiteSpace(snapshot.UntrustedBodyText);

    private async Task RehydrateSourceSnapshotAsync(
        Guid companyId,
        EmailMessageSnapshot snapshot,
        EmailAttachmentSnapshot? sourceAttachment,
        string? sourceAttachmentId,
        CancellationToken cancellationToken)
    {
        if (_mailboxProviderRegistry is null || _fieldEncryption is null)
        {
            return;
        }

        var connection = await _dbContext.MailboxConnections
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                x => x.CompanyId == companyId && x.Id == snapshot.MailboxConnectionId,
                cancellationToken);
        if (connection is null ||
            connection.Status is not (MailboxConnectionStatus.Active or MailboxConnectionStatus.Failed) ||
            string.IsNullOrWhiteSpace(connection.EncryptedAccessToken))
        {
            return;
        }

        try
        {
            var provider = _mailboxProviderRegistry.Resolve(connection.Provider);
            var accessToken = await GetMailboxAccessTokenAsync(provider, connection, forceRefresh: false, cancellationToken);

            try
            {
                await RehydrateMessageBodyAsync(provider, accessToken, snapshot, cancellationToken);
                await EnsureSourceAttachmentSnapshotsAsync(provider, accessToken, connection, snapshot, sourceAttachmentId, cancellationToken);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                accessToken = await GetMailboxAccessTokenAsync(provider, connection, forceRefresh: true, cancellationToken);
                await RehydrateMessageBodyAsync(provider, accessToken, snapshot, cancellationToken);
                await EnsureSourceAttachmentSnapshotsAsync(provider, accessToken, connection, snapshot, sourceAttachmentId, cancellationToken);
            }

            var attachment = sourceAttachment is not null && snapshot.Attachments.Contains(sourceAttachment)
                ? sourceAttachment
                : ResolveSourceAttachment(snapshot, sourceAttachmentId);
            try
            {
                await RehydrateAttachmentTextAsync(provider, accessToken, companyId, snapshot, attachment, cancellationToken);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                accessToken = await GetMailboxAccessTokenAsync(provider, connection, forceRefresh: true, cancellationToken);
                await RehydrateAttachmentTextAsync(provider, accessToken, companyId, snapshot, attachment, cancellationToken);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Bill inbox source preview rehydration failed. CompanyId: {CompanyId}. SnapshotId: {SnapshotId}. MessageId: {MessageId}.",
                companyId,
                snapshot.Id,
                snapshot.ExternalMessageId);
        }
    }

    private async Task<string> GetMailboxAccessTokenAsync(
        IMailboxProviderClient provider,
        MailboxConnection connection,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        if (_fieldEncryption is null)
        {
            throw new InvalidOperationException("Mailbox credential encryption service is unavailable.");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (!forceRefresh &&
            !string.IsNullOrWhiteSpace(connection.EncryptedAccessToken) &&
            (!connection.AccessTokenExpiresUtc.HasValue || connection.AccessTokenExpiresUtc.Value > now.AddMinutes(5)))
        {
            return _fieldEncryption.Decrypt(
                connection.CompanyId,
                MailboxConnectionDefaults.TokenPurpose(connection.Provider, "access_token"),
                connection.EncryptedAccessToken);
        }

        if (string.IsNullOrWhiteSpace(connection.EncryptedRefreshToken))
        {
            if (!string.IsNullOrWhiteSpace(connection.EncryptedAccessToken))
            {
                return _fieldEncryption.Decrypt(
                    connection.CompanyId,
                    MailboxConnectionDefaults.TokenPurpose(connection.Provider, "access_token"),
                    connection.EncryptedAccessToken);
            }

            throw new InvalidOperationException("Mailbox access token is missing.");
        }

        var refreshToken = _fieldEncryption.Decrypt(
            connection.CompanyId,
            MailboxConnectionDefaults.TokenPurpose(connection.Provider, "refresh_token"),
            connection.EncryptedRefreshToken);
        var tokenResult = await provider.RefreshTokenAsync(new MailboxRefreshTokenRequest(refreshToken), cancellationToken);

        connection.StoreEncryptedCredentials(
            _fieldEncryption.Encrypt(
                connection.CompanyId,
                MailboxConnectionDefaults.TokenPurpose(connection.Provider, "access_token"),
                tokenResult.AccessToken),
            string.IsNullOrWhiteSpace(tokenResult.RefreshToken)
                ? connection.EncryptedRefreshToken
                : _fieldEncryption.Encrypt(
                    connection.CompanyId,
                    MailboxConnectionDefaults.TokenPurpose(connection.Provider, "refresh_token"),
                    tokenResult.RefreshToken),
            tokenResult.AccessTokenExpiresUtc,
            tokenResult.GrantedScopes.Count > 0 ? tokenResult.GrantedScopes : connection.GrantedScopes);
        connection.SetStatus(MailboxConnectionStatus.Active);

        return tokenResult.AccessToken;
    }

    private static async Task RehydrateMessageBodyAsync(
        IMailboxProviderClient provider,
        string accessToken,
        EmailMessageSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.UntrustedBodyText))
        {
            return;
        }

        var message = await provider.GetMessageAsync(
            accessToken,
            new MailboxMessageFetchRequest(snapshot.ExternalMessageId),
            cancellationToken);
        var bodyText = !string.IsNullOrWhiteSpace(message.PlainTextBody)
            ? message.PlainTextBody
            : message.HtmlBody;
        if (!string.IsNullOrWhiteSpace(bodyText))
        {
            snapshot.UpdateUntrustedBodyText(bodyText);
        }
    }

    private async Task EnsureSourceAttachmentSnapshotsAsync(
        IMailboxProviderClient provider,
        string accessToken,
        MailboxConnection connection,
        EmailMessageSnapshot snapshot,
        string? sourceAttachmentId,
        CancellationToken cancellationToken)
    {
        if (snapshot.Attachments.Count > 0 || string.IsNullOrWhiteSpace(sourceAttachmentId))
        {
            return;
        }

        var scanFromUtc = snapshot.ReceivedUtc?.AddMinutes(-5) ?? snapshot.CreatedUtc.AddDays(-1);
        var scanToUtc = snapshot.ReceivedUtc?.AddMinutes(5) ?? snapshot.CreatedUtc.AddDays(1);
        var summaries = await provider.ListMessagesAsync(
            accessToken,
            new MailboxMessageQuery(
                scanFromUtc,
                scanToUtc,
                MailboxConnectionDefaults.NormalizeFolders(connection.ConfiguredFolders, connection.Provider)),
            cancellationToken);
        var summary = summaries.FirstOrDefault(x => string.Equals(x.ProviderMessageId, snapshot.ExternalMessageId, StringComparison.OrdinalIgnoreCase));
        if (summary is null)
        {
            return;
        }

        foreach (var attachment in summary.AttachmentSummaries)
        {
            if (!IsSupportedAttachment(attachment))
            {
                continue;
            }

            var sourceType = ResolveAttachmentSourceType(attachment);
            snapshot.Attachments.Add(new EmailAttachmentSnapshot(
                Guid.NewGuid(),
                snapshot.CompanyId,
                snapshot.Id,
                attachment.ExternalAttachmentId,
                attachment.FileName,
                attachment.MimeType,
                attachment.SizeBytes,
                MailboxAttachmentHash.ComputeDeterministicHash(attachment),
                attachment.StorageReference,
                sourceType,
                attachment.UntrustedExtractedText,
                false,
                null,
                _timeProvider.GetUtcNow().UtcDateTime));
        }
    }

    private async Task RehydrateAttachmentTextAsync(
        IMailboxProviderClient provider,
        string accessToken,
        Guid companyId,
        EmailMessageSnapshot snapshot,
        EmailAttachmentSnapshot? attachment,
        CancellationToken cancellationToken)
    {
        if (attachment is null ||
            !string.IsNullOrWhiteSpace(attachment.UntrustedExtractedText) ||
            string.IsNullOrWhiteSpace(attachment.ExternalAttachmentId))
        {
            return;
        }

        var inputType = GetAttachmentInputType(attachment.SourceType);
        if (inputType is null)
        {
            return;
        }

        var content = await provider.GetAttachmentContentAsync(
            accessToken,
            new MailboxAttachmentFetchRequest(
                snapshot.ExternalMessageId,
                attachment.ExternalAttachmentId,
                attachment.FileName,
                attachment.MimeType),
            cancellationToken);
        if (content is null || content.Content.Length == 0)
        {
            return;
        }

        var extractedText = await ExtractAttachmentTextAsync(
            inputType.Value,
            content.Content,
            attachment.FileName ?? attachment.ExternalAttachmentId ?? "mailbox-attachment",
            cancellationToken);
        if (!string.IsNullOrWhiteSpace(extractedText))
        {
            attachment.UpdateUntrustedExtractedText(extractedText);
        }
    }

    private async Task<string?> ExtractAttachmentTextAsync(
        BillDocumentInputType inputType,
        byte[] content,
        string sourceDocumentName,
        CancellationToken cancellationToken)
    {
        foreach (var extractor in _documentTextExtractors.Where(x => x.Supports(inputType)))
        {
            await using var stream = new MemoryStream(content);
            var document = await extractor.ExtractAsync(stream, sourceDocumentName, inputType, cancellationToken);
            if (!string.IsNullOrWhiteSpace(document.FullText))
            {
                return document.FullText.Trim();
            }
        }

        return null;
    }

    private static bool IsSupportedAttachment(MailboxAttachmentSummary attachment) =>
        ResolveAttachmentSourceType(attachment) is BillSourceType.PdfAttachment or BillSourceType.DocxAttachment or BillSourceType.ImageAttachment;

    private static BillSourceType ResolveAttachmentSourceType(MailboxAttachmentSummary attachment)
    {
        var fileName = attachment.FileName ?? attachment.ExternalAttachmentId;
        var mimeType = attachment.MimeType ?? string.Empty;
        if (mimeType.Contains("pdf", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return BillSourceType.PdfAttachment;
        }

        if (mimeType.Contains("wordprocessingml", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
        {
            return BillSourceType.DocxAttachment;
        }

        if (mimeType.Equals("image/png", StringComparison.OrdinalIgnoreCase) ||
            mimeType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase) ||
            mimeType.Equals("image/webp", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
        {
            return BillSourceType.ImageAttachment;
        }

        return BillSourceType.EmailBodyOnly;
    }

    private static BillDocumentInputType? GetAttachmentInputType(BillSourceType sourceType) =>
        sourceType switch
        {
            BillSourceType.PdfAttachment => BillDocumentInputType.Pdf,
            BillSourceType.DocxAttachment => BillDocumentInputType.Docx,
            BillSourceType.ImageAttachment => BillDocumentInputType.Image,
            _ => null
        };

    private static EmailAttachmentSnapshot? ResolveSourceAttachment(EmailMessageSnapshot snapshot, string? sourceAttachmentId)
    {
        if (snapshot.Attachments.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(sourceAttachmentId))
        {
            var source = sourceAttachmentId.Trim();
            if (Guid.TryParse(source, out var attachmentSnapshotId))
            {
                var bySnapshotId = snapshot.Attachments.FirstOrDefault(x => x.Id == attachmentSnapshotId);
                if (bySnapshotId is not null)
                {
                    return bySnapshotId;
                }
            }

            var byExternalId = snapshot.Attachments.FirstOrDefault(x =>
                string.Equals(x.ExternalAttachmentId, source, StringComparison.OrdinalIgnoreCase));
            if (byExternalId is not null)
            {
                return byExternalId;
            }
        }

        return snapshot.Attachments
            .Where(x => !string.IsNullOrWhiteSpace(x.UntrustedExtractedText))
            .OrderBy(x => x.CreatedUtc)
            .FirstOrDefault();
    }

    private static string FormatSourceLabel(VirtualCompany.Domain.Enums.BillSourceType sourceType) =>
        sourceType switch
        {
            VirtualCompany.Domain.Enums.BillSourceType.PdfAttachment => "PDF attachment",
            VirtualCompany.Domain.Enums.BillSourceType.DocxAttachment => "Word attachment",
            VirtualCompany.Domain.Enums.BillSourceType.ImageAttachment => "Image attachment",
            VirtualCompany.Domain.Enums.BillSourceType.EmailBodyOnly => "Email body",
            _ => "Attachment"
        };

}
