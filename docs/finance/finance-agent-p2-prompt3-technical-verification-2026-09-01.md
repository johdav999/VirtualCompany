# Finance agent P2 Prompt 3 technical verification — 2026-09-01

This record freezes the technical evidence reviewed for Finance agent P2 Prompt 3. It is engineering evidence for
human review. It is not a qualified accountant's opinion, professional approval, statutory sign-off, or filing
confirmation.

## Frozen review scope

| Item | Identity |
| --- | --- |
| Repository base revision | `52b556f643e3473b843efcde9430b36e46119b34` |
| Prompt pack | `finance-update-p2-prompts.md`; SHA-256 `391d1f42cb470a767634bd73393cd20227a3813fba8d78e80bc0490d6a44d5de` |
| Agent contract | `FinanceCloseComplianceAgentContracts.cs`; SHA-256 `0cfb8f0562ebcd8328b54005fb18e5fad70f7a0cc3f43d498c0abcf7487fecfc` |
| Read/recommendation service | `FinanceCloseComplianceAgentService.cs`; SHA-256 `01ed49bf57f1c5ce997bf7105c25e61f24ed8205ed69e2169be2f8a593601d11` |
| Focused proof tests | `FinanceCloseComplianceAgentToolTests.cs`; SHA-256 `c4512b7e1b83a6230ee7226819e476abaafd587779a4573a880666480d9feef2` |
| Tool guide | `finance-agent-close-compliance-tools.md`; SHA-256 `52ae4e8907b533c5a6e2d426bd9f44903db98cec38f3e590aa808ad32d8c6f3d` |

The repository contained other in-progress work. This review did not treat the dirty worktree as a signed release
revision and did not attribute unrelated changes to Prompt 3.

## Technical checkpoints

| Checkpoint | Technical status | Evidence |
| --- | --- | --- |
| Tool authority boundary | Verified | 8 reads and 4 recommendation tools; no Prompt 3 execute or download tool |
| Close blocker explanation | Verified | Period-grounded request returns readiness hash, owners, evidence hash/age, and safe next action |
| Compliance state separation | Verified | Manual evidence without acknowledgement returns `submittedOrAccepted=false` |
| Audit-package protection | Verified | Metadata only; no content, token, authorization creation, or link renewal |
| Accountant access scope | Verified | Existing company-scoped grant and engagement queries; absent scoped IDs return not found |
| Final authority boundary | Verified | Lock, reopen, filing, rollover, professional approval, and statutory sign-off remain human-only |
| Focused Prompt 3 and catalogue tests | Verified | 16 passed, 0 failed, 0 skipped |
| Prompt 4 Finance proof selection | Verified | 51 passed, 0 failed, 0 skipped |
| Prompt 4 API/authorization/planner proof selection | Verified with external-lane limitation | 69 passed, 0 failed, 2 SQL Server migration tests skipped because the external SQL lane was not configured |

## Swedish accounting technical verification

The deterministic Swedish accounting evidence verifier returned:

- release classification: `technically_verified_for_human_review`;
- technical checks: 13 verified, 0 failed;
- human decision: `pending`;
- statutory approval: `false`.

It verified the frozen BAS workbook/catalogue identity and hashes, 1,285 source records, 1,282 unique codes, 1,283
code/name variants, duplicate code `2087`, 26 K2-restricted accounts, 810 subaccounts with no broken parent links,
no automatic class-8 semantic suggestions, manifest hashes, enabled domestic VAT fixture coverage, approval-scope
hashes, and retained `review_pending` gates.

## Release classification

`technically_verified_for_human_review`

Human review remains pending. Statutory approval remains false. The unconfigured SQL Server migration lane must be
run in its dedicated environment if this work is included in a release that requires that external proof.
