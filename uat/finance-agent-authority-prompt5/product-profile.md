# Product profile: finance agent authority workspace

## Product and user

- Product: Virtual Company authenticated agent profile.
- Primary user: company owner, administrator, manager, finance approver, accountant, or tester reviewing Laura's effective finance authority.
- Primary job: understand what the agent can read, recommend, or execute and inspect the safe evidence for its latest approval request.
- Critical boundary: the interface must consume effective authority and approval APIs; it must not reproduce authorization decisions or expose raw policy/payload data.

## Happy path

1. Open Laura's agent profile in an active company.
2. Scan capability rows for action mode, actor permission, approval behavior, integration state, and effective readiness.
3. Inspect the latest approval target, effect, evidence age, risk, approver requirement, expiry, and lifecycle state.
4. Open the target record, approval, or audit trail.
5. If the signed-in membership has system-diagnostics access, use the visually separate operator links.

## Acceptance targets

- Desktop and narrow layouts preserve readable hierarchy and do not clip content.
- Loading, empty, error, denied/configuration, approval-required, and stale states are explicit.
- English and Swedish resources have matching keys.
- Keyboard focus is visible and regions, table headers, live status, and errors have appropriate semantics.
- No raw threshold context, hashes, secrets, hidden prompts, or inaccessible record links are rendered.

## Visual baseline

- Reference: `docs/design/references/agent-authority-approval-reference.png`.
- Generated before production UI edits using the built-in image generation workflow.
- Match targets: dense authority matrix, clear approval preview, restrained enterprise palette, compact states, and a separated operator footer.
