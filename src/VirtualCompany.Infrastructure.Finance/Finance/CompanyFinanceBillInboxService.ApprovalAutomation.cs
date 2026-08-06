using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Finance;

public sealed partial class CompanyFinanceBillInboxService
{
    public async Task<SupplierApprovalAutomationDto> GetApprovalAutomationAsync(
        GetSupplierApprovalAutomationQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        var bill = await LoadBillForWriteAsync(query.CompanyId, query.BillId, cancellationToken);
        if (!_supplierApprovalAutomationOptions.Enabled)
        {
            return DisabledApprovalAutomation(bill);
        }

        return await BuildApprovalAutomationDtoAsync(bill, cancellationToken);
    }

    public async Task<SupplierApprovalAutomationDto> SetApprovalAutomationAsync(
        SetSupplierApprovalAutomationCommand command,
        CancellationToken cancellationToken)
    {
        EnsureTenant(command.CompanyId);
        EnsureSupplierApprovalAutomationEnabled();
        var stage = SupplierApprovalAutomationStages.Normalize(command.Stage);
        var bill = await LoadBillForWriteAsync(command.CompanyId, command.BillId, cancellationToken);
        var supplierKey = BuildSupplierKey(bill.SupplierOrgNumber);
        if (supplierKey is null)
        {
            throw new InvalidOperationException("A supplier organization number is required before approval automation can be enabled.");
        }

        if (command.ActorUserId is not Guid grantorUserId)
        {
            throw new InvalidOperationException("A signed-in user is required to change supplier approval automation.");
        }

        var agent = await ResolveFinanceManagerAgentAsync(command.CompanyId, cancellationToken)
            ?? throw new InvalidOperationException("The Finance Manager agent is not available for this company.");
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var rule = await _dbContext.SupplierApprovalAutomationRules
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                x => x.CompanyId == command.CompanyId && x.SupplierKey == supplierKey && x.Stage == stage,
                cancellationToken);

        if (command.Enabled)
        {
            if (rule is null)
            {
                rule = new SupplierApprovalAutomationRule(
                    Guid.NewGuid(),
                    command.CompanyId,
                    supplierKey,
                    bill.SupplierName ?? "Unknown supplier",
                    bill.SupplierOrgNumber!,
                    stage,
                    agent.Id,
                    agent.DisplayName,
                    grantorUserId,
                    command.ActorDisplayName,
                    now);
                _dbContext.SupplierApprovalAutomationRules.Add(rule);
            }
            else
            {
                rule.Enable(agent.Id, agent.DisplayName, grantorUserId, command.ActorDisplayName, now);
            }
        }
        else if (rule is not null)
        {
            rule.Revoke(now);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        if (rule is not null)
        {
            await WriteApprovalAutomationAuditAsync(
                rule,
                command.Enabled,
                grantorUserId,
                command.ActorDisplayName,
                cancellationToken);
            if (command.Enabled)
            {
                await ApplyRuleToCurrentStageAsync(bill, rule, cancellationToken);
            }
        }

        return await BuildApprovalAutomationDtoAsync(bill, cancellationToken);
    }

    private async Task<FinanceIntegrationWriteResult> ApplyTrustedSupplierApprovalAsync(
        DetectedBill bill,
        string stage,
        FinanceIntegrationWriteResult writeResult,
        CancellationToken cancellationToken)
    {
        if (!_supplierApprovalAutomationOptions.Enabled)
        {
            return writeResult;
        }

        if (writeResult.ApprovalId is not Guid approvalId)
        {
            return writeResult;
        }

        var supplierKey = BuildSupplierKey(bill.SupplierOrgNumber);
        if (supplierKey is null)
        {
            return writeResult;
        }

        var rule = await _dbContext.SupplierApprovalAutomationRules
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.CompanyId == bill.CompanyId && x.SupplierKey == supplierKey && x.Stage == stage && x.IsActive,
                cancellationToken);
        if (rule?.GrantedByUserId is not Guid grantorUserId)
        {
            return writeResult;
        }

        var decision = await _approvalAutomationService.ApproveUnderStandingGrantAsync(
            bill.CompanyId,
            approvalId,
            new AutomatedApprovalGrant(
                rule.Id,
                grantorUserId,
                rule.AgentId,
                rule.AgentDisplayName,
                rule.SupplierName,
                rule.Stage),
            cancellationToken);

        return writeResult with
        {
            Status = decision.IsFinalized ? FinanceIntegrationWriteCommandRecordStatuses.Approved : writeResult.Status,
            Message = decision.IsFinalized
                ? $"{rule.AgentDisplayName} approved this action under the trusted supplier rule."
                : $"{rule.AgentDisplayName} approved the current stage under the trusted supplier rule."
        };
    }

    private async Task ApplyRuleToCurrentStageAsync(
        DetectedBill bill,
        SupplierApprovalAutomationRule rule,
        CancellationToken cancellationToken)
    {
        var writeRequestId = rule.Stage == SupplierApprovalAutomationStages.SupplierCreation
            ? CreateFortnoxSupplierCreationWriteRequestId(bill.Id)
            : CreateFortnoxRegistrationWriteRequestId(bill.Id);
        var approvalId = await _dbContext.FinanceIntegrationWriteCommands
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == bill.CompanyId && x.Id == writeRequestId)
            .Select(x => x.ApprovalId)
            .SingleOrDefaultAsync(cancellationToken);
        if (approvalId is not Guid currentApprovalId || rule.GrantedByUserId is not Guid grantorUserId)
        {
            return;
        }

        await _approvalAutomationService.ApproveUnderStandingGrantAsync(
            bill.CompanyId,
            currentApprovalId,
            new AutomatedApprovalGrant(
                rule.Id,
                grantorUserId,
                rule.AgentId,
                rule.AgentDisplayName,
                rule.SupplierName,
                rule.Stage),
            cancellationToken);
    }

    private async Task<SupplierApprovalAutomationDto> BuildApprovalAutomationDtoAsync(
        DetectedBill bill,
        CancellationToken cancellationToken)
    {
        var agent = await ResolveFinanceManagerAgentAsync(bill.CompanyId, cancellationToken);
        var supplierKey = BuildSupplierKey(bill.SupplierOrgNumber);
        var rules = supplierKey is null
            ? []
            : await _dbContext.SupplierApprovalAutomationRules
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x => x.CompanyId == bill.CompanyId && x.SupplierKey == supplierKey)
                .ToListAsync(cancellationToken);
        var blockedReason = supplierKey is null
            ? "Add a supplier organization number before enabling trusted supplier approvals."
            : agent is null
                ? "The Finance Manager agent is not available."
                : null;

        return new SupplierApprovalAutomationDto(
            bill.Id,
            bill.SupplierName ?? "Unknown supplier",
            bill.SupplierOrgNumber,
            SupplierApprovalAutomationStages.All
                .OrderBy(stage => stage == SupplierApprovalAutomationStages.SupplierCreation ? 0 : 1)
                .Select(stage =>
                {
                    var rule = rules.SingleOrDefault(x => x.Stage == stage);
                    return new SupplierApprovalAutomationStageDto(
                        stage,
                        stage == SupplierApprovalAutomationStages.SupplierCreation ? "supplier creation" : "invoice registration",
                        rule?.IsActive == true,
                        rule?.Id,
                        agent?.Id ?? Guid.Empty,
                        agent?.DisplayName ?? "Finance Manager",
                        blockedReason is null,
                        blockedReason);
                })
                .ToList());
    }

    private async Task<Agent?> ResolveFinanceManagerAgentAsync(Guid companyId, CancellationToken cancellationToken) =>
        await _dbContext.Agents
            .IgnoreQueryFilters()
            .Where(x =>
                x.CompanyId == companyId &&
                x.Status != AgentStatus.Paused &&
                x.Status != AgentStatus.Archived &&
                x.TemplateId == CoreAgentTemplateIds.Finance)
            .OrderBy(x => x.CreatedUtc)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task WriteApprovalAutomationAuditAsync(
        SupplierApprovalAutomationRule rule,
        bool enabled,
        Guid actorUserId,
        string actorDisplayName,
        CancellationToken cancellationToken) =>
        await _auditEventWriter.WriteAsync(
            new AuditEventWriteRequest(
                rule.CompanyId,
                "human",
                actorUserId,
                enabled ? AuditEventActions.SupplierApprovalAutomationGranted : AuditEventActions.SupplierApprovalAutomationRevoked,
                "supplier_approval_automation_rule",
                rule.Id.ToString("N"),
                AuditEventOutcomes.Succeeded,
                DataSources: ["supplier_bill", "approval_policy"],
                RationaleSummary: enabled
                    ? $"{actorDisplayName} allowed {rule.AgentDisplayName} to approve {rule.Stage} for {rule.SupplierName}."
                    : $"{actorDisplayName} stopped automatic {rule.Stage} approval for {rule.SupplierName}.",
                Metadata: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["supplierKey"] = rule.SupplierKey,
                    ["supplierName"] = rule.SupplierName,
                    ["stage"] = rule.Stage,
                    ["agentId"] = rule.AgentId.ToString("N"),
                    ["agentDisplayName"] = rule.AgentDisplayName
                }),
            cancellationToken);

    private static string? BuildSupplierKey(string? organizationNumber)
    {
        if (string.IsNullOrWhiteSpace(organizationNumber))
        {
            return null;
        }

        var key = new string(organizationNumber.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        return string.IsNullOrWhiteSpace(key) ? null : key;
    }

    private SupplierApprovalAutomationDto DisabledApprovalAutomation(DetectedBill bill) =>
        new(
            bill.Id,
            bill.SupplierName ?? "Unknown supplier",
            bill.SupplierOrgNumber,
            []);

    private void EnsureSupplierApprovalAutomationEnabled()
    {
        if (!_supplierApprovalAutomationOptions.Enabled)
        {
            throw new InvalidOperationException(_supplierApprovalAutomationOptions.DisabledMessage);
        }
    }
}
