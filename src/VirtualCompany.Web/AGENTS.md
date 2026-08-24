# VirtualCompany.Web operational guidance

These instructions apply when building, starting, or verifying
`VirtualCompany.Web`. Product design requirements remain solely in
`/docs/design.md` and are not repeated here.

## Local Web verification

- Never use a nested detached command such as `cmd.exe /c start ...`, especially
  when launch, output redirection, and listener polling are combined in one
  command. This can leave an untracked process, hold build artifacts open, and
  cause the agent run to hang.
- Check whether the expected Web port is already listening before starting
  another host. Reuse the existing host when it belongs to this repository.
- Prefer build and focused component or integration tests when runtime browser
  verification is not required.
- For normal interactive startup, use `.\client.ps1` in its own terminal and
  allow its build to finish before starting another build.
- When an automated runtime host is required, start `dotnet` directly with
  PowerShell `Start-Process -PassThru -WindowStyle Hidden`. Do not wrap it in
  `cmd.exe`, use `start`, or combine startup with a polling loop in the same
  command.
- Record the returned process ID immediately. Poll health in a separate bounded
  command with a short per-request timeout and a fixed overall deadline of at
  most 30 seconds.
- If startup fails or verification finishes, stop only the recorded process ID.
  Never stop every `dotnet` process.
- Do not redirect a detached host's output through nested shell syntax. If logs
  are required, use a repository run script with explicit log handling or run
  the host in a managed foreground terminal.
- If the bounded health check fails, inspect available process or build output
  and continue with static verification. Do not repeat detached launch attempts.
