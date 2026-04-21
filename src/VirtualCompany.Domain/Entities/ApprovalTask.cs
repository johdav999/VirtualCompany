using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class ApprovalTask : ICompanyOwnedEntity
{
    private ApprovalTask()
    {
    }

    public ApprovalTask(
        Guid id,
        Guid companyId,
        ApprovalTargetType targetType,
        Guid targetId,
        Guid? assigneeId = null,
        DateTime? dueDate = null,
        ApprovalTaskStatus status = ApprovalTaskStatus.Pending,
        DateTime? createdUtc = null,
        DateTime? updatedUtc = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (targetId == Guid.Empty)
        {
            throw new ArgumentException("TargetId is required.", nameof(targetId));
        }

        if (assigneeId == Guid.Empty)
        {
            throw new ArgumentException("AssigneeId cannot be empty.", nameof(assigneeId));
        }

        ApprovalTargetTypeValues.EnsureSupported(targetType, nameof(targetType));
        ApprovalTaskStatusValues.EnsureSupported(status, nameof(status));

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        TargetType = targetType;
        TargetId = targetId;
        AssigneeId = assigneeId;
        DueDate = NormalizeOptionalUtc(dueDate, nameof(dueDate));
        Status = status;
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc ?? DateTime.UtcNow, nameof(createdUtc));
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc ?? CreatedUtc, nameof(updatedUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public ApprovalTargetType TargetType { get; private set; }
    public Guid TargetId { get; private set; }
    public Guid? AssigneeId { get; private set; }
    public ApprovalTaskStatus Status { get; private set; }
    public DateTime? DueDate { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public User? Assignee { get; private set; }

    public void Approve()
    {
        EnsureActionable("approved");
        Status = ApprovalTaskStatus.Approved;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void Reject()
    {
        EnsureActionable("rejected");
        Status = ApprovalTaskStatus.Rejected;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void Escalate(DateTime? dueDate = null)
    {
        EnsureActionable("escalated");
        Status = ApprovalTaskStatus.Escalated;
        if (dueDate.HasValue)
        {
            DueDate = NormalizeOptionalUtc(dueDate, nameof(dueDate));
        }
        UpdatedUtc = DateTime.UtcNow;
    }

    public void Assign(Guid? assigneeId, DateTime? dueDate = null)
    {
        if (assigneeId == Guid.Empty)
        {
            throw new ArgumentException("AssigneeId cannot be empty.", nameof(assigneeId));
        }

        AssigneeId = assigneeId;
        DueDate = NormalizeOptionalUtc(dueDate, nameof(dueDate));
        UpdatedUtc = DateTime.UtcNow;
    }

    public void SetStatus(ApprovalTaskStatus status, DateTime? dueDate = null)
    {
        ApprovalTaskStatusValues.EnsureSupported(status, nameof(status));
        Status = status;
        DueDate = dueDate.HasValue ? NormalizeOptionalUtc(dueDate, nameof(dueDate)) : DueDate;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void SetDueDate(DateTime? dueDate)
    {
        DueDate = NormalizeOptionalUtc(dueDate, nameof(dueDate));
        UpdatedUtc = DateTime.UtcNow;
    }

    private void EnsureActionable(string action)
    {
        if (Status is ApprovalTaskStatus.Pending or ApprovalTaskStatus.Escalated)
        {
            return;
        }

        throw new InvalidOperationException($"Approval task cannot be {action} when status is '{Status.ToStorageValue()}'.");
    }

    private static DateTime? NormalizeOptionalUtc(DateTime? value, string name)
    {
        if (!value.HasValue)
        {
            return null;
        }

        return NormalizeUtc(value.Value, name);
    }

    private static DateTime NormalizeUtc(DateTime value, string name)
    {
        if (value == default)
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }
}