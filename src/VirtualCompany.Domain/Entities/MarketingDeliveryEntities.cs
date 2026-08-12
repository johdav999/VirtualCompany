namespace VirtualCompany.Domain.Entities;

public sealed class MarketingChannelOAuthSession : ICompanyOwnedEntity
{
    private MarketingChannelOAuthSession() { }
    public MarketingChannelOAuthSession(Guid id, Guid companyId, Guid userId, string provider, string stateHash,
        string redirectUri, DateTime expiresUtc)
    {
        SalesEntityText.EnsureCompany(companyId); Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId;
        UserId = Required(userId); Provider = MarketingChannelConnection.NormalizeProvider(provider);
        StateHash = Text(stateHash, nameof(stateHash), 128); RedirectUri = Text(redirectUri, nameof(redirectUri), 2000);
        ExpiresUtc = SalesEntityText.NormalizeUtc(expiresUtc, nameof(expiresUtc)); Status = "pending"; CreatedUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid UserId { get; private set; }
    public string Provider { get; private set; } = null!; public string StateHash { get; private set; } = null!;
    public string RedirectUri { get; private set; } = null!; public string Status { get; private set; } = null!;
    public DateTime ExpiresUtc { get; private set; } public DateTime CreatedUtc { get; private set; } public DateTime? ConsumedUtc { get; private set; }
    public void Consume(DateTime nowUtc)
    {
        nowUtc = SalesEntityText.NormalizeUtc(nowUtc, nameof(nowUtc));
        if (Status != "pending" || ExpiresUtc <= nowUtc) throw new InvalidOperationException("Marketing authorization state is expired or already used.");
        Status = "consumed"; ConsumedUtc = nowUtc;
    }
    public void Expire() { if (Status == "pending") Status = "expired"; }
    private static Guid Required(Guid value) => value == Guid.Empty ? throw new ArgumentException("User is required.") : value;
    private static string Text(string value, string name, int max) => SalesEntityText.NormalizeRequired(value, name, max);
}

public sealed class MarketingChannelDestination : ICompanyOwnedEntity
{
    private MarketingChannelDestination() { }
    public MarketingChannelDestination(Guid id, Guid companyId, Guid connectionId, string providerReference,
        string displayName, string destinationType, string capabilitiesJson, string? secretReference)
    {
        SalesEntityText.EnsureCompany(companyId); Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId;
        MarketingChannelConnectionId = connectionId == Guid.Empty ? throw new ArgumentException("Connection is required.") : connectionId;
        ProviderReference = Text(providerReference, nameof(providerReference), 500); DisplayName = Text(displayName, nameof(displayName), 200);
        DestinationType = Text(destinationType, nameof(destinationType), 64).ToLowerInvariant();
        CapabilitiesJson = Text(capabilitiesJson, nameof(capabilitiesJson), 8000);
        SecretReference = SalesEntityText.NormalizeOptional(secretReference, nameof(secretReference), 500);
        Status = "active"; LastDiscoveredUtc = CreatedUtc = UpdatedUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid MarketingChannelConnectionId { get; private set; }
    public string ProviderReference { get; private set; } = null!; public string DisplayName { get; private set; } = null!;
    public string DestinationType { get; private set; } = null!; public string CapabilitiesJson { get; private set; } = null!;
    public string? SecretReference { get; private set; } public string Status { get; private set; } = null!;
    public DateTime LastDiscoveredUtc { get; private set; } public DateTime CreatedUtc { get; private set; } public DateTime UpdatedUtc { get; private set; }
    public void Refresh(string displayName, string capabilitiesJson, string? secretReference)
    { DisplayName = Text(displayName, nameof(displayName), 200); CapabilitiesJson = Text(capabilitiesJson, nameof(capabilitiesJson), 8000); SecretReference = SalesEntityText.NormalizeOptional(secretReference, nameof(secretReference), 500); Status = "active"; LastDiscoveredUtc = UpdatedUtc = DateTime.UtcNow; }
    public void MarkUnavailable() { Status = "unavailable"; UpdatedUtc = DateTime.UtcNow; }
    private static string Text(string value, string name, int max) => SalesEntityText.NormalizeRequired(value, name, max);
}

public sealed class MarketingCreativeAsset : ICompanyOwnedEntity
{
    private MarketingCreativeAsset() { }
    public MarketingCreativeAsset(Guid id, Guid companyId, Guid briefId, Guid? campaignId, string name,
        string mediaType, string dimensions, string language, string generationSummary, string promptVersion,
        string providerReference, string brandProfileVersion, string safetyResult, string altText,
        string storageReference, string checksum, Guid ownerUserId, string idempotencyKey,
        Guid? assetFamilyId = null, int versionNumber = 1, Guid? contentVariantId = null,
        string sourceAssetIdsJson = "[]", string provenanceJson = "{}", string? auditReference = null)
    {
        SalesEntityText.EnsureCompany(companyId); Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId;
        MarketingContentBriefId = Required(briefId, nameof(briefId)); SalesCampaignId = campaignId;
        Name = Text(name, nameof(name), 200); MediaType = Text(mediaType, nameof(mediaType), 80); Dimensions = Text(dimensions, nameof(dimensions), 40);
        Language = Text(language, nameof(language), 20); GenerationSummary = Text(generationSummary, nameof(generationSummary), 4000);
        PromptVersion = Text(promptVersion, nameof(promptVersion), 64); ProviderReference = Text(providerReference, nameof(providerReference), 500);
        BrandProfileVersion = Text(brandProfileVersion, nameof(brandProfileVersion), 64); SafetyResult = Text(safetyResult, nameof(safetyResult), 1000);
        AltText = Text(altText, nameof(altText), 1000); StorageReference = Text(storageReference, nameof(storageReference), 2000);
        Checksum = Text(checksum, nameof(checksum), 128); OwnerUserId = Required(ownerUserId, nameof(ownerUserId));
        if (versionNumber < 1) throw new ArgumentOutOfRangeException(nameof(versionNumber));
        AssetFamilyId = assetFamilyId.GetValueOrDefault(Id); VersionNumber = versionNumber;
        MarketingContentVariantId = contentVariantId;
        SourceAssetIdsJson = Text(sourceAssetIdsJson, nameof(sourceAssetIdsJson), 16000);
        ProvenanceJson = Text(provenanceJson, nameof(provenanceJson), 16000);
        AuditReference = Text(auditReference ?? $"marketing-creative:{Id:N}:v{versionNumber}", nameof(auditReference), 200);
        IdempotencyKey = Text(idempotencyKey, nameof(idempotencyKey), 160); Status = MarketingStatuses.Draft; Version = 1; CreatedUtc = UpdatedUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid MarketingContentBriefId { get; private set; }
    public Guid? SalesCampaignId { get; private set; } public string Name { get; private set; } = null!; public string MediaType { get; private set; } = null!;
    public string Dimensions { get; private set; } = null!; public string Language { get; private set; } = null!; public string GenerationSummary { get; private set; } = null!;
    public string PromptVersion { get; private set; } = null!; public string ProviderReference { get; private set; } = null!; public string BrandProfileVersion { get; private set; } = null!;
    public string SafetyResult { get; private set; } = null!; public string AltText { get; private set; } = null!; public string StorageReference { get; private set; } = null!;
    public string Checksum { get; private set; } = null!; public Guid OwnerUserId { get; private set; } public string IdempotencyKey { get; private set; } = null!;
    public Guid AssetFamilyId { get; private set; } public int VersionNumber { get; private set; }
    public Guid? MarketingContentVariantId { get; private set; } public string SourceAssetIdsJson { get; private set; } = null!;
    public string ProvenanceJson { get; private set; } = null!; public string AuditReference { get; private set; } = null!;
    public string Status { get; private set; } = null!; public int Version { get; private set; } public DateTime CreatedUtc { get; private set; } public DateTime UpdatedUtc { get; private set; }
    public void Submit() { if (Status is not (MarketingStatuses.Draft or "changes_requested")) throw new InvalidOperationException("Asset must be draft or changes requested."); Status = MarketingStatuses.Submitted; Touch(); }
    public void UpdateMetadata(string name, string language, string altText)
    { if (Status is not (MarketingStatuses.Draft or "changes_requested")) throw new InvalidOperationException("Only editable asset versions can change metadata."); Name = Text(name, nameof(name), 200); Language = Text(language, nameof(language), 20); AltText = Text(altText, nameof(altText), 1000); Touch(); }
    public void Review(bool approved) { Require(MarketingStatuses.Submitted); Status = approved ? MarketingStatuses.Approved : MarketingStatuses.Rejected; Touch(); }
    public void RequestChanges() { Require(MarketingStatuses.Submitted); Status = "changes_requested"; Touch(); }
    public void Retire() { if (Status != MarketingStatuses.Approved) throw new InvalidOperationException("Only approved assets can be retired."); Status = "retired"; Touch(); }
    private void Require(string value) { if (Status != value) throw new InvalidOperationException($"Asset must be {value}."); } private void Touch() { Version++; UpdatedUtc = DateTime.UtcNow; }
    private static Guid Required(Guid value, string name) => value == Guid.Empty ? throw new ArgumentException($"{name} is required.") : value;
    private static string Text(string value, string name, int max) => SalesEntityText.NormalizeRequired(value, name, max);
}

public sealed class MarketingCreativeAssetScan : ICompanyOwnedEntity
{
    private MarketingCreativeAssetScan() { }
    public MarketingCreativeAssetScan(Guid id, Guid companyId, Guid assetId, string provider, string providerReference,
        string scannerVersion, string result, string reasonCode, string evidenceJson, DateTime scannedUtc)
    {
        SalesEntityText.EnsureCompany(companyId); Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId;
        MarketingCreativeAssetId = assetId == Guid.Empty ? throw new ArgumentException("Asset is required.") : assetId;
        Provider = T(provider, nameof(provider), 100); ProviderReference = T(providerReference, nameof(providerReference), 300);
        ScannerVersion = T(scannerVersion, nameof(scannerVersion), 100); Result = NormalizeResult(result);
        ReasonCode = T(reasonCode, nameof(reasonCode), 100); EvidenceJson = T(evidenceJson, nameof(evidenceJson), 16000);
        ScannedUtc = SalesEntityText.NormalizeUtc(scannedUtc, nameof(scannedUtc)); CreatedUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid MarketingCreativeAssetId { get; private set; }
    public string Provider { get; private set; } = null!; public string ProviderReference { get; private set; } = null!;
    public string ScannerVersion { get; private set; } = null!; public string Result { get; private set; } = null!;
    public string ReasonCode { get; private set; } = null!; public string EvidenceJson { get; private set; } = null!;
    public DateTime ScannedUtc { get; private set; } public DateTime CreatedUtc { get; private set; }
    public bool AllowsUse => Result == "passed";
    private static string NormalizeResult(string value) => value.Trim().ToLowerInvariant() switch
    { "passed" => "passed", "pending" => "pending", "failed" => "failed", "error" => "error", _ => throw new ArgumentException("Unsupported scan result.") };
    private static string T(string value, string name, int max) => SalesEntityText.NormalizeRequired(value, name, max);
}

public sealed class MarketingChannelConnection : ICompanyOwnedEntity
{
    private MarketingChannelConnection() { }
    public MarketingChannelConnection(Guid id, Guid companyId, string provider, string externalAccountReference,
        string displayName, string capabilitiesJson, string secretReference, Guid ownerUserId)
    { SalesEntityText.EnsureCompany(companyId); Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; Provider = NormalizeProvider(provider); ExternalAccountReference = Text(externalAccountReference, nameof(externalAccountReference), 500); DisplayName = Text(displayName, nameof(displayName), 200); CapabilitiesJson = Text(capabilitiesJson, nameof(capabilitiesJson), 16000); SecretReference = Text(secretReference, nameof(secretReference), 500); OwnerUserId = Required(ownerUserId); Status = "connected"; HealthStatus = "unknown"; CreatedUtc = UpdatedUtc = DateTime.UtcNow; }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public string Provider { get; private set; } = null!; public string ExternalAccountReference { get; private set; } = null!; public string DisplayName { get; private set; } = null!; public string CapabilitiesJson { get; private set; } = null!; public string SecretReference { get; private set; } = null!; public Guid OwnerUserId { get; private set; } public string Status { get; private set; } = null!; public string HealthStatus { get; private set; } = null!; public string? FailureSummary { get; private set; } public DateTime? LastCheckedUtc { get; private set; } public DateTime CreatedUtc { get; private set; } public DateTime UpdatedUtc { get; private set; }
    public void RecordHealth(bool healthy, string? failure) { HealthStatus = healthy ? "healthy" : "reauthorization_required"; FailureSummary = healthy ? null : SalesEntityText.NormalizeOptional(failure, nameof(failure), 1000); LastCheckedUtc = UpdatedUtc = DateTime.UtcNow; }
    public void Reconnect(string externalAccountReference, string displayName, string capabilitiesJson, string secretReference)
    { ExternalAccountReference = Text(externalAccountReference, nameof(externalAccountReference), 500); DisplayName = Text(displayName, nameof(displayName), 200); CapabilitiesJson = Text(capabilitiesJson, nameof(capabilitiesJson), 16000); SecretReference = Text(secretReference, nameof(secretReference), 500); Status = "connected"; HealthStatus = "healthy"; FailureSummary = null; LastCheckedUtc = UpdatedUtc = DateTime.UtcNow; }
    public void Disconnect() { Status = "disconnected"; UpdatedUtc = DateTime.UtcNow; }
    public static string NormalizeProvider(string value) => value.Trim().ToLowerInvariant() switch { "linkedin" => "linkedin", "meta" or "facebook" or "instagram" => "meta", "x" or "twitter" => "x", _ => throw new ArgumentException("Unsupported Marketing channel provider.") };
    private static Guid Required(Guid id) => id == Guid.Empty ? throw new ArgumentException("Owner is required.") : id; private static string Text(string v, string n, int m) => SalesEntityText.NormalizeRequired(v, n, m);
}

public sealed class MarketingChannelAction : ICompanyOwnedEntity
{
    private MarketingChannelAction() { }
    public MarketingChannelAction(Guid id, Guid companyId, Guid connectionId, Guid? campaignId, Guid? contentBriefId,
        string destinationReference, string actionType, string payloadJson, DateTime? scheduledUtc, string idempotencyKey,
        int? contentBriefVersion = null)
    { SalesEntityText.EnsureCompany(companyId); Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; MarketingChannelConnectionId = Required(connectionId); SalesCampaignId = campaignId; MarketingContentBriefId = contentBriefId; if(contentBriefId.HasValue && (!contentBriefVersion.HasValue || contentBriefVersion < 1)) throw new ArgumentException("Content brief version is required."); ContentBriefVersion = contentBriefVersion; DestinationReference = Text(destinationReference, nameof(destinationReference), 500); ActionType = Text(actionType, nameof(actionType), 80); PayloadJson = Text(payloadJson, nameof(payloadJson), 32000); ScheduledUtc = scheduledUtc?.ToUniversalTime(); IdempotencyKey = Text(idempotencyKey, nameof(idempotencyKey), 200); Status = "proposed"; Version = 1; CreatedUtc = UpdatedUtc = DateTime.UtcNow; }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid MarketingChannelConnectionId { get; private set; } public Guid? SalesCampaignId { get; private set; } public Guid? MarketingContentBriefId { get; private set; } public int? ContentBriefVersion { get; private set; } public string DestinationReference { get; private set; } = null!; public string ActionType { get; private set; } = null!; public string PayloadJson { get; private set; } = null!; public DateTime? ScheduledUtc { get; private set; } public string IdempotencyKey { get; private set; } = null!; public Guid? ApprovalRequestId { get; private set; } public string Status { get; private set; } = null!; public int Version { get; private set; } public int AttemptCount { get; private set; } public string? ProviderReference { get; private set; } public string? FailureCode { get; private set; } public DateTime CreatedUtc { get; private set; } public DateTime UpdatedUtc { get; private set; }
    public void Submit(Guid approvalId) { if (Status != "proposed") throw new InvalidOperationException("Only proposed actions can be submitted."); ApprovalRequestId = Required(approvalId); Status = "awaiting_approval"; Touch(); }
    public void Queue() { if (Status != "awaiting_approval" || !ApprovalRequestId.HasValue) throw new InvalidOperationException("Approved action is required."); Status = "queued"; Touch(); }
    public void ClaimForDispatch() { if (Status is not ("queued" or "retry_scheduled")) throw new InvalidOperationException("Action is not dispatchable."); Status = "dispatching"; Touch(); }
    public void RecordDispatch(string providerReference) { if (Status != "dispatching") throw new InvalidOperationException("Action has not been claimed for dispatch."); AttemptCount++; ProviderReference = Text(providerReference, nameof(providerReference), 500); FailureCode = null; Status = "dispatched"; Touch(); }
    public void RecordFailure(string code, bool retryable) { if (Status != "dispatching") throw new InvalidOperationException("Action has not been claimed for dispatch."); AttemptCount++; FailureCode = Text(code, nameof(code), 100); Status = retryable ? "retry_scheduled" : "failed"; Touch(); }
    public void RecordAmbiguous(string code) { if (Status != "dispatching") throw new InvalidOperationException("Action has not been claimed for dispatch."); AttemptCount++; FailureCode = Text(code, nameof(code), 100); Status = "ambiguous"; Touch(); }
    public void Reconcile(bool delivered) { if (Status is not ("dispatched" or "ambiguous")) throw new InvalidOperationException("Only dispatched or ambiguous actions can reconcile."); Status = delivered ? "delivered" : "failed"; FailureCode = delivered ? null : "provider_not_found"; Touch(); }
    public void Cancel() { if (Status is "delivered" or "cancelled") return; Status = "cancelled"; Touch(); }
    private void Touch() { Version++; UpdatedUtc = DateTime.UtcNow; } private static Guid Required(Guid id) => id == Guid.Empty ? throw new ArgumentException("Reference is required.") : id; private static string Text(string v, string n, int m) => SalesEntityText.NormalizeRequired(v, n, m);
}

public sealed class MarketingLifecycleJourney : ICompanyOwnedEntity
{
    private MarketingLifecycleJourney() { }
    public MarketingLifecycleJourney(Guid id, Guid companyId, string name, string audienceEligibilityJson,
        string entryExitCriteriaJson, string stepsJson, string guardrailsJson, int frequencyCap,
        DateTime validFromUtc, DateTime validToUtc, Guid ownerUserId, string idempotencyKey,
        Guid? supersedesJourneyId = null, int definitionVersion = 1, Guid? segmentVersionId = null)
    { SalesEntityText.EnsureCompany(companyId); if (frequencyCap < 1) throw new ArgumentOutOfRangeException(nameof(frequencyCap)); if (definitionVersion < 1) throw new ArgumentOutOfRangeException(nameof(definitionVersion)); validFromUtc = validFromUtc.ToUniversalTime(); validToUtc = validToUtc.ToUniversalTime(); if (validToUtc <= validFromUtc) throw new ArgumentException("Journey validity is invalid."); Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; Name = T(name, nameof(name), 200); AudienceEligibilityJson = T(audienceEligibilityJson, nameof(audienceEligibilityJson), 16000); EntryExitCriteriaJson = T(entryExitCriteriaJson, nameof(entryExitCriteriaJson), 16000); StepsJson = T(stepsJson, nameof(stepsJson), 32000); GuardrailsJson = T(guardrailsJson, nameof(guardrailsJson), 16000); FrequencyCap = frequencyCap; ValidFromUtc = validFromUtc; ValidToUtc = validToUtc; OwnerUserId = ownerUserId == Guid.Empty ? throw new ArgumentException("Owner is required.") : ownerUserId; IdempotencyKey = T(idempotencyKey, nameof(idempotencyKey), 160); SupersedesJourneyId = supersedesJourneyId; MarketingCustomerSegmentVersionId = segmentVersionId; Status = "draft"; Version = definitionVersion; ConcurrencyVersion = 1; CreatedUtc = UpdatedUtc = DateTime.UtcNow; }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public string Name { get; private set; } = null!; public string AudienceEligibilityJson { get; private set; } = null!; public string EntryExitCriteriaJson { get; private set; } = null!; public string StepsJson { get; private set; } = null!; public string GuardrailsJson { get; private set; } = null!; public int FrequencyCap { get; private set; } public DateTime ValidFromUtc { get; private set; } public DateTime ValidToUtc { get; private set; } public Guid OwnerUserId { get; private set; } public string IdempotencyKey { get; private set; } = null!; public Guid? SupersedesJourneyId { get; private set; } public Guid? MarketingCustomerSegmentVersionId { get; private set; } public string Status { get; private set; } = null!; public Guid? ApprovalRequestId { get; private set; } public int Version { get; private set; } public int ConcurrencyVersion { get; private set; } public DateTime CreatedUtc { get; private set; } public DateTime UpdatedUtc { get; private set; }
    public void Submit(Guid approvalId) { if (Status != "draft") throw new InvalidOperationException("Journey is not a draft."); ApprovalRequestId = approvalId; Status = "in_review"; Touch(); } public void Activate() { if (Status != "in_review" || !ApprovalRequestId.HasValue) throw new InvalidOperationException("Approved journey is required."); Status = "active"; Touch(); } public void Pause() { if (Status != "active") throw new InvalidOperationException("Only active journeys can pause."); Status = "paused"; Touch(); } public void Resume() { if (Status != "paused") throw new InvalidOperationException("Only paused journeys can resume."); Status = "active"; Touch(); } public void Complete() { if (Status is not ("active" or "paused")) throw new InvalidOperationException("Journey is not running."); Status = "completed"; Touch(); } public void Cancel() { if (Status is not ("draft" or "in_review" or "paused")) throw new InvalidOperationException("Journey cannot be cancelled in its current state."); Status = "cancelled"; Touch(); } public void Supersede() { if (Status is not ("active" or "paused" or "completed")) throw new InvalidOperationException("Journey cannot be superseded in its current state."); Status = "superseded"; Touch(); }
    private void Touch() { ConcurrencyVersion++; UpdatedUtc = DateTime.UtcNow; } private static string T(string v, string n, int m) => SalesEntityText.NormalizeRequired(v, n, m);
}

public sealed class MarketingJourneyEnrollment : ICompanyOwnedEntity
{
    private MarketingJourneyEnrollment() { }
    public MarketingJourneyEnrollment(Guid id, Guid companyId, Guid journeyId, Guid contactId,
        int journeyVersion, string consentEvidenceReference, string idempotencyKey, DateTime nextStepUtc)
    {
        SalesEntityText.EnsureCompany(companyId);
        if (journeyId == Guid.Empty || contactId == Guid.Empty) throw new ArgumentException("Journey and contact are required.");
        if (journeyVersion < 1) throw new ArgumentOutOfRangeException(nameof(journeyVersion));
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; MarketingLifecycleJourneyId = journeyId;
        ContactId = contactId; JourneyVersion = journeyVersion;
        ConsentEvidenceReference = SalesEntityText.NormalizeRequired(consentEvidenceReference, nameof(consentEvidenceReference), 500);
        IdempotencyKey = SalesEntityText.NormalizeRequired(idempotencyKey, nameof(idempotencyKey), 200);
        Status = "active"; NextStepIndex = 0; NextStepUtc = nextStepUtc.ToUniversalTime();
        WindowStartedUtc = DateTime.UtcNow; CreatedUtc = UpdatedUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; }
    public Guid MarketingLifecycleJourneyId { get; private set; } public Guid ContactId { get; private set; }
    public int JourneyVersion { get; private set; } public string ConsentEvidenceReference { get; private set; } = null!;
    public string IdempotencyKey { get; private set; } = null!; public string Status { get; private set; } = null!;
    public int NextStepIndex { get; private set; } public DateTime? NextStepUtc { get; private set; }
    public int ActionsInWindow { get; private set; } public DateTime WindowStartedUtc { get; private set; }
    public Guid? LastChannelActionId { get; private set; } public string? FailureCode { get; private set; }
    public string? LeaseOwner { get; private set; } public DateTime? LeaseExpiresUtc { get; private set; }
    public int AttemptCount { get; private set; } public int MaximumAttempts { get; private set; } = 5;
    public DateTime? NextAttemptUtc { get; private set; } public int ConcurrencyVersion { get; private set; } = 1;
    public string LastEvaluationJson { get; private set; } = "{}";
    public DateTime CreatedUtc { get; private set; } public DateTime UpdatedUtc { get; private set; }
    public void Advance(Guid channelActionId, DateTime? nextStepUtc)
    { if (Status != "active") throw new InvalidOperationException("Enrollment is not active."); LastChannelActionId = channelActionId; NextStepIndex++; ActionsInWindow++; NextStepUtc = nextStepUtc?.ToUniversalTime(); if (!nextStepUtc.HasValue) Status = "completed"; ClearLease(); UpdatedUtc = DateTime.UtcNow; }
    public void Block(string code) { FailureCode = SalesEntityText.NormalizeRequired(code, nameof(code), 100); Status = "blocked"; NextStepUtc = null; ClearLease(); UpdatedUtc = DateTime.UtcNow; }
    public void Exit(string code) { FailureCode = SalesEntityText.NormalizeRequired(code, nameof(code), 100); Status = "exited"; NextStepUtc = null; ClearLease(); UpdatedUtc = DateTime.UtcNow; }
    public void Complete() { if (Status != "active") throw new InvalidOperationException("Enrollment is not active."); Status = "completed"; NextStepUtc = null; ClearLease(); UpdatedUtc = DateTime.UtcNow; }
    public void ResetWindow(DateTime nowUtc) { WindowStartedUtc = nowUtc.ToUniversalTime(); ActionsInWindow = 0; UpdatedUtc = DateTime.UtcNow; }
    public void WaitUntil(DateTime nextCheckUtc) { if (Status != "active") throw new InvalidOperationException("Enrollment is not active."); NextStepUtc = nextCheckUtc.ToUniversalTime(); ClearLease(); UpdatedUtc = DateTime.UtcNow; }
    public void Claim(string owner, TimeSpan lease, DateTime nowUtc)
    {
        nowUtc = nowUtc.ToUniversalTime();
        if (Status != "active" || (LeaseExpiresUtc.HasValue && LeaseExpiresUtc > nowUtc)) throw new InvalidOperationException("Enrollment is not claimable.");
        if (NextAttemptUtc.HasValue && NextAttemptUtc > nowUtc) throw new InvalidOperationException("Enrollment retry is not due.");
        LeaseOwner = SalesEntityText.NormalizeRequired(owner, nameof(owner), 128); LeaseExpiresUtc = nowUtc.Add(lease);
        AttemptCount++; ConcurrencyVersion++; UpdatedUtc = DateTime.UtcNow;
    }
    public void RecordEvaluation(string owner, string evidenceJson)
    {
        EnsureLease(owner); System.Text.Json.JsonDocument.Parse(evidenceJson);
        LastEvaluationJson = SalesEntityText.NormalizeRequired(evidenceJson, nameof(evidenceJson), 16000); ConcurrencyVersion++; UpdatedUtc = DateTime.UtcNow;
    }
    public void ReleaseLease(string owner)
    { EnsureLease(owner); LeaseOwner = null; LeaseExpiresUtc = null; NextAttemptUtc = null; ConcurrencyVersion++; UpdatedUtc = DateTime.UtcNow; }
    public void Retry(string owner, string code, DateTime retryUtc)
    {
        EnsureLease(owner); FailureCode = SalesEntityText.NormalizeRequired(code, nameof(code), 100);
        LeaseOwner = null; LeaseExpiresUtc = null; NextAttemptUtc = retryUtc.ToUniversalTime();
        if (AttemptCount >= MaximumAttempts) { Status = "dead_letter"; NextStepUtc = null; }
        ConcurrencyVersion++; UpdatedUtc = DateTime.UtcNow;
    }
    private void EnsureLease(string owner)
    { if (LeaseOwner is null || !string.Equals(LeaseOwner, owner, StringComparison.Ordinal)) throw new InvalidOperationException("Enrollment lease is not owned by this worker."); }
    private void ClearLease() { LeaseOwner = null; LeaseExpiresUtc = null; NextAttemptUtc = null; ConcurrencyVersion++; }
}

public sealed class MarketingAttributionResult : ICompanyOwnedEntity
{
    private MarketingAttributionResult() { }
    public MarketingAttributionResult(Guid id, Guid companyId, string subjectType, Guid subjectId, string model,
        string classification, decimal attributedValue, string unit, string evidenceJson, decimal confidence,
        DateTime periodStartUtc, DateTime periodEndUtc, string idempotencyKey)
    { SalesEntityText.EnsureCompany(companyId); if (subjectId == Guid.Empty || confidence is < 0 or > 1 || periodEndUtc <= periodStartUtc) throw new ArgumentException("Attribution input is invalid."); Classification = classification.Trim().ToLowerInvariant() is "observed" or "configured_rule" or "correlation" or "inference" ? classification.Trim().ToLowerInvariant() : throw new ArgumentException("Unsupported attribution classification."); Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; SubjectType = SalesEntityText.NormalizeRequired(subjectType, nameof(subjectType), 80); SubjectId = subjectId; Model = SalesEntityText.NormalizeRequired(model, nameof(model), 80); AttributedValue = attributedValue; Unit = SalesEntityText.NormalizeRequired(unit, nameof(unit), 40); EvidenceJson = SalesEntityText.NormalizeRequired(evidenceJson, nameof(evidenceJson), 32000); Confidence = confidence; PeriodStartUtc = periodStartUtc.ToUniversalTime(); PeriodEndUtc = periodEndUtc.ToUniversalTime(); IdempotencyKey = SalesEntityText.NormalizeRequired(idempotencyKey, nameof(idempotencyKey), 200); CreatedUtc = DateTime.UtcNow; }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public string SubjectType { get; private set; } = null!; public Guid SubjectId { get; private set; } public string Model { get; private set; } = null!; public string Classification { get; private set; } = null!; public decimal AttributedValue { get; private set; } public string Unit { get; private set; } = null!; public string EvidenceJson { get; private set; } = null!; public decimal Confidence { get; private set; } public DateTime PeriodStartUtc { get; private set; } public DateTime PeriodEndUtc { get; private set; } public string IdempotencyKey { get; private set; } = null!; public DateTime CreatedUtc { get; private set; }
}

public sealed class MarketingEventTrigger : ICompanyOwnedEntity
{
    private MarketingEventTrigger() { }
    public MarketingEventTrigger(Guid id, Guid companyId, string eventType, string sourceType, string sourceId,
        int sourceVersion, string severity, string evidenceJson, string idempotencyKey, string correlationId)
    { SalesEntityText.EnsureCompany(companyId); Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; EventType = T(eventType, nameof(eventType), 100); SourceType = T(sourceType, nameof(sourceType), 80); SourceId = T(sourceId, nameof(sourceId), 200); SourceVersion = sourceVersion; Severity = T(severity, nameof(severity), 32); EvidenceJson = T(evidenceJson, nameof(evidenceJson), 32000); IdempotencyKey = T(idempotencyKey, nameof(idempotencyKey), 200); CorrelationId = T(correlationId, nameof(correlationId), 128); Status = "pending"; CreatedUtc = UpdatedUtc = DateTime.UtcNow; }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public string EventType { get; private set; } = null!; public string SourceType { get; private set; } = null!; public string SourceId { get; private set; } = null!; public int SourceVersion { get; private set; } public string Severity { get; private set; } = null!; public string EvidenceJson { get; private set; } = null!; public string IdempotencyKey { get; private set; } = null!; public string CorrelationId { get; private set; } = null!; public string Status { get; private set; } = null!; public Guid? OperatingRunId { get; private set; } public Guid? RelatedTaskId { get; private set; } public string? FailureSummary { get; private set; } public DateTime CreatedUtc { get; private set; } public DateTime UpdatedUtc { get; private set; }
    public void LinkTask(Guid taskId) { RelatedTaskId = taskId == Guid.Empty ? throw new ArgumentException("Task is required.") : taskId; UpdatedUtc = DateTime.UtcNow; }
    public void LinkRun(Guid runId) { OperatingRunId = runId == Guid.Empty ? throw new ArgumentException("Run is required.") : runId; Status = "processed"; FailureSummary = null; UpdatedUtc = DateTime.UtcNow; } public void Fail(string summary) { FailureSummary = T(summary, nameof(summary), 2000); Status = "failed"; UpdatedUtc = DateTime.UtcNow; } public void Resolve() { Status = "resolved"; UpdatedUtc = DateTime.UtcNow; }
    private static string T(string v, string n, int m) => SalesEntityText.NormalizeRequired(v, n, m);
}
