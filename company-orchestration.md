# Company-Level Agent Orchestration

## Purpose

This document assesses whether Virtual Company currently contains an orchestration layer that can run the company, decide what work is needed, lead its agents, delegate work, coordinate collaboration, and adapt based on results. It also proposes an architecture and delivery path for adding that capability.

The short answer is that the solution contains substantial execution and orchestration infrastructure, but it does not yet contain a company-level executive orchestrator. Existing components can execute assigned work, schedules, conditions, and predefined workflows. They do not form a persistent operating loop that observes company state, evaluates goals, originates work, delegates it, monitors outcomes, and replans.

## Current implementation

### Single-agent orchestration

`SingleAgentOrchestrationService` provides a shared runtime for individual agents. It resolves the company, agent, task, intent, grounded context, policies, and permitted tools; builds a structured prompt; executes requested tools; and records task, rationale, source, tool-execution, and audit artifacts.

This is a strong execution primitive, but it is reactive. An API request, task, scheduled trigger, condition trigger, or other caller must already have selected the work or supplied the intent. It does not independently decide what the company should do next.

Relevant implementation:

- `src/VirtualCompany.Infrastructure.Operations/Companies/SingleAgentOrchestrationService.cs`
- `src/VirtualCompany.Application/Orchestration/SingleAgentOrchestrationContracts.cs`

### Multi-agent coordination

`MultiAgentCoordinator` implements bounded manager-worker collaboration. It creates a parent task and worker subtasks, executes the selected worker agents through the shared single-agent runtime, records their contributions, and produces a consolidated response. It enforces limits on fan-out, depth, runtime, and total steps, and prevents recursive unplanned delegation.

The live coordinator requires the caller to provide an explicit worker plan. A request without `Workers` is rejected with the message that an explicit manager-worker collaboration plan is required. Therefore, this component coordinates a team after another actor has already selected the team and divided the objective. It does not currently perform autonomous team formation or work decomposition.

An internal `GenerateWorkerPlanAsync` method exists, but it is not used by the coordinator entry point. Its presence suggests that automatic team selection was considered, while the active behavior deliberately remains explicit and bounded.

Relevant implementation:

- `src/VirtualCompany.Infrastructure.Operations/Companies/MultiAgentCoordinator.cs`
- `src/VirtualCompany.Application/Orchestration/MultiAgentCollaborationContracts.cs`
- `src/VirtualCompany.Application/Orchestration/BoundedCollaborationPolicy.cs`

### Agent planning and prioritization

The shared agent AI capabilities include work prioritization and planning. Work prioritization uses deterministic priority and due-date scoring, with AI limited to explaining the authoritative ranking. Agent planning can turn a supplied objective into a bounded draft plan. A separately invoked commit operation can create durable tasks from a reviewed plan.

This is useful planning support, but it is still user- or caller-driven. The objective is supplied externally, the plan is scoped to one agent, and committing it requires an explicit action. It is not a company-wide source of goals or a continuous planning loop.

Relevant implementation:

- `src/VirtualCompany.Infrastructure.Operations/Companies/SharedAgentAiCapabilityServices.cs`
- `src/VirtualCompany.Infrastructure.Operations/Companies/SharedAgentReasoningGateway.cs`

### Scheduled and condition-triggered execution

Agent scheduled triggers can enqueue durable execution requests. Trigger workers apply policy checks, idempotency, retries, audit recording, and dispatch to the single-agent orchestrator. Condition-trigger infrastructure also exists for evaluating configured conditions and requesting execution.

These components provide autonomous execution in the narrow sense that work can start without a user clicking a button at that moment. They remain reactive to schedules or configured conditions, however. They do not determine which company outcome deserves attention or originate a new cross-functional initiative.

Some generic metric and entity-field condition-resolution paths also appear intentionally incomplete, so the condition system is not yet a general business-state observation layer.

Relevant implementation:

- `src/VirtualCompany.Infrastructure.Operations/Companies/AgentScheduledTriggerServices.cs`
- `src/VirtualCompany.Infrastructure.Operations/Companies/ConditionTriggerEvaluationInfrastructure.cs`
- `src/VirtualCompany.Infrastructure.Operations/Companies/TriggerExecutionInfrastructure.cs`

### Role-agent operating cadence

`RoleAgentCadenceBackgroundService` automatically invokes Finance, Sales, Marketing, and Support analysis on daily, weekly, and selected monthly cadences. It asks each agent to prepare a manager brief and ranked review queue.

This is the closest current component to proactive company operation, but its departments, cadence behavior, and instruction are hard-coded. It produces analysis rather than a durable company operating plan, delegated initiatives, coordinated execution, and outcome-driven replanning.

Relevant implementation:

- `src/VirtualCompany.Infrastructure.Operations/Companies/RoleAgentCadenceBackgroundService.cs`

### Workflow, tasks, approvals, and outbox

The solution already has durable tasks and assignments, workflow scheduling and progression, approval enforcement, background execution, retries, reconciliation-oriented failure handling, audit history, and an outbox for important external effects.

These are the correct foundations for an executive orchestrator. They provide controlled execution but do not decide which work should exist. Workflow definitions and several handlers are predetermined and execute known business processes rather than form company strategy.

Relevant implementation:

- `src/VirtualCompany.Infrastructure.Operations/Companies/CompanyTaskService.cs`
- `src/VirtualCompany.Infrastructure.Operations/Companies/WorkflowSchedulerInfrastructure.cs`
- `src/VirtualCompany.Infrastructure.Operations/Companies/WorkflowProgressionInfrastructure.cs`
- `src/VirtualCompany.Infrastructure.Operations/Companies/CompanyOutboxInfrastructure.cs`

## Capability gap

The missing capability is a persistent company operating loop:

1. Observe authoritative company state.
2. Compare the state with active company goals and constraints.
3. Identify gaps, opportunities, risks, and blocked outcomes.
4. Propose a bounded operating plan.
5. Validate the plan against deterministic policies, capacity, budgets, permissions, and autonomy.
6. Create initiatives, workflows, and assigned tasks.
7. Coordinate individual and multi-agent execution.
8. Review completion evidence and business impact.
9. Replan, escalate, pause, or close the initiative.

The current solution has much of steps 5 through 7. It lacks a general implementation of steps 1 through 4 and 8 through 9.

## Proposed architecture

The new capability should be a control plane over the existing agent, task, workflow, approval, and outbox infrastructure. It must not introduce a second AI orchestration stack or move deterministic business rules into prompts.

### Ownership and boundaries

Application contracts should live in a focused Company Operations or AI Orchestration application area. Durable entities belong in the Domain project, EF configuration in Persistence, and execution implementations in `VirtualCompany.Infrastructure.Operations`. Background polling and event handling should use the existing Platform background-execution conventions.

Feature modules such as Finance, Sales, Marketing, and Support should expose authoritative read projections, signals, policies, commands, and workflows through Application contracts. The company orchestrator must not reference sibling infrastructure implementations directly.

The API should expose transport-only endpoints for goals, plans, decisions, operating-cycle queries, approval actions, and controlled manual cycle requests. The Web project should consume read models and commands and must not own planning or autonomy decisions.

### Durable operating model

Introduce company-scoped concepts such as:

- `CompanyGoal`: a durable company-wide outcome with metric, target, period, priority, owner, constraints, and status.
- `OperatingCycle`: one scheduled, event-driven, or manually requested management cycle, including lease, status, trigger, state snapshot, budgets, and outcome.
- `OperatingPlan`: a versioned proposal produced for one or more goals from a specific state snapshot.
- `OperatingInitiative`: a bounded business outcome connecting a goal to tasks, workflows, dependencies, budget, owner, and completion evidence.
- `OperatingDecision`: a proposed action or assignment with rationale, sources, policy result, risk, approval requirement, and idempotency identity.
- `OperatingReview`: an assessment of delivered results, deviations, confidence, lessons, and recommended next action.
- `OperatingEvent`: a durable signal that may request evaluation, such as a material KPI change, task failure, workflow blockage, approval decision, or integration failure.

Existing agent configuration objectives and capability-specific objectives can contribute context, but they should not be the only company-wide goal store. Important goal state must be represented in queryable relational columns rather than only in JSON.

Schema changes require an EF Core migration and must preserve equivalent local SQL Server and Docker SQL Server restore and run paths.

### Company-state snapshot

Each operating cycle should assemble a bounded, company-scoped snapshot from authoritative projections:

- Active goals, initiatives, plans, constraints, and deadlines
- KPI changes and material business signals
- Current tasks, ownership, dependencies, due dates, and workload
- Agent capabilities, permissions, autonomy, availability, and execution health
- Open approvals, workflow exceptions, escalations, and failed background work
- Finance constraints, liquidity risks, overdue obligations, and forecast changes
- Sales pipeline movement and stalled opportunities
- Marketing objectives, active plans, budget use, and performance
- Support demand, SLA risk, knowledge gaps, and customer-impact signals
- Relevant recent decisions, their predicted outcomes, and actual results

The snapshot should preserve source references and timestamps. The prompt should receive only relevant bounded context, not an unrestricted database dump. The persisted decision must retain enough evidence to explain why it was made later.

### Executive coordinator

Each company should be able to designate a coordinator agent, such as a CEO, COO, or general manager. The designation is configuration, not a separate runtime. The coordinator uses the existing shared AI reasoning and orchestration interfaces.

The coordinator should produce a structured proposal containing:

- The observed material change or unmet goal
- The evidence and affected goal
- Whether action is necessary now
- The desired business outcome
- Proposed initiatives, tasks, or workflows
- Proposed agent owner for each item
- Required collaborating agents and their bounded contributions
- Dependencies, priority, target dates, and budgets
- Expected completion evidence and outcome metrics
- Assumptions, uncertainty, and missing evidence
- Required approvals and escalation conditions

The model proposes this plan. Backend policy remains authoritative about whether it may be committed or executed.

### Deterministic plan validation

Before a proposed plan can mutate business state, backend validators should verify at least:

- Every referenced goal, record, agent, and workflow belongs to the same company.
- The goal is active and the proposed work is relevant to it.
- Equivalent work is not already proposed, approved, running, or recently completed.
- Selected agents are active, assignable, appropriately scoped, and capable of the work.
- Workload and concurrency limits allow each assignment.
- Dependencies are valid, acyclic, and bounded.
- The number of initiatives, tasks, collaborators, and tool executions remains within cycle limits.
- Proposed work stays within configured time, monetary, AI-usage, and operational budgets.
- Sensitive actions have a valid backend policy and approval route.
- External effects have stable business idempotency keys and use the outbox.
- Every task has observable completion evidence and an owner.
- Unsupported or ambiguous proposals become review items rather than silent execution.

Validation results should expose a stable reason code, plain-English explanation, evidence, and whether review or approval is required.

### Dispatch through existing primitives

Once a plan is authorized:

- Create durable initiatives and tasks through the existing task command boundary.
- Execute individual assignments through `ISingleAgentOrchestrationService`.
- Convert validated team proposals into `StartMultiAgentCollaborationCommand` for `IMultiAgentCoordinator`.
- Start known multi-step business processes through the workflow subsystem.
- Create approval requests before sensitive execution.
- Queue reliable external effects through the existing outbox and provider dispatchers.
- Carry a stable correlation chain from goal to cycle, plan, initiative, task, orchestration run, tool execution, approval, and provider outcome.

The multi-agent coordinator should remain bounded. The company coordinator may propose and validate team formation, but worker agents should not recursively create arbitrary teams or bypass assignment policies.

### Collaboration model

Agent collaboration should be artifact- and task-based rather than an unbounded group chat. A plan should define explicit roles such as owner, contributor, reviewer, or approver.

Useful collaboration patterns include:

- Parallel specialist assessments consolidated by the coordinator
- Sequential handoff where one agent's durable artifact becomes another agent's input
- Independent review or challenge of a high-impact recommendation
- Cross-functional dependency where one initiative cannot start until another produces evidence
- Escalation from a specialist to the coordinator when scope, authority, or evidence is insufficient

Each contribution should have a bounded objective, source task, expected output schema, deadline, rationale summary, confidence, and completion evidence.

### Outcome review and replanning

Task completion, failure, blockage, approval decisions, material KPI changes, and workflow exceptions should emit durable operating events. These events may request another operating cycle, but should not recursively invoke the planner in the same transaction.

The review stage should compare expected and actual results, determine whether the initiative achieved its goal, capture useful bounded learning, and select one of the following outcomes:

- Close the initiative as successful
- Continue the current plan
- Propose a revised plan
- Reassign or add bounded collaboration
- Request missing evidence
- Escalate for human review
- Pause because a dependency or external service is unavailable
- Stop because the goal, policy, or business context changed

### Loop and cost controls

A persistent orchestrator needs explicit protection against duplicate work and uncontrolled AI loops:

- One renewable operating-cycle lease per company
- Stable idempotency keys for cycles, decisions, initiatives, and assignments
- Minimum interval between scheduled cycles
- Material-change threshold for event-driven replanning
- Maximum new initiatives and tasks per cycle
- Maximum collaboration fan-out and depth
- Maximum runtime, model calls, tool calls, and AI cost per cycle
- Cooldown after rejected or repeatedly invalid proposals
- Suppression of events produced only by the cycle's own administrative writes
- Bounded retries with permanent-failure classification
- Dead-letter state and operator-visible recovery actions
- Company-level pause and emergency stop controls

### Graduated autonomy

Autonomy should be configured per company, goal, agent, capability, and action class. A single global autonomous switch is insufficient.

Recommended levels are:

1. **Recommend**: produce a reviewable operating plan without mutating tasks or workflows.
2. **Organize**: automatically create and assign internal tasks after plan validation, but do not execute them.
3. **Operate internally**: execute approved read, analysis, recommendation, and low-risk internal workflow actions within limits.
4. **Controlled execution**: execute explicitly permitted external actions through policy, approval, outbox, idempotency, retries, and reconciliation controls.

New companies and newly added capabilities should default to `Recommend`. Increasing autonomy must be explicit and audited. Sensitive actions remain approval- and policy-controlled regardless of configured agent autonomy or model output.

## Proposed operating-cycle sequence

1. A schedule, material business event, operator request, or previous review requests a cycle.
2. The background coordinator claims a company-scoped lease and creates an `OperatingCycle`.
3. The state-snapshot service gathers authoritative, bounded company context and source references.
4. Deterministic prechecks decide whether material evaluation is necessary.
5. The designated coordinator agent proposes a structured operating plan.
6. Schema validation rejects malformed or unsupported output safely.
7. Plan policies validate tenancy, goals, duplication, capability, capacity, dependencies, budgets, risk, and autonomy.
8. The system either records a reviewable proposal, creates an approval request, or commits allowed internal work.
9. The dispatcher creates durable initiatives, tasks, workflows, and bounded collaboration plans.
10. Existing workers execute assignments through the shared orchestration, workflow, approval, and outbox infrastructure.
11. Completion and exception events update initiative state and request review when material.
12. A review cycle compares expected and actual results, closes work, proposes revision, or escalates.
13. The lease is released and the complete decision chain remains available in audit and operating-history views.

## Implementation phases

### Phase 1: Recommendation-only management cycle

Deliver company goals, state snapshots, operating cycles, structured plan proposals, validation results, source evidence, audit history, and a UI for reviewing the proposed company plan.

No plan should create tasks or execute tools automatically in this phase. The goal is to establish an explainable, tenant-safe company management view and verify proposal quality before granting mutation authority.

### Phase 2: Reviewed delegation

Allow an authorized user to approve a valid operating plan. Approval commits initiatives and tasks, selects qualified agents, starts known workflows, and invokes bounded manager-worker collaboration where required.

Add duplicate-work detection, dependency validation, workload policies, authorization tests, tenant-isolation tests, and end-to-end correlation evidence.

### Phase 3: Low-risk autonomous operation

Allow configured low-risk plans to create and execute internal read or recommend work without case-by-case approval. Add event-driven review, result comparison, bounded replanning, cycle budgets, leases, cooldowns, dead-letter handling, and operator pause controls.

### Phase 4: Governed external execution

Enable narrowly configured execute actions through the existing approval and outbox infrastructure. Add financial and operational budgets, provider reconciliation, explicit kill switches, autonomy reporting, and safe recovery from ambiguous provider outcomes.

## User experience

The main UI should present company operation in plain English and make the distinction between proposals, approved work, active execution, blocked work, and completed outcomes clear.

Recommended surfaces include:

- Company goals and measured progress
- Current operating cycle and why it started
- Proposed plan with evidence and validation results
- Initiatives and responsible agents
- Agent workload and collaboration map
- Approvals and decisions required from people
- Exceptions, blocked work, and recovery actions
- Expected versus actual outcomes
- Autonomy settings, budgets, pause control, and audit trail

The UI must follow `docs/design.md`, reuse existing task, workflow, approval, activity, agent, and cockpit patterns, and avoid exposing storage values, enum names, prompt internals, or technical identifiers to users.

## Security, governance, and reliability requirements

- All entities, queries, commands, events, tasks, prompts, and audit records must be company-scoped.
- Server-side authorization is required for goals, plans, approvals, autonomy changes, and manual cycle requests.
- Agent capability and data-scope checks must occur before assignment and again before tool execution where relevant.
- Prompt instructions cannot grant authority that backend policy denies.
- Sensitive actions require authoritative policy evaluation and approval immediately before execution.
- External effects use durable outbox execution, stable idempotency, bounded retries, and reconciliation for ambiguous outcomes.
- Model failure, invalid structured output, insufficient evidence, missing integrations, and policy denial must be visible and recoverable.
- The system must preserve rationale summaries and source evidence without exposing hidden system prompts, credentials, or sensitive provider payloads.
- Audit history must identify the trigger, coordinator, plan version, validators, approval actors, assigned agents, tool executions, outcomes, and correlation ID.

## Testing strategy

Testing should be placed in the narrowest owning project and include:

- Domain tests for goals, plans, initiatives, decisions, state transitions, and dependency rules
- Policy tests for autonomy, duplication, capacity, budgets, assignment, and approval requirements
- Tenant-isolation tests for every new company-owned read and write path
- Authorization tests for cycle requests, plan approval, autonomy changes, and operator controls
- Structured-output validation tests for malformed, oversized, contradictory, and unsupported proposals
- Idempotency and concurrency tests for cycle leases, repeated signals, duplicate planning responses, and repeated dispatch
- Background execution tests for retryable, permanent, blocked, expired, and dead-letter outcomes
- Workflow and outbox integration tests for sensitive or external actions
- End-to-end tests from goal and signal through plan, approval, assignment, execution, and review
- UI component and browser checks for plan review, evidence, blocked states, pause controls, and plain-English failure messages
- SQL Server migration validation and equivalent local and Docker restore/run verification

## Design principle

The company orchestrator should not replace hard-coded business capabilities. Finance posting, support safety, customer communication, approval rules, workflow transitions, accounting eligibility, and provider integrations should remain deterministic, permissioned tools and workflows owned by their business modules.

The new orchestration layer should decide:

- What requires attention
- Which company goal or constraint is affected
- Which bounded outcome should be pursued
- Which agent should own the work
- How work should be divided and reviewed
- Which existing capability or workflow should be used
- When a result requires replanning, escalation, pause, or closure

This approach turns the current collection of user-triggered agents, schedules, and predefined automations into a governed virtual-company operating system while preserving the architecture's existing safety, approval, tenant-isolation, audit, and reliability boundaries.
