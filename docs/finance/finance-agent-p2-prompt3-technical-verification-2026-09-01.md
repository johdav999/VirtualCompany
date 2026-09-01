# Finance agent P2 Prompt 3 technical verification — 2026-09-01

This record freezes the technical evidence reviewed for Finance agent P2 Prompt 3. It is engineering evidence for
human review. It is not a qualified accountant's opinion, professional approval, statutory sign-off, or filing
confirmation.

## Frozen review scope

| Item | Identity |
| --- | --- |
| Repository base revision | `ce2cf6a0fc449f1ea3f129b898db4ed93fbebdcd` |
| Prompt pack | `finance-update-p2-prompts.md`; SHA-256 `391d1f42cb470a767634bd73393cd20227a3813fba8d78e80bc0490d6a44d5de` |
| Agent contract | `FinanceCloseComplianceAgentContracts.cs`; SHA-256 `482ec71dcd8a59e2b59447c544881a9a41924b7631588a7e99d125d36945503d` |
| Read/recommendation service | `FinanceCloseComplianceAgentService.cs`; SHA-256 `ef41a75a0812fbf62bf8c8bfdd66fd1b3f9c05bb363ab449679031c2686f8647` |
| Focused proof tests | `FinanceCloseComplianceAgentToolTests.cs`; SHA-256 `e4f7c58e31d90b1e9bc6834f0ba6e99e5a274fca852471115a2a238d41e8893a` |
| Tool guide | `finance-agent-close-compliance-tools.md`; SHA-256 `aa7f114b723f99f4ad255c7e558fa0d8c5fc3e4c25c961360ddc9c332a6ace95` |

The repository contained other in-progress work. This review did not treat the dirty worktree as a signed release
revision and did not attribute unrelated changes to Prompt 3.

## Technical checkpoints

| Checkpoint | Technical status | Evidence |
| --- | --- | --- |
| Tool authority boundary | Verified | 8 reads and 4 recommendation tools; no Prompt 3 execute or download tool |
| Close blocker explanation | Verified | Period-grounded request returns readiness hash, owners, evidence hash/age, and safe next action |
| Compliance state separation | Verified | Manual evidence without acknowledgement returns `submittedOrAccepted=false` |
| Audit-package protection | Verified | Metadata only; no content, token, authorization creation, or link renewal |
| Current-version audit verification | Verified | Technical completeness requires a valid verification matching both current package and manifest checksums |
| Accountant access scope | Verified | Existing company-scoped grant and engagement queries; absent scoped IDs return not found |
| Bounded output and provenance | Verified | 100-item pages, 366-day compliance range, explicit truncation, and 2,000 returned source-ID cap with full source count |
| Final authority boundary | Verified | Lock, reopen, filing, rollover, professional approval, and statutory sign-off remain human-only |
| Focused Prompt 3 and catalogue tests | Verified | 26 passed, 0 failed, 0 skipped |
| Owning close/compliance/audit/year-end domain proof selection | Verified | 48 passed, 0 failed, 0 skipped |
| API authorization, tenant, surface, catalogue, and planner proof selection | Verified with external-lane limitation | 63 passed, 0 failed; 2 SQL Server migration tests skipped because the external SQL lane was not configured |
| Close/compliance/audit/year-end web surface proof selection | Verified | 27 passed, 0 failed, 0 skipped |
| Release builds | Verified | API and Web release builds completed with 0 errors; API retained 28 existing nullable/async warnings |

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
