# P11 issue ledger

| ID | Severity / flow | Finding / owner | Acceptance and regression evidence | Status |
| --- | --- | --- | --- | --- |
| P11-001 | P1 / execution | Existing coordinator converted blocked and approval-pending nonfailed workers into completed steps | `MultiAgentHandoffRetryTests` human-review/failed-handoff cases; real browser still shows parent awaiting decision | Verified |
| P11-002 | P1 / access | Protect derived contribution content through raw Work and dependency entry paths, not only new UI | Scoped integration test verifies hidden ancestors, derived IDs/content, cleared raw payload and refused cross-area delegation before lease; restricted browser/source reconciliation | Verified |
| P11-003 | P2 / F11-03 | Inherited P10 approval link used `approvalId`, while Work selects `itemId` | API assertion and actual `owning-approval.jpg` show correct pending decision selected; exact artifact/list return replayed | Verified |
| P11-004 | P2 / F11-02 | Failed/earlier versions and independent/dependent cards needed clear separation | Earlier/latest badges, sequential full-width rows/connectors, version-specific input buttons; final flow/list and mobile captures | Verified |
| P11-005 | P1 / execution | Overlapping retries could duplicate apparent contributions or release another run | Company/correlation unique persisted lease; overlap 409, material mismatch 400, stable retry/version IDs and expiration tests; isolated SQL uniqueness | Verified |

No open P0/P1 local implementation defect remains. Named human release acceptance, deployed tenant migration/replay and any real provider delivery are separate unverified gates. Earlier P01–P10 external/physical/statutory gates remain recorded in their original packets; P11 does not close them.
