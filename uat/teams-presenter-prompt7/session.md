# Prompt 7 polish and UAT session

## Product profile

```yaml
product: Virtual Company — Alex Teams presenter
type: web
revision: working tree
launch: dotnet run --project src/VirtualCompany.Api and dotnet run --project src/VirtualCompany.Web
environment: local automated substitute; live Microsoft 365 tenant unavailable
roles:
  - name: Platform administrator
    access: test authentication only
  - name: Meeting organizer
    access: company-scoped test identity
evidence:
  screenshots: docs/design/references/teams-presenter-admin-reference.png
  logs:
    - 29 focused API/domain/migration tests passed on 2026-09-04
    - 10 focused Web/component/client tests passed on 2026-09-04
    - EF Core reported no pending model changes on 2026-09-04
    - API and Web production-relevant project builds completed with 0 errors on 2026-09-04
flows:
  - id: TP7-FLOW-ADMIN
    name: Review tenant readiness and controlled rollout
    role: Platform administrator
    outcome: Exact gates, evidence, remediation, and emergency disable are visible
  - id: TP7-FLOW-ORGANIZER
    name: Invite, admit, present, take control, and stop Alex
    role: Meeting organizer
    outcome: Provider state is truthful and media needs consent plus start authorization
```

## Evidence boundary

Repository tests cover deterministic contracts, policy, domain, component, authorization, and isolation. Native Teams lobby, role, sharing dialog, desktop/web, guest/federated/anonymous, and network behavior remain blocked until an authorized tenant and deployed Prompt 6 host are available. See `docs/teams-presenter-live-uat-matrix.md`.

## Issue ledger

| ID | Severity | Flow | Type | Summary | Evidence | Acceptance | Status |
|---|---|---|---|---|---|---|---|
| TP7-UAT-001 | P0 | Organizer | external gate | Live restrictive-policy tenant UAT unavailable locally | UAT matrix | All rows have owner/date/evidence/pass | Blocked |
| TP7-UAT-002 | P1 | Organizer | external gate | Native lobby/role/stage denial not locally verifiable | UAT matrix | Provider state matches desktop/web observation | Blocked |

## Automated evidence

| Check | Result | Date | Notes |
|---|---|---|---|
| Rollout, tenant registration/readiness, meeting-call domain, migration, and scheduling disclosure | Pass — 29 tests | 2026-09-04 | Includes fail-closed tenant/package/UAT/cost gates, explicit media authorization, revocation, and package authorization. |
| Organizer/admin surface and API clients | Pass — 10 tests | 2026-09-04 | Covers authoritative command routes, lifecycle controls, autonomy/preemption affordances, and protected admin content. |
| EF Core pending model changes | Pass | 2026-09-04 | No model changes exist beyond the checked-in migration. |
| API and Web builds | Pass | 2026-09-04 | Both projects built with 0 warnings and 0 errors under the final verification command. |

These results are deterministic repository evidence only. They do not change either blocked live-UAT item above into a pass.
