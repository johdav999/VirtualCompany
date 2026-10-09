from pathlib import Path
p=Path('src/VirtualCompany.Infrastructure.Operations/Companies/CompanyApprovalRequestService.cs')
s=p.read_text(encoding='utf-8-sig').replace('public sealed class CompanyApprovalRequestService','public sealed partial class CompanyApprovalRequestService')
s=s.replace('        await _auditEventWriter.WriteAsync(\n            new AuditEventWriteRequest(\n                companyId,\n                approval.RequestedByActorType,','        await BindReviewAsync(approval, cancellationToken);\n        await _auditEventWriter.WriteAsync(\n            new AuditEventWriteRequest(\n                companyId,\n                approval.RequestedByActorType,',1)
s=s.replace('return await DecideCoreAsync(companyId, command, cancellationToken);', '''return await _dbContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
                try
                {
                    var result = await DecideCoreAsync(companyId, command, cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return result;
                }
                catch (ApprovalProposalChangedException) { await transaction.CommitAsync(cancellationToken); throw; }
                catch (ApprovalValidationException) { await transaction.CommitAsync(cancellationToken); throw; }
                catch (DbUpdateConcurrencyException)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    throw new InvalidOperationException("Another reviewer decided this proposal. Refresh its current status.");
                }
            });''',1)
old='''        if (approval.Status != ApprovalRequestStatus.Pending)
        {
            if (command.ClientRequestId.HasValue'''
new='''        if (!await CanReadReviewAsync(approval, membership, cancellationToken))
            throw new ApprovalDecisionForbiddenException("This proposal is outside the current user's review scope.");
        if (command.ClientRequestId.HasValue && command.ClientRequestId.Value != Guid.Empty &&
            TryGetGuid(approval.DecisionChain, "lastDecisionClientRequestId") == command.ClientRequestId.Value)
        {
            if (TryGetGuid(approval.DecisionChain, "lastDecisionActorId") != membership.UserId ||
                approval.DecisionChain.GetValueOrDefault("lastDecision")?.ToString() != normalizedDecision ||
                approval.DecisionChain.GetValueOrDefault("lastDecisionComment")?.ToString() != (command.Comment?.Trim() ?? "") ||
                TryGetGuid(approval.DecisionChain, "lastDecisionStepId") != command.StepId)
                throw new InvalidOperationException("This request identifier was already used for a different decision.");
            var step = approval.Steps.Single(x => x.Id == TryGetGuid(approval.DecisionChain, "lastDecisionStepId"));
            return new(await ToDtoAsync(approval, cancellationToken), ToStepDto(step),
                approval.CurrentActionableStep is { } next ? ToStepDto(next) : null, approval.IsTerminal);
        }
        if (approval.Status != ApprovalRequestStatus.Pending)
        {
            if (false && command.ClientRequestId.HasValue'''
assert old in s;s=s.replace(old,new,1)
# Remove superseded legacy replay block entirely.
start=s.index('            if (false && command.ClientRequestId.HasValue');end=s.index('            throw new ApprovalValidationException',start);s=s[:start]+s[end:]
s=s.replace('if (IsExpiredFinanceActionApproval(approval, DateTime.UtcNow))','if (ReviewExpiry(approval) <= DateTime.UtcNow)',1)
# Version checks run only after reviewer authorization and before any side effects.
needle='        var requestedApproval = normalizedDecision is "approve" or "approved";'
s=s.replace(needle,'''        if (normalizedDecision is "request_changes" or "changes_requested" && string.IsNullOrWhiteSpace(command.Comment))
            throw new ApprovalValidationException(new Dictionary<string, string[]> { [nameof(command.Comment)] = ["Explain the requested changes."] });
        await EnsureReviewedVersionAsync(approval, command, membership.UserId, cancellationToken);
'''+needle,1)
s=s.replace('approval.MarkChangesRequested(decisionComment);','decidedStep = approval.RequestChangesCurrentStep(currentStep.Id, membership.UserId, decisionComment!);',1)
s=s.replace('decisionChain["lastDecisionClientRequestId"] = command.ClientRequestId.Value;', '''decisionChain["lastDecisionClientRequestId"] = command.ClientRequestId.Value;
            decisionChain["lastDecisionActorId"] = membership.UserId;
            decisionChain["lastDecision"] = normalizedDecision;
            decisionChain["lastDecisionStepId"] = currentStep.Id;
            decisionChain["lastDecisionComment"] = command.Comment?.Trim() ?? "";''',1)
s=s.replace('        EnqueueApprovalUpdatedEvent(approval, approval.Status.ToStorageValue());\n        var linkedEntityTransition', '''        // Claim the concurrency token before owner execution. The transaction keeps the claim,
        // linked transitions and outbox atomic; a racing process loses before it can execute.
        await _dbContext.SaveChangesAsync(cancellationToken);
        await WriteReviewAuditAsync(approval, membership.UserId, normalizedDecision, cancellationToken);
        EnqueueApprovalUpdatedEvent(approval, approval.Status.ToStorageValue());
        var linkedEntityTransition''',1)
s=s.replace('if (approval.Status is ApprovalRequestStatus.Rejected or ApprovalRequestStatus.Expired)', 'if (approval.Status is ApprovalRequestStatus.Rejected or ApprovalRequestStatus.Expired or ApprovalRequestStatus.ChangesRequested or ApprovalRequestStatus.Stale or ApprovalRequestStatus.Superseded or ApprovalRequestStatus.Revoked)')
s=s.replace('ApprovalRequestStatus.Rejected or ApprovalRequestStatus.Expired or ApprovalRequestStatus.Cancelled)', 'ApprovalRequestStatus.Rejected or ApprovalRequestStatus.Expired or ApprovalRequestStatus.Cancelled or ApprovalRequestStatus.ChangesRequested or ApprovalRequestStatus.Stale or ApprovalRequestStatus.Superseded or ApprovalRequestStatus.Revoked)')
s=s.replace('if (approval.Status == ApprovalRequestStatus.Rejected)\n            {\n                command.MarkRejected(now);', 'if (approval.Status is ApprovalRequestStatus.Rejected or ApprovalRequestStatus.ChangesRequested or ApprovalRequestStatus.Stale or ApprovalRequestStatus.Superseded or ApprovalRequestStatus.Revoked or ApprovalRequestStatus.Cancelled)\n            {\n                command.MarkRejected(now);')
s=s.replace('        return ToDto(approval, contexts.GetValueOrDefault(approval.Id));','        return ToDto(approval, contexts.GetValueOrDefault(approval.Id)) with { Review = await ReviewAsync(approval, cancellationToken) };',1)
# Filter all list/detail read paths to the same identity and source scope.
pos=s.index('    public async Task<IReadOnlyList<ApprovalRequestDto>> ListAsync')
tail=s[pos:].replace('await RequireMembershipAsync(companyId, cancellationToken);','var membership = await RequireMembershipAsync(companyId, cancellationToken);',2)
tail=tail.replace('        var contexts = await BuildSummaryContextsAsync(companyId, approvals, cancellationToken);','''        var visible = new List<ApprovalRequest>();
        foreach (var item in approvals)
            if (await CanReadReviewAsync(item, membership, cancellationToken)) visible.Add(item);
        approvals = visible;
        var contexts = await BuildSummaryContextsAsync(companyId, approvals, cancellationToken);''',1)
tail=tail.replace('        return await ToDtoAsync(approval, cancellationToken);','''        if (!await CanReadReviewAsync(approval, membership, cancellationToken)) throw new KeyNotFoundException("Approval request not found.");
        return await ToDtoAsync(approval, cancellationToken);''',1)
s=s[:pos]+tail
p.write_text(s,encoding='utf-8')
