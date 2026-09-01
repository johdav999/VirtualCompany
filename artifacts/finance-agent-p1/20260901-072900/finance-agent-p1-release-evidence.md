# Finance natural-language P1 release evidence

- Generated UTC: 2026-09-01T05:42:35.7456429Z
- Repository revision: 52b556f643e3473b843efcde9430b36e46119b34
- Dirty working tree: True
- Working-tree manifest checksum: ce12e8890a3bcce87518df72b2f9dc2dcc67b6fd9a4b6f0d8d8a13e3433bdbb1
- Evaluation pack: finance-natural-language-safety-v1 (dc1c5c6f26c9676c26195a7ef551ab6b1de6cc2ec2eb8a6c83b651d7de7538dc)
- Manifest core checksum: 65afd4e1ded17416c0c4bde0d4be98a8dcc24a315bb451f44aa208cfba1a8cff
- P1 decision: no_go
- Technical classification: technical_verification_incomplete
- Human accounting review: human_accountant_review_pending

| Checkpoint | Outcome | Passed | Failed | Skipped | Evidence |
| --- | --- | ---: | ---: | ---: | --- |
| fixed-safety-evaluation | passed | 46 | 0 | 0 | artifacts\finance-agent-p1\20260901-072900\fixed-safety-evaluation.log |
| authenticated-ui-contracts | passed | 24 | 0 | 0 | artifacts\finance-agent-p1\20260901-072900\authenticated-ui-contracts.log |
| p0-safety-gates | failed | 148 | 0 | 1 | artifacts\finance-agent-p1\20260901-072900\p0-safety-gates.log |
| release-build | passed | 0 | 0 | 0 | artifacts\finance-agent-p1\20260901-072900\release-build.log |
| hermetic-matrix | passed | 3414 | 0 | 0 | artifacts\finance-agent-p1\20260901-072900\hermetic-matrix.log |
| ef-pending-model | passed | 0 | 0 | 0 | artifacts\finance-agent-p1\20260901-072900\ef-pending-model.log |
| sqlserver-finance-lanes | prerequisite_missing | 0 | 0 | 0 |  |
| swedish-accounting-evidence-verifier | passed | 0 | 0 | 0 | artifacts\finance-agent-p1\20260901-072900\swedish-accounting-evidence-verifier.log |
| authenticated-browser-uat-and-restart-recovery | failed | 0 | 0 | 0 | .codex-build\uat\finance-agent-p1-release-2026-09-01\uat-evidence.json |
| optional-live-provider-evaluation | not_applicable | 0 | 0 | 0 |  |

This is engineering evidence only; it is not statutory approval or a signed professional opinion.
