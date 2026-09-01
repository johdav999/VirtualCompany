# Finance tool planning boundary

`IFinanceToolPlanner` converts a bounded natural-language Finance request into a
review-only `finance-tool-plan-v1` contract. Planning never invokes
`IToolExecutor` or `ICompanyToolExecutor`; `FinanceToolPlan.CanExecute` is always
false.

The HTTP entry point is:

`POST /api/companies/{companyId}/agents/{agentId}/finance/tool-plans`

It requires the existing `FinanceView` policy. The planner intersects the
agent's effective authority with the current actor's P0 Finance authorization,
and sends only that permitted manifest projection to the shared reasoning
gateway. Model output is accepted only after closed-schema validation and
deterministic checks for tool/version/action/scope equality, JSON input schema,
grounded target and evidence IDs, request action class, dependency integrity,
tenant scope, and configured resource limits.
Context that resembles credentials, bearer authorization, tokens, passwords, or
private keys is rejected before a model call.

The projection is versioned as `finance-planning-context-v1`. Each permitted
Finance manifest supplies a safe purpose, action class, target entity types,
side-effect summary, evidence and freshness requirements, human checkpoint
behavior, result semantics, bounded examples, deterministic ranking hints, and
the authoritative public input schema. Denied tools, output/provider payloads,
policy implementation details, and risk internals are not projected.

Human references to invoice numbers, supplier bill numbers, customers,
suppliers, fiscal-period labels, and accounting migrations are resolved through
the company-scoped Finance evidence resolver. A single match contributes only a
safe source ID, entity ID, source version, update time, and freshness state.
Zero or multiple matches require clarification; the planner does not guess or
send candidate record content to the model. The combined authority, manifest,
policy-decision, target-version, and freshness snapshot has a canonical SHA-256
hash retained on the plan. `IFinancePlanningContextProjector.CheckFreshnessAsync`
recomputes the same projection before supervised execution so registry,
permission, or target changes produce `finance_planning_context_stale`.

Plans return one of `ready`, `needs_clarification`, `confirmation_required`,
`approval_required`, `unsupported`, or `failed` with a stable safe reason code.
Execute steps are never run by this endpoint; their confirmation and approval
states remain review metadata for a separate supervised execution path.

## Conversational read and recommendation execution

`POST /api/companies/{companyId}/agents/{agentId}/finance/tool-plans/execute`
accepts a required idempotency key and runs only plans whose steps are all
`read` or `recommend` with no confirmation or approval checkpoint. It rechecks
the planning-context hash, executes dependency order through the existing P0
policy-enforced tool executor, validates every registered output schema, and
uses the shared reasoning gateway only after at least one validated tool result
exists. Execute/write plans and unsupported questions are never answered from
general model knowledge.

The result contract distinguishes completed, partially completed,
needs-clarification, unsupported, failed, timed-out, and cancelled runs. Each
step reports its attempts, dependency state, schema validity, evidence
freshness, truncation, error code, and safe validated output; the run carries
the shared correlation ID.
Read-only transient failures receive at most the configured safe retry count;
recommendations are not retried. Re-planning is capped and occurs only for a
stale pre-execution context or a validated result that declares changed target
resolution or a supplied plan dependency. Every revision and planner/tool/model
call count is returned.

The trusted `analyze_finance_capability` recommendation adapter exposes the six
existing Finance analysis types (`cash_liquidity`, `payables`, `receivables`,
`accounting_treatment`, `close_analysis`, and `operating_cadence`) through
`IFinanceAgentAnalysisService`; it does not duplicate their calculations.

Runtime limits are configured under `FinanceToolPlanner` in `appsettings.json`:
steps, records, input/output characters, model/tool calls, elapsed seconds, and
estimated cost. The service uses one model call, writes a safe business audit
event for every terminal result, and does not retain raw structured provider
output as a validated plan.

Conversational limits are configured separately under
`FinanceConversationExecution`: total elapsed seconds, read attempts, plan
revisions, and validated output characters.
