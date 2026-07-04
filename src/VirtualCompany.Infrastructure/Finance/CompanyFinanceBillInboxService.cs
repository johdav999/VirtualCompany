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
using VirtualCompany.Infrastructure.Mailbox;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Security;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class CompanyFinanceBillInboxService : IFinanceBillInboxService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyContextAccessor? _companyContextAccessor;
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly IFinanceIntegrationWriteCommandService _financeWriteCommands;
    private readonly IFortnoxOutboundActionExecutor _fortnoxOutboundActionExecutor;
    private readonly IFortnoxApiClient _fortnoxApiClient;
    private readonly IMailboxProviderRegistry? _mailboxProviderRegistry;
    private readonly IFieldEncryptionService? _fieldEncryption;
    private readonly IReadOnlyList<IDocumentTextExtractor> _documentTextExtractors;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CompanyFinanceBillInboxService> _logger;

    public CompanyFinanceBillInboxService(
        VirtualCompanyDbContext dbContext,
        IAuditEventWriter auditEventWriter,
        IFinanceIntegrationWriteCommandService financeWriteCommands,
        IFortnoxOutboundActionExecutor fortnoxOutboundActionExecutor,
        IFortnoxApiClient fortnoxApiClient,
        TimeProvider timeProvider,
        ILogger<CompanyFinanceBillInboxService> logger,
        ICompanyContextAccessor? companyContextAccessor = null,
        IMailboxProviderRegistry? mailboxProviderRegistry = null,
        IFieldEncryptionService? fieldEncryption = null,
        IEnumerable<IDocumentTextExtractor>? documentTextExtractors = null)
    {
        _dbContext = dbContext;
        _auditEventWriter = auditEventWriter;
        _financeWriteCommands = financeWriteCommands;
        _fortnoxOutboundActionExecutor = fortnoxOutboundActionExecutor;
        _fortnoxApiClient = fortnoxApiClient;
        _mailboxProviderRegistry = mailboxProviderRegistry;
        _fieldEncryption = fieldEncryption;
        _documentTextExtractors = documentTextExtractors?.ToArray() ?? [];
        _timeProvider = timeProvider;
        _logger = logger;
        _companyContextAccessor = companyContextAccessor;
    }

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
            .OrderByDescending(x => x.Bill.UpdatedUtc)
            .ThenByDescending(x => x.Bill.CreatedUtc)
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
        var fortnoxRegistration = await BuildFortnoxRegistrationStateAsync(
            query.CompanyId,
            bill,
            canRequestFortnoxRegistration,
            BuildFortnoxRegistrationBlockedMessage(validationWarnings),
            cancellationToken);

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
                CompanyMailboxConnectionService.BuildTokenPurpose(connection.Provider, "access_token"),
                connection.EncryptedAccessToken);
        }

        if (string.IsNullOrWhiteSpace(connection.EncryptedRefreshToken))
        {
            if (!string.IsNullOrWhiteSpace(connection.EncryptedAccessToken))
            {
                return _fieldEncryption.Decrypt(
                    connection.CompanyId,
                    CompanyMailboxConnectionService.BuildTokenPurpose(connection.Provider, "access_token"),
                    connection.EncryptedAccessToken);
            }

            throw new InvalidOperationException("Mailbox access token is missing.");
        }

        var refreshToken = _fieldEncryption.Decrypt(
            connection.CompanyId,
            CompanyMailboxConnectionService.BuildTokenPurpose(connection.Provider, "refresh_token"),
            connection.EncryptedRefreshToken);
        var tokenResult = await provider.RefreshTokenAsync(new MailboxRefreshTokenRequest(refreshToken), cancellationToken);

        connection.StoreEncryptedCredentials(
            _fieldEncryption.Encrypt(
                connection.CompanyId,
                CompanyMailboxConnectionService.BuildTokenPurpose(connection.Provider, "access_token"),
                tokenResult.AccessToken),
            string.IsNullOrWhiteSpace(tokenResult.RefreshToken)
                ? connection.EncryptedRefreshToken
                : _fieldEncryption.Encrypt(
                    connection.CompanyId,
                    CompanyMailboxConnectionService.BuildTokenPurpose(connection.Provider, "refresh_token"),
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
                CompanyMailboxConnectionService.NormalizeFolders(connection.ConfiguredFolders, connection.Provider)),
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

    public async Task<FinanceBillReviewActionResultDto> ApproveAsync(ApproveFinanceBillCommand command, CancellationToken cancellationToken)
    {
        return await ExecuteReviewActionWithConcurrencyRetryAsync(
            "approve",
            command.CompanyId,
            command.BillId,
            () => ApproveOnceAsync(command, cancellationToken),
            cancellationToken);
    }

    private async Task<FinanceBillReviewActionResultDto> ApproveOnceAsync(ApproveFinanceBillCommand command, CancellationToken cancellationToken)
    {
        return await ExecuteReviewTransitionOnceAsync(
            command.CompanyId,
            command.BillId,
            command.ActorUserId,
            command.ActorDisplayName,
            command.Rationale,
            "approve",
            FinanceBillInboxStatuses.Approved,
            "finance.bill_inbox.approved",
            AuditEventOutcomes.Approved,
            addApprovalProposal: true,
            blockOnValidationFailures: true,
            cancellationToken);
    }

    public async Task<FinanceBillReviewActionResultDto> RejectAsync(RejectFinanceBillCommand command, CancellationToken cancellationToken)
    {
        return await ExecuteReviewActionWithConcurrencyRetryAsync(
            "reject",
            command.CompanyId,
            command.BillId,
            () => RejectOnceAsync(command, cancellationToken),
            cancellationToken);
    }

    private async Task<FinanceBillReviewActionResultDto> RejectOnceAsync(RejectFinanceBillCommand command, CancellationToken cancellationToken)
    {
        return await ExecuteReviewTransitionOnceAsync(
            command.CompanyId,
            command.BillId,
            command.ActorUserId,
            command.ActorDisplayName,
            command.Rationale,
            "reject",
            FinanceBillInboxStatuses.Rejected,
            "finance.bill_inbox.rejected",
            AuditEventOutcomes.Rejected,
            addApprovalProposal: false,
            blockOnValidationFailures: false,
            cancellationToken);
    }

    public async Task<FinanceBillReviewActionResultDto> RequestClarificationAsync(RequestFinanceBillClarificationCommand command, CancellationToken cancellationToken)
    {
        return await ExecuteReviewActionWithConcurrencyRetryAsync(
            "request clarification",
            command.CompanyId,
            command.BillId,
            () => RequestClarificationOnceAsync(command, cancellationToken),
            cancellationToken);
    }

    private async Task<FinanceBillReviewActionResultDto> RequestClarificationOnceAsync(RequestFinanceBillClarificationCommand command, CancellationToken cancellationToken)
    {
        return await ExecuteReviewTransitionOnceAsync(
            command.CompanyId,
            command.BillId,
            command.ActorUserId,
            command.ActorDisplayName,
            command.Rationale,
            "clarification_requested",
            FinanceBillInboxStatuses.NeedsReview,
            "finance.bill_inbox.clarification_requested",
            AuditEventOutcomes.Requested,
            addApprovalProposal: false,
            blockOnValidationFailures: false,
            cancellationToken);
    }

    private async Task<FinanceBillReviewActionResultDto> ExecuteReviewTransitionOnceAsync(
        Guid companyId,
        Guid billId,
        Guid? actorUserId,
        string actorDisplayName,
        string rationale,
        string actionName,
        string newStatus,
        string auditActionName,
        string auditOutcome,
        bool addApprovalProposal,
        bool blockOnValidationFailures,
        CancellationToken cancellationToken)
    {
        EnsureTenant(companyId);
        var bill = await LoadBillForReviewActionAsync(companyId, billId, cancellationToken);
        var validationWarnings = ParseValidationWarnings(bill);
        if (blockOnValidationFailures && HasUnresolvedValidationFailures(bill, validationWarnings))
        {
            throw new InvalidOperationException("Finance bill approval is blocked while validation failures are unresolved.");
        }

        var state = await _dbContext.FinanceBillReviewStates
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.DetectedBillId == billId, cancellationToken);
        var priorStatus = FinanceBillInboxStatuses.Normalize(ResolveInboxStatus(bill, state));
        EnsureActiveReviewStatus(priorStatus, actionName);

        var occurredUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var reviewStateId = state?.Id ?? Guid.NewGuid();
        var proposalSummary = BuildProposalSummary(bill, validationWarnings, BuildDuplicateWarnings(bill), state?.ProposalSummary).Summary;

        if (state is null)
        {
            _dbContext.FinanceBillReviewStates.Add(new FinanceBillReviewState(
                reviewStateId,
                companyId,
                billId,
                newStatus,
                proposalSummary,
                occurredUtc,
                occurredUtc));
        }
        else
        {
            var updatedRows = await _dbContext.FinanceBillReviewStates
                .IgnoreQueryFilters()
                .Where(x => x.CompanyId == companyId && x.Id == reviewStateId)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(x => x.Status, newStatus)
                        .SetProperty(x => x.UpdatedUtc, occurredUtc),
                    cancellationToken);
            if (updatedRows == 0)
            {
                throw new DbUpdateConcurrencyException("The finance bill review state changed before the review action could be saved.");
            }
        }

        var action = new FinanceBillReviewAction(
            Guid.NewGuid(),
            companyId,
            reviewStateId,
            billId,
            actionName,
            actorUserId,
            actorDisplayName,
            priorStatus,
            newStatus,
            rationale,
            occurredUtc);
        _dbContext.FinanceBillReviewActions.Add(action);

        if (addApprovalProposal)
        {
            var existingProposal = await _dbContext.BillApprovalProposals
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(x => x.CompanyId == companyId && x.DetectedBillId == billId, cancellationToken);
            if (!existingProposal)
            {
                _dbContext.BillApprovalProposals.Add(new BillApprovalProposal(
                    Guid.NewGuid(),
                    companyId,
                    billId,
                    reviewStateId,
                    proposalSummary,
                    actorUserId,
                    occurredUtc));
            }
        }

        await WriteAuditAsync(companyId, actorUserId, auditActionName, billId, auditOutcome, action, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new FinanceBillReviewActionResultDto(billId, FormatStatus(priorStatus), FormatStatus(newStatus), occurredUtc);
    }

    private async Task<T> ExecuteReviewActionWithConcurrencyRetryAsync<T>(
        string actionName,
        Guid companyId,
        Guid billId,
        Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 2;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (DbUpdateConcurrencyException exception) when (attempt < maxAttempts)
            {
                _logger.LogWarning(
                    exception,
                    "Bill inbox review action hit a concurrency conflict; retrying with a fresh change tracker. Action: {ActionName}. CompanyId: {CompanyId}. BillId: {BillId}. Attempt: {Attempt}.",
                    actionName,
                    companyId,
                    billId,
                    attempt);
                _dbContext.ChangeTracker.Clear();
            }
        }

        throw new InvalidOperationException("Review action retry loop exited unexpectedly.");
    }

    public async Task<FinanceBillFortnoxRegistrationDto> RequestFortnoxRegistrationAsync(RequestFinanceBillFortnoxRegistrationCommand command, CancellationToken cancellationToken)
    {
        EnsureTenant(command.CompanyId);
        _logger.LogInformation(
            "Bill inbox Fortnox registration service request started. CompanyId: {CompanyId}. BillId: {BillId}. ActorUserId: {ActorUserId}. HasRationale: {HasRationale}.",
            command.CompanyId,
            command.BillId,
            command.ActorUserId,
            !string.IsNullOrWhiteSpace(command.Rationale));

        if (string.IsNullOrWhiteSpace(command.Rationale))
        {
            _logger.LogWarning(
                "Bill inbox Fortnox registration service request blocked because rationale is missing. CompanyId: {CompanyId}. BillId: {BillId}.",
                command.CompanyId,
                command.BillId);
            throw new ArgumentException("A rationale is required before requesting Fortnox registration.", nameof(command.Rationale));
        }

        var bill = await LoadBillForWriteAsync(command.CompanyId, command.BillId, cancellationToken);
        var state = await LoadOrCreateStateAsync(bill, cancellationToken);
        _logger.LogInformation(
            "Bill inbox Fortnox registration loaded bill. CompanyId: {CompanyId}. BillId: {BillId}. InvoiceNumber: {InvoiceNumber}. SupplierName: {SupplierName}. TotalAmount: {TotalAmount}. Currency: {Currency}. ReviewStatus: {ReviewStatus}.",
            command.CompanyId,
            bill.Id,
            bill.InvoiceNumber,
            bill.SupplierName,
            bill.TotalAmount,
            bill.Currency,
            state.Status);

        if (state.Status is FinanceBillInboxStatuses.Rejected or FinanceBillInboxStatuses.SentToPaymentExported)
        {
            _logger.LogWarning(
                "Bill inbox Fortnox registration service request blocked because the review status is not active. CompanyId: {CompanyId}. BillId: {BillId}. ReviewStatus: {ReviewStatus}.",
                command.CompanyId,
                bill.Id,
                state.Status);
            throw new InvalidOperationException("Only approved or active bill reviews can be registered in Fortnox.");
        }

        var validationWarnings = ParseValidationWarnings(bill);
        if (HasUnresolvedValidationFailures(bill, validationWarnings))
        {
            _logger.LogWarning(
                "Bill inbox Fortnox registration service request blocked because validation failures remain. CompanyId: {CompanyId}. BillId: {BillId}. WarningCount: {WarningCount}.",
                command.CompanyId,
                bill.Id,
                validationWarnings.Count);
            throw new InvalidOperationException("Resolve validation failures before requesting Fortnox registration.");
        }

        var connection = await ResolveActiveFortnoxConnectionAsync(command.CompanyId, cancellationToken);
        var supplierMatch = await TryResolveFortnoxSupplierNumberAsync(command.CompanyId, connection.Id, bill, cancellationToken);
        if (!supplierMatch.Found)
        {
            return await RequestFortnoxSupplierCreationAsync(command, bill, connection, cancellationToken);
        }

        var supplierNumber = supplierMatch.SupplierNumber!;
        _logger.LogInformation(
            "Bill inbox Fortnox registration resolved Fortnox connection and supplier. CompanyId: {CompanyId}. BillId: {BillId}. ConnectionId: {ConnectionId}. SupplierNumber: {SupplierNumber}.",
            command.CompanyId,
            bill.Id,
            connection.Id,
            supplierNumber);

        var payload = BuildSupplierInvoicePayload(bill, supplierNumber);
        var writeRequestId = CreateFortnoxRegistrationWriteRequestId(bill.Id);
        _logger.LogInformation(
            "Bill inbox Fortnox registration write command approval request starting. CompanyId: {CompanyId}. BillId: {BillId}. WriteRequestId: {WriteRequestId}. FortnoxPath: {FortnoxPath}.",
            command.CompanyId,
            bill.Id,
            writeRequestId,
            "supplierinvoices");

        var writeResult = await _financeWriteCommands.RequestApprovalAsync(
            new FinanceIntegrationWriteCommand(
                FinanceIntegrationProviderKeys.Fortnox,
                command.CompanyId,
                connection.Id,
                command.ActorUserId,
                FinanceIntegrationWriteCommandTypes.InvoiceExport,
                "POST",
                "supplierinvoices",
                bill.SupplierName ?? "Fortnox supplier",
                CreateRegistrationSummary(bill),
                FortnoxWritePayloadSanitizer.CreatePayloadHash(payload),
                new FinanceIntegrationWritePayload(FortnoxWritePayloadSanitizer.CreateSanitizedJson(payload), "SupplierInvoiceRegistration"),
                writeRequestId,
                $"finance-bill-inbox:{bill.Id:N}:fortnox-registration"),
            cancellationToken);
        _logger.LogInformation(
            "Bill inbox Fortnox registration write command approval request completed. CompanyId: {CompanyId}. BillId: {BillId}. WriteRequestId: {WriteRequestId}. ApprovalId: {ApprovalId}. Status: {Status}.",
            command.CompanyId,
            bill.Id,
            writeResult.WriteRequestId,
            writeResult.ApprovalId,
            writeResult.Status);

        _logger.LogInformation(
            "Bill inbox Fortnox registration service request completed without secondary bill review mutation. CompanyId: {CompanyId}. BillId: {BillId}. WriteRequestId: {WriteRequestId}. ApprovalId: {ApprovalId}. Status: {Status}.",
            command.CompanyId,
            bill.Id,
            writeResult.WriteRequestId,
            writeResult.ApprovalId,
            writeResult.Status);

        return MapFortnoxRegistration(writeResult, canRequest: false);
    }

    public async Task<FinanceBillFortnoxRegistrationDto> ExecuteFortnoxRegistrationAsync(ExecuteFinanceBillFortnoxRegistrationCommand command, CancellationToken cancellationToken)
    {
        EnsureTenant(command.CompanyId);
        var writeRequestId = CreateFortnoxRegistrationWriteRequestId(command.BillId);
        _logger.LogInformation(
            "Bill inbox Fortnox registration execute started. CompanyId: {CompanyId}. BillId: {BillId}. WriteRequestId: {WriteRequestId}.",
            command.CompanyId,
            command.BillId,
            writeRequestId);

        var result = await _fortnoxOutboundActionExecutor.ExecuteApprovedAsync(command.CompanyId, writeRequestId, cancellationToken);
        _logger.LogInformation(
            "Bill inbox Fortnox registration execute completed. CompanyId: {CompanyId}. BillId: {BillId}. WriteRequestId: {WriteRequestId}. ApprovalId: {ApprovalId}. Status: {Status}. Executed: {Executed}.",
            command.CompanyId,
            command.BillId,
            writeRequestId,
            result.ApprovalId,
            result.Status,
            result.Executed);

        return new FinanceBillFortnoxRegistrationDto(
            result.WriteRequestId,
            result.ApprovalId,
            FormatWriteStatus(result.Status),
            result.Summary,
            CanRequest: false,
            CanSendDirect: false,
            CanExecute: false,
            HasPendingRequest: !result.Executed,
            HasExecuted: result.Executed || string.Equals(result.Status, FinanceIntegrationWriteCommandRecordStatuses.Executed, StringComparison.OrdinalIgnoreCase),
            FortnoxPath: "supplierinvoices");
    }

    public async Task<FinanceBillFortnoxRegistrationDto> SendFortnoxRegistrationDirectAsync(ExecuteFinanceBillFortnoxRegistrationCommand command, CancellationToken cancellationToken)
    {
        EnsureTenant(command.CompanyId);
        var bill = await LoadBillForWriteAsync(command.CompanyId, command.BillId, cancellationToken);
        var validationWarnings = ParseValidationWarnings(bill);
        if (HasUnresolvedValidationFailures(bill, validationWarnings))
        {
            throw new InvalidOperationException("Resolve validation failures before sending this bill to Fortnox.");
        }

        var connection = await ResolveActiveFortnoxConnectionAsync(command.CompanyId, cancellationToken);
        var supplierMatch = await TryResolveFortnoxSupplierNumberAsync(command.CompanyId, connection.Id, bill, cancellationToken);
        if (!supplierMatch.Found)
        {
            return await SendOrExecuteFortnoxSupplierCreationAsync(command, bill, connection, supplierMatch, cancellationToken);
        }

        var supplierNumber = supplierMatch.SupplierNumber!;
        var payload = BuildSupplierInvoicePayload(bill, supplierNumber);
        var writeRequestId = CreateFortnoxRegistrationWriteRequestId(bill.Id);
        var payloadHash = FortnoxWritePayloadSanitizer.CreatePayloadHash(payload);
        var sanitizedPayload = FortnoxWritePayloadSanitizer.CreateSanitizedJson(payload);
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var writeCommand = await _dbContext.FinanceIntegrationWriteCommands
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == writeRequestId, cancellationToken);

        if (writeCommand is not null &&
            writeCommand.Status == FinanceIntegrationWriteCommandRecordStatuses.Executed &&
            string.Equals(writeCommand.PayloadHash, payloadHash, StringComparison.OrdinalIgnoreCase))
        {
            return new FinanceBillFortnoxRegistrationDto(
                writeCommand.Id,
                writeCommand.ApprovalId,
                FormatWriteStatus(writeCommand.Status),
                "Fortnox already accepted this supplier invoice.",
                CanRequest: false,
                CanSendDirect: false,
                CanExecute: false,
                HasPendingRequest: false,
                HasExecuted: true,
                FortnoxPath: writeCommand.Path,
                ExternalId: writeCommand.ExternalId);
        }

        if (writeCommand is not null && writeCommand.Status == FinanceIntegrationWriteCommandRecordStatuses.Executed)
        {
            return new FinanceBillFortnoxRegistrationDto(
                writeCommand.Id,
                writeCommand.ApprovalId,
                "Sent with outdated details",
                "Fortnox accepted an earlier registration for this bill, but the extracted bill details have changed since then. Void or delete the old supplier invoice in Fortnox before sending a corrected invoice.",
                CanRequest: false,
                CanSendDirect: false,
                CanExecute: false,
                HasPendingRequest: false,
                HasExecuted: true,
                FortnoxPath: writeCommand.Path,
                ExternalId: writeCommand.ExternalId);
        }

        if (writeCommand is null)
        {
            writeCommand = new FinanceIntegrationWriteCommandRecord(
                writeRequestId,
                command.CompanyId,
                connection.Id,
                command.ActorUserId,
                FinanceIntegrationWriteCommandTypes.InvoiceExport,
                "POST",
                "supplierinvoices",
                await ResolveTargetCompanyAsync(command.CompanyId, cancellationToken),
                CreateRegistrationSummary(bill),
                payloadHash,
                sanitizedPayload,
                $"finance-bill-inbox:{bill.Id:N}:fortnox-registration",
                now);
            _dbContext.FinanceIntegrationWriteCommands.Add(writeCommand);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        else if (writeCommand.Status is FinanceIntegrationWriteCommandRecordStatuses.AwaitingApproval or
                 FinanceIntegrationWriteCommandRecordStatuses.Approved or
                 FinanceIntegrationWriteCommandRecordStatuses.Failed)
        {
            writeCommand.ReplaceUnexecutedRequest(
                connection.Id,
                command.ActorUserId,
                "POST",
                "supplierinvoices",
                await ResolveTargetCompanyAsync(command.CompanyId, cancellationToken),
                CreateRegistrationSummary(bill),
                payloadHash,
                sanitizedPayload,
                $"finance-bill-inbox:{bill.Id:N}:fortnox-registration",
                now);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        else if (writeCommand.Status is FinanceIntegrationWriteCommandRecordStatuses.Executing)
        {
            return new FinanceBillFortnoxRegistrationDto(
                writeCommand.Id,
                writeCommand.ApprovalId,
                FormatWriteStatus(writeCommand.Status),
                "This bill is already being sent to Fortnox.",
                CanRequest: false,
                CanSendDirect: false,
                CanExecute: false,
                HasPendingRequest: true,
                HasExecuted: false,
                FortnoxPath: writeCommand.Path,
                ExternalId: writeCommand.ExternalId);
        }
        else
        {
            throw new InvalidOperationException("This Fortnox registration cannot be sent from its current state.");
        }

        writeCommand.MarkApproved(Guid.Empty, command.ActorUserId, now);
        writeCommand.MarkExecutionStarted(now);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var integrationCommand = BuildFortnoxRegistrationCommand(
            command.CompanyId,
            connection.Id,
            command.ActorUserId,
            bill,
            payload,
            writeRequestId);
        await WriteFortnoxDirectAuditAsync(writeCommand, "write_execution_started", FinanceIntegrationAuditOutcomes.Succeeded, "User sent this supplier invoice to Fortnox.", cancellationToken);

        try
        {
            var context = new FortnoxRequestContext(command.CompanyId, connection.Id, integrationCommand.CorrelationId, ActorUserId: command.ActorUserId, WriteRequestId: writeRequestId);
            var response = await _fortnoxApiClient.PostDirectAsync<JsonNode?, JsonNode?>(context, "supplierinvoices", payload, cancellationToken);
            await _financeWriteCommands.RecordExecutionSucceededAsync(integrationCommand, response, cancellationToken);
            var refreshed = await _dbContext.FinanceIntegrationWriteCommands
                .IgnoreQueryFilters()
                .AsNoTracking()
                .SingleAsync(x => x.CompanyId == command.CompanyId && x.Id == writeRequestId, cancellationToken);
            await WriteFortnoxDirectAuditAsync(refreshed, "write_execution_succeeded", FinanceIntegrationAuditOutcomes.Succeeded, "Fortnox accepted this supplier invoice.", cancellationToken);
            return new FinanceBillFortnoxRegistrationDto(
                refreshed.Id,
                refreshed.ApprovalId,
                FormatWriteStatus(refreshed.Status),
                "Fortnox accepted this supplier invoice.",
                CanRequest: false,
                CanSendDirect: false,
                CanExecute: false,
                HasPendingRequest: false,
                HasExecuted: true,
                FortnoxPath: refreshed.Path,
                ExternalId: refreshed.ExternalId);
        }
        catch (Exception exception) when (exception is FortnoxApiException or HttpRequestException or TaskCanceledException)
        {
            await _financeWriteCommands.RecordExecutionFailedAsync(integrationCommand, exception, cancellationToken);
            var refreshed = await _dbContext.FinanceIntegrationWriteCommands
                .IgnoreQueryFilters()
                .AsNoTracking()
                .SingleAsync(x => x.CompanyId == command.CompanyId && x.Id == writeRequestId, cancellationToken);
            var safeSummary = exception is FortnoxApiException apiException
                ? apiException.SafeMessage
                : "Fortnox could not register this supplier invoice.";
            await WriteFortnoxDirectAuditAsync(refreshed, "write_execution_failed", FinanceIntegrationAuditOutcomes.Failed, safeSummary, cancellationToken);
            return new FinanceBillFortnoxRegistrationDto(
                refreshed.Id,
                refreshed.ApprovalId,
                FormatWriteStatus(refreshed.Status),
                safeSummary,
                CanRequest: false,
                CanSendDirect: true,
                CanExecute: false,
                HasPendingRequest: false,
                HasExecuted: false,
                FortnoxPath: refreshed.Path,
                ExternalId: refreshed.ExternalId);
        }
    }

    private async Task<FinanceBillFortnoxRegistrationDto> RequestFortnoxSupplierCreationAsync(
        RequestFinanceBillFortnoxRegistrationCommand command,
        DetectedBill bill,
        FinanceIntegrationConnection connection,
        CancellationToken cancellationToken)
    {
        var payload = BuildSupplierPayload(bill);
        var writeRequestId = CreateFortnoxSupplierCreationWriteRequestId(bill.Id);
        _logger.LogInformation(
            "Bill inbox Fortnox supplier creation approval request starting. CompanyId: {CompanyId}. BillId: {BillId}. SupplierName: {SupplierName}. SupplierOrgNumber: {SupplierOrgNumber}. WriteRequestId: {WriteRequestId}.",
            command.CompanyId,
            bill.Id,
            bill.SupplierName,
            bill.SupplierOrgNumber,
            writeRequestId);

        var result = await _financeWriteCommands.RequestApprovalAsync(
            new FinanceIntegrationWriteCommand(
                FinanceIntegrationProviderKeys.Fortnox,
                command.CompanyId,
                connection.Id,
                command.ActorUserId,
                FinanceIntegrationWriteCommandTypes.SupplierMasterData,
                "POST",
                "suppliers",
                bill.SupplierName ?? "Fortnox supplier",
                CreateSupplierCreationSummary(bill),
                FortnoxWritePayloadSanitizer.CreatePayloadHash(payload),
                new FinanceIntegrationWritePayload(FortnoxWritePayloadSanitizer.CreateSanitizedJson(payload), "SupplierCreation"),
                writeRequestId,
                $"finance-bill-inbox:{bill.Id:N}:fortnox-supplier-creation"),
            cancellationToken);

        await WriteSupplierCreationAuditAsync(
            command.CompanyId,
            connection.Id,
            writeRequestId,
            result.ApprovalId,
            "supplier_creation_requested",
            FinanceIntegrationAuditOutcomes.Succeeded,
            "Supplier creation approval was requested before registering this invoice.",
            cancellationToken);

        return new FinanceBillFortnoxRegistrationDto(
            result.WriteRequestId,
            result.ApprovalId,
            FormatWriteStatus(result.Status),
            "Create and approve this supplier in Fortnox before sending the invoice.",
            CanRequest: false,
            CanSendDirect: false,
            CanExecute: false,
            HasPendingRequest: result.Status is FinanceIntegrationWriteCommandRecordStatuses.AwaitingApproval or FinanceIntegrationWriteCommandRecordStatuses.Approved,
            HasExecuted: result.Status == FinanceIntegrationWriteCommandRecordStatuses.Executed,
            FortnoxPath: "suppliers",
            ActionKind: "supplier_creation");
    }

    private async Task<FinanceBillFortnoxRegistrationDto> SendOrExecuteFortnoxSupplierCreationAsync(
        ExecuteFinanceBillFortnoxRegistrationCommand command,
        DetectedBill bill,
        FinanceIntegrationConnection connection,
        SupplierResolutionResult supplierMatch,
        CancellationToken cancellationToken)
    {
        var writeRequestId = CreateFortnoxSupplierCreationWriteRequestId(bill.Id);
        var writeCommand = await _dbContext.FinanceIntegrationWriteCommands
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == writeRequestId, cancellationToken);

        if (writeCommand is null)
        {
            return new FinanceBillFortnoxRegistrationDto(
                null,
                null,
                "Supplier approval needed",
                "Create a supplier proposal before this invoice can be sent to Fortnox.",
                CanRequest: true,
                CanSendDirect: false,
                CanExecute: false,
                HasPendingRequest: false,
                HasExecuted: false,
                FortnoxPath: "suppliers",
                ActionKind: "supplier_creation");
        }

        if (writeCommand.Status == FinanceIntegrationWriteCommandRecordStatuses.Executed)
        {
            var supplierNumber = await EnsureSupplierReferenceFromWriteCommandAsync(
                command.CompanyId,
                connection.Id,
                bill,
                writeCommand,
                cancellationToken);

            return new FinanceBillFortnoxRegistrationDto(
                writeCommand.Id,
                writeCommand.ApprovalId,
                "Supplier ready",
                $"Fortnox created supplier {supplierNumber}. You can now send the invoice.",
                CanRequest: false,
                CanSendDirect: true,
                CanExecute: false,
                HasPendingRequest: false,
                HasExecuted: true,
                FortnoxPath: "supplierinvoices",
                ExternalId: supplierNumber,
                ActionKind: "invoice_registration");
        }

        if (writeCommand.Status is not FinanceIntegrationWriteCommandRecordStatuses.Approved and not FinanceIntegrationWriteCommandRecordStatuses.Failed)
        {
            return new FinanceBillFortnoxRegistrationDto(
                writeCommand.Id,
                writeCommand.ApprovalId,
                FormatWriteStatus(writeCommand.Status),
                BuildSupplierCreationMessage(writeCommand),
                CanRequest: false,
                CanSendDirect: false,
                CanExecute: false,
                HasPendingRequest: writeCommand.Status is FinanceIntegrationWriteCommandRecordStatuses.AwaitingApproval or FinanceIntegrationWriteCommandRecordStatuses.Approved,
                HasExecuted: false,
                FortnoxPath: "suppliers",
                ExternalId: writeCommand.ExternalId,
                ActionKind: "supplier_creation");
        }

        _logger.LogInformation(
            "Executing approved Fortnox supplier creation before invoice registration. CompanyId: {CompanyId}. BillId: {BillId}. WriteRequestId: {WriteRequestId}. Status: {Status}.",
            command.CompanyId,
            bill.Id,
            writeRequestId,
            writeCommand.Status);

        if (IsSupplierCreationRequestOutdated(writeCommand, bill))
        {
            _logger.LogInformation(
                "Refreshing Fortnox supplier creation request before execution because the serialized supplier payload changed. CompanyId: {CompanyId}. BillId: {BillId}. WriteRequestId: {WriteRequestId}. ApprovalId: {ApprovalId}.",
                command.CompanyId,
                bill.Id,
                writeRequestId,
                writeCommand.ApprovalId);

            return await RequestFortnoxSupplierCreationAsync(
                new RequestFinanceBillFortnoxRegistrationCommand(
                    command.CompanyId,
                    command.BillId,
                    command.ActorUserId,
                    "Finance user",
                    "Supplier details changed before sending to Fortnox."),
                bill,
                connection,
                cancellationToken);
        }

        var result = await _fortnoxOutboundActionExecutor.ExecuteApprovedAsync(command.CompanyId, writeRequestId, cancellationToken);
        var refreshed = await _dbContext.FinanceIntegrationWriteCommands
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(x => x.CompanyId == command.CompanyId && x.Id == writeRequestId, cancellationToken);

        if (refreshed.Status == FinanceIntegrationWriteCommandRecordStatuses.Executed)
        {
            var supplierNumber = await EnsureSupplierReferenceFromWriteCommandAsync(
                command.CompanyId,
                connection.Id,
                bill,
                refreshed,
                cancellationToken);

            return new FinanceBillFortnoxRegistrationDto(
                refreshed.Id,
                refreshed.ApprovalId,
                "Supplier ready",
                $"Fortnox created supplier {supplierNumber}. You can now send the invoice.",
                CanRequest: false,
                CanSendDirect: true,
                CanExecute: false,
                HasPendingRequest: false,
                HasExecuted: true,
                FortnoxPath: "supplierinvoices",
                ExternalId: supplierNumber,
                ActionKind: "invoice_registration");
        }

        return new FinanceBillFortnoxRegistrationDto(
            refreshed.Id,
            refreshed.ApprovalId,
            FormatWriteStatus(refreshed.Status),
            result.Summary,
            CanRequest: refreshed.Status == FinanceIntegrationWriteCommandRecordStatuses.Failed,
            CanSendDirect: refreshed.Status == FinanceIntegrationWriteCommandRecordStatuses.Failed,
            CanExecute: false,
            HasPendingRequest: false,
            HasExecuted: false,
            FortnoxPath: "suppliers",
            ExternalId: refreshed.ExternalId,
            ActionKind: "supplier_creation");
    }

    private async Task<DetectedBill> LoadBillForWriteAsync(Guid companyId, Guid billId, CancellationToken cancellationToken) =>
        await _dbContext.DetectedBills
            .IgnoreQueryFilters()
            .Include(x => x.Fields)
            .Include(x => x.DuplicateCheck)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == billId, cancellationToken)
        ?? throw new InvalidOperationException("The selected finance bill was not found in the active company.");

    private async Task<DetectedBill> LoadBillForReviewActionAsync(Guid companyId, Guid billId, CancellationToken cancellationToken) =>
        await _dbContext.DetectedBills
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(x => x.Fields)
            .Include(x => x.DuplicateCheck)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == billId, cancellationToken)
        ?? throw new InvalidOperationException("The selected finance bill was not found in the active company.");

    private async Task<FinanceBillReviewState> LoadOrCreateStateAsync(DetectedBill bill, CancellationToken cancellationToken)
    {
        var state = await _dbContext.FinanceBillReviewStates
            .IgnoreQueryFilters()
            .Include(x => x.Actions)
            .SingleOrDefaultAsync(x => x.CompanyId == bill.CompanyId && x.DetectedBillId == bill.Id, cancellationToken);

        if (state is not null)
        {
            return state;
        }

        state = new FinanceBillReviewState(
            Guid.NewGuid(),
            bill.CompanyId,
            bill.Id,
            ResolveInboxStatus(bill, null),
            BuildProposalSummary(bill, ParseValidationWarnings(bill), BuildDuplicateWarnings(bill), null).Summary,
            _timeProvider.GetUtcNow().UtcDateTime);
        _dbContext.FinanceBillReviewStates.Add(state);
        return state;
    }

    private async Task<FinanceBillFortnoxRegistrationDto?> BuildFortnoxRegistrationStateAsync(
        Guid companyId,
        DetectedBill bill,
        bool canRequest,
        string blockedMessage,
        CancellationToken cancellationToken)
    {
        var writeRequestId = CreateFortnoxRegistrationWriteRequestId(bill.Id);
        var command = await _dbContext.FinanceIntegrationWriteCommands
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == writeRequestId, cancellationToken);

        if (command is null)
        {
            var connection = await _dbContext.FinanceIntegrationConnections
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId &&
                            x.ProviderKey == FinanceIntegrationProviderKeys.Fortnox &&
                            x.Status == FinanceIntegrationConnectionStatuses.Connected)
                .OrderByDescending(x => x.ConnectedUtc ?? x.UpdatedUtc)
                .FirstOrDefaultAsync(cancellationToken);

            if (connection is not null)
            {
                var supplierMatch = await TryResolveFortnoxSupplierNumberAsync(companyId, connection.Id, bill, cancellationToken);
                if (!supplierMatch.Found)
                {
                    return await BuildFortnoxSupplierCreationStateAsync(
                        companyId,
                        bill,
                        canRequest,
                        supplierMatch,
                        cancellationToken);
                }
            }

            return canRequest
                ? new FinanceBillFortnoxRegistrationDto(null, null, "Ready to send", "This bill is ready to send to Fortnox.", CanRequest: true, CanSendDirect: true, CanExecute: false, HasPendingRequest: false, HasExecuted: false, FortnoxPath: "supplierinvoices")
                : new FinanceBillFortnoxRegistrationDto(
                    null,
                    null,
                    "Not ready",
                    blockedMessage,
                    CanRequest: false,
                    CanSendDirect: false,
                    CanExecute: false,
                    HasPendingRequest: false,
                    HasExecuted: false,
                    FortnoxPath: "supplierinvoices");
        }

        if (command.Status == FinanceIntegrationWriteCommandRecordStatuses.Executed &&
            IsExecutedFortnoxRegistrationOutdated(command, bill))
        {
            return new FinanceBillFortnoxRegistrationDto(
                command.Id,
                command.ApprovalId,
                "Sent with outdated details",
                "Fortnox accepted an earlier registration for this bill, but the extracted bill details have changed since then. Void or delete the old supplier invoice in Fortnox, then request a corrected registration.",
                canRequest,
                CanSendDirect: false,
                CanExecute: false,
                HasPendingRequest: false,
                HasExecuted: true,
                FortnoxPath: command.Path,
                ExternalId: command.ExternalId);
        }

        if (command.Status == FinanceIntegrationWriteCommandRecordStatuses.Failed)
        {
            return new FinanceBillFortnoxRegistrationDto(
                command.Id,
                command.ApprovalId,
                FormatWriteStatus(command.Status),
                BuildFortnoxRegistrationMessage(command),
                canRequest,
                CanSendDirect: canRequest,
                CanExecute: false,
                HasPendingRequest: false,
                HasExecuted: false,
                FortnoxPath: command.Path,
                ExternalId: command.ExternalId);
        }

        var canExecute = command.Status == FinanceIntegrationWriteCommandRecordStatuses.Approved &&
            command.ApprovalId.HasValue;
        return new FinanceBillFortnoxRegistrationDto(
            command.Id,
            command.ApprovalId,
            FormatWriteStatus(command.Status),
            BuildFortnoxRegistrationMessage(command),
            false,
            command.Status is not FinanceIntegrationWriteCommandRecordStatuses.Executed and not FinanceIntegrationWriteCommandRecordStatuses.Executing,
            canExecute,
            command.Status is FinanceIntegrationWriteCommandRecordStatuses.AwaitingApproval or FinanceIntegrationWriteCommandRecordStatuses.Approved or FinanceIntegrationWriteCommandRecordStatuses.Failed,
            command.Status == FinanceIntegrationWriteCommandRecordStatuses.Executed,
            FortnoxPath: command.Path,
            ExternalId: command.ExternalId);
    }

    private async Task<FinanceBillFortnoxRegistrationDto> BuildFortnoxSupplierCreationStateAsync(
        Guid companyId,
        DetectedBill bill,
        bool canRequest,
        SupplierResolutionResult supplierMatch,
        CancellationToken cancellationToken)
    {
        var writeRequestId = CreateFortnoxSupplierCreationWriteRequestId(bill.Id);
        var command = await _dbContext.FinanceIntegrationWriteCommands
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == writeRequestId, cancellationToken);

        if (command is null)
        {
            return new FinanceBillFortnoxRegistrationDto(
                null,
                null,
                "Supplier approval needed",
                supplierMatch.BlockedMessage,
                CanRequest: canRequest,
                CanSendDirect: false,
                CanExecute: false,
                HasPendingRequest: false,
                HasExecuted: false,
                FortnoxPath: "suppliers",
                ActionKind: "supplier_creation");
        }

        if (command.Status == FinanceIntegrationWriteCommandRecordStatuses.Executed)
        {
            return new FinanceBillFortnoxRegistrationDto(
                command.Id,
                command.ApprovalId,
                "Supplier ready",
                "Fortnox created this supplier. The invoice can now be sent.",
                CanRequest: false,
                CanSendDirect: true,
                CanExecute: false,
                HasPendingRequest: false,
                HasExecuted: true,
                FortnoxPath: "supplierinvoices",
                ExternalId: command.ExternalId,
                ActionKind: "invoice_registration");
        }

        if (IsSupplierCreationRequestOutdated(command, bill))
        {
            _logger.LogInformation(
                "Bill inbox Fortnox supplier creation request is outdated and needs a refreshed approval. CompanyId: {CompanyId}. BillId: {BillId}. WriteRequestId: {WriteRequestId}. Status: {Status}.",
                companyId,
                bill.Id,
                command.Id,
                command.Status);

            return new FinanceBillFortnoxRegistrationDto(
                command.Id,
                null,
                "Supplier approval needed",
                "The earlier supplier creation request used older details. Create a new supplier proposal before sending this invoice.",
                CanRequest: canRequest,
                CanSendDirect: false,
                CanExecute: false,
                HasPendingRequest: false,
                HasExecuted: false,
                FortnoxPath: "suppliers",
                ExternalId: command.ExternalId,
                ActionKind: "supplier_creation");
        }

        return new FinanceBillFortnoxRegistrationDto(
            command.Id,
            command.ApprovalId,
            FormatWriteStatus(command.Status),
            BuildSupplierCreationMessage(command),
            CanRequest: command.Status == FinanceIntegrationWriteCommandRecordStatuses.Failed && canRequest,
            CanSendDirect: command.Status is FinanceIntegrationWriteCommandRecordStatuses.Approved or FinanceIntegrationWriteCommandRecordStatuses.Failed,
            CanExecute: false,
            HasPendingRequest: command.Status is FinanceIntegrationWriteCommandRecordStatuses.AwaitingApproval or FinanceIntegrationWriteCommandRecordStatuses.Approved,
            HasExecuted: false,
            FortnoxPath: "suppliers",
            ExternalId: command.ExternalId,
            ActionKind: "supplier_creation");
    }

    private async Task<FinanceIntegrationConnection> ResolveActiveFortnoxConnectionAsync(Guid companyId, CancellationToken cancellationToken) =>
        await _dbContext.FinanceIntegrationConnections
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                        x.ProviderKey == FinanceIntegrationProviderKeys.Fortnox &&
                        x.Status == FinanceIntegrationConnectionStatuses.Connected)
            .OrderByDescending(x => x.ConnectedUtc ?? x.UpdatedUtc)
            .FirstOrDefaultAsync(cancellationToken)
        ?? throw new InvalidOperationException("Connect Fortnox before registering this supplier bill.");

    private async Task<string> ResolveTargetCompanyAsync(Guid companyId, CancellationToken cancellationToken) =>
        await _dbContext.Companies
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.Id == companyId)
            .Select(x => x.Name)
            .SingleOrDefaultAsync(cancellationToken) ?? "Current company";

    private static FinanceIntegrationWriteCommand BuildFortnoxRegistrationCommand(
        Guid companyId,
        Guid connectionId,
        Guid? actorUserId,
        DetectedBill bill,
        JsonObject payload,
        Guid writeRequestId) =>
        new(
            FinanceIntegrationProviderKeys.Fortnox,
            companyId,
            connectionId,
            actorUserId,
            FinanceIntegrationWriteCommandTypes.InvoiceExport,
            "POST",
            "supplierinvoices",
            bill.SupplierName ?? "Fortnox supplier",
            CreateRegistrationSummary(bill),
            FortnoxWritePayloadSanitizer.CreatePayloadHash(payload),
            new FinanceIntegrationWritePayload(FortnoxWritePayloadSanitizer.CreateSanitizedJson(payload), "SupplierInvoiceRegistration"),
            writeRequestId,
            $"finance-bill-inbox:{bill.Id:N}:fortnox-registration");

    private async Task WriteFortnoxDirectAuditAsync(
        FinanceIntegrationWriteCommandRecord command,
        string eventType,
        string outcome,
        string summary,
        CancellationToken cancellationToken)
    {
        var correlationId = command.Id.ToString("N");
        var alreadyRecorded = await _dbContext.FinanceIntegrationAuditEvents
            .AsNoTracking()
            .AnyAsync(x =>
                x.CompanyId == command.CompanyId &&
                x.ProviderKey == FinanceIntegrationProviderKeys.Fortnox &&
                x.EventType == eventType &&
                x.InternalRecordId == command.Id &&
                x.CorrelationId == correlationId,
                cancellationToken);

        if (alreadyRecorded)
        {
            return;
        }

        var audit = new FinanceIntegrationAuditEvent(
            Guid.NewGuid(),
            command.CompanyId,
            command.ConnectionId,
            FinanceIntegrationProviderKeys.Fortnox,
            eventType,
            outcome,
            command.CommandType,
            command.Id,
            command.ExternalId,
            correlationId,
            summary,
            _timeProvider.GetUtcNow().UtcDateTime,
            errorCount: outcome == FinanceIntegrationAuditOutcomes.Failed ? 1 : 0);
        audit.Metadata["direction"] = "outbound";
        audit.Metadata["initiatedBy"] = "user";
        audit.Metadata["payloadHash"] = command.PayloadHash;
        audit.Metadata["payloadSummary"] = command.PayloadSummary;
        _dbContext.FinanceIntegrationAuditEvents.Add(audit);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task WriteSupplierCreationAuditAsync(
        Guid companyId,
        Guid connectionId,
        Guid writeRequestId,
        Guid? approvalId,
        string eventType,
        string outcome,
        string summary,
        CancellationToken cancellationToken)
    {
        _dbContext.FinanceIntegrationAuditEvents.Add(new FinanceIntegrationAuditEvent(
            Guid.NewGuid(),
            companyId,
            connectionId,
            FinanceIntegrationProviderKeys.Fortnox,
            eventType,
            outcome,
            "supplier",
            writeRequestId,
            null,
            approvalId?.ToString("N") ?? writeRequestId.ToString("N"),
            summary,
            _timeProvider.GetUtcNow().UtcDateTime,
            createdCount: outcome == FinanceIntegrationAuditOutcomes.Succeeded ? 1 : 0,
            errorCount: outcome == FinanceIntegrationAuditOutcomes.Failed ? 1 : 0));
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<string> EnsureSupplierReferenceFromWriteCommandAsync(
        Guid companyId,
        Guid connectionId,
        DetectedBill bill,
        FinanceIntegrationWriteCommandRecord writeCommand,
        CancellationToken cancellationToken)
    {
        var supplierNumber = ExtractSupplierNumber(writeCommand);
        if (string.IsNullOrWhiteSpace(supplierNumber))
        {
            throw new InvalidOperationException("Fortnox created the supplier, but no supplier number was returned. Sync Fortnox suppliers before registering the invoice.");
        }

        var supplierName = bill.SupplierName?.Trim();
        if (string.IsNullOrWhiteSpace(supplierName))
        {
            throw new InvalidOperationException("Confirm the supplier name before linking the Fortnox supplier.");
        }

        var normalizedName = NormalizeMatchValue(supplierName);
        var normalizedOrgNumber = NormalizeOrgNumber(bill.SupplierOrgNumber);
        var counterpartyCandidates = await _dbContext.FinanceCounterparties
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.CounterpartyType == "supplier")
            .ToListAsync(cancellationToken);
        var counterparty = counterpartyCandidates
            .FirstOrDefault(x =>
                OrgNumbersMatch(x.TaxId, normalizedOrgNumber) ||
                NamesMatch(x.Name, normalizedName));

        if (counterparty is null)
        {
            counterparty = new FinanceCounterparty(
                Guid.NewGuid(),
                companyId,
                supplierName,
                "supplier",
                taxId: bill.SupplierOrgNumber,
                preferredPaymentMethod: ResolvePreferredPaymentMethod(bill),
                createdUtc: _timeProvider.GetUtcNow().UtcDateTime);
            _dbContext.FinanceCounterparties.Add(counterparty);
        }
        else
        {
            counterparty.UpdateMasterData(
                counterparty.Name,
                "supplier",
                counterparty.Email,
                counterparty.PaymentTerms,
                string.IsNullOrWhiteSpace(counterparty.TaxId) ? bill.SupplierOrgNumber : counterparty.TaxId,
                counterparty.CreditLimit,
                counterparty.PreferredPaymentMethod,
                counterparty.DefaultAccountMapping);
        }

        var existingReference = await _dbContext.FinanceExternalReferences
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x =>
                x.CompanyId == companyId &&
                x.ProviderKey == FinanceIntegrationProviderKeys.Fortnox &&
                x.EntityType == "supplier" &&
                (x.ExternalId == supplierNumber || x.InternalRecordId == counterparty.Id),
                cancellationToken);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var metadata = new JsonObject
        {
            ["source"] = "supplier_invoice_missing_supplier_flow",
            ["sourceBillId"] = bill.Id.ToString("D"),
            ["writeRequestId"] = writeCommand.Id.ToString("D"),
            ["safeResponseSummary"] = writeCommand.SafeResponseSummary
        };

        if (existingReference is null)
        {
            existingReference = new FinanceExternalReference(
                Guid.NewGuid(),
                companyId,
                connectionId,
                FinanceIntegrationProviderKeys.Fortnox,
                "supplier",
                counterparty.Id,
                supplierNumber,
                supplierNumber,
                null,
                now);
            existingReference.ReplaceMetadata(metadata, now);
            _dbContext.FinanceExternalReferences.Add(existingReference);
        }
        else
        {
            existingReference.RepointToInternalRecord(counterparty.Id, supplierNumber, null, now);
            existingReference.ReplaceMetadata(metadata, now);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await WriteSupplierCreationAuditAsync(
            companyId,
            connectionId,
            writeCommand.Id,
            writeCommand.ApprovalId,
            "supplier_created",
            FinanceIntegrationAuditOutcomes.Succeeded,
            $"Fortnox created supplier {supplierNumber} for this invoice.",
            cancellationToken);

        return supplierNumber;
    }

    private async Task<string> ResolveFortnoxSupplierNumberAsync(Guid companyId, Guid connectionId, DetectedBill bill, CancellationToken cancellationToken)
    {
        var result = await TryResolveFortnoxSupplierNumberAsync(companyId, connectionId, bill, cancellationToken);
        return result.Found && !string.IsNullOrWhiteSpace(result.SupplierNumber)
            ? result.SupplierNumber
            : throw new InvalidOperationException(result.BlockedMessage);
    }

    private async Task<SupplierResolutionResult> TryResolveFortnoxSupplierNumberAsync(Guid companyId, Guid connectionId, DetectedBill bill, CancellationToken cancellationToken)
    {
        var supplierName = NormalizeMatchValue(bill.SupplierName);
        var supplierOrgNumber = NormalizeOrgNumber(bill.SupplierOrgNumber);
        if (string.IsNullOrWhiteSpace(supplierName) && string.IsNullOrWhiteSpace(supplierOrgNumber))
        {
            return SupplierResolutionResult.Missing("Confirm the supplier name or organisation number before registering this bill in Fortnox.");
        }

        var candidates = await _dbContext.FinanceCounterparties
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.CounterpartyType == "supplier")
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.TaxId
            })
            .ToListAsync(cancellationToken);

        var counterpartyIds = candidates
            .Where(x => OrgNumbersMatch(x.TaxId, supplierOrgNumber) ||
                        NamesMatch(x.Name, supplierName))
            .Select(x => x.Id)
            .ToList();

        if (counterpartyIds.Count == 0)
        {
            _logger.LogWarning(
                "Bill inbox Fortnox registration supplier match failed. CompanyId: {CompanyId}. BillId: {BillId}. SupplierName: {SupplierName}. SupplierOrgNumber: {SupplierOrgNumber}. SyncedSupplierCount: {SyncedSupplierCount}.",
                companyId,
                bill.Id,
                bill.SupplierName,
                bill.SupplierOrgNumber,
                candidates.Count);
            return SupplierResolutionResult.Missing("No synced Fortnox supplier matched this bill. Create the supplier in Fortnox before registering the invoice.");
        }

        var reference = await _dbContext.FinanceExternalReferences
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                        x.ConnectionId == connectionId &&
                        x.ProviderKey == FinanceIntegrationProviderKeys.Fortnox &&
                        x.EntityType == "supplier" &&
                        counterpartyIds.Contains(x.InternalRecordId))
            .OrderByDescending(x => x.UpdatedUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var supplierNumber = reference?.ExternalNumber ?? reference?.ExternalId;
        return string.IsNullOrWhiteSpace(supplierNumber)
            ? SupplierResolutionResult.Missing("The matched supplier is missing a Fortnox supplier number. Sync Fortnox suppliers before registering this bill.")
            : SupplierResolutionResult.FoundSupplier(supplierNumber);
    }

    private static JsonObject BuildSupplierInvoicePayload(DetectedBill bill, string supplierNumber)
    {
        if (string.IsNullOrWhiteSpace(bill.InvoiceNumber))
        {
            throw new InvalidOperationException("Confirm the supplier invoice number before registering this bill in Fortnox.");
        }

        if (!bill.InvoiceDateUtc.HasValue)
        {
            throw new InvalidOperationException("Confirm the invoice date before registering this bill in Fortnox.");
        }

        if (!bill.DueDateUtc.HasValue)
        {
            throw new InvalidOperationException("Confirm the due date before registering this bill in Fortnox.");
        }

        if (!bill.TotalAmount.HasValue)
        {
            throw new InvalidOperationException("Confirm the total amount before registering this bill in Fortnox.");
        }

        var supplierInvoice = new JsonObject
        {
            ["SupplierNumber"] = supplierNumber,
            ["InvoiceNumber"] = bill.InvoiceNumber,
            ["InvoiceDate"] = bill.InvoiceDateUtc.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            ["DueDate"] = bill.DueDateUtc.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            ["Total"] = bill.TotalAmount.Value
        };

        if (bill.VatAmount.HasValue)
        {
            supplierInvoice["VAT"] = bill.VatAmount.Value;
        }

        if (!string.IsNullOrWhiteSpace(bill.Currency))
        {
            supplierInvoice["Currency"] = bill.Currency;
        }

        if (IsValidOcrReference(bill.PaymentReference))
        {
            supplierInvoice["OCR"] = bill.PaymentReference;
        }

        return new JsonObject
        {
            ["SupplierInvoice"] = supplierInvoice
        };
    }

    private static JsonObject BuildSupplierPayload(DetectedBill bill)
    {
        if (string.IsNullOrWhiteSpace(bill.SupplierName))
        {
            throw new InvalidOperationException("Confirm the supplier name before creating the supplier in Fortnox.");
        }

        var supplier = new JsonObject
        {
            ["Name"] = bill.SupplierName.Trim()
        };

        if (!string.IsNullOrWhiteSpace(bill.SupplierOrgNumber))
        {
            supplier["OrganisationNumber"] = bill.SupplierOrgNumber.Trim();
        }

        var email = FindBillFieldValue(bill, "email", "supplier_email");
        if (!string.IsNullOrWhiteSpace(email))
        {
            supplier["Email"] = email;
        }

        var phone = FindBillFieldValue(bill, "phone", "supplier_phone");
        if (!string.IsNullOrWhiteSpace(phone))
        {
            supplier["Phone1"] = phone;
        }

        var address = FindBillFieldValue(bill, "address", "supplier_address");
        if (!string.IsNullOrWhiteSpace(address))
        {
            supplier["Address1"] = address;
        }

        var bankgiro = NormalizeValidBankgiro(bill.Bankgiro);
        if (!string.IsNullOrWhiteSpace(bankgiro))
        {
            supplier["BG"] = bankgiro;
        }

        var plusgiro = NormalizeDigitsOnly(bill.Plusgiro);
        if (!string.IsNullOrWhiteSpace(plusgiro))
        {
            supplier["PG"] = plusgiro;
        }

        var iban = NormalizeValidIban(bill.Iban);
        if (!string.IsNullOrWhiteSpace(iban))
        {
            supplier["IBAN"] = iban;
        }

        var bic = NormalizeValidBic(bill.Bic);
        if (!string.IsNullOrWhiteSpace(bic) && !string.IsNullOrWhiteSpace(iban))
        {
            supplier["BIC"] = bic;
        }

        return new JsonObject
        {
            ["Supplier"] = supplier
        };
    }

    private static string? NormalizeValidBankgiro(string? value)
    {
        var digits = NormalizeDigitsOnly(value);
        return digits is not null &&
               digits.Length is >= 7 and <= 8 &&
               PassesLuhnChecksum(digits)
            ? digits
            : null;
    }

    private static string? NormalizeValidIban(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = new string(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        if (normalized.Length < 15 || normalized.Length > 34 ||
            !normalized.Take(2).All(char.IsLetter) ||
            !normalized.Skip(2).Take(2).All(char.IsDigit))
        {
            return null;
        }

        var rearranged = string.Concat(normalized.AsSpan(4), normalized.AsSpan(0, 4));
        var modulo = 0;
        foreach (var character in rearranged)
        {
            if (char.IsDigit(character))
            {
                modulo = (modulo * 10 + character - '0') % 97;
                continue;
            }

            if (!char.IsLetter(character))
            {
                return null;
            }

            var number = character - 'A' + 10;
            modulo = (modulo * 10 + number / 10) % 97;
            modulo = (modulo * 10 + number % 10) % 97;
        }

        return modulo == 1 ? normalized : null;
    }

    private static string? NormalizeValidBic(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = new string(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        return normalized.Length is 8 or 11 &&
               normalized.All(char.IsLetterOrDigit)
            ? normalized
            : null;
    }

    private static string? NormalizeDigitsOnly(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var digits = new string(value.Where(char.IsDigit).ToArray());
        return string.IsNullOrWhiteSpace(digits) ? null : digits;
    }

    private static bool PassesLuhnChecksum(string digits)
    {
        var sum = 0;
        var doubleDigit = false;
        for (var index = digits.Length - 1; index >= 0; index--)
        {
            var value = digits[index] - '0';
            if (value is < 0 or > 9)
            {
                return false;
            }

            if (doubleDigit)
            {
                value *= 2;
                if (value > 9)
                {
                    value -= 9;
                }
            }

            sum += value;
            doubleDigit = !doubleDigit;
        }

        return sum % 10 == 0;
    }

    private static bool IsExecutedFortnoxRegistrationOutdated(FinanceIntegrationWriteCommandRecord command, DetectedBill bill)
    {
        try
        {
            if (JsonNode.Parse(command.SanitizedPayloadJson)?["SupplierInvoice"] is not JsonObject supplierInvoice)
            {
                return false;
            }

            if (bill.TotalAmount.HasValue &&
                TryReadDecimal(supplierInvoice["Total"], out var sentTotal) &&
                sentTotal != bill.TotalAmount.Value)
            {
                return true;
            }

            if (bill.VatAmount.HasValue &&
                TryReadDecimal(supplierInvoice["VAT"], out var sentVat) &&
                sentVat != bill.VatAmount.Value)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(bill.InvoiceNumber) &&
                !string.Equals(ReadString(supplierInvoice["InvoiceNumber"]), bill.InvoiceNumber, StringComparison.Ordinal))
            {
                return true;
            }

            var expectedOcr = IsValidOcrReference(bill.PaymentReference)
                ? bill.PaymentReference?.Trim()
                : null;
            var sentOcr = ReadString(supplierInvoice["OCR"]);
            return !string.Equals(sentOcr, expectedOcr, StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool IsSupplierCreationRequestOutdated(FinanceIntegrationWriteCommandRecord command, DetectedBill bill)
    {
        if (!string.Equals(command.Path, "suppliers", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var currentPayload = BuildSupplierPayload(bill);
        var currentPayloadHash = FortnoxWritePayloadSanitizer.CreatePayloadHash(currentPayload);
        return !string.Equals(command.PayloadHash, currentPayloadHash, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryReadDecimal(JsonNode? node, out decimal value)
    {
        value = default;
        if (node is null)
        {
            return false;
        }

        if (node is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<decimal>(out value))
            {
                return true;
            }

            if (jsonValue.TryGetValue<double>(out var doubleValue))
            {
                value = Convert.ToDecimal(doubleValue);
                return true;
            }

            if (jsonValue.TryGetValue<string>(out var text) &&
                decimal.TryParse(text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out value))
            {
                return true;
            }
        }

        return decimal.TryParse(node.ToJsonString(), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    private static string? ReadString(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        return node is JsonValue value && value.TryGetValue<string>(out var text)
            ? string.IsNullOrWhiteSpace(text) ? null : text.Trim()
            : null;
    }

    private static Guid CreateFortnoxRegistrationWriteRequestId(Guid billId)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"finance-bill-inbox-fortnox-registration:{billId:N}"));
        return new Guid(bytes.AsSpan(0, 16));
    }

    private static Guid CreateFortnoxSupplierCreationWriteRequestId(Guid billId)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"finance-bill-inbox-fortnox-supplier-creation:{billId:N}"));
        return new Guid(bytes.AsSpan(0, 16));
    }

    private static string CreateSupplierCreationSummary(DetectedBill bill) =>
        $"Create Fortnox supplier {bill.SupplierName ?? "Unknown supplier"} before registering invoice {bill.InvoiceNumber ?? bill.Id.ToString("D")}.";

    private static string BuildSupplierCreationMessage(FinanceIntegrationWriteCommandRecord command) =>
        command.Status switch
        {
            FinanceIntegrationWriteCommandRecordStatuses.AwaitingApproval => "Approve this supplier creation request before the invoice can be sent.",
            FinanceIntegrationWriteCommandRecordStatuses.Approved => "This supplier is approved for creation in Fortnox. Send it to continue.",
            FinanceIntegrationWriteCommandRecordStatuses.Executing => "This supplier is being created in Fortnox.",
            FinanceIntegrationWriteCommandRecordStatuses.Executed => "Fortnox created this supplier.",
            FinanceIntegrationWriteCommandRecordStatuses.Failed => command.SafeFailureSummary ?? "Fortnox could not create this supplier. You can retry after fixing the issue.",
            FinanceIntegrationWriteCommandRecordStatuses.Rejected => "This supplier creation request was rejected.",
            FinanceIntegrationWriteCommandRecordStatuses.Expired => "This supplier creation request expired.",
            FinanceIntegrationWriteCommandRecordStatuses.Cancelled => "This supplier creation request was cancelled.",
            _ => "Supplier creation is waiting for review."
        };

    private static string? FindBillFieldValue(DetectedBill bill, params string[] fieldNames)
    {
        foreach (var fieldName in fieldNames)
        {
            var field = bill.Fields.FirstOrDefault(x => string.Equals(x.FieldName, fieldName, StringComparison.OrdinalIgnoreCase));
            var value = field?.NormalizedValue ?? field?.RawValue;
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static string? ExtractSupplierNumber(FinanceIntegrationWriteCommandRecord writeCommand)
    {
        if (!string.IsNullOrWhiteSpace(writeCommand.ExternalId))
        {
            return writeCommand.ExternalId.Trim();
        }

        if (string.IsNullOrWhiteSpace(writeCommand.SafeResponseSummary))
        {
            return null;
        }

        try
        {
            var node = JsonNode.Parse(writeCommand.SafeResponseSummary);
            return ReadString(node?["Supplier"]?["SupplierNumber"]) ??
                ReadString(node?["Supplier"]?["Number"]) ??
                ReadString(node?["SupplierNumber"]) ??
                ReadString(node?["Number"]);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ResolvePreferredPaymentMethod(DetectedBill bill) =>
        !string.IsNullOrWhiteSpace(bill.Bankgiro) ? "bankgiro" :
        !string.IsNullOrWhiteSpace(bill.Plusgiro) ? "plusgiro" :
        !string.IsNullOrWhiteSpace(bill.Iban) ? "iban" :
        "bank_transfer";

    private static bool IsValidOcrReference(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim();
        if (normalized.Any(ch => !char.IsDigit(ch) && ch is not ' ' and not '-'))
        {
            return false;
        }

        var digits = new string(normalized.Where(char.IsDigit).ToArray());
        return
            digits.Length is >= 2 and <= 25 &&
            HasValidLuhnCheckDigit(digits);
    }

    private static bool HasValidLuhnCheckDigit(string digits)
    {
        var sum = 0;
        var doubleDigit = false;
        for (var index = digits.Length - 1; index >= 0; index--)
        {
            var value = digits[index] - '0';
            if (doubleDigit)
            {
                value *= 2;
                if (value > 9)
                {
                    value -= 9;
                }
            }

            sum += value;
            doubleDigit = !doubleDigit;
        }

        return sum % 10 == 0;
    }

    private static FinanceBillFortnoxRegistrationDto MapFortnoxRegistration(FinanceIntegrationWriteResult result, bool canRequest) =>
        new(
            result.WriteRequestId,
            result.ApprovalId,
            FormatWriteStatus(result.Status),
            result.Message,
            canRequest,
            CanSendDirect: false,
            result.CanExecute,
            result.Status is FinanceIntegrationWriteCommandRecordStatuses.AwaitingApproval or FinanceIntegrationWriteCommandRecordStatuses.Approved,
            result.Status == FinanceIntegrationWriteCommandRecordStatuses.Executed,
            "supplierinvoices");

    private static string CreateRegistrationSummary(DetectedBill bill)
    {
        var supplier = bill.SupplierName ?? "Unknown supplier";
        var reference = bill.InvoiceNumber ?? bill.Id.ToString("D");
        var amount = bill.TotalAmount.HasValue
            ? $"{bill.Currency ?? string.Empty} {bill.TotalAmount.Value:0.00}".Trim()
            : "the extracted amount";
        return $"Register supplier bill {reference} from {supplier} in Fortnox for {amount}.";
    }

    private static string BuildFortnoxRegistrationMessage(FinanceIntegrationWriteCommandRecord command) =>
        command.Status switch
        {
            FinanceIntegrationWriteCommandRecordStatuses.AwaitingApproval => "Approve this Fortnox registration request before sending it.",
            FinanceIntegrationWriteCommandRecordStatuses.Approved => "This bill is approved for Fortnox registration and can be sent.",
            FinanceIntegrationWriteCommandRecordStatuses.Executing => "This bill is being sent to Fortnox.",
            FinanceIntegrationWriteCommandRecordStatuses.Executed => "Fortnox accepted this supplier bill registration.",
            FinanceIntegrationWriteCommandRecordStatuses.Failed => command.SafeFailureSummary ?? "Fortnox could not register this bill. You can retry after fixing the issue.",
            FinanceIntegrationWriteCommandRecordStatuses.Rejected => "This Fortnox registration request was rejected.",
            FinanceIntegrationWriteCommandRecordStatuses.Expired => "This Fortnox registration request expired.",
            FinanceIntegrationWriteCommandRecordStatuses.Cancelled => "This Fortnox registration request was cancelled.",
            _ => "Fortnox registration status is available."
        };

    private static string BuildFortnoxRegistrationBlockedMessage(IReadOnlyList<FinanceBillWarningDto> validationWarnings)
    {
        var unresolvedWarnings = validationWarnings
            .Where(x => !x.IsResolved && !string.IsNullOrWhiteSpace(x.Message))
            .Select(x => x.Message)
            .Take(3)
            .ToList();

        if (unresolvedWarnings.Count == 0)
        {
            return "Fortnox registration is blocked until the bill has complete required fields and no unresolved validation warnings.";
        }

        return $"Fortnox registration is blocked: {string.Join(" ", unresolvedWarnings)}";
    }

    private static string? NormalizeMatchValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static bool NamesMatch(string? candidate, string? expected)
    {
        var candidateKey = NormalizeNameKey(candidate);
        var expectedKey = NormalizeNameKey(expected);
        return !string.IsNullOrWhiteSpace(candidateKey) &&
            !string.IsNullOrWhiteSpace(expectedKey) &&
            string.Equals(candidateKey, expectedKey, StringComparison.Ordinal);
    }

    private static string? NormalizeNameKey(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : new string(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static bool OrgNumbersMatch(string? candidate, string? expected)
    {
        var candidateValues = NormalizeOrgNumberVariants(candidate);
        var expectedValues = NormalizeOrgNumberVariants(expected);
        return candidateValues.Count > 0 &&
            expectedValues.Count > 0 &&
            candidateValues.Overlaps(expectedValues);
    }

    private static HashSet<string> NormalizeOrgNumberVariants(string? value)
    {
        var normalized = NormalizeOrgNumber(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return [];
        }

        var variants = new HashSet<string>(StringComparer.Ordinal)
        {
            normalized
        };

        if (normalized.StartsWith("SE", StringComparison.Ordinal) && normalized.Length > 2)
        {
            variants.Add(normalized[2..]);
        }

        foreach (var variant in variants.ToList())
        {
            if (variant.Length == 12 && variant.EndsWith("01", StringComparison.Ordinal))
            {
                variants.Add(variant[..10]);
            }
        }

        return variants;
    }

    private static string? NormalizeOrgNumber(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : new string(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private async Task WriteAuditAsync(Guid companyId, Guid? actorUserId, string actionName, Guid billId, string outcome, FinanceBillReviewAction action, CancellationToken cancellationToken)
    {
        await _auditEventWriter.WriteAsync(
            new AuditEventWriteRequest(
                companyId,
                AuditActorTypes.User,
                actorUserId,
                actionName,
                "finance_bill_inbox_item",
                billId.ToString("D"),
                outcome,
                action.Rationale,
                Metadata: new Dictionary<string, string?>
                {
                    ["priorStatus"] = FormatBillStatus(action.PriorStatus),
                    ["newStatus"] = FormatBillStatus(action.NewStatus),
                    ["reviewActionId"] = action.Id.ToString("D")
                },
                OccurredUtc: action.OccurredUtc),
            cancellationToken);
    }

    private void EnsureTenant(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (_companyContextAccessor?.CompanyId is Guid currentCompanyId && currentCompanyId != companyId)
        {
            throw new UnauthorizedAccessException("The requested finance bill inbox is outside the active company context.");
        }
    }

    private static string ResolveInboxStatus(DetectedBill bill, FinanceBillReviewState? state)
    {
        if (state is not null)
        {
            return state.Status;
        }

        if (bill.ValidationStatusPersisted && bill.IsEligibleForApprovalProposal)
        {
            return FinanceBillInboxStatuses.ProposedForApproval;
        }

        if (bill.RequiresReview || string.Equals(bill.ValidationStatus, "flagged", StringComparison.OrdinalIgnoreCase))
        {
            return FinanceBillInboxStatuses.NeedsReview;
        }

        return bill.Fields.Count > 0 ? FinanceBillInboxStatuses.Extracted : FinanceBillInboxStatuses.Detected;
    }

    private static void EnsureActiveReviewStatus(string status, string actionName)
    {
        var normalized = FinanceBillInboxStatuses.Normalize(status);
        if (normalized is FinanceBillInboxStatuses.Detected or
            FinanceBillInboxStatuses.Extracted or
            FinanceBillInboxStatuses.NeedsReview or
            FinanceBillInboxStatuses.ProposedForApproval)
        {
            return;
        }

        throw new InvalidOperationException($"Cannot {actionName} a finance bill from status '{normalized}'.");
    }

    private static FinanceBillProposalSummaryDto BuildProposalSummary(
        DetectedBill bill,
        IReadOnlyList<FinanceBillWarningDto> validationWarnings,
        IReadOnlyList<FinanceBillWarningDto> duplicateWarnings,
        string? storedSummary)
    {
        var reference = bill.InvoiceNumber ?? bill.SourceAttachmentId ?? bill.Id.ToString("D");
        var amount = bill.TotalAmount.HasValue && !string.IsNullOrWhiteSpace(bill.Currency)
            ? $"{bill.TotalAmount.Value:0.##} {bill.Currency}"
            : "the extracted amount";
        var due = bill.DueDateUtc.HasValue ? $" due {bill.DueDateUtc.Value:yyyy-MM-dd}" : string.Empty;
        var supplier = bill.SupplierName ?? "the supplier";
        var unresolvedWarnings = validationWarnings.Concat(duplicateWarnings)
            .Where(x => !x.IsResolved)
            .Select(x => $"{x.Severity}: {x.Message}")
            .Take(5)
            .ToList();
        var riskFlags = unresolvedWarnings.Count == 0
            ? ["No unresolved validation or duplicate warnings were found."]
            : unresolvedWarnings;
        var recommendedAction = unresolvedWarnings.Count == 0
            ? "Approve the invoice only, or approve it and register it in Fortnox."
            : "Review the warnings before approving or sending this invoice.";
        var approvalAsk = "Choose whether to approve the supplier invoice only or approve it and register it in Fortnox.";
        var generatedSummary = unresolvedWarnings.Count == 0
            ? $"This bill looks ready. Laura found invoice {reference} from {supplier} for {amount}{due}. Confidence is {FormatStatus(bill.ConfidenceLevel)}, and no validation or duplicate warnings were found."
            : $"This bill needs attention. Laura found invoice {reference} from {supplier} for {amount}{due}. Confidence is {FormatStatus(bill.ConfidenceLevel)}, but there are unresolved warnings to review.";
        var summary = NormalizeProposalSummary(storedSummary, generatedSummary, approvalAsk);

        return new FinanceBillProposalSummaryDto(
            $"Proposal for {reference}",
            summary,
            riskFlags,
            approvalAsk,
            recommendedAction,
            true,
            false);
    }

    private static string NormalizeProposalSummary(string? storedSummary, string generatedSummary, string approvalAsk)
    {
        if (string.IsNullOrWhiteSpace(storedSummary) ||
            ContainsAutoPaymentLanguage(storedSummary) ||
            ContainsLegacyProposalLanguage(storedSummary))
        {
            return generatedSummary;
        }

        var summary = storedSummary.Trim();
        return summary.Contains("approve", StringComparison.OrdinalIgnoreCase) &&
               summary.Contains("does not initiate payment", StringComparison.OrdinalIgnoreCase)
            ? summary
            : $"{summary} {approvalAsk}";
    }

    private static bool ContainsAutoPaymentLanguage(string value)
    {
        var normalized = value.ToLowerInvariant();
        return normalized.Contains("payment was initiated", StringComparison.Ordinal) ||
               normalized.Contains("payment has been initiated", StringComparison.Ordinal) ||
               normalized.Contains("payment will be initiated", StringComparison.Ordinal) ||
               normalized.Contains("automatically pay", StringComparison.Ordinal) ||
               normalized.Contains("exported for payment", StringComparison.Ordinal);
    }

    private static bool ContainsLegacyProposalLanguage(string value)
    {
        var normalized = value.ToLowerInvariant();
        return normalized.Contains("financeagent proposal:", StringComparison.Ordinal) ||
               normalized.Contains("bill proposal", StringComparison.Ordinal) ||
               normalized.Contains("please approve, reject, or request clarification", StringComparison.Ordinal) ||
               normalized.Contains("approval records the decision only", StringComparison.Ordinal);
    }

    private static string? FormatEmailSender(string? displayName, string? address)
    {
        var normalizedDisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        var normalizedAddress = string.IsNullOrWhiteSpace(address) ? null : address.Trim();

        if (normalizedDisplayName is null)
        {
            return normalizedAddress;
        }

        if (normalizedAddress is null ||
            normalizedDisplayName.Equals(normalizedAddress, StringComparison.OrdinalIgnoreCase))
        {
            return normalizedDisplayName;
        }

        return $"{normalizedDisplayName} <{normalizedAddress}>";
    }

    private static FinanceBillExtractedFieldDto MapField(DetectedBillField field) =>
        new(
            field.FieldName,
            FormatFieldName(field.FieldName),
            field.RawValue,
            field.NormalizedValue,
            field.FieldConfidence,
            [
                new FinanceBillEvidenceReferenceDto(
                    field.SourceDocument,
                    field.SourceDocumentType,
                    field.PageReference,
                    field.SectionReference,
                    field.TextSpan,
                    field.Locator,
                    field.Snippet)
            ]);

    private static FinanceBillReviewActionDto MapAction(FinanceBillReviewAction action) =>
        new(
            action.Id,
            FormatStatus(action.Action),
            action.ActorDisplayName,
            action.ActorUserId,
            action.OccurredUtc,
            FormatBillStatus(action.PriorStatus),
            FormatBillStatus(action.NewStatus),
            action.Rationale);

    private static IReadOnlyList<FinanceBillWarningDto> ParseValidationWarnings(DetectedBill bill)
    {
        if (string.IsNullOrWhiteSpace(bill.ValidationIssuesJson) || bill.ValidationIssuesJson == "[]")
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(bill.ValidationIssuesJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [new FinanceBillWarningDto("validation_payload", bill.ValidationStatus, bill.ValidationIssuesJson, false)];
            }

            return document.RootElement.EnumerateArray()
                .Select((item, index) =>
                {
                    var code = TryGetString(item, "code") ?? $"validation_{index + 1}";
                    var severity = TryGetString(item, "severity") ?? bill.ValidationStatus;
                    var message = TryGetString(item, "message") ?? item.ToString();
                    var resolved = item.TryGetProperty("isResolved", out var resolvedProperty) && resolvedProperty.ValueKind == JsonValueKind.True;
                    return new FinanceBillWarningDto(code, FormatStatus(severity), message, resolved);
                })
                .ToList();
        }
        catch (JsonException)
        {
            return [new FinanceBillWarningDto("validation_payload", FormatStatus(bill.ValidationStatus), bill.ValidationIssuesJson, false)];
        }
    }

    private static IReadOnlyList<FinanceBillWarningDto> BuildDuplicateWarnings(DetectedBill bill)
    {
        if (bill.DuplicateCheck is null || !bill.DuplicateCheck.IsDuplicate)
        {
            return [];
        }

        return
        [
            new FinanceBillWarningDto(
                "possible_duplicate",
                FormatStatus(bill.DuplicateCheck.ResultStatus),
                bill.DuplicateCheck.CriteriaSummary,
                false)
        ];
    }

    private static bool HasUnresolvedValidationFailures(DetectedBill bill, IReadOnlyList<FinanceBillWarningDto> warnings)
    {
        if (warnings.Any(x => !x.IsResolved))
        {
            return true;
        }

        return string.Equals(bill.ValidationStatus, "pending", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(bill.ValidationStatus, "flagged", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(bill.ValidationStatus, "rejected", StringComparison.OrdinalIgnoreCase);
    }

    private static int CountValidationWarnings(DetectedBill bill) => ParseValidationWarnings(bill).Count(x => !x.IsResolved);

    private static string? TryGetString(JsonElement item, string propertyName) =>
        item.ValueKind == JsonValueKind.Object &&
        item.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static string FormatFieldName(string value) => FormatStatus(value.Replace("Utc", string.Empty, StringComparison.Ordinal));

    private static string FormatStatus(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? "Unknown"
            : string.Join(" ", value.Trim().Replace("-", "_", StringComparison.Ordinal).Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => char.ToUpperInvariant(x[0]) + x[1..]));

    private static string FormatBillStatus(string value) =>
        FinanceBillInboxStatuses.Normalize(value) switch
        {
            FinanceBillInboxStatuses.Detected => "Detected",
            FinanceBillInboxStatuses.Extracted => "Extracted",
            FinanceBillInboxStatuses.NeedsReview => "Needs review",
            FinanceBillInboxStatuses.ProposedForApproval => "Proposed for approval",
            FinanceBillInboxStatuses.Approved => "Approved",
            FinanceBillInboxStatuses.Rejected => "Rejected",
            FinanceBillInboxStatuses.SentToPaymentExported => "Sent to payment/exported",
            _ => FormatStatus(value)
        };

    private static string FormatWriteStatus(string value) =>
        value switch
        {
            FinanceIntegrationWriteCommandRecordStatuses.AwaitingApproval => "Waiting for approval",
            FinanceIntegrationWriteCommandRecordStatuses.Approved => "Approved to send",
            FinanceIntegrationWriteCommandRecordStatuses.Executing => "Sending",
            FinanceIntegrationWriteCommandRecordStatuses.Executed => "Sent to Fortnox",
            FinanceIntegrationWriteCommandRecordStatuses.Failed => "Failed",
            FinanceIntegrationWriteCommandRecordStatuses.Rejected => "Rejected",
            FinanceIntegrationWriteCommandRecordStatuses.Expired => "Expired",
            FinanceIntegrationWriteCommandRecordStatuses.Cancelled => "Cancelled",
            _ => FormatStatus(value)
        };

    private static bool IsAllowedDisplayStatus(string value) =>
        new[] { "Detected", "Extracted", "Needs review", "Proposed for approval", "Approved", "Rejected", "Sent to payment/exported" }
            .Contains(value, StringComparer.OrdinalIgnoreCase);

    private sealed record SupplierResolutionResult(bool Found, string? SupplierNumber, string BlockedMessage)
    {
        public static SupplierResolutionResult FoundSupplier(string supplierNumber) =>
            new(true, supplierNumber, string.Empty);

        public static SupplierResolutionResult Missing(string blockedMessage) =>
            new(false, null, blockedMessage);
    }
}
