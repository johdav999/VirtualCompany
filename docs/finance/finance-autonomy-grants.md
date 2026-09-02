# Finance autonomy grants

Finance autonomy policy version: `finance-autonomy-policy-v1`.

Finance autonomy is a company-owned grant over one agent and one named capability
from `finance-agent-coverage-v1`. Agent profile autonomy remains a compatibility
label and does not create or activate a grant. Existing and newly hired agents
therefore receive no proactive Finance authority during migration.

## Levels and boundaries

| Level | Maximum action class | Activation |
| --- | --- | --- |
| `read_monitor` | Read | Explicit activation |
| `recommend_draft` | Read and recommend | Explicit activation |
| `supervised_internal_execute` | Governed internal execute | Independent documented review |
| `scheduled_bounded_execute` | Low-risk governed internal execute | Independent documented review |

Every version names its triggers, action classes, tools, record/action/amount
limits, schedule and local execution window, timezone, evidence freshness,
confirmation or approval behavior, escalation route, effective time, and expiry.
Wildcards, human-only operations, unconfigured tools, external or permanent
execute effects, and grants that exceed the selected level are rejected.

Policy content is immutable. An edit creates a new prospective version and the
currently active version remains effective until the replacement is explicitly
activated. Activation revalidates the Finance catalogue/risk snapshot and the
agent's effective P0/P2 authority. Elevated versions require a different
authorized reviewer. Revocation clears the active version without deleting
history.

## Runtime decision

Workers and other proactive callers must supply a
`FinanceAutonomyEvaluationRequest` to the existing Finance actor authorization
path immediately before a step. The decision fails closed when there is no
active grant, a grant is expired or not yet effective, evidence is stale, a
limit/window/trigger/action/tool is outside scope, agent authority changed, or
the capability/risk catalogue changed.

The runtime also checks existing company operating pause/emergency-stop state
and the Finance controls below:

- company: all Finance autonomy in the company
- agent: all Finance autonomy for one agent
- capability: one named Finance capability across the company

Controls support `active`, `paused`, and `emergency_stopped`. Every change
requires a reason and authorized company manager, is audited, and is visible to
the next policy evaluation.

## API

Authenticated, company-scoped routes are under
`/api/companies/{companyId}/finance/autonomy`.

- Finance viewers may list grants, inspect effective policy, and evaluate a
  proposed bounded action.
- Company managers may create versions, activate, revoke, pause/resume, and set
  an emergency stop.
- Route and persisted queries reapply company scope; cross-company identifiers
  return no grant.

Audit actions use the `finance.autonomy_*` namespace and include grant/version,
catalogue version, authority version/hash, actor, outcome, and reason without
storing provider payloads or hidden model reasoning.
