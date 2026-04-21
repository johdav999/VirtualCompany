# Goal
Implement backlog task **TASK-25.1.1 — Reconciliation scoring service with amount, date, reference, and counterparty rules** for story **US-25.1 Build reconciliation matching engine for bank transactions, payments, invoices, and bills**.

Deliver a tenant-aware application/domain service in the existing .NET modular monolith that:

- Scores and ranks **bank transaction → payment** candidate matches
- Scores and ranks **invoice/bill → payment** linkage suggestions
- Produces a **normalized confidence score** in the range **0.00–1.00**
- Includes **per-rule scoring details** in the result payload
- Applies **tenant-configurable amount tolerance and date proximity windows**
- Is covered by automated tests for:
  - exact match
  - near amount match
  - date-only match
  - reference-only match
  - counterparty-only match
  - no-match

Keep the implementation deterministic, testable, and isolated from UI concerns. Prefer clean architecture boundaries: domain scoring logic in Domain/Application, tenant config access via Application/Infrastructure abstractions, and tests in the test projects.

# Scope
In scope:

- Add a reconciliation scoring model and service for:
  - bank transaction vs payment candidates
  - invoice vs payment candidates
  - bill vs payment candidates
- Implement scoring rules for:
  - amount exact match
  - amount near match using tenant tolerance
  - date proximity using tenant-configured window
  - reference similarity
  - counterparty similarity
- Return ranked suggestions with:
  - candidate identifier
  - normalized confidence score
  - per-rule detail breakdown
  - enough metadata to explain why a candidate ranked where it did
- Add tenant-scoped configuration model/access for:
  - near-match amount tolerance
  - date proximity window
- Add automated tests for acceptance criteria

Out of scope unless required by existing code structure:

- Full reconciliation workflow UI
- Persistence of final reconciliation decisions
- Background jobs
- LLM/vector features
- New external integrations
- Broad accounting domain redesign

If relevant domain entities already exist, reuse them. If they do not, create minimal scoring DTOs/contracts rather than inventing large persistence models.

# Files to touch
Inspect the solution first and then touch only the minimum necessary files. Expected areas:

- `src/VirtualCompany.Domain/...`
  - add scoring value objects/enums/models
  - add pure scoring logic if domain-centric
- `src/VirtualCompany.Application/...`
  - add service interface and orchestration layer
  - add request/response DTOs
  - add tenant settings abstraction if not already present
- `src/VirtualCompany.Infrastructure/...`
  - implement tenant settings/config provider if needed
- `src/VirtualCompany.Api/...`
  - only if an API endpoint already exists or is the obvious integration point
  - otherwise do not add speculative endpoints
- `tests/VirtualCompany.Api.Tests/...` and/or other existing test projects
  - add unit/integration tests for scoring scenarios

Before coding, inspect:
- existing finance/accounting/reconciliation modules
- existing tenant settings/config patterns
- existing Result/DTO conventions
- existing test style and naming conventions

Do not create duplicate abstractions if equivalents already exist.

# Implementation plan
1. **Explore the current codebase**
   - Find whether there are existing models for:
     - bank transactions
     - payments
     - invoices
     - bills
     - tenant/company settings
   - Find existing patterns for:
     - application services
     - domain services
     - options/config retrieval
     - CQRS handlers
     - test fixtures/builders
   - Align naming and folder placement with the current codebase.

2. **Define scoring contracts**
   Create minimal, explicit contracts for reconciliation scoring. Prefer immutable records where consistent with the codebase.

   Include:
   - a request model for bank transaction candidate scoring
   - a request model for invoice/bill candidate scoring
   - a candidate result model
   - a per-rule detail model

   Suggested shape:
   - `ReconciliationSuggestion`
     - `CandidatePaymentId`
     - `ConfidenceScore`
     - `Rank`
     - `RuleDetails`
   - `ReconciliationRuleDetail`
     - `RuleName`
     - `Score`
     - `Weight`
     - `Matched`
     - `Explanation`
     - optional raw comparison values
   - `ReconciliationScoringSettings`
     - `NearAmountTolerance`
     - `DateProximityWindowDays`

3. **Implement deterministic scoring rules**
   Build a pure scoring component that evaluates each candidate payment against the source record.

   Required rules:
   - **Amount**
     - exact match should score highest
     - near match should score lower than exact but above no match
     - near match must use tenant-configured tolerance
   - **Date**
     - score based on proximity within configured day window
     - exact date should score highest
     - outside the window should score zero for this rule
   - **Reference**
     - normalize strings before comparison
     - support exact/contains/token-overlap style similarity
     - deterministic only; no fuzzy libraries unless already used in repo
   - **Counterparty**
     - normalize names before comparison
     - support exact/contains/token-overlap style similarity

   Normalize score to **0.00–1.00** overall.

   Important:
   - Keep rule weights explicit and easy to tune
   - Ensure no rule can push total score outside bounds
   - Round consistently, preferably at payload boundary, not during internal math
   - Return rule-level details for explainability

4. **Support both source types**
   Expose methods for:
   - bank transaction → payment candidates
   - invoice → payment candidates
   - bill → payment candidates

   Reuse the same internal scoring engine where possible. The source adapter can map source fields into a common comparable shape:
   - amount
   - date
   - reference
   - counterparty

5. **Add tenant-configurable settings**
   Retrieve scoring settings per tenant/company using existing settings patterns if present.

   Requirements:
   - amount tolerance configurable per tenant
   - date proximity window configurable per tenant
   - sensible defaults if tenant config is absent
   - no cross-tenant leakage

   If no settings infrastructure exists, add a minimal abstraction in Application and a simple Infrastructure implementation that can later be backed by persisted company settings.

6. **Rank and return suggestions**
   For each candidate set:
   - score all candidates
   - sort descending by confidence score
   - assign rank
   - include per-rule details in each result payload

   Ensure stable ordering for ties, e.g.:
   - score desc
   - exact amount before near amount if needed
   - then candidate ID or date for deterministic output

7. **Add tests**
   Add focused automated tests covering all acceptance criteria. At minimum include:
   - **exact match**
     - amount/date/reference/counterparty all align
     - highest score near 1.00 and ranked first
   - **near amount match**
     - amount differs but within tolerance
     - lower than exact match, above zero
   - **date-only match**
     - only date proximity contributes materially
   - **reference-only match**
     - only reference similarity contributes materially
   - **counterparty-only match**
     - only counterparty similarity contributes materially
   - **no-match**
     - all rules fail, score should be 0 or near-minimum per design
   - also add:
     - outside tolerance amount yields no amount score
     - outside date window yields no date score
     - tenant-specific settings alter scoring as expected
     - results are ranked descending and deterministic

8. **Keep the implementation explainable**
   Each suggestion payload should clearly show:
   - total confidence score
   - each rule’s contribution
   - whether the rule matched
   - concise explanation text

   Example explanations:
   - `Amount exact match`
   - `Amount within tenant tolerance of 5.00`
   - `Date difference 2 days within window 7`
   - `Reference token overlap matched on INV-1001`
   - `Counterparty normalized name exact match`

9. **Integrate minimally**
   If there is already a reconciliation command/query/service surface, wire the new scoring service into it.
   If not, stop at the application service plus tests unless an endpoint is clearly required by existing architecture or test harness.

10. **Code quality constraints**
   - Follow existing solution conventions
   - Keep methods small and deterministic
   - Avoid hidden magic constants; centralize weights/defaults
   - Add XML/comments only if consistent with repo style
   - Do not introduce unnecessary packages

# Validation steps
Run these after implementation:

1. Restore/build:
   - `dotnet build`

2. Run tests:
   - `dotnet test`

3. Verify acceptance behavior in tests:
   - bank transaction with multiple payment candidates returns ranked matches
   - invoice/bill with payment candidates returns ranked linkage suggestions
   - every suggestion has:
     - confidence score between `0.00` and `1.00`
     - per-rule scoring details
   - tenant-configured amount tolerance affects near-match scoring
   - tenant-configured date window affects date scoring
   - exact/near/date-only/reference-only/counterparty-only/no-match scenarios all pass

4. Sanity-check implementation details:
   - no cross-tenant config leakage
   - deterministic ordering for equal scores
   - normalized string comparison handles casing/spacing/punctuation reasonably
   - no score exceeds bounds due to rounding or weighting

# Risks and follow-ups
- **Unknown existing finance models**: the repo may not yet contain bank/payment/invoice/bill entities. If absent, use minimal scoring DTOs and avoid speculative persistence.
- **Tenant settings storage may be incomplete**: if there is no persisted settings mechanism yet, implement an abstraction with defaults and document the persistence follow-up.
- **Reference/counterparty similarity can become subjective**: keep v1 deterministic and simple; document future enhancement options like better tokenization or phonetic/fuzzy matching.
- **Weight tuning risk**: choose sensible defaults and centralize them so they can be adjusted without rewriting logic.
- **API surface may not exist yet**: do not invent broad endpoints unless the current module structure clearly expects them.

Follow-up candidates after this task:
- persist reconciliation decisions and audit trail
- expose reconciliation scoring via query/API endpoint
- add configurable rule weights per tenant
- support many-to-one and one-to-many reconciliation scenarios
- add explainability/audit views for reconciliation suggestions