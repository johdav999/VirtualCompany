namespace VirtualCompany.Domain.Entities;

/// <summary>Resumable, non-destructive disposition of one legacy session-owned presentation deck.</summary>
public sealed class SalesPresentationLegacyCompatibilityRecord : ICompanyOwnedEntity
{
    private SalesPresentationLegacyCompatibilityRecord() { }
    public SalesPresentationLegacyCompatibilityRecord(Guid id,Guid companyId,Guid deckId,Guid sessionId,string sourceContentHash,DateTime nowUtc)
    {
        if(companyId==Guid.Empty||deckId==Guid.Empty||sessionId==Guid.Empty)throw new ArgumentException("Company, deck, and session are required.");
        Id=id==Guid.Empty?Guid.NewGuid():id;CompanyId=companyId;DeckId=deckId;SessionId=sessionId;SourceContentHash=Required(sourceContentHash,64);
        Status="pending";Disposition="unclassified";CreatedUtc=UpdatedUtc=Utc(nowUtc);ConcurrencyVersion=1;
    }
    public Guid Id{get;private set;}public Guid CompanyId{get;private set;}public Guid DeckId{get;private set;}public Guid SessionId{get;private set;}
    public Guid? PresentationRunId{get;private set;}public string SourceContentHash{get;private set;}=null!;public string Status{get;private set;}=null!;
    public string Disposition{get;private set;}=null!;public string? ReasonCode{get;private set;}public string? Summary{get;private set;}
    public int AttemptCount{get;private set;}public DateTime? CompletedUtc{get;private set;}public DateTime? FailedUtc{get;private set;}
    public DateTime CreatedUtc{get;private set;}public DateTime UpdatedUtc{get;private set;}public long ConcurrencyVersion{get;private set;}
    public SalesPresentationDeck Deck{get;private set;}=null!;public SalesPresentationRun? PresentationRun{get;private set;}
    public void Complete(string disposition,string reasonCode,string summary,Guid? runId,DateTime nowUtc){if(disposition is not("preset_backed" or "compatibility_only"))throw new ArgumentOutOfRangeException(nameof(disposition));if(runId==Guid.Empty)throw new ArgumentException("Run cannot be empty.",nameof(runId));Status="completed";Disposition=disposition;ReasonCode=Required(reasonCode,100);Summary=Required(summary,1000);PresentationRunId=runId;AttemptCount++;CompletedUtc=Utc(nowUtc);FailedUtc=null;Touch(nowUtc);}
    public void Fail(string reasonCode,string summary,DateTime nowUtc){Status="failed";ReasonCode=Required(reasonCode,100);Summary=Required(summary,1000);AttemptCount++;FailedUtc=Utc(nowUtc);Touch(nowUtc);}
    public void QueueRetry(DateTime nowUtc){if(Status!="failed")throw new InvalidOperationException("Only a failed compatibility record can be retried.");Status="pending";FailedUtc=null;Touch(nowUtc);}
    private void Touch(DateTime now){UpdatedUtc=Utc(now);ConcurrencyVersion++;}private static string Required(string? value,int max){if(string.IsNullOrWhiteSpace(value))throw new ArgumentException("A value is required.");var result=value.Trim();if(result.Length>max)throw new ArgumentOutOfRangeException(nameof(value));return result;}private static DateTime Utc(DateTime value)=>value.Kind==DateTimeKind.Utc?value:value.ToUniversalTime();
}
