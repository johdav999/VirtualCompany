using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class CompanyInvitation : ICompanyOwnedEntity
{
    private CompanyInvitation()
    {
    }

    public CompanyInvitation(
        Guid id,
        Guid companyId,
        string email,
        CompanyMembershipRole role,
        Guid invitedByUserId,
        string tokenHash,
        DateTime expiresAtUtc)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (invitedByUserId == Guid.Empty)
        {
            throw new ArgumentException("InvitedByUserId is required.", nameof(invitedByUserId));
        }

        CompanyMembershipRoles.EnsureSupported(role, nameof(role));

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        Email = NormalizeEmail(email);
        Role = role;
        Status = CompanyInvitationStatus.Pending;
        InvitedByUserId = invitedByUserId;
        TokenHash = NormalizeRequired(tokenHash, nameof(tokenHash), 128);
        ExpiresAtUtc = EnsureFuture(expiresAtUtc, nameof(expiresAtUtc));
        CreatedUtc = DateTime.UtcNow;
        UpdatedUtc = CreatedUtc;
        DeliveryStatus = CompanyInvitationDeliveryStatus.Pending;
        LastSentUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Email { get; private set; } = null!;
    public CompanyMembershipRole Role { get; private set; }
    public CompanyInvitationStatus Status { get; private set; }
    public Guid InvitedByUserId { get; private set; }
    public Guid? AcceptedByUserId { get; private set; }
    public string TokenHash { get; private set; } = null!;
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? LastSentUtc { get; private set; }
    public CompanyInvitationDeliveryStatus DeliveryStatus { get; private set; }
    public DateTime? LastDeliveryAttemptUtc { get; private set; }
    public DateTime? DeliveredUtc { get; private set; }
    public string? DeliveryError { get; private set; }
    public string? LastDeliveryCorrelationId { get; private set; }
    public string? LastDeliveredTokenHash { get; private set; }
    public DateTime? AcceptedUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;

    public User InvitedByUser { get; private set; } = null!;
    public User? AcceptedByUser { get; private set; }

    public void SyncExpiration(DateTime utcNow)
    {
        if (Status == CompanyInvitationStatus.Pending && ExpiresAtUtc <= utcNow)
        {
            Status = CompanyInvitationStatus.Expired;
            if (!HasDeliveredCurrentToken())
            {
                DeliveryStatus = CompanyInvitationDeliveryStatus.Skipped;
                DeliveryError = "Invitation expired before delivery.";
            }

            UpdatedUtc = utcNow;
        }

    }

    public void Resend(string tokenHash, DateTime expiresAtUtc, CompanyMembershipRole role, Guid invitedByUserId)
    {
        if (Status == CompanyInvitationStatus.Accepted)
        {
            throw new InvalidOperationException("Accepted invitations cannot be resent.");
        }

        if (invitedByUserId == Guid.Empty)
        {
            throw new ArgumentException("InvitedByUserId is required.", nameof(invitedByUserId));
        }

        CompanyMembershipRoles.EnsureSupported(role, nameof(role));

        TokenHash = NormalizeRequired(tokenHash, nameof(tokenHash), 128);
        ExpiresAtUtc = EnsureFuture(expiresAtUtc, nameof(expiresAtUtc));
        Role = role;
        Status = CompanyInvitationStatus.Pending;
        InvitedByUserId = invitedByUserId;
        LastSentUtc = DateTime.UtcNow;
        DeliveryStatus = CompanyInvitationDeliveryStatus.Pending;
        LastDeliveryAttemptUtc = null;
        DeliveredUtc = null;
        DeliveryError = null;
        LastDeliveryCorrelationId = null;
        LastDeliveredTokenHash = null;
        UpdatedUtc = LastSentUtc.Value;
    }

    public bool HasDeliveredCurrentToken() =>
        DeliveredUtc.HasValue &&
        string.Equals(LastDeliveredTokenHash, TokenHash, StringComparison.Ordinal);

    public void MarkDeliveryAttempt(string? correlationId)
    {
        LastDeliveryAttemptUtc = DateTime.UtcNow;
        LastDeliveryCorrelationId = NormalizeOptional(correlationId, nameof(correlationId), 128);
        DeliveryError = null;
        UpdatedUtc = LastDeliveryAttemptUtc.Value;
    }

    public void RecordIgnoredDeliveryAttempt(string? correlationId, string? reason = null)
    {
        LastDeliveryAttemptUtc = DateTime.UtcNow;
        LastDeliveryCorrelationId = NormalizeOptional(correlationId, nameof(correlationId), 128);
        if (DeliveryStatus != CompanyInvitationDeliveryStatus.Delivered &&
            !string.IsNullOrWhiteSpace(reason))
        {
            DeliveryError = NormalizeOptional(reason, nameof(reason), 2000);
        }

        UpdatedUtc = LastDeliveryAttemptUtc.Value;
    }

    public void MarkDelivered(string? correlationId)
    {
        var utcNow = DateTime.UtcNow;
        LastSentUtc = utcNow;
        LastDeliveryAttemptUtc ??= utcNow;
        DeliveredUtc = utcNow;
        DeliveryStatus = CompanyInvitationDeliveryStatus.Delivered;
        DeliveryError = null;
        LastDeliveryCorrelationId = NormalizeOptional(correlationId, nameof(correlationId), 128);
        LastDeliveredTokenHash = TokenHash;
        UpdatedUtc = utcNow;
    }

    public void Accept(Guid acceptedByUserId)
    {
        if (acceptedByUserId == Guid.Empty)
        {
            throw new ArgumentException("AcceptedByUserId is required.", nameof(acceptedByUserId));
        }

        if (Status != CompanyInvitationStatus.Pending)
        {
            throw new InvalidOperationException("Only pending invitations can be accepted.");
        }

        if (ExpiresAtUtc <= DateTime.UtcNow)
        {
            Status = CompanyInvitationStatus.Expired;
            UpdatedUtc = DateTime.UtcNow;
            throw new InvalidOperationException("Expired invitations cannot be accepted.");
        }

        AcceptedByUserId = acceptedByUserId;
        AcceptedUtc = DateTime.UtcNow;
        Status = CompanyInvitationStatus.Accepted;
        UpdatedUtc = AcceptedUtc.Value;
    }

    public void MarkDeliveryFailed(string error, string? correlationId)
    {
        var utcNow = DateTime.UtcNow;
        LastDeliveryAttemptUtc = utcNow;
        DeliveryStatus = CompanyInvitationDeliveryStatus.Failed;
        DeliveryError = NormalizeRequired(error, nameof(error), 2000);
        LastDeliveryCorrelationId = NormalizeOptional(correlationId, nameof(correlationId), 128);
        UpdatedUtc = utcNow;
    }

    public void Revoke()
    {
        if (Status == CompanyInvitationStatus.Accepted)
        {
            throw new InvalidOperationException("Accepted invitations cannot be revoked.");
        }

        if (Status == CompanyInvitationStatus.Revoked)
        {
            return;
        }

        Status = CompanyInvitationStatus.Revoked;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void Cancel()
    {
        if (Status == CompanyInvitationStatus.Accepted)
        {
            throw new InvalidOperationException("Accepted invitations cannot be cancelled.");
        }

        if (Status == CompanyInvitationStatus.Cancelled)
        {
            return;
        }

        Status = CompanyInvitationStatus.Cancelled;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void MarkDeliverySkipped(string reason, string? correlationId)
    {
        var utcNow = DateTime.UtcNow;
        LastDeliveryAttemptUtc = utcNow;
        DeliveryStatus = CompanyInvitationDeliveryStatus.Skipped;
        DeliveryError = NormalizeRequired(reason, nameof(reason), 2000);
        LastDeliveryCorrelationId = NormalizeOptional(correlationId, nameof(correlationId), 128);
        UpdatedUtc = utcNow;
    }

    private static string NormalizeEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("Email is required.", nameof(email));
        }

        return email.Trim().ToLowerInvariant();
    }

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }
}

    private static string? NormalizeOptional(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }



    private static string? NormalizeOptional(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }



    private static DateTime EnsureFuture(DateTime value, string name)
    {
        if (value <= DateTime.UtcNow)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be in the future.");
        }

        return value;
    }
}

public sealed class CompanyOutboxMessage : ICompanyOwnedEntity
{
    private CompanyOutboxMessage()
    {
    }

    public CompanyOutboxMessage(Guid id, Guid companyId, string topic, string payloadJson, string? correlationId = null, DateTime? availableUtc = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        Topic = NormalizeRequired(topic, nameof(topic), 200);
        PayloadJson = string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson.Trim();
        CreatedUtc = DateTime.UtcNow;
        AvailableUtc = availableUtc ?? CreatedUtc;
        CorrelationId = NormalizeOptional(correlationId, nameof(correlationId), 128);
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Topic { get; private set; } = null!;
    public string PayloadJson { get; private set; } = null!;
    public string? CorrelationId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime AvailableUtc { get; private set; }
    public int AttemptCount { get; private set; }
    public string? LastError { get; private set; }
    public DateTime? ClaimedUtc { get; private set; }
    public string? ClaimToken { get; private set; }
    public DateTime? ProcessedUtc { get; private set; }
    public Company Company { get; private set; } = null!;

    public bool TryClaim(string claimToken, DateTime utcNow, TimeSpan claimTimeout)
    {
        if (ProcessedUtc.HasValue || AvailableUtc > utcNow)
        {
            return false;
        }

        if (ClaimedUtc.HasValue &&
            !string.IsNullOrWhiteSpace(ClaimToken) &&
            ClaimedUtc.Value > utcNow.Subtract(claimTimeout))
        {
            return false;
        }

        ClaimToken = NormalizeRequired(claimToken, nameof(claimToken), 64);
        ClaimedUtc = utcNow;
        LastError = null;
        return true;
    }

    public void ScheduleRetry(DateTime availableUtc, string error)
    {
        AttemptCount++;
        AvailableUtc = availableUtc;
        LastError = NormalizeRequired(error, nameof(error), 4000);
        ClaimedUtc = null;
        ClaimToken = null;
    }

    public void MarkDiscarded(string error)
    {
        AttemptCount++;
        LastError = NormalizeRequired(error, nameof(error), 4000);
        ProcessedUtc = DateTime.UtcNow;
        ClaimedUtc = null;
        ClaimToken = null;
    }

    public void ReleaseClaim()
    {
        ClaimedUtc = null;
        ClaimToken = null;
    }

    public void MarkProcessed()
    {
        ProcessedUtc = DateTime.UtcNow;
    }

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }
