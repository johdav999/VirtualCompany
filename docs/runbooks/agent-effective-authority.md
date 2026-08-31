# Agent effective authority

The effective authority projection is the authoritative explanation of which tools, action classes, and scopes an agent may use. Capability APIs, profile presentation, planning, schedule validation, approvals, and execution resolve the same projection. Execution always resolves it again immediately before policy evaluation.

## Grant sources

- `configured` grants come from the persisted agent operating profile. They remain visible separately and are never rewritten by registry discovery.
- `compatibility_role_policy` grants come from an explicit, versioned role policy. Laura's initial Finance compatibility policy is `laura-finance-role-policy-v1`; it enumerates the shipped tools and does not grant future registry additions.
- A registered tool without either grant is `configuration_required`. A configured tool without an implementation is `not_implemented`.

Each projection has `agent-effective-authority-v1` plus a deterministic SHA-256 hash over effective tool versions, action classes, scopes, states, grant sources, and actor permission requirements. Plans, schedules, approval requests, and execution requests retain this pair. A mismatch returns `effective_authority_stale` and requires a fresh review; it never falls back to registry membership or agent identity.

## Diagnosis

1. Compare the profile or capability API authority version and hash with the execution or approval metadata.
2. Inspect the tool's state and stable reason code. Do not inspect or log request payloads to diagnose authority.
3. For `configuration_required`, add a reviewed configured grant or release a new explicit role-policy version.
4. For `integration_unavailable`, restore the named integration through its owning settings workflow and resolve the projection again.
5. For `effective_authority_stale`, discard the old preview, plan, schedule review, or approval and create a new reviewed request.

## Migration and recovery

Migration `20260831071848_AddAgentEffectiveAuthorityVersion` adds nullable authority version and hash columns to existing orchestration runs. Existing runs remain readable but are treated as stale for a new plan commit because they were not reviewed against the effective authority contract.

Rollback drops only those two nullable columns. Before rollback, stop new plan creation so newly created plans are not left without their authority binding. Forward recovery is to reapply the migration and regenerate affected plans; do not copy hashes from an older run or approval.
