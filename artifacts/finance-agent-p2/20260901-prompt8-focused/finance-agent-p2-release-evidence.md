# Finance agent P2 release evidence

- Generated UTC: 2026-09-01T14:36:43.1755724Z
- Repository revision: ce2cf6a0fc449f1ea3f129b898db4ed93fbebdcd
- Dirty working tree: True
- Working-tree manifest checksum: 609c41175a72c95e45588a5fd64a869474446b51873de8b136c5babc5e286b06
- Catalogue version: finance-agent-coverage-v1
- P2 decision: no_go
- Classification: technical_or_human_verification_incomplete

| Checkpoint | Outcome | Passed | Failed | Skipped | Evidence / detail |
| --- | --- | ---: | ---: | ---: | --- |
| catalogue-tools-orchestration | passed | 87 | 0 | 0 | artifacts\finance-agent-p2\20260901-prompt8-focused\catalogue-tools-orchestration.log |
| coverage-workbench-localization-ui | passed | 26 | 0 | 0 | artifacts\finance-agent-p2\20260901-prompt8-focused\coverage-workbench-localization-ui.log |
| release-build | failed | 0 | 0 | 0 | artifacts\finance-agent-p2\20260901-prompt8-focused\release-build.log |
| hermetic-matrix | not_run | 0 | 0 | 0 | FocusedOnly was selected; this mandatory release checkpoint was not executed. |
| ef-pending-model | not_run | 0 | 0 | 0 | FocusedOnly was selected; this mandatory release checkpoint was not executed. |
| sqlserver-lanes | not_run | 0 | 0 | 0 | FocusedOnly was selected; this mandatory release checkpoint was not executed. |
| small-capacity-and-audit-package | not_run | 0 | 0 | 0 | FocusedOnly was selected; this mandatory release checkpoint was not executed. |
| p0-release-gate | prerequisite_missing | 0 | 0 | 0 | A current revision-bound manifest was not supplied. |
| p1-release-gate | prerequisite_missing | 0 | 0 | 0 | A current revision-bound manifest was not supplied. |
| sql-object-recovery | prerequisite_missing | 0 | 0 | 0 | Coordinated SQL/object recovery evidence is missing. |
| authenticated-en-sv-browser-uat | prerequisite_missing | 0 | 0 | 0 | Authenticated EN/SV desktop, narrow, accessibility, and recovery UAT evidence is missing. |
| swedish-accounting-technical-verification | passed | 0 | 0 | 0 | artifacts\finance-agent-p2\20260901-prompt8-focused\swedish-accounting-technical-verification.log |
| qualified-swedish-accountant-approval | human_approval_pending | 0 | 0 | 0 | Attributable approval for the exact working-tree checksum is missing. |
| external-provider-scope-approval | human_approval_pending | 0 | 0 | 0 | Attributable approval for the exact working-tree checksum is missing. |

Tool counts are not presented as percentage completion. Any failed safety checkpoint remains a release blocker.

This is engineering evidence only; it is not statutory approval or a signed professional opinion.
