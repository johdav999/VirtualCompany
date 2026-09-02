# Product profile: responsibility-driven workspaces

```yaml
product: Virtual Company responsibility-driven workspaces
type: web
revision: 3ed44b3 plus current responsibility-workspace working tree
launch: dotnet run --project src/VirtualCompany.Api/VirtualCompany.Api.csproj
environment: local
roles:
  - name: Company owner
    access: isolated integration-test company with Owner membership and executive oversight
  - name: Functional manager
    access: isolated integration-test company with an assigned Sales or Finance responsibility
  - name: Ordinary member
    access: isolated integration-test company with active membership and no management authority
evidence:
  screenshots: docs/design/references/responsibility-driven-today-workspace-*-verified.png and generated Settings/Monthly references
  logs: focused dotnet test and build output from the completion audit
flows:
  - id: FLOW-RESP-001
    name: Configure responsibility ownership and presets
    role: Company owner
    preconditions: Active company owner, members, and compatible agents
    outcome: Preview, apply, edit, and remove remain tenant-scoped, authorized, audited, and visible on the next workspace load
  - id: FLOW-RESP-002
    name: Review Today as owner and functional manager
    role: Company owner and Functional manager
    preconditions: Responsibility assignments and prepared feature state
    outcome: The shared route adapts its lenses, priorities, metrics, decisions, and agent updates without leaking another responsibility
  - id: FLOW-RESP-003
    name: Request a company review
    role: Company owner
    preconditions: Operating mode and cycle budget allow a review
    outcome: One durable idempotent operating-cycle request is queued and its safe progress or denial state is visible
  - id: FLOW-RESP-004
    name: Review an explicit reporting month
    role: Company owner and Functional manager
    preconditions: Company timezone, responsibility assignments, and available feature records
    outcome: Monthly results use authoritative period semantics, truthful coverage, distinct caching, and canonical follow-up links
```

