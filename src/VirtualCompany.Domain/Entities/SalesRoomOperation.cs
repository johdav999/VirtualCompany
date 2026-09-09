namespace VirtualCompany.Domain.Entities;

public sealed class SalesRoomOperation : ICompanyOwnedEntity
{
    private SalesRoomOperation() { }
    public SalesRoomOperation(Guid company, Guid room, Guid command, string action, Guid? target, Guid? actor, string requestHash, DateTime now, bool queued = false)
    { Id = Guid.NewGuid(); CompanyId = company; RoomId = room; CommandId = command; Action = action; TargetId = target; ActorId = actor; RequestHash = requestHash; CreatedUtc = now; State = queued ? "queued" : "completed"; Version = 1; }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid RoomId { get; private set; }
    public Guid CommandId { get; private set; }
    public string Action { get; private set; } = "";
    public Guid? TargetId { get; private set; }
    public Guid? ActorId { get; private set; }
    public string RequestHash { get; private set; } = "";
    public string State { get; private set; } = "queued";
    public int Attempts { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public Guid? LeaseId { get; private set; }
    public DateTime? LeaseUntilUtc { get; private set; }
    public string? ProblemCode { get; private set; }
    public long Version { get; private set; }
    public Guid Claim(DateTime now) { LeaseId = Guid.NewGuid(); LeaseUntilUtc = now.AddMinutes(3); State = "running"; Attempts++; Version++; return LeaseId.Value; }
    public void Complete() { State = "completed"; LeaseId = null; LeaseUntilUtc = null; ProblemCode = null; Version++; }
    public void Retry(string code) { State = Attempts >= 5 ? "needs_review" : "reconciliation_required"; ProblemCode = code; LeaseId = null; LeaseUntilUtc = null; Version++; }
}
