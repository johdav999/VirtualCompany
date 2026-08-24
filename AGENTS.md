# Virtual Company repository guidance

## Instruction routing

This file contains repository-wide operating rules. More specific guidance is
kept near the work it governs so unrelated tasks do not carry it as context.

- Before changing production code under `/src`, read and follow `/src/AGENTS.md`
  and any nearer `AGENTS.md` on the path to the files being changed.
- Before changing tests under `/tests`, read and follow `/tests/AGENTS.md`.
- Before creating implementation prompts, prompt packs, or phased delivery
  documents, read and follow `/docs/AGENTS.md`, even when the output file will
  be stored outside `/docs`.
- When a task spans multiple areas, read every relevant scoped `AGENTS.md`.
- Scoped files extend this file. In a conflict, the instruction closest to the
  files being changed wins.

The authoritative technical rules live in `/docs/architecture-rules.md`, and
the authoritative product design rules live in `/docs/design.md`. `AGENTS.md`
files should route work to those sources instead of duplicating their content.

## Multi-prompt implementation persistence

When implementing a sequence of prompts or a multi-part implementation plan:

- Do not stop at intermediate checkpoints.
- Do not stop after only reporting build, test, or analysis status.
- Continue through the ordered prompts until the full requested implementation
  sequence is complete, genuinely blocked, or the user explicitly asks to pause
  or stop.
- If a build or test fails, diagnose and fix the failure when it is in scope for
  the current implementation sequence, then continue.
- Only stop when further progress requires missing external credentials,
  unavailable services, an unsafe or destructive action without approval, or a
  user decision that cannot reasonably be inferred.

## Sandbox efficiency

- Use the normal workspace sandbox for repository-local reads and searches.
- Do not request escalated permissions for `rg`, `Get-Content`, `git status`,
  `git diff`, or `git log`.
- Try repository-local builds and focused tests in the normal workspace sandbox
  first. Escalate only after a concrete sandbox failure, and state the specific
  boundary requiring it.
- Reserve escalation for process lifecycle, databases, network or external
  services, writes outside the workspace, or a verified sandbox limitation.

## Execution discipline

For large implementation tasks, use this default workflow:

1. Perform one broad repository inspection.
2. Identify the relevant files and implementation boundaries.
3. Implement the requested change in one coherent pass.
4. Run focused tests for the affected area.
5. Run one full build or broader validation when appropriate.

Do not repeat repository-wide searches, unchanged test runs, or full builds
unless:

- new evidence changes the working diagnosis
- implementation changes invalidate earlier results
- a test or build failure requires another investigation
- final verification requires rerunning the command

Quality and correctness remain the stopping criteria. These limits prevent
redundant work; they must not be used to leave required implementation or
verification incomplete.

## Repository inspection

- Use `rg` or `rg --files` for the initial repository scan.
- Batch related searches and file reads whenever practical.
- Gather relevant paths with one targeted search.
- Search for related symbols or patterns together.
- Read related files in the same inspection step.
- Avoid reopening unchanged files or making many small searches when one bounded
  query provides the same evidence.
- Prefer targeted follow-up searches after the initial scan. Repeat broad
  inspection only when new evidence shows that the original scope was incomplete.
