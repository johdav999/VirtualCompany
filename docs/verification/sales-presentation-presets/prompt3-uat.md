# Presentation presets Prompt 3 UAT

## Product profile

- Product: Virtual Company meeting presentation runs
- Type: web
- Environment: local company-member meeting-preparation route
- Launch: `dotnet run --project src/VirtualCompany.Web/VirtualCompany.Web.csproj --no-build --no-launch-profile --urls http://localhost:5066`
- Reference: `docs/design/references/sales-meeting-preset-run-reference.png`
- Automated substitute: focused bUnit meeting-preparation and preset component/client tests

## Evidence packets

### FLOW-001 — Apply a published preset to a meeting

Expected: A member can select a published preset, review inherited defaults and meeting overrides, prepare an isolated run, and see authoritative readiness without replacing an existing run silently.

Observed: Backend, Web client, and component behavior compile and the focused meeting preparation suite passes. Native browser capture was blocked before navigation because the Windows automation kernel repeatedly failed during sandbox ACL initialization (`helper_unknown_error: apply deny-read ACLs`). No credentials or browser state were invented.

Result: automated substitute passed; native browser capture blocked by host tooling.

### FLOW-002 — Explicit version update and responsive layout

Expected: A pinned run advertises a newer version, shows a comparison, preserves compatible overrides, and requires explicit confirmation. At narrow widths, content stacks without horizontal overflow.

Observed: Version comparison and explicit replacement are represented by typed API contracts and the component; scoped CSS switches the two-column workspace to one column at 900px. The focused Web suite passes. Screenshot comparison could not be completed because of the same host ACL failure.

Result: automated substitute passed; visual comparison blocked by host tooling.

## Issue ledger

| ID | Severity | Flow | Type | Summary | Acceptance / regression | Status |
|---|---|---|---|---|---|---|
| UAT-P3-001 | P2 | FLOW-001/002 | environment | Browser automation kernel exits during ACL setup | Re-run desktop and 390px meeting-preparation captures against the reference when the host sandbox initializes normally. | Blocked |
