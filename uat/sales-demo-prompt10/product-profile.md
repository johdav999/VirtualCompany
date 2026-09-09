# Product profile: controlled sales demo

```yaml
product: Virtual Company controlled sales demo
type: web and typed backend workflow
revision: f9be926 plus current Prompt 10 working tree
launch: dotnet run --project src/VirtualCompany.Api/VirtualCompany.Api.csproj and dotnet run --project src/VirtualCompany.Web/VirtualCompany.Web.csproj
environment: local
roles:
  - name: Demo operator
    access: isolated synthetic company with active Owner, Admin, or Manager membership
  - name: Ordinary member
    access: isolated synthetic company with Employee membership; demo controls denied
evidence:
  screenshots: docs/design/references/sales-meeting-demo-controls-reference.png
  logs: focused API/Web tests, tenant regression suite, API/Web builds, and EF pending-model check
flows:
  - id: FLOW-DEMO-001
    name: Preview and reset the exact demo tenant
    role: Demo operator
    preconditions: Matching demo marker, scenario version, and fresh reset preview
    outcome: Only deterministic scenario records are restored and audit evidence is preserved
  - id: FLOW-DEMO-002
    name: Run the ordered product demonstration
    role: Demo operator
    preconditions: Company-owned meeting linked to a ready scenario run
    outcome: Three allowlisted commands update real Sales records in order and can be repeated after reset
  - id: FLOW-DEMO-003
    name: Block unauthorized or externally visible effects
    role: Demo operator and Ordinary member
    preconditions: Synthetic demo tenant
    outcome: Insufficient roles, cross-company targets, stale writes, arbitrary commands, and provider delivery are denied
  - id: FLOW-DEMO-004
    name: Operate the private side panel
    role: Demo operator
    preconditions: Authenticated local runtime, prepared meeting, demo feature flags enabled
    outcome: Start, next-step, preview, exact confirmation, reset, status, and recovery feedback work at desktop and mobile widths
```
