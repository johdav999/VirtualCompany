# Product profile

```yaml
product: Virtual Company Sales presentation journey
type: web
revision: working tree on 2026-09-05
launch: server.ps1 on http://localhost:5301; client.ps1 on http://localhost:5062
environment: local Development with TeamsMeetingUi:BrowserDiagnosticsEnabled=true
roles:
  - name: Authorized Sales company member
    access: repository development authentication and seeded local company
evidence:
  screenshots: docs/design/references when the full host can start
  logs: evidence.md startup transcript and focused test results
flows:
  - id: FLOW-001
    name: Lead invitation to preparation
    role: Authorized Sales company member
    preconditions: Scheduled invitation with provider event
    outcome: Invitation status and action route to its authoritative preparation workspace
  - id: FLOW-002
    name: Prepare and activate Wellheld deck
    role: Authorized Sales company member
    preconditions: Eligible Sales agent and saved meeting session
    outcome: Three-slide deck is processed, activated, and readiness becomes ready
  - id: FLOW-003
    name: Private browser presenter controls
    role: Authorized Sales company member
    preconditions: Ready meeting and browser diagnostics enabled
    outcome: Private preview and controls use authoritative presentation state
  - id: FLOW-004
    name: Diagnostics and tenant fail-closed paths
    role: Unauthorized or non-Teams browser user
    preconditions: Diagnostics disabled, revoked access, or wrong company
    outcome: No preparation or presenter state is disclosed
```
