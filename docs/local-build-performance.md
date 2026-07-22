# Local build performance

## Supported configurations

- `Debug`: IDE development and debugging.
- `LocalRun`: repository launch scripts and local runtime snapshots.
- `Release`: release and deployment validation.

Do not create timestamped or task-specific configuration names. Runtime isolation is provided by snapshots under `.codex-build`, not by changing MSBuild configuration or output paths.

## Build flow

`run-api.ps1` and `client.ps1` build shared projects into their stable `bin/LocalRun/net9.0` directories and their startup projects into stable `.codex-build/build-output` directories. This lets MSBuild, the C# compiler, and Razor reuse prior outputs. Separating startup outputs also bypasses assemblies left locked by legacy `dotnet run` processes. After a successful build, the launcher copies the output to a timestamped runtime snapshot and starts that copy, so a running process does not lock the next build's assemblies.

API and web builds share `.codex-build/local-build.lock`. Concurrent launch commands wait instead of compiling shared projects into the same output directories at the same time. Each launcher retains the five newest successful runtime snapshots.

The old process remains available during compilation and is stopped only after the replacement snapshot is ready.
If an older launcher or a manual command is still running directly from a stable startup build directory, the launcher stops that legacy process before compiling so its assembly locks cannot block the replacement build. Processes running from timestamped snapshots remain available during compilation.

## Baseline and measurement

Before this change, observed cold builds included approximately 70.9 seconds in `VirtualCompany.Infrastructure` and 81.5 seconds for `VirtualCompany.Web`. The web launcher used a new `OutputPath` on every run, making every launch effectively cold.

The first verified baseline after establishing the stable outputs was:

| Project | First build | Unchanged warm build |
| --- | ---: | ---: |
| API | 91.608 seconds | 5.847 seconds |
| Web | 34.867 seconds | 4.223 seconds |

The first build includes compilation invalidated by build-configuration changes. The unchanged second build is the expected restart baseline. Existing compiler and Razor warnings remain visible on a cold build and are not suppressed by these scripts.

Capture repeatable incremental timings with:

```powershell
.\measure-local-build.ps1 -Project All -Iterations 2
```

Capture intentional cold rebuilds and diagnostic binary logs with:

```powershell
.\measure-local-build.ps1 -Project All -Iterations 1 -Rebuild -BinaryLog
```

Results are written to `.codex-build/measurements`. Compare the second incremental iteration to the first; the second is the warm-build baseline.

Snapshot startup was also exercised independently of the launch scripts. The API loaded through EF Core initialization before the validation environment failed to connect to the local SQL Server. The web snapshot bound Kestrel before the restricted validation environment rejected Windows Event Log writes. Run the normal launch scripts in the interactive user session for end-to-end SQL and browser validation.
