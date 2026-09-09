# Controlled sales demo scenarios

## Safety model

The sales demo workflow operates only on a company that carries all three matching markers: `is_demo_tenant`, `demo_scenario_key`, and `demo_scenario_version`. Reset and command requests also require an active Owner, Admin, or Manager membership. The API never accepts a company wildcard, SQL, arbitrary HTTP, scripts, cursor coordinates, or model-authored executable instructions.

All demo features are disabled by default:

```json
"DemoScenarios": {
  "Enabled": false,
  "ProvisioningEnabled": false,
  "ResetEnabled": false
}
```

Enable only the required switches in an explicitly authorized local or test deployment. Do not enable provisioning or reset against production infrastructure without separate operational authorization.

## Versioned scenario

Version 1 is the embedded, reviewable specification at `src/VirtualCompany.Infrastructure.Sales/Sales/DemoScenarios/northstar-sales-v1.json`.

- Scenario: `northstar-sales`, version `1`
- Company label: `Northstar Demo Company`
- Synthetic customer/contact/lead only; addresses use the reserved `.invalid` domain
- Fixed identifiers are derived from company, scenario, version, and logical record key
- Fixed seed timestamp, amounts, currency, stages, and expected visible outcomes
- Ordered allowlist: qualify lead, convert lead, move deal to proposal
- Allowed roles: owner, admin, manager
- No credentials, mailbox/calendar connections, provider identifiers, or production data are copied

The parser accepts schema version 1 and known record/role/command values only. Adding a version requires a new reviewed JSON resource and matching typed command implementation; do not change an already deployed version in place.

## API workflow

1. `GET /api/demo-scenarios` lists approved specifications.
2. `POST /api/demo-scenarios/provision` creates a new isolated company after `confirmSyntheticDataOnly` is set.
3. `POST /api/demo-scenarios/current/link-meeting` links one company-owned meeting.
4. `POST /api/demo-scenarios/current/start` starts the ordered run.
5. `POST /api/demo-scenarios/current/commands` executes the next allowlisted command with an idempotency key.
6. `GET /api/demo-scenarios/current/reset-preview` reports the exact company, version, record counts, disabled integrations, validations, invariants, and confirmation token without changing scenario data.
7. `POST /api/demo-scenarios/current/reset` requires the exact scenario key/version, exact company name, and fresh preview token.

Every company-scoped call requires `X-Company-Id`; the server resolves the actor and membership independently. The Web client deliberately fails in offline mode instead of displaying mock demo state.

## Reset behavior

Reset runs inside a transaction. It revalidates the company marker, version, deterministic record ownership, meeting ownership, permissions, exact company name, current affected counts, and preview token before mutation. It restores the meeting-referenced customer, contact, and lead in place, removes only deterministic scenario-created activities/deals, increments the reset generation, and preserves audit and prior command evidence.

The preview token changes when the scenario generation or affected record counts change. A stale token is rejected. Command idempotency is scoped by company, run, reset generation, and caller key; a second execution of the same step is prevented by database constraints and optimistic concurrency.

## External effects

Demo tenants are deny-by-default at the company outbox dispatch boundary. Email, Teams, calendar, payment, accounting, CRM, marketing, and every other outbox-backed provider action are blocked before provider access and recorded as audit evidence. No safe delivery adapter is enabled by this scenario. A future adapter must be explicit, test-only, visibly labelled in the UI, and separately reviewed.

## Audit and telemetry

Audit actions cover provisioning, meeting linking, start, preview, reset, command execution/rejection, and blocked external effects. Audit rows and demo command evidence are not deleted by reset. Traces/counters identify the company, scenario/version, command, and rejection category without including synthetic contact content.

## Repeatability check

The automated verification provisions two isolated demo companies, runs all three typed actions, resets the first company, and runs the same actions again. It asserts stable lead/deal identifiers, ordered and idempotent commands, preserved audit evidence, tenant isolation, stale-write rejection, and identical starting state after repeated reset.

## Cleanup

There is intentionally no broad cleanup endpoint. If an authorized test operator retires a demo company, use the normal company lifecycle for the one resolved company ID after independently verifying its demo markers and scenario version. Preserve audit evidence according to the normal retention policy. Never delete by name pattern, scenario wildcard, or shared database scope.

## Private-panel controls

The implemented `SalesMeetingDemoControlPanel` follows `docs/design/references/sales-meeting-demo-controls-reference.png`, with its generation prompt alongside it. A meeting can be supplied explicitly through the lead page's `meetingSessionId` query value or resolved from its invitation. The panel renders only when both the meeting and current demo scenario resolve inside the active company; normal-company lead views retain the existing Alex assessment without demo controls.
