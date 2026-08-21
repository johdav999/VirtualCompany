using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Chat;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.GuidedWork;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Observability;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class GuidedWorkSessionService : IGuidedWorkSessionService, IGuidedResearchContinuationService
{
    private const int DefaultPageSize = 30;
    private const int MaxPageSize = 100;
    private static readonly HashSet<string> SupportedStatuses = new(StringComparer.OrdinalIgnoreCase)
    { GuidedDraftFieldStatuses.Proposed, GuidedDraftFieldStatuses.NeedsWork, GuidedDraftFieldStatuses.Confirmed, GuidedDraftFieldStatuses.Conflicting, GuidedDraftFieldStatuses.Unknown };

    private readonly VirtualCompanyDbContext _db;
    private readonly ICompanyMembershipContextResolver _memberships;
    private readonly ICompanyDirectChatService _chat;
    private readonly IGuidedCheckpointProvider _checkpoint;
    private readonly IGuidedWorkshopDocumentService _workshopDocuments;
    private readonly IGuidedEvidenceResearchService _evidenceResearch;
    private readonly ICompanyOutboxEnqueuer _outbox;
    private readonly IReadOnlyDictionary<string, IGuidedArtifactDefinition> _definitions;
    private readonly IAuditEventWriter _audit;
    private readonly ICorrelationContextAccessor _correlation;
    private readonly ILogger<GuidedWorkSessionService> _logger;
    private readonly GuidedDialogueOptions _options;
    private readonly IDataProtector _operationProtector;

    public GuidedWorkSessionService(VirtualCompanyDbContext db, ICompanyMembershipContextResolver memberships,
        ICompanyDirectChatService chat, IGuidedCheckpointProvider checkpoint, IGuidedWorkshopDocumentService workshopDocuments,
        IGuidedEvidenceResearchService evidenceResearch, ICompanyOutboxEnqueuer outbox, IEnumerable<IGuidedArtifactDefinition> definitions,
        IAuditEventWriter audit, ICorrelationContextAccessor correlation, IOptions<GuidedDialogueOptions> options,
        IDataProtectionProvider dataProtection, ILogger<GuidedWorkSessionService> logger)
    {
        _db = db; _memberships = memberships; _chat = chat; _checkpoint = checkpoint; _workshopDocuments=workshopDocuments; _evidenceResearch=evidenceResearch; _outbox=outbox; _audit = audit; _correlation = correlation;
        _options = options.Value; _logger = logger;
        _operationProtector = dataProtection.CreateProtector("VirtualCompany.GuidedWork.OperationResponse.v1");
        try { _definitions = definitions.ToDictionary(x => x.ArtifactType, StringComparer.OrdinalIgnoreCase); }
        catch (ArgumentException) { throw new InvalidOperationException("Guided artifact types must be unique."); }
        foreach(var definition in _definitions.Values)
        {
            if(definition.Fields.Count==0||definition.Fields.Count>Math.Clamp(_options.MaxFieldsPerArtifact,1,200))throw new InvalidOperationException($"Guided artifact {definition.ArtifactType} has an unsupported field count.");
            if(definition.Fields.Select(x=>x.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=definition.Fields.Count)throw new InvalidOperationException($"Guided artifact {definition.ArtifactType} has duplicate field paths.");
        }
    }

    public async Task<GuidedWorkSessionDto> StartAsync(Guid companyId, StartGuidedWorkSessionCommand command, CancellationToken cancellationToken)
    {
        EnsureFeatureEnabled();
        var member = await RequireMemberAsync(companyId, cancellationToken);
        if (string.IsNullOrWhiteSpace(command.ArtifactType) || command.AgentId == Guid.Empty)
            throw Validation((nameof(command.ArtifactType), "An artifact type and agent are required."));
        var definition = ResolveDefinition(command.ArtifactType);
        await definition.EnsureEligibleAsync(companyId, command.AgentId, cancellationToken);
        var initial = await definition.InitializeAsync(companyId, command.AgentId, command.TargetArtifactId, cancellationToken);
        var resumableId = await _db.GuidedWorkSessions.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.CreatedByUserId == member.UserId && x.AgentId == command.AgentId &&
                        x.ArtifactType == definition.ArtifactType && x.SchemaVersion == definition.SchemaVersion && x.TargetArtifactId == initial.TargetArtifactId &&
                        (x.Status == GuidedWorkSessionStatuses.Active || x.Status == GuidedWorkSessionStatuses.ReviewReady))
            .OrderByDescending(x => x.UpdatedUtc).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(cancellationToken);
        if (resumableId is Guid existingId)
        {
            GuidedWorkTelemetry.DeduplicatedOperations.Add(1, new KeyValuePair<string, object?>("operation.type", "start_resume"));
            return await LoadDtoAsync(companyId, existingId, member.UserId, cancellationToken);
        }
        var activeCount=await _db.GuidedWorkSessions.AsNoTracking().CountAsync(x=>x.CompanyId==companyId&&x.CreatedByUserId==member.UserId&&(x.Status==GuidedWorkSessionStatuses.Active||x.Status==GuidedWorkSessionStatuses.ReviewReady),cancellationToken);
        if(activeCount>=Math.Clamp(_options.MaxActiveSessionsPerUser,1,100))throw Validation(("sessions","Finish or cancel an existing workshop before starting another."));
        var conversation = await _chat.GetOrCreateDirectAgentConversationAsync(companyId, command.AgentId, cancellationToken);
        var correlationId = string.IsNullOrWhiteSpace(_correlation.CorrelationId) ? Guid.NewGuid().ToString("N") : _correlation.CorrelationId;
        var session = new GuidedWorkSession(Guid.NewGuid(), companyId, conversation.Id, command.AgentId, member.UserId,
            definition.ArtifactType, definition.SchemaVersion, initial.TargetArtifactId, correlationId!);
        session.SetInitialTargetVersion(initial.TargetArtifactVersion);
        foreach (var fieldDefinition in definition.Fields)
        {
            var field = new GuidedDraftField(Guid.NewGuid(), companyId, session.Id, fieldDefinition.Path, fieldDefinition.Label,
                fieldDefinition.ValueType, fieldDefinition.IsRequired);
            if (initial.InitialValues.TryGetValue(fieldDefinition.Path, out var value))
            {
                var status = initial.InitialStatuses?.GetValueOrDefault(fieldDefinition.Path) ?? GuidedDraftFieldStatuses.Confirmed;
                ValidateValue(fieldDefinition, value);
                field.Set(value?.ToJsonString(), status, "existing_artifact", null,
                    new Dictionary<string,JsonNode?>{{"original_value_json",JsonValue.Create(value?.ToJsonString())}}, "Loaded from the current artifact.");
            }
            session.Fields.Add(field);
        }
        var ready = CountReady(session.Fields);
        session.Advance(initial.OpeningSummary ?? $"Started {definition.DisplayName}.", initial.OpeningQuestion,
            definition.Fields.Count(x => x.IsRequired), ready);
        _db.GuidedWorkSessions.Add(session);
        await QueueAuditAsync(member, session, "guided_session.started", "Started a guided work session.", cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        GuidedWorkTelemetry.SessionsStarted.Add(1,new KeyValuePair<string,object?>("artifact.type",definition.ArtifactType));
        return await LoadDtoAsync(companyId, session.Id, member.UserId, cancellationToken);
    }

    public async Task<IReadOnlyList<GuidedArtifactOptionDto>> ListArtifactOptionsAsync(Guid companyId,Guid agentId,CancellationToken cancellationToken)
    {
        await RequireMemberAsync(companyId,cancellationToken);if(agentId==Guid.Empty)throw Validation((nameof(agentId),"AgentId is required."));var result=new List<GuidedArtifactOptionDto>();
        foreach(var definition in _definitions.Values.OrderBy(x=>x.DisplayName,StringComparer.OrdinalIgnoreCase))
        {try{await definition.EnsureEligibleAsync(companyId,agentId,cancellationToken);result.Add(new(definition.ArtifactType,definition.DisplayName,definition.SchemaVersion,definition.RequiresTargetArtifact,definition.Capabilities));}catch(UnauthorizedAccessException){}}
        return result;
    }

    public async Task<GuidedWorkSessionListDto> ListAsync(Guid companyId, ListGuidedWorkSessionsQuery query, CancellationToken cancellationToken)
    {
        var member = await RequireMemberAsync(companyId, cancellationToken);
        var skip = Math.Max(0, query.Skip ?? 0); var take = Math.Clamp(query.Take ?? DefaultPageSize, 1, MaxPageSize);
        var sessions = _db.GuidedWorkSessions.AsNoTracking().Where(x => x.CompanyId == companyId && x.CreatedByUserId == member.UserId);
        if (!string.IsNullOrWhiteSpace(query.Status)) sessions = sessions.Where(x => x.Status == query.Status.Trim());
        if (!string.IsNullOrWhiteSpace(query.ArtifactType)) sessions = sessions.Where(x => x.ArtifactType == query.ArtifactType.Trim());
        var total = await sessions.CountAsync(cancellationToken);
        var ids = await sessions.OrderByDescending(x => x.UpdatedUtc).ThenByDescending(x => x.Id).Skip(skip).Take(take).Select(x => x.Id).ToListAsync(cancellationToken);
        var items = new List<GuidedWorkSessionDto>();
        foreach (var id in ids) items.Add(await LoadDtoAsync(companyId, id, member.UserId, cancellationToken));
        return new GuidedWorkSessionListDto(items, total, skip, take);
    }

    public async Task<GuidedWorkSessionDto> GetAsync(Guid companyId, Guid sessionId, CancellationToken cancellationToken)
    {
        var member = await RequireMemberAsync(companyId, cancellationToken);
        return await LoadDtoAsync(companyId, sessionId, member.UserId, cancellationToken);
    }

    public async Task<GuidedWorkTurnResultDto> AddTurnAsync(Guid companyId, Guid sessionId, AddGuidedWorkTurnCommand command, CancellationToken cancellationToken)
    {
        EnsureFeatureEnabled();
        var member = await RequireMemberAsync(companyId, cancellationToken);
        if (string.IsNullOrWhiteSpace(command.Body) || command.Body.Trim().Length > Math.Clamp(_options.MaxTurnCharacters, 100, 16000))
            throw Validation((nameof(command.Body), "A bounded message is required."));
        if (command.ClientRequestId == Guid.Empty) throw Validation((nameof(command.ClientRequestId), "ClientRequestId is required."));
        if (command.Modality is not ("text" or "voice")) throw Validation((nameof(command.Modality), "Modality must be text or voice."));
        if (command.DurationMs is < 0) throw Validation((nameof(command.DurationMs), "Duration cannot be negative."));
        if (await ReadOperationAsync<GuidedWorkTurnResultDto>(companyId, sessionId, command.ClientRequestId, "turn", cancellationToken) is { } retry) return retry;
        if (command.Modality == "voice" && command.Interrupted) GuidedWorkTelemetry.VoiceInterruptions.Add(1);

        var session = await LoadSessionAsync(companyId, sessionId, member.UserId, cancellationToken);
        EnsureVersion(session, command.ExpectedVersion);
        EnsureMutable(session);
        var definition = ResolveDefinition(session.ArtifactType);
        var checkpointFieldVersions = session.Fields.ToDictionary(x => x.Path, x => x.Version, StringComparer.OrdinalIgnoreCase);
        var normalizedBody = command.Body.Trim();
        var recentUserMessages = await _db.Messages.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.ConversationId == session.ConversationId &&
                        x.SenderType == ChatSenderTypes.User && x.SenderId == member.UserId)
            .OrderByDescending(x => x.CreatedUtc)
            .Take(500)
            .ToListAsync(cancellationToken);
        var userMessage = recentUserMessages.FirstOrDefault(x =>
            ReadSessionId(x.StructuredPayload) == session.Id &&
            ReadClientRequestId(x.StructuredPayload) == command.ClientRequestId &&
            string.Equals(ReadMessageKind(x.StructuredPayload), "guided_turn", StringComparison.Ordinal));
        if (userMessage is not null && !string.Equals(userMessage.Body, normalizedBody, StringComparison.Ordinal))
            throw new GuidedWorkConflictException("This request identifier was already used for a different workshop turn.");
        if (userMessage is null)
        {
            userMessage = new Message(Guid.NewGuid(), companyId, session.ConversationId, ChatSenderTypes.User, member.UserId,
                ChatMessageTypes.Text, normalizedBody, SessionMessageMetadata(session.Id, command.ClientRequestId, "guided_turn", command));
            _db.Messages.Add(userMessage);
            await _db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            GuidedWorkTelemetry.DeduplicatedOperations.Add(1, new KeyValuePair<string, object?>("operation.type", "turn_input"));
        }
        var recentConversationRows = await _db.Messages.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.ConversationId == session.ConversationId && x.Id != userMessage.Id)
            .OrderByDescending(x => x.CreatedUtc)
            .ThenByDescending(x => x.Id)
            .Take(20)
            .ToListAsync(cancellationToken);
        var recentConversation = BuildRecentConversation(recentConversationRows);
        var attachedDocumentContext=definition.Capabilities.SupportsDocumentAttachments
            ?await _workshopDocuments.SearchAsync(companyId,sessionId,session.AgentId,normalizedBody,cancellationToken)
            :"No workshop documents are attached for this artifact.";
        var companyReferenceContext=await definition.BuildCompanyReferenceContextAsync(companyId,session.AgentId,cancellationToken);
        var checkpointRequest=new GuidedCheckpointRequest(companyId, sessionId, session.AgentId,
            session.ArtifactType, session.SchemaVersion, command.ExpectedVersion, normalizedBody, session.SafeSummary,
            recentConversation, BuildCheckpointFields(session, definition), definition.QuestionPriorities,attachedDocumentContext,
            command.Modality=="voice"?"Public research is handled by the active Realtime tool path for voice turns; do not request research in this checkpoint.":"Public research has not been performed for this turn.",companyReferenceContext);
        var checkpointStarted=System.Diagnostics.Stopwatch.GetTimestamp();
        GuidedCheckpointResult checkpoint;
        GuidedResearchContinuationRequestedMessage? researchContinuation = null;
        try
        {
            checkpoint = await _checkpoint.CreateCheckpointAsync(checkpointRequest, cancellationToken);
            if(command.Modality=="text"&&!string.IsNullOrWhiteSpace(checkpoint.ResearchQuery))
            {
                var query=checkpoint.ResearchQuery.Trim();
                if(definition.Capabilities.SupportsExternalResearch)
                {
                    var idempotencyKey=$"guided-research:{companyId:N}:{sessionId:N}:{command.ClientRequestId:N}:{HashToken(query)[..16]}";
                    researchContinuation=new GuidedResearchContinuationRequestedMessage(companyId,sessionId,session.AgentId,userMessage.Id,command.ClientRequestId,
                        session.ArtifactType,session.SchemaVersion,Bound(query,500),idempotencyKey,session.CorrelationId);
                    checkpoint=checkpoint with
                    {
                        Patches=[], StatusChanges=[], ResearchQuery=null,
                        AgentMessage=string.IsNullOrWhiteSpace(checkpoint.AgentMessage)?"I’ll check the requested public information and we can keep shaping the strategy while I do.":checkpoint.AgentMessage,
                        SafeSummary="Public research is being checked alongside the workshop."
                    };
                }
                else
                {
                    checkpoint=checkpoint with{ResearchQuery=null,AgentMessage="This workshop cannot use public research, but we can continue with the information you provide.",SafeSummary="Public research is not available for this workshop."};
                }
            }
        }
        catch
        {
            GuidedWorkTelemetry.CheckpointFailures.Add(1, new KeyValuePair<string, object?>("artifact.type", session.ArtifactType));
            throw;
        }
        finally
        {
            GuidedWorkTelemetry.CheckpointDuration.Record(System.Diagnostics.Stopwatch.GetElapsedTime(checkpointStarted).TotalMilliseconds,new KeyValuePair<string,object?>("artifact.type",session.ArtifactType));
        }
        checkpoint=NormalizeCheckpoint(definition,checkpoint);
        ValidateCheckpoint(definition, checkpoint);

        var executionStrategy = _db.Database.CreateExecutionStrategy();
        try
        {
            (GuidedWorkTurnResultDto Result, int ChangeCount) persisted = default;
            for (var persistenceAttempt = 1; persistenceAttempt <= 3; persistenceAttempt++)
            {
                try
                {
                    persisted = await executionStrategy.ExecuteAsync(async () =>
                    {
                        // A retry may follow an uncertain commit. Clear tracked state and first
                        // check the durable idempotency record before applying the checkpoint again.
                        _db.ChangeTracker.Clear();
                        if (await ReadOperationAsync<GuidedWorkTurnResultDto>(companyId, sessionId, command.ClientRequestId, "turn", cancellationToken) is { } completed)
                            return (Result: completed, ChangeCount: completed.Changes.Count);

                        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
                        var transactionSession = await LoadSessionAsync(companyId, sessionId, member.UserId, cancellationToken);
                        EnsureMutable(transactionSession);

                        var changes = new List<GuidedFieldChangeDto>();
                        foreach (var statusChange in checkpoint.StatusChanges ?? [])
                        {
                            var field = transactionSession.Fields.Single(x => string.Equals(x.Path, statusChange.Path, StringComparison.OrdinalIgnoreCase));
                            var changedWhileCheckpointRan = !checkpointFieldVersions.TryGetValue(field.Path, out var checkpointVersion)
                                ? field.Version > 1
                                : field.Version != checkpointVersion;
                            if (changedWhileCheckpointRan) continue;
                            var previousStatus = field.Status;
                            var updated = GuidedDraftStatusChangePolicy.Apply(transactionSession, definition, [field.Path], null,
                                statusChange.Status, statusChange.Explanation, "at the user's request");
                            if (updated.Count == 0 || string.Equals(previousStatus, field.Status, StringComparison.OrdinalIgnoreCase)) continue;
                            var value = ParseNode(field.ValueJson);
                            changes.Add(new GuidedFieldChangeDto(field.Path, field.Label, value?.DeepClone(), value?.DeepClone(), field.Status,
                                field.Explanation ?? "Status updated from this conversation."));
                        }
                        foreach (var patch in checkpoint.Patches)
                        {
                            var definitionField = GuidedWorkshopFields.Resolve(definition,patch.Path)!;
                            var normalizedValue = NormalizeProviderValue(definitionField, patch.Value);
                            ValidateValue(definitionField, normalizedValue);
                            var field = FindOrCreateField(transactionSession,definitionField);
                            var changedWhileCheckpointRan = !checkpointFieldVersions.TryGetValue(field.Path, out var checkpointVersion)
                                ? field.Version > 1
                                : field.Version != checkpointVersion;
                            if (changedWhileCheckpointRan && !string.Equals(field.Path, GuidedWorkshopFields.InsightsPath, StringComparison.OrdinalIgnoreCase))
                            {
                                _logger.LogInformation(
                                    "Preserved a newer guided draft field while rebasing a checkpoint. CompanyId: {CompanyId}, SessionId: {SessionId}, Path: {Path}, CheckpointVersion: {CheckpointVersion}, CurrentVersion: {CurrentVersion}.",
                                    companyId, sessionId, field.Path, checkpointFieldVersions.GetValueOrDefault(field.Path), field.Version);
                                continue;
                            }
                            if (changedWhileCheckpointRan)
                                normalizedValue = MergeWorkshopInsights(ParseNode(field.ValueJson), normalizedValue, definitionField.MaxLength ?? 8000);
                            var previous = ParseNode(field.ValueJson);
                            if (JsonNode.DeepEquals(previous, normalizedValue)) continue;
                            field.Set(normalizedValue?.ToJsonString(), patch.Status, patch.SourceType, userMessage.Id,
                                patch.SourceMetadata?.ToDictionary(x => x.Key, x => x.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase), patch.Explanation);
                            changes.Add(new GuidedFieldChangeDto(field.Path, field.Label, previous, normalizedValue?.DeepClone(), field.Status,
                                field.Explanation ?? "Updated from this conversation."));
                        }

                        if (transactionSession.Status == GuidedWorkSessionStatuses.ReviewReady) transactionSession.ReturnToActive();
                        transactionSession.Advance(checkpoint.SafeSummary, checkpoint.NextQuestion,
                            definition.Fields.Count(x => x.IsRequired), CountReady(transactionSession.Fields));
                        var checkpointMessageKind=command.Modality=="voice"?"guided_checkpoint_internal":"guided_checkpoint";
                        var agentMetadata = SessionMessageMetadata(transactionSession.Id, command.ClientRequestId, checkpointMessageKind, command);
                        agentMetadata["safe_summary"] = JsonValue.Create(Bound(checkpoint.SafeSummary, 2000));
                        agentMetadata["confirmations"] = SafeStringArray(checkpoint.Confirmations);
                        agentMetadata["assumptions"] = SafeStringArray(checkpoint.Assumptions);
                        agentMetadata["conflicts"] = SafeStringArray(checkpoint.Conflicts);
                        agentMetadata["missing_fields"] = SafeStringArray(checkpoint.MissingFields);
                        var agentMessage = new Message(Guid.NewGuid(), companyId, transactionSession.ConversationId,
                            ChatSenderTypes.Agent, transactionSession.AgentId, ChatMessageTypes.Text,
                            Bound(checkpoint.AgentMessage, 16000), agentMetadata);
                        _db.Messages.Add(agentMessage);
                        if(researchContinuation is not null)
                        {
                            _outbox.Enqueue(companyId,CompanyOutboxTopics.GuidedResearchContinuationRequested,researchContinuation,
                                transactionSession.CorrelationId,idempotencyKey:researchContinuation.IdempotencyKey);
                            await QueueAuditAsync(member,transactionSession,"guided_session.research_queued","Queued permitted public research while the workshop continues.",cancellationToken);
                        }
                        await _db.SaveChangesAsync(cancellationToken);
                        var dto = await LoadDtoAsync(companyId, transactionSession.Id, member.UserId, cancellationToken);
                        var result = new GuidedWorkTurnResultDto(dto, ToMessageDto(userMessage), ToMessageDto(agentMessage), changes);
                        await QueueAuditAsync(member, transactionSession, "guided_session.turn_recorded",
                            $"Recorded a guided turn with {changes.Count} field change(s).", cancellationToken);
                        await StoreOperationAsync(transactionSession, command.ClientRequestId, "turn", result, cancellationToken);
                        await transaction.CommitAsync(cancellationToken);
                        return (Result: result, ChangeCount: changes.Count);
                    });
                    break;
                }
                catch (DbUpdateConcurrencyException) when (persistenceAttempt < 3)
                {
                    GuidedWorkTelemetry.CheckpointFailures.Add(1, new KeyValuePair<string, object?>("failure.reason", "persistence_concurrency"));
                    _logger.LogWarning(
                        "Retrying guided checkpoint after concurrent draft activity. CompanyId: {CompanyId}, SessionId: {SessionId}, ClientRequestId: {ClientRequestId}, Attempt: {Attempt}.",
                        companyId, sessionId, command.ClientRequestId, persistenceAttempt);
                    _db.ChangeTracker.Clear();
                }
            }

            GuidedWorkTelemetry.TurnsCompleted.Add(1, new KeyValuePair<string, object?>("artifact.type", session.ArtifactType));
            GuidedWorkTelemetry.FieldsChanged.Add(persisted.ChangeCount, new KeyValuePair<string, object?>("artifact.type", session.ArtifactType));
            return persisted.Result!;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to persist guided checkpoint. CompanyId: {CompanyId}, SessionId: {SessionId}, ClientRequestId: {ClientRequestId}, ArtifactType: {ArtifactType}.",
                companyId, sessionId, command.ClientRequestId, session.ArtifactType);
            throw;
        }
    }

    public async Task<GuidedWorkMessageDto> RecordVoiceAgentMessageAsync(Guid companyId,Guid sessionId,RecordGuidedVoiceAgentMessageCommand command,CancellationToken ct)
    {
        var member=await RequireMemberAsync(companyId,ct);
        var responseId=command.ProviderResponseId?.Trim()??string.Empty;
        var body=command.Body?.Trim()??string.Empty;
        if(responseId.Length is < 3 or > 200)throw Validation((nameof(command.ProviderResponseId),"A bounded provider response identifier is required."));
        if(body.Length is < 1 or > 16000)throw Validation((nameof(command.Body),"A bounded finalized agent transcript is required."));
        var session=await _db.GuidedWorkSessions.AsNoTracking().SingleOrDefaultAsync(x=>x.CompanyId==companyId&&x.Id==sessionId&&x.CreatedByUserId==member.UserId,ct)
            ??throw new KeyNotFoundException("Guided work session not found.");
        var hasActiveBinding=await _db.GuidedVoiceBindings.AsNoTracking().AnyAsync(x=>x.CompanyId==companyId&&x.SessionId==sessionId&&x.UserId==member.UserId&&x.EndedUtc==null&&x.ExpiresUtc>DateTime.UtcNow,ct);
        if(!hasActiveBinding)throw new GuidedWorkConflictException("The voice conversation is no longer active.");
        var responseHash=HashProviderIdentity(responseId);
        var recent=await _db.Messages.Where(x=>x.CompanyId==companyId&&x.ConversationId==session.ConversationId&&x.SenderType==ChatSenderTypes.Agent).OrderByDescending(x=>x.CreatedUtc).Take(500).ToListAsync(ct);
        var existing=recent.FirstOrDefault(x=>ReadSessionId(x.StructuredPayload)==sessionId&&ReadMessageKind(x.StructuredPayload)=="guided_realtime_agent"&&ReadString(x.StructuredPayload,"provider_response_hash")==responseHash);
        if(existing is not null)return ToMessageDto(existing);
        var metadata=new Dictionary<string,JsonNode?>(StringComparer.OrdinalIgnoreCase)
        {
            ["guided_session_id"]=JsonValue.Create(sessionId.ToString("N")),
            ["guided_message_kind"]=JsonValue.Create("guided_realtime_agent"),
            ["modality"]=JsonValue.Create("voice"),
            ["provider_response_hash"]=JsonValue.Create(responseHash),
            ["final"]=JsonValue.Create(true)
        };
        var message=new Message(Guid.NewGuid(),companyId,session.ConversationId,ChatSenderTypes.Agent,session.AgentId,ChatMessageTypes.Text,body,metadata);
        _db.Messages.Add(message);await _db.SaveChangesAsync(ct);
        return ToMessageDto(message);
    }

    public async Task<GuidedWorkSessionDto> CorrectFieldAsync(Guid companyId, Guid sessionId, string path, CorrectGuidedDraftFieldCommand command, CancellationToken cancellationToken)
    {
        EnsureFeatureEnabled();
        var member = await RequireMemberAsync(companyId, cancellationToken);
        if (command.ClientRequestId == Guid.Empty) throw Validation((nameof(command.ClientRequestId), "ClientRequestId is required."));
        if(!string.Equals(command.Status,GuidedDraftFieldStatuses.Unknown,StringComparison.OrdinalIgnoreCase)&&!string.Equals(command.Status,GuidedDraftFieldStatuses.Confirmed,StringComparison.OrdinalIgnoreCase))
            throw Validation((nameof(command.Status),"A direct correction must be confirmed or marked unknown."));
        return await ExecuteIdempotentTransactionAsync<GuidedWorkSessionDto>(companyId,sessionId,command.ClientRequestId,"correct",async()=>
        {
            var session=await LoadSessionAsync(companyId,sessionId,member.UserId,cancellationToken);EnsureVersion(session,command.ExpectedVersion);EnsureMutable(session);
            var definition=ResolveDefinition(session.ArtifactType);
            var fieldDefinition=GuidedWorkshopFields.Resolve(definition,path)
                ??throw Validation((nameof(path),"This draft does not contain that field. Relevant off-schema information can be kept in Workshop insights."));
            ValidateValue(fieldDefinition,command.Value);
            var status=string.Equals(command.Status,GuidedDraftFieldStatuses.Unknown,StringComparison.OrdinalIgnoreCase)?GuidedDraftFieldStatuses.Unknown:GuidedDraftFieldStatuses.Confirmed;
            var field=FindOrCreateField(session,fieldDefinition);
            field.Set(command.Value?.ToJsonString(),status,"user",null,null,"Confirmed directly by the user.");
            if(session.Status==GuidedWorkSessionStatuses.ReviewReady)session.ReturnToActive();
            session.Advance("The draft was corrected directly by the user.",session.NextQuestion,definition.Fields.Count(x=>x.IsRequired),CountReady(session.Fields));
            await _db.SaveChangesAsync(cancellationToken);
            var dto=await LoadDtoAsync(companyId,sessionId,member.UserId,cancellationToken);
            await QueueAuditAsync(member,session,"guided_session.field_corrected",$"Corrected draft field {field.Label}.",cancellationToken);
            await StoreOperationAsync(session,command.ClientRequestId,"correct",dto,cancellationToken);
            return dto;
        },cancellationToken);
    }

    public async Task<GuidedWorkSessionDto> ChangeFieldStatusesAsync(Guid companyId, Guid sessionId, ChangeGuidedDraftFieldStatusesCommand command, CancellationToken cancellationToken)
    {
        EnsureFeatureEnabled();
        var member = await RequireMemberAsync(companyId, cancellationToken);
        if (command.ClientRequestId == Guid.Empty) throw Validation((nameof(command.ClientRequestId), "ClientRequestId is required."));
        return await ExecuteIdempotentTransactionAsync<GuidedWorkSessionDto>(companyId,sessionId,command.ClientRequestId,"change_status",async() =>
        {
            var session=await LoadSessionAsync(companyId,sessionId,member.UserId,cancellationToken);
            EnsureVersion(session,command.ExpectedVersion);EnsureMutable(session);
            var definition=ResolveDefinition(session.ArtifactType);
            var changed=GuidedDraftStatusChangePolicy.Apply(session,definition,command.Paths??[],command.FromStatus,command.Status,
                command.Explanation,"by the user");
            if(changed.Count>0)
            {
                if(session.Status==GuidedWorkSessionStatuses.ReviewReady)session.ReturnToActive();
                session.Advance($"Updated the review status of {changed.Count} draft field(s).",session.NextQuestion,
                    definition.Fields.Count(x=>x.IsRequired),CountReady(session.Fields));
            }
            await _db.SaveChangesAsync(cancellationToken);
            var dto=await LoadDtoAsync(companyId,sessionId,member.UserId,cancellationToken);
            await QueueAuditAsync(member,session,"guided_session.field_status_changed",
                $"Changed {changed.Count} draft field status value(s) to {command.Status}.",cancellationToken);
            await StoreOperationAsync(session,command.ClientRequestId,"change_status",dto,cancellationToken);
            return dto;
        },cancellationToken);
    }

    public async Task<GuidedWorkReviewDto> PrepareReviewAsync(Guid companyId, Guid sessionId, PrepareGuidedWorkReviewCommand command, CancellationToken cancellationToken)
    {
        EnsureFeatureEnabled();
        var member = await RequireMemberAsync(companyId, cancellationToken);
        var result=await ExecuteIdempotentTransactionAsync<GuidedWorkReviewDto>(companyId,sessionId,command.ClientRequestId,"review",async()=>
        {
            var session=await LoadSessionAsync(companyId,sessionId,member.UserId,cancellationToken);EnsureVersion(session,command.ExpectedVersion);EnsureMutable(session);
            var definition=ResolveDefinition(session.ArtifactType);var values=ToValues(session.Fields);
            var domainGaps=await definition.ValidateAsync(companyId,session.AgentId,session.TargetArtifactId,values,cancellationToken);
            var missing=session.Fields.Where(x=>x.IsRequired&&!IsReady(x)).Select(x=>x.Label).Concat(domainGaps).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var conflicts=session.Fields.Where(x=>x.Status==GuidedDraftFieldStatuses.Conflicting).Select(x=>x.Label).ToArray();
            if(missing.Length>0||conflicts.Length>0)throw new GuidedWorkValidationException(new Dictionary<string,string[]>{{"review",[..missing.Select(x=>$"Complete {x}.").Concat(conflicts.Select(x=>$"Resolve {x}."))]}});
            var token=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));var expires=DateTime.UtcNow.AddMinutes(Math.Clamp(_options.ReviewTokenMinutes,5,60));
            session.PrepareReview(HashToken(token),expires,definition.Fields.Count(x=>x.IsRequired),CountReady(session.Fields),"Review the proposed changes before confirming.");
            await _db.SaveChangesAsync(cancellationToken);
            var dto=await LoadDtoAsync(companyId,sessionId,member.UserId,cancellationToken);
            var changes=session.Fields.Where(x=>!GuidedWorkshopFields.IsInsights(x.Path)&&x.ValueJson is not null&&HasChangedFromOriginal(x)).Select(x=>new GuidedFieldChangeDto(x.Path,x.Label,OriginalValue(x),ParseNode(x.ValueJson),x.Status,x.Explanation??"Included in review.")).ToArray();
            var insights=await definition.BuildReviewInsightsAsync(companyId,session.AgentId,session.TargetArtifactId,values,cancellationToken);
            var review=new GuidedWorkReviewDto(dto,token,expires,missing,conflicts,changes,insights);
            await QueueAuditAsync(member,session,"guided_session.review_prepared","Prepared an expiring review checkpoint.",cancellationToken);
            await StoreOperationAsync(session,command.ClientRequestId,"review",review,cancellationToken);
            return review;
        },cancellationToken);
        GuidedWorkTelemetry.ReviewsPrepared.Add(1,new KeyValuePair<string,object?>("artifact.type",result.Session.ArtifactType));
        return result;
    }

    public async Task<GuidedWorkCommitResultDto> ConfirmCommitAsync(Guid companyId, Guid sessionId, ConfirmGuidedWorkCommitCommand command, CancellationToken cancellationToken)
    {
        EnsureFeatureEnabled();
        var member = await RequireMemberAsync(companyId, cancellationToken);
        GuidedWorkCommitResultDto result;
        try
        {
            result=await ExecuteIdempotentTransactionAsync<GuidedWorkCommitResultDto>(companyId,sessionId,command.ClientRequestId,"commit",async()=>
            {
                var session=await LoadSessionAsync(companyId,sessionId,member.UserId,cancellationToken);EnsureVersion(session,command.ExpectedVersion);
                if(session.Status!=GuidedWorkSessionStatuses.ReviewReady||session.ReviewTokenExpiresUtc<=DateTime.UtcNow||string.IsNullOrWhiteSpace(command.ReviewToken)||!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(session.ReviewTokenHash!),Convert.FromHexString(HashToken(command.ReviewToken))))throw new GuidedWorkConflictException("The review confirmation is missing, expired, or stale. Prepare review again.");
                var definition=ResolveDefinition(session.ArtifactType);
                var context=new GuidedArtifactCommitContext(companyId,session.Id,session.AgentId,member.UserId,session.TargetArtifactId,session.TargetArtifactVersion,ToValues(session.Fields),session.CorrelationId??_correlation.CorrelationId??session.Id.ToString("N"));
                var committed=await definition.CommitAsync(context,cancellationToken);
                session.Complete(committed.ArtifactVersion);await _db.SaveChangesAsync(cancellationToken);
                var dto=await LoadDtoAsync(companyId,sessionId,member.UserId,cancellationToken);
                var committedResult=new GuidedWorkCommitResultDto(dto,session.ArtifactType,committed.ArtifactId,committed.ArtifactVersion,committed.Summary);
                await QueueAuditAsync(member,session,"guided_session.committed",committed.Summary,cancellationToken);
                await StoreOperationAsync(session,command.ClientRequestId,"commit",committedResult,cancellationToken);
                return committedResult;
            },cancellationToken);
        }
        catch(GuidedWorkConflictException){GuidedWorkTelemetry.CommitConflicts.Add(1);throw;}
        catch{GuidedWorkTelemetry.CommitFailures.Add(1);throw;}
        GuidedWorkTelemetry.ArtifactsCommitted.Add(1,new KeyValuePair<string,object?>("artifact.type",result.ArtifactType));
        return result;
    }

    public async Task<GuidedWorkSessionDto> CancelAsync(Guid companyId, Guid sessionId, CancelGuidedWorkSessionCommand command, CancellationToken cancellationToken)
    {
        var member = await RequireMemberAsync(companyId, cancellationToken);
        return await ExecuteIdempotentTransactionAsync<GuidedWorkSessionDto>(companyId,sessionId,command.ClientRequestId,"cancel",async()=>
        {
            var session=await LoadSessionAsync(companyId,sessionId,member.UserId,cancellationToken);EnsureVersion(session,command.ExpectedVersion);EnsureMutable(session);
            session.Cancel();await _db.SaveChangesAsync(cancellationToken);
            var dto=await LoadDtoAsync(companyId,sessionId,member.UserId,cancellationToken);
            await QueueAuditAsync(member,session,"guided_session.cancelled","Cancelled the guided work session without changing the target artifact.",cancellationToken);
            await StoreOperationAsync(session,command.ClientRequestId,"cancel",dto,cancellationToken);
            return dto;
        },cancellationToken);
    }

    public async Task ProcessAsync(GuidedResearchContinuationRequestedMessage request, CancellationToken cancellationToken)
    {
        if(await ReadOperationAsync<GuidedWorkMessageDto>(request.CompanyId,request.SessionId,request.ClientRequestId,"research_followup",cancellationToken) is not null)return;
        var session=await _db.GuidedWorkSessions.Include(x=>x.Fields).SingleOrDefaultAsync(x=>x.CompanyId==request.CompanyId&&x.Id==request.SessionId,cancellationToken);
        if(session is null||session.Status is GuidedWorkSessionStatuses.Completed or GuidedWorkSessionStatuses.Cancelled)return;
        if(session.AgentId!=request.AgentId||!string.Equals(session.ArtifactType,request.ArtifactType,StringComparison.OrdinalIgnoreCase))throw new CompanyOutboxPermanentException("Guided research continuation no longer matches the workshop.");
        var user=await _db.Messages.SingleOrDefaultAsync(x=>x.CompanyId==request.CompanyId&&x.ConversationId==session.ConversationId&&x.Id==request.UserMessageId&&x.SenderType==ChatSenderTypes.User,cancellationToken);
        if(user is null)throw new CompanyOutboxPermanentException("Guided research source message was not found.");
        var definition=ResolveDefinition(session.ArtifactType);
        var started=System.Diagnostics.Stopwatch.GetTimestamp();
        var research=await _evidenceResearch.ResearchAsync(request.CompanyId,session.AgentId,request.Query,cancellationToken);
        GuidedWorkTelemetry.ResearchDuration.Record(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds,new KeyValuePair<string,object?>("artifact.type",session.ArtifactType));
        var recent=await _db.Messages.AsNoTracking().Where(x=>x.CompanyId==request.CompanyId&&x.ConversationId==session.ConversationId&&x.Id!=user.Id).OrderByDescending(x=>x.CreatedUtc).Take(20).ToListAsync(cancellationToken);
        var companyReferenceContext=await definition.BuildCompanyReferenceContextAsync(request.CompanyId,session.AgentId,cancellationToken);
        var checkpointRequest=new GuidedCheckpointRequest(request.CompanyId,session.Id,session.AgentId,session.ArtifactType,session.SchemaVersion,session.Version,user.Body,session.SafeSummary,BuildRecentConversation(recent),BuildCheckpointFields(session,definition),definition.QuestionPriorities,"No workshop documents are attached for this research continuation.",BuildPublicResearchContext(request.Query,research),companyReferenceContext);
        var checkpoint=NormalizeCheckpoint(definition,await _checkpoint.CreateCheckpointAsync(checkpointRequest,cancellationToken));ValidateCheckpoint(definition,checkpoint);
        await using var transaction=await _db.Database.BeginTransactionAsync(cancellationToken);
        foreach(var patch in checkpoint.Patches)
        {
            var schema=GuidedWorkshopFields.Resolve(definition,patch.Path)!;var field=FindOrCreateField(session,schema);
            if(field.UpdatedUtc>user.CreatedUtc&&field.SourceMessageId!=user.Id&&!GuidedWorkshopFields.IsInsights(field.Path))continue;
            var value=NormalizeProviderValue(schema,patch.Value);ValidateValue(schema,value);if(JsonNode.DeepEquals(ParseNode(field.ValueJson),value))continue;
            field.Set(value?.ToJsonString(),GuidedDraftFieldStatuses.Proposed,"observation",user.Id,patch.SourceMetadata?.ToDictionary(x=>x.Key,x=>x.Value?.DeepClone(),StringComparer.OrdinalIgnoreCase),patch.Explanation);
        }
        session.Advance(checkpoint.SafeSummary,checkpoint.NextQuestion,definition.Fields.Count(x=>x.IsRequired),CountReady(session.Fields));
        var metadata=SessionMessageMetadata(session.Id,request.ClientRequestId,"guided_research_followup");metadata["research_available"]=JsonValue.Create(research.Available);metadata["source_count"]=JsonValue.Create(research.Sources.Count);
        var agent=new Message(Guid.NewGuid(),request.CompanyId,session.ConversationId,ChatSenderTypes.Agent,session.AgentId,ChatMessageTypes.Text,Bound(checkpoint.AgentMessage,16000),metadata);_db.Messages.Add(agent);
        await _audit.WriteAsync(new AuditEventWriteRequest(request.CompanyId,"system",null,"guided_session.text_research","guided_work_session",session.Id.ToString("N"),research.Available?"succeeded":"unavailable",research.Available?"Completed permitted public research for a text workshop turn.":"Public research was unavailable for a text workshop turn.",Metadata:new Dictionary<string,string?>{{"artifactType",session.ArtifactType},{"sourceCount",research.Sources.Count.ToString()},{"failureCode",research.FailureCode},{"committed","false"}},CorrelationId:request.CorrelationId),cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);await StoreOperationAsync(session,request.ClientRequestId,"research_followup",ToMessageDto(agent),cancellationToken);await transaction.CommitAsync(cancellationToken);GuidedWorkTelemetry.ResearchCompleted.Add(1,new KeyValuePair<string,object?>("artifact.type",session.ArtifactType));
    }

    private async Task<GuidedWorkSession> LoadSessionAsync(Guid companyId, Guid sessionId, Guid userId, CancellationToken ct) =>
        await _db.GuidedWorkSessions.Include(x => x.Fields).SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == sessionId && x.CreatedByUserId == userId, ct)
        ?? throw new KeyNotFoundException("Guided work session not found.");

    private async Task<GuidedWorkSessionDto> LoadDtoAsync(Guid companyId, Guid sessionId, Guid userId, CancellationToken ct)
    {
        var session = await _db.GuidedWorkSessions.AsNoTracking().Include(x => x.Agent).Include(x => x.Fields)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == sessionId && x.CreatedByUserId == userId, ct)
            ?? throw new KeyNotFoundException("Guided work session not found.");
        var definition = ResolveDefinition(session.ArtifactType);
        var messages = await _db.Messages.AsNoTracking().Where(x => x.CompanyId == companyId && x.ConversationId == session.ConversationId)
            .OrderByDescending(x => x.CreatedUtc).Take(500).OrderBy(x => x.CreatedUtc).ToListAsync(ct);
        return new GuidedWorkSessionDto(session.Id, session.CompanyId, session.ConversationId, session.AgentId, session.Agent.DisplayName,
            session.Agent.RoleName, session.ArtifactType, definition.DisplayName, session.SchemaVersion, session.TargetArtifactId,
            session.TargetArtifactVersion, session.Status, session.Sequence, session.Version, session.RequiredFieldCount, session.ReadyFieldCount,
            session.SafeSummary, session.NextQuestion, definition.Capabilities, BuildFieldDtos(session,definition),
            messages.Where(x => ReadSessionId(x.StructuredPayload) == session.Id&&ReadMessageKind(x.StructuredPayload)!="guided_checkpoint_internal").Select(ToMessageDto).ToArray(), session.CreatedUtc, session.UpdatedUtc,
            session.CompletedUtc, session.CancelledUtc);
    }

    private static GuidedDraftFieldDto ToFieldDto(GuidedDraftField field, IGuidedArtifactDefinition definition)
    {
        var schema = GuidedWorkshopFields.Resolve(definition,field.Path)??throw new InvalidOperationException("The stored workshop field is no longer supported.");
        return new(field.Id, field.Path, field.Label, schema.Description, field.ValueType, field.IsRequired, ParseNode(field.ValueJson), field.Status,
            field.SourceType, field.SourceMessageId, field.SourceMetadata.ToDictionary(x => x.Key, x => x.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase),
            field.Explanation, schema.AllowedValues ?? [], field.Version, field.UpdatedUtc);
    }
    private static GuidedCheckpointField ToCheckpointField(GuidedDraftField field, IGuidedArtifactDefinition definition)
    {
        var schema = GuidedWorkshopFields.Resolve(definition,field.Path)??throw new InvalidOperationException("The stored workshop field is no longer supported.");
        return new(field.Path, field.Label, field.ValueType, field.IsRequired, ParseNode(field.ValueJson), field.Status, schema.AllowedValues ?? [],schema.Description,schema.MaxLength,schema.Minimum,schema.Maximum,schema.AllowsEvidence);
    }
    private static GuidedDraftField FindOrCreateField(GuidedWorkSession session,GuidedFieldDefinition schema)
    {
        var existing=session.Fields.SingleOrDefault(x=>string.Equals(x.Path,schema.Path,StringComparison.OrdinalIgnoreCase));if(existing is not null)return existing;
        if(!GuidedWorkshopFields.IsInsights(schema.Path))throw new InvalidOperationException("The requested draft field is missing.");
        var field=new GuidedDraftField(Guid.NewGuid(),session.CompanyId,session.Id,schema.Path,schema.Label,schema.ValueType,schema.IsRequired);session.Fields.Add(field);return field;
    }
    private static GuidedCheckpointField[] BuildCheckpointFields(GuidedWorkSession session,IGuidedArtifactDefinition definition)
    {
        var fields=definition.Fields.Select(schema=>ToCheckpointField(session.Fields.Single(x=>string.Equals(x.Path,schema.Path,StringComparison.OrdinalIgnoreCase)),definition)).ToList();
        var insight=session.Fields.SingleOrDefault(x=>GuidedWorkshopFields.IsInsights(x.Path));fields.Add(insight is null
            ?new GuidedCheckpointField(GuidedWorkshopFields.Insights.Path,GuidedWorkshopFields.Insights.Label,GuidedWorkshopFields.Insights.ValueType,false,null,GuidedDraftFieldStatuses.Missing,[],GuidedWorkshopFields.Insights.Description,GuidedWorkshopFields.Insights.MaxLength,GuidedWorkshopFields.Insights.Minimum,GuidedWorkshopFields.Insights.Maximum,GuidedWorkshopFields.Insights.AllowsEvidence)
            :ToCheckpointField(insight,definition));return [..fields];
    }
    private static GuidedDraftFieldDto[] BuildFieldDtos(GuidedWorkSession session,IGuidedArtifactDefinition definition)
    {
        var fields=definition.Fields.Select(schema=>session.Fields.SingleOrDefault(x=>string.Equals(x.Path,schema.Path,StringComparison.OrdinalIgnoreCase)) is { } stored
            ?ToFieldDto(stored,definition)
            :new GuidedDraftFieldDto(Guid.Empty,schema.Path,schema.Label,schema.Description,schema.ValueType,schema.IsRequired,null,GuidedDraftFieldStatuses.Missing,"none",null,new Dictionary<string,JsonNode?>(),null,schema.AllowedValues??[],0,session.UpdatedUtc)).ToList();
        var insight=session.Fields.SingleOrDefault(x=>GuidedWorkshopFields.IsInsights(x.Path));fields.Add(insight is null
            ?new GuidedDraftFieldDto(Guid.Empty,GuidedWorkshopFields.Insights.Path,GuidedWorkshopFields.Insights.Label,GuidedWorkshopFields.Insights.Description,GuidedWorkshopFields.Insights.ValueType,false,null,GuidedDraftFieldStatuses.Missing,"none",null,new Dictionary<string,JsonNode?>(),null,[],0,session.UpdatedUtc)
            :ToFieldDto(insight,definition));return [..fields];
    }
    private static GuidedWorkMessageDto ToMessageDto(Message message) => new(message.Id, message.SenderType, message.SenderId, message.Body, message.CreatedUtc);
    private static Dictionary<string, JsonNode?> SessionMessageMetadata(Guid sessionId, Guid requestId, string kind, AddGuidedWorkTurnCommand? turn = null)
    {
        var metadata = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
        {
            ["guided_session_id"] = JsonValue.Create(sessionId.ToString("N")),
            ["client_request_id"] = JsonValue.Create(requestId.ToString("N")),
            ["guided_message_kind"] = JsonValue.Create(kind),
            ["modality"] = JsonValue.Create(turn?.Modality ?? "text")
        };
        if (turn?.Modality == "voice")
        {
            metadata["provider_event_hash"] = JsonValue.Create(HashProviderIdentity(turn.ProviderEventId));
            metadata["interrupted"] = JsonValue.Create(turn.Interrupted);
            metadata["final"] = JsonValue.Create(true);
            metadata["duration_ms"] = JsonValue.Create(Math.Clamp(turn.DurationMs ?? 0, 0, 3_600_000));
            metadata["transport_version"] = JsonValue.Create(Bound(turn.TransportVersion ?? "realtime-webrtc-v1", 64));
        }
        return metadata;
    }
    private static string HashProviderIdentity(string? value) => string.IsNullOrWhiteSpace(value)
        ? "unavailable"
        : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim()))).ToLowerInvariant();
    private static JsonArray SafeStringArray(IEnumerable<string> values)=>new(values.Where(x=>!string.IsNullOrWhiteSpace(x)).Take(30).Select(x=>(JsonNode?)JsonValue.Create(Bound(x,500))).ToArray());
    private static Guid? ReadSessionId(IReadOnlyDictionary<string, JsonNode?> metadata) => metadata.TryGetValue("guided_session_id", out var value) && Guid.TryParse(value?.GetValue<string>(), out var id) ? id : null;
    private static Guid? ReadClientRequestId(IReadOnlyDictionary<string, JsonNode?> metadata) => metadata.TryGetValue("client_request_id", out var value) && Guid.TryParse(value?.GetValue<string>(), out var id) ? id : null;
    private static string? ReadMessageKind(IReadOnlyDictionary<string, JsonNode?> metadata) => metadata.TryGetValue("guided_message_kind", out var value) && value is JsonValue scalar && scalar.TryGetValue<string>(out var kind) ? kind : null;
    private static string? ReadString(IReadOnlyDictionary<string,JsonNode?> metadata,string key)=>metadata.TryGetValue(key,out var value)&&value is JsonValue scalar&&scalar.TryGetValue<string>(out var text)?text:null;
    private IGuidedArtifactDefinition ResolveDefinition(string artifactType) => _definitions.TryGetValue(artifactType.Trim(), out var definition)
        ? definition : throw Validation((nameof(artifactType), "The guided artifact type is not available."));
    private async Task<VirtualCompany.Application.Auth.ResolvedCompanyMembershipContext> RequireMemberAsync(Guid companyId, CancellationToken ct) =>
        await _memberships.ResolveAsync(companyId, ct) ?? throw new UnauthorizedAccessException("The current user does not have access to this company.");
    private static void EnsureVersion(GuidedWorkSession session, int expected) { if (session.Version != expected) throw new GuidedWorkConflictException("The session changed. Refresh it and try again."); }
    private static void EnsureMutable(GuidedWorkSession session) { if (session.Status is GuidedWorkSessionStatuses.Completed or GuidedWorkSessionStatuses.Cancelled) throw new GuidedWorkConflictException("The session is no longer editable."); }
    private void EnsureFeatureEnabled()
    {
        if (!_options.Enabled)
            throw new GuidedCheckpointUnavailableException("Guided workshops are currently disabled. Existing work is preserved and can be viewed or cancelled.");
    }
    private static int CountReady(IEnumerable<GuidedDraftField> fields) => fields.Count(x=>x.IsRequired&&IsReady(x));
    private static bool IsReady(GuidedDraftField field) => field.ValueJson is not null && field.Status == GuidedDraftFieldStatuses.Confirmed;
    private static Dictionary<string, JsonNode?> ToValues(IEnumerable<GuidedDraftField> fields) => fields.ToDictionary(x => x.Path, x => ParseNode(x.ValueJson), StringComparer.OrdinalIgnoreCase);
    private static JsonNode? ParseNode(string? json) => string.IsNullOrWhiteSpace(json) ? null : JsonNode.Parse(json);
    private static JsonNode? OriginalValue(GuidedDraftField field)=>field.SourceMetadata.TryGetValue("original_value_json",out var original)&&original is JsonValue json&&json.TryGetValue<string>(out var text)?ParseNode(text):null;
    private static bool HasChangedFromOriginal(GuidedDraftField field)=>!field.SourceMetadata.ContainsKey("original_value_json")||!JsonNode.DeepEquals(OriginalValue(field),ParseNode(field.ValueJson));
    private static IReadOnlyList<GuidedCheckpointConversationTurn> BuildRecentConversation(IEnumerable<Message> messages)
    {
        const int maxTotalCharacters = 12000;
        const int maxMessageCharacters = 2000;
        var remaining = maxTotalCharacters;
        var selected = new List<GuidedCheckpointConversationTurn>();
        foreach (var message in messages.OrderByDescending(x => x.CreatedUtc).ThenByDescending(x => x.Id))
        {
            if (remaining <= 0) break;
            if (string.Equals(ReadMessageKind(message.StructuredPayload), "guided_checkpoint_internal", StringComparison.Ordinal)) continue;
            var body = string.IsNullOrWhiteSpace(message.Body) ? string.Empty : message.Body.Trim();
            if (body.Length == 0) continue;
            var bounded = body[..Math.Min(body.Length, Math.Min(maxMessageCharacters, remaining))];
            selected.Add(new GuidedCheckpointConversationTurn(message.SenderType, bounded));
            remaining -= bounded.Length;
        }
        selected.Reverse();
        return selected;
    }
    private static string Bound(string value, int max) { var text = string.IsNullOrWhiteSpace(value) ? "I recorded the information provided." : value.Trim(); return text[..Math.Min(text.Length, max)]; }
    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static void ValidateCheckpoint(IGuidedArtifactDefinition definition, GuidedCheckpointResult checkpoint)
    {
        if (string.IsNullOrWhiteSpace(checkpoint.AgentMessage) || string.IsNullOrWhiteSpace(checkpoint.SafeSummary) || checkpoint.Patches.Count > 30)
            throw new GuidedCheckpointUnavailableException("The checkpoint was incomplete or exceeded its safe bounds.");
        foreach (var patch in checkpoint.Patches)
        {
            var field=GuidedWorkshopFields.Resolve(definition,patch.Path);
            if (!SupportedStatuses.Contains(patch.Status) || string.IsNullOrWhiteSpace(patch.Explanation) || field is null ||
                string.Equals(patch.SourceType,"evidence",StringComparison.OrdinalIgnoreCase) ||
                string.Equals(patch.SourceType,"assumption",StringComparison.OrdinalIgnoreCase)&&string.Equals(patch.Status,GuidedDraftFieldStatuses.Confirmed,StringComparison.OrdinalIgnoreCase))
                throw new GuidedCheckpointUnavailableException("The checkpoint attempted an unsupported draft change.");
        }
        foreach(var change in checkpoint.StatusChanges??[])
        {
            if(GuidedWorkshopFields.Resolve(definition,change.Path) is null||
               !GuidedDraftStatusChangePolicy.SettableStatuses.Contains(change.Status)||
               string.IsNullOrWhiteSpace(change.Explanation))
                throw new GuidedCheckpointUnavailableException("The checkpoint attempted an unsupported draft status change.");
        }
        if(checkpoint.Patches.GroupBy(x=>x.Path,StringComparer.OrdinalIgnoreCase).Any(group=>group.Count()>1))
            throw new GuidedCheckpointUnavailableException("The checkpoint attempted multiple value changes for the same draft field.");
        if((checkpoint.StatusChanges??[]).GroupBy(x=>x.Path,StringComparer.OrdinalIgnoreCase).Any(group=>group.Count()>1))
            throw new GuidedCheckpointUnavailableException("The checkpoint attempted multiple review-status changes for the same draft field.");
        if(checkpoint.Patches.Select(x=>x.Path).Intersect((checkpoint.StatusChanges??[]).Select(x=>x.Path),StringComparer.OrdinalIgnoreCase).Any())
            throw new GuidedCheckpointUnavailableException("The checkpoint attempted two changes to the same draft field.");
        if(checkpoint.Conflicts.Count>0&&
           !checkpoint.Patches.Any(x=>x.Status==GuidedDraftFieldStatuses.Conflicting)&&
           !(checkpoint.StatusChanges??[]).Any(x=>x.Status==GuidedDraftFieldStatuses.Conflicting))
            throw new GuidedCheckpointUnavailableException("The checkpoint reported a conflict without marking the affected field.");
    }
    internal static GuidedCheckpointResult NormalizeCheckpoint(IGuidedArtifactDefinition definition,GuidedCheckpointResult checkpoint)
    {
        var normalized=new List<GuidedPatchOperation>();
        var statusChanges=(checkpoint.StatusChanges??[])
            .Select(change=>change with{Path=CanonicalFieldPath(definition,change.Path)})
            .ToArray();
        foreach(var patch in checkpoint.Patches)
        {
            var field=GuidedWorkshopFields.Resolve(definition,patch.Path);
            var path=field is null?GuidedWorkshopFields.InsightsPath:field.Path;
            var value=field is null?FormatUnmappedInsight(patch):patch.Value?.DeepClone();
            var sourceType=patch.SourceType?.Trim().ToLowerInvariant() switch
            {
                "user"=>"user",
                "assumption"=>"assumption",
                "projection"=>"projection",
                "observation" or "evidence"=>"observation",
                _=>"observation"
            };
            var status=SupportedStatuses.Contains(patch.Status)?patch.Status:GuidedDraftFieldStatuses.Proposed;
            if(!string.Equals(sourceType,"user",StringComparison.OrdinalIgnoreCase)&&string.Equals(status,GuidedDraftFieldStatuses.Confirmed,StringComparison.OrdinalIgnoreCase))
                status=GuidedDraftFieldStatuses.Proposed;
            var explanation=string.IsNullOrWhiteSpace(patch.Explanation)
                ?field is null?"Relevant off-schema information retained for deliberate mapping.":"Updated from this workshop conversation."
                :patch.Explanation.Trim();
            var matchingStatusChanges=statusChanges.Where(change=>string.Equals(change.Path,path,StringComparison.OrdinalIgnoreCase)).ToArray();
            if(matchingStatusChanges.Length>1)
                throw new GuidedCheckpointUnavailableException("The checkpoint attempted multiple review-status changes for the same draft field.");
            if(matchingStatusChanges.Length==1)
            {
                status=matchingStatusChanges[0].Status;
                explanation=CombineExplanations(explanation,matchingStatusChanges[0].Explanation);
            }
            normalized.Add(new(path,value,status,sourceType,explanation,patch.SourceMetadata));
        }
        var patchedPaths=normalized.Select(x=>x.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return checkpoint with
        {
            Patches=normalized,
            StatusChanges=statusChanges.Where(change=>!patchedPaths.Contains(change.Path)).ToArray()
        };
    }
    private static string CanonicalFieldPath(IGuidedArtifactDefinition definition,string path) =>
        GuidedWorkshopFields.Resolve(definition,path)?.Path??path.Trim();
    private static string CombineExplanations(string patchExplanation,string statusExplanation)
    {
        var status=statusExplanation.Trim();
        if(string.IsNullOrWhiteSpace(status)||patchExplanation.Contains(status,StringComparison.OrdinalIgnoreCase))return patchExplanation;
        return $"{patchExplanation} Review status: {status}"[..Math.Min(patchExplanation.Length+16+status.Length,1000)];
    }
    internal static JsonNode? MergeWorkshopInsights(JsonNode? current,JsonNode? incoming,int maxLength)
    {
        if(current is not JsonValue currentScalar||!currentScalar.TryGetValue<string>(out var currentText)||string.IsNullOrWhiteSpace(currentText))return incoming?.DeepClone();
        if(incoming is not JsonValue incomingScalar||!incomingScalar.TryGetValue<string>(out var incomingText)||string.IsNullOrWhiteSpace(incomingText))return current.DeepClone();
        currentText=currentText.Trim();incomingText=incomingText.Trim();
        if(currentText.Contains(incomingText,StringComparison.OrdinalIgnoreCase))return JsonValue.Create(currentText);
        if(incomingText.Contains(currentText,StringComparison.OrdinalIgnoreCase))return JsonValue.Create(incomingText[..Math.Min(incomingText.Length,maxLength)]);
        var merged=$"{currentText}\n\n{incomingText}";
        return JsonValue.Create(merged[..Math.Min(merged.Length,maxLength)]);
    }
    private static JsonNode FormatUnmappedInsight(GuidedPatchOperation patch)
    {
        var value=patch.Value is JsonValue scalar&&scalar.TryGetValue<string>(out var text)?text:patch.Value?.ToJsonString()??"No value supplied.";
        return JsonValue.Create($"Unmapped workshop insight\n\nWhat was learned\n{Bound(value,6000)}\n\nWhy it is retained\n{Bound(patch.Explanation,1000)}\n\nSuggested destination\nReview the closest existing field or a related business artifact. Original suggested field: {Bound(patch.Path,160)}.")!;
    }
    private static string BuildPublicResearchContext(string query,GuidedEvidenceResearchResult result)=>JsonSerializer.Serialize(new
    {
        performed=true,
        query=Bound(query,500),
        available=result.Available,
        summary=Bound(result.Summary,8000),
        sources=result.Sources.Take(12).Select(x=>new{title=Bound(x.Title,300),url=Bound(x.Url,2048)}),
        failure_code=string.IsNullOrWhiteSpace(result.FailureCode)?null:result.FailureCode.Trim()[..Math.Min(result.FailureCode.Trim().Length,100)],
        evidence_status=result.Available?"public_web_research":"unavailable",
        fallback_policy=result.Available?"cite_supplied_sources":"do_not_substitute_model_knowledge",
        committed=false
    },JsonOptions);
    private static void ValidateValue(GuidedFieldDefinition definition, JsonNode? value)
    {
        if (value is null) return;
        try
        {
            switch (definition.ValueType)
            {
                case GuidedFieldValueTypes.Text: var text = value.GetValue<string>(); if (definition.MaxLength is int max && text.Length > max) throw new JsonException(); break;
                case GuidedFieldValueTypes.Number: var number = value.GetValue<decimal>(); if (definition.Minimum is decimal min && number < min || definition.Maximum is decimal maxNumber && number > maxNumber) throw new JsonException(); break;
                case GuidedFieldValueTypes.Boolean: _ = value.GetValue<bool>(); break;
                case GuidedFieldValueTypes.Date: if (!DateOnly.TryParse(value.GetValue<string>(), out _)) throw new JsonException(); break;
                case GuidedFieldValueTypes.Identifier: if (!Guid.TryParse(value.GetValue<string>(), out _)) throw new JsonException(); break;
                case GuidedFieldValueTypes.TextList: if (value is not JsonArray array || array.Count > 100 || array.Any(x => x is null || !x.AsValue().TryGetValue<string>(out _))) throw new JsonException(); break;
                case GuidedFieldValueTypes.Object: if (value is not JsonObject) throw new JsonException(); break;
                default: throw new JsonException();
            }
            if (definition.AllowedValues is { Count: > 0 } && !definition.AllowedValues.Contains(value.GetValue<string>(), StringComparer.OrdinalIgnoreCase)) throw new JsonException();
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or JsonException)
        { throw Validation((definition.Path, $"{definition.Label} has an invalid value.")); }
    }
    private static JsonNode? NormalizeProviderValue(GuidedFieldDefinition definition,JsonNode? value)
    {
        if(value is JsonValue scalar&&scalar.TryGetValue<string>(out var text)&&definition.ValueType is GuidedFieldValueTypes.TextList or GuidedFieldValueTypes.Object)
        {try{return JsonNode.Parse(text);}catch(JsonException){throw Validation((definition.Path,$"{definition.Label} must contain valid structured JSON."));}}
        return value;
    }
    private static GuidedWorkValidationException Validation((string Key, string Message) error) => new(new Dictionary<string, string[]> { [error.Key] = [error.Message] });
    private async Task<T> ExecuteIdempotentTransactionAsync<T>(Guid companyId,Guid sessionId,Guid requestId,string operationType,Func<Task<T>> operation,CancellationToken ct) where T:class
    {
        var strategy=_db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async()=>
        {
            _db.ChangeTracker.Clear();
            if(await ReadOperationAsync<T>(companyId,sessionId,requestId,operationType,ct) is{ } completed)return completed;
            await using var transaction=await _db.Database.BeginTransactionAsync(ct);
            var result=await operation();
            await transaction.CommitAsync(ct);
            return result;
        });
    }
    private async Task<T?> ReadOperationAsync<T>(Guid companyId, Guid sessionId, Guid requestId, string type, CancellationToken ct) where T : class
    {
        if (requestId == Guid.Empty) return null;
        var json = await _db.GuidedSessionOperations.AsNoTracking().Where(x => x.CompanyId == companyId && x.SessionId == sessionId && x.ClientRequestId == requestId && x.OperationType == type).Select(x => x.ResponseJson).SingleOrDefaultAsync(ct);
        if(json is null)return null; if(json.StartsWith("protected:",StringComparison.Ordinal))json=_operationProtector.Unprotect(json[10..]);
        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }
    private async Task StoreOperationAsync<T>(GuidedWorkSession session, Guid requestId, string type, T result, CancellationToken ct)
    {
        var json=JsonSerializer.Serialize(result,JsonOptions);if(type=="review")json="protected:"+_operationProtector.Protect(json);
        _db.GuidedSessionOperations.Add(new GuidedSessionOperation(Guid.NewGuid(), session.CompanyId, session.Id, requestId, type, json));
        await _db.SaveChangesAsync(ct);
    }
    private Task QueueAuditAsync(VirtualCompany.Application.Auth.ResolvedCompanyMembershipContext member, GuidedWorkSession session, string action, string summary, CancellationToken ct)
    {
        var changedFields = session.Fields.Where(HasChangedFromOriginal).Select(x => x.Path).Order(StringComparer.Ordinal).Take(100).ToArray();
        var sources = session.Fields.Select(x => x.SourceType).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).Take(20).ToArray();
        return _audit.WriteAsync(new AuditEventWriteRequest(session.CompanyId, "user", member.UserId, action, "guided_work_session", session.Id.ToString("N"), "succeeded", summary,
            Metadata: new Dictionary<string, string?>
            {
                ["artifactType"] = session.ArtifactType, ["status"] = session.Status, ["version"] = session.Version.ToString(),
                ["targetArtifactId"] = session.TargetArtifactId?.ToString("N"), ["targetArtifactVersion"] = session.TargetArtifactVersion,
                ["changedFields"] = string.Join(',', changedFields), ["dataSources"] = string.Join(',', sources),
                ["evidence"] = "safe_field_paths_and_source_classifications_only"
            }, CorrelationId: session.CorrelationId), ct);
    }
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
