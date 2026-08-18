# Guided dialogue evaluation and release gates

## What is evaluated

Evaluation scores business output only. Hidden reasoning is neither requested nor stored. Maintain a representative, de-identified fixture set for every artifact type and both text and voice transcripts. Include direct answers, corrections, ambiguity, contradictions, unsupported claims, stale versions, invalid identifiers, mixed languages, interruptions, reconnects, duplicate events, and malicious tool arguments.

Record these measures per artifact type:

| Measure | Release threshold |
| --- | ---: |
| Exact or semantically equivalent field extraction | at least 95% |
| Unsupported assumptions classified as confirmed | at most 1% |
| Invalid or inaccessible evidence accepted | 0% |
| User correction applied to the selected field | 100% |
| Required-field readiness classification | at least 99% |
| Domain validation pass after ready classification | 100% |
| Review token replay, expiry, and stale-version rejection | 100% |
| Commit maps only to the selected artifact/company | 100% |
| Duplicate text turn or provider tool call creates duplicate state | 0% |
| Voice transcript and equivalent typed text produce equivalent draft semantics | at least 95% |

## Required suites

For each artifact definition, cover:

1. Eligible and ineligible agents.
2. Empty initialization and editing an existing artifact.
3. Every field type, allowed-value constraint, size bound, and domain relationship.
4. Missing required data, explicit unknowns, assumptions, evidence, conflicts, and corrections.
5. Complete review, incomplete review, expired token, replayed token, concurrent artifact change, and idempotent commit.
6. Same-company second user, cross-company session/agent/artifact/field references, inactive membership, and capability loss.
7. Provider disabled, timeout, non-JSON output, unknown paths, too many patches, hidden-reasoning keys, and transient retry.
8. WebRTC permission denial, no-device behavior, SDP failure, call expiry, interruption, reconnect exhaustion, repeated transcription event, repeated tool call, oversized arguments, and stop cleanup.

Marketing strategy fixtures must cover current segment-version eligibility, validity dates, STP, positioning, the four Ps, evidence, success metrics, risks, and optimistic versioning. Marketing segment fixtures must cover criteria, needs, behaviors, channels, pricing, size bounds/method, economics, evidence, score dimensions, confidence, risk, and target rationale. Finance fixtures must use real finance-account IDs and the existing budget uniqueness rules. Sales fixtures must target an existing campaign and cover objective, dates, audience segment, offer, budget, activity structure, readiness, and campaign version. Support fixtures must cover the exact SLA upsert fields and first-response/resolution/risk relationships.

## Release procedure

The release candidate must pass:

- guided Domain/Application/API/Web tests;
- affected agent brief, direct chat, orchestration, Marketing, Finance, Sales, and Support tests;
- authorization and tenant-isolation suites;
- API and Web builds plus dependency architecture checks;
- localization key parity for English and Swedish;
- EF pending-model-change check and migration script inspection;
- local SQL Server restore, migrate, API start, and bounded smoke test;
- Docker SQL Server restore, the same migrations, API start, and the same smoke test;
- current Chromium, Firefox, and WebKit desktop checks plus a narrow responsive viewport;
- keyboard, focus, screen-reader naming, microphone-denial, and reduced-motion checks;
- secret/static analysis confirming that no standard provider key or transcript content reaches browser assets/logs.

Roll out text first with Realtime disabled, then enable voice for an internal tenant, then a bounded tenant cohort. Watch checkpoint latency/failures, conflicts, tool rejection, reconnects, correction rate, and review acceptance. Expansion stops automatically when a security invariant fails, inaccessible evidence is accepted, cross-tenant access occurs, duplicate commits appear, or error/latency budgets are exceeded.

## Smoke scenario

1. Start an agent operating brief workshop and answer all required sections.
2. Correct one field directly and confirm it is marked user-confirmed.
3. Refresh and resume the same session.
4. Prepare review, allow one token to expire, prepare again, and commit once.
5. Verify the selected brief changed and tools, scopes, autonomy, status, objectives, and unrelated settings did not.
6. Repeat a Marketing strategy session with valid segment-version IDs.
7. Start voice, deny microphone once, allow it, interrupt the agent, produce one finalized transcript, stop voice, and confirm tracks/call binding end.
8. Replay the transcription event/tool call and confirm no duplicate field/message/commit.
9. Attempt each session from a second company and confirm no existence disclosure.

## Release-candidate result — 2026-08-12

This result records the verification performed for the initial implementation. It is an implementation quality gate, not approval to enable Realtime voice for production tenants.

### Automated results

| Gate | Result |
| --- | --- |
| Deterministic evaluation corpus | Passed: 66 cases (6 artifact types × 11 required scenarios) |
| Guided Domain/Application/API/integration tests | Passed: 18/18, including durable lifecycle, tenant isolation, provider recovery, finalized voice metadata, bounded/idempotent sideband tools, evaluation thresholds, and retention execution |
| Dependency architecture and affected adapter tests | Passed: 38/38 |
| Finance regression project | Passed: 34/34 |
| Sales source regression project | Passed: 6/6 |
| Support grounding regression project | Passed: 5/5 |
| Guided Web and localization tests | Passed: 49/49 |
| EF model/migration drift | Passed: no pending model changes |
| Solution build | Passed: 0 errors; existing repository warnings remain outside this feature |

The corpus enforces the thresholds above by artifact and scenario and identifies failures by artifact type, scenario, and bounded measure. It contains no production records and does not request or score chain of thought.

### Browser result

Chromium verification passed at desktop width and at 390 × 844. Marketing Strategy and Segments exposed their Maya workshop entry points, the workshop route rendered its accessible service-unavailable fallback with the backend intentionally absent, and the narrow layout had no horizontal overflow. The browser state and temporary application host were cleaned up after verification.

### Database compatibility result

The two additive migrations target the repository's shared SQL Server migration assembly and use the same model and migration history for local and Docker SQL Server. The EF pending-model-change check passed, the migration snapshot compiles, and no restore script, Docker configuration, startup-DDL path, or migration-history behavior was changed. The retention execution test proved that eligible terminal session data expires while active guided work remains.

### Environment-dependent release checks still required

Before production rollout, run the documented smoke scenario against a restored local SQL Server database and a restored Docker SQL Server database, and confirm both reach `/health/ready` after applying the same migrations. This workstation's Docker runtime constraint prevents claiming those two live restore/start checks here.

Also perform the live Realtime smoke with a configured OpenAI project, supported browser microphone, and the intended project data controls: permission denial and grant, one interruption, reconnect, finalized transcript, review, conflict, commit, and replay protection. Cross-browser microphone behavior must be checked in current Chromium, Firefox, and WebKit. No live provider credential or microphone call was used for this implementation result, so model/audio quality is deliberately not claimed from the static and deterministic checks.
