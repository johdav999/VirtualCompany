namespace VirtualCompany.Application.Finance;

public interface IFortnoxIntegrationDiagnostics
{
    void SyncStarted(Guid companyId, Guid connectionId, string? correlationId);

    void SyncCompleted(
        Guid companyId,
        Guid connectionId,
        string? correlationId,
        string status,
        int created,
        int updated,
        int skipped,
        int errors,
        TimeSpan duration);

    void SyncFailed(
        Guid companyId,
        Guid connectionId,
        string? correlationId,
        string safeReason,
        TimeSpan duration);

    void DuplicateSkipped(Guid companyId, Guid connectionId, string entityType);

    void CursorAdvanced(Guid companyId, Guid connectionId, string entityType, string? previousCursor, string? nextCursor);

    void TokenRefreshStarted(Guid companyId, Guid connectionId);

    void TokenRefreshCompleted(Guid companyId, Guid connectionId, bool succeeded, bool needsReconnect, TimeSpan duration);

    void TokenRefreshFailed(Guid companyId, Guid connectionId, string safeReason, bool needsReconnect, TimeSpan duration);

    void ApprovalCreated(Guid companyId, Guid connectionId, Guid approvalId, string entityType, string payloadHash);
}
