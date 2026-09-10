# Prompt 10 — complete-call cost and Azure capacity benchmark

Run preparation date: 10 September 2026. Status: **benchmark harness complete; live measurement blocked**.

No 30-minute call, provider request, Azure deployment, load test, customer send or Teams traffic was performed. The machine-readable report leaves every unavailable cost, latency, resource and capacity result null. This is the required outcome when the named target, credentials and authorization envelope are absent.

## Delivered benchmark path

- `tests/benchmarks/sales-room/full_call_benchmark.py` is the fail-closed coordinator, analyzer and accounting test suite. It freezes fixture/rate hashes, validates authorization before invoking a target driver, enforces bounded 1/5/10/25/50 steps, rejects load after the first failed step, deduplicates stable usage events and writes immutable evidence/report directories.
- `fixture-v1.json` defines the exact 1,800-second synthetic protocol: 1,080 seconds approved presentation, 360 seconds human discussion, 180 seconds live answers and 180 seconds host activity/pauses. It includes live-generated, cached and separately labelled live-composed arms; English/Swedish; 10/30/60 percent speech duty; quiet speech, short words, overlap, noise and excluded output echo.
- `driver-contract.md` requires the deployment-specific driver to use the deployed Virtual Company API and LiveKit media path. It forbids direct provider orchestration, raw audio/provider payloads, customer data, invitation credentials and Teams routes in evidence.
- `rates-2026-09-10.json` preserves the checked UTC date, USD currency, public rate assumptions and primary sources. Its SHA-256 is `a020861be7841a32fa7375b0879ed9f49f24301efe39cd6dc7b17374c9f9a9a8`; the fixture SHA-256 is `e9156161ab5ea9bbdcec3bc2a09c949b0d9356f8d2b48c712f7c10fc8010522c`.
- `infra/browser-sales-room-benchmark/main.bicep` creates a disposable Sweden Central Windows App Service target, managed identity, Key Vault reader assignment, Log Analytics/Application Insights, one-second-capable application telemetry destination, diagnostics and a CPU stop alert. It requires a separate load-generator resource ID and an explicit true authorization parameter. Both Teams feature flags are false. It does not create a database, LiveKit project, AI account or user.

The production path emits the `VirtualCompany.Sales.BrowserRoom` meter through Azure Monitor. It records active/started sessions, received/detected/forwarded/provider-billed/generated/played/cancelled audio duration, provider tokens, cache hit/miss, narration generation outcomes, queue depth, drops, reconnects and failures. Tags describe stage, kind and outcome only; tenant, participant, user and room identifiers are excluded.

## Result and blockers

The immutable result is [report.json](../../../artifacts/sales-room-full-call-benchmark/2026-09-10-blocked-v3/report.json). The analyzed values are:

| Result | Value |
| --- | --- |
| Live-generated total cost per completed 30-minute call | null / not run |
| Cached total cost per completed 30-minute call | null / not run |
| Live-composed total cost per completed 30-minute call | null / not run |
| Incremental Azure cost per completed call | null / not run |
| Existing fixed hosting cost/allocation | null / target absent |
| CPU and memory per concurrent call | null / not run |
| Highest quality-passing tested concurrency | null / not run |
| Speech-detection metered savings at 10/30/60 percent | null / not run |
| Customer-audible p95 latency and misses | zero samples / not run |

Exact blockers recorded by the harness:

1. No named, explicitly authorized isolated Azure benchmark target and separate load generator are configured.
2. `LIVEKIT_URL`, `LIVEKIT_API_KEY` and `LIVEKIT_API_SECRET` are absent from the benchmark process.
3. Permitted synthetic host/guest test credentials are not configured.
4. No named authorization owner, maximum spend or reviewed concurrency ceiling is configured.

`OPENAI_API_KEY` is configured, but that alone does not authorize provider usage or a load test. Azure CLI sign-in exists, but no existing App Service was inferred to be an authorized target. The source inspection base was Git revision `77ec3c8dc71e4b4465c8b7c37eeaa62b92f1ad82`; the Prompt 10 work is uncommitted, and no deployment occurred, so the report correctly leaves the deployed application revision null. A live plan and its evidence must contain the exact deployed package revision and must match.

## Accounting and quality gates

Every usage item carries a stable ID, billing vendor/category, quantity/unit and either reconciled cost or a dated rate plus unit size. Identical duplicates count once; conflicting duplicates invalidate the report. All failed, retried and cancelled attempt IDs are included in the numerator, divided by quality-passing completed calls. An underlying model charge cannot be repeated when LiveKit is the billing vendor.

The report separates provider/LiveKit categories, incremental Azure resources, and existing fixed hosting with an explicit allocation assumption. It reports cached cold construction, partial revision, valid warm reuse and generation amortization at 1/10/40/80 uses. A valid warm result is blocked if it made any narration-generation request. Speech-detection savings use returned provider-billed duration and sit beside first/last-word loss, false triggers and latency.

Capacity passes only with completed quality-passing trials, one-second resource samples, no target misses, no dropped frames or restarts, no queue growth and complete drain. The p95 targets are join under 5 seconds, takeover-to-silence under 300 ms, answer start under 3 seconds and slide sync under 500 ms. The highest passing value is a tested bound on the recorded SKU, not an extrapolation.

The rate snapshot records [OpenAI's official pricing](https://developers.openai.com/api/docs/pricing), [LiveKit's official pricing](https://livekit.com/pricing), and the [Azure Retail Prices API](https://prices.azure.com/api/retail/prices). OpenAI and LiveKit public values are assumptions until invoice reconciliation. The Azure rate remains unresolved because the actual authorized SKU/resource does not exist. No microbenchmark projection is copied into the full-call fields.

## Recommendation and execution

There is no evidence-based production concurrency or call-budget default yet. Existing paid plans, quotas and production settings must remain unchanged. The prepared plan defaults to concurrency 1 as a safety gate; that is the first measurement step, not a capacity recommendation. Escalation to 5/10/25/50 is allowed only within the reviewed target, spend and concurrency limits and stops at the first resource, spend, latency or quality failure.

From the repository root, validate and prepare a fresh immutable evidence directory:

```powershell
python tests/benchmarks/sales-room/full_call_benchmark.py --self-test
python tests/benchmarks/sales-room/full_call_benchmark.py --prepare artifacts/sales-room-full-call-benchmark/<new-run>
```

Copy the generated run plan, record the exact deployed application revision, isolated target/runtime/SKU/region and separate load-generator resource ID, then add the named authorization owner and reviewed spend/concurrency limits. Supply LiveKit and synthetic participant credentials only through environment variables. Invoke the deployment-specific production-path adapter described by `driver-contract.md`:

```powershell
python tests/benchmarks/sales-room/full_call_benchmark.py --plan <reviewed-plan.json> --driver "<production-path-driver-command>" --output artifacts/sales-room-full-call-benchmark/<new-live-run>
```

The coordinator passes the frozen plan, fixture and rates as JSON on standard input, rejects stale hashes and mismatched target/revision evidence, and refuses to overwrite results. The driver is intentionally a thin deployment adapter because room seeding and authentication depend on the authorized isolated target; application behavior remains in the deployed production services.

## Verification and preservation

Seven offline tests pass for protocol shape, per-unit conversion, duplicate/conflicting usage events, cache amortization, failed-attempt cost, metered VAD savings, capacity baseline arithmetic/drain and fail-closed authorization. The Bicep template compiles. The focused API/agent/narration/VAD/telemetry and shared Teams regression selection passes 63 tests with one credential-gated live narration test skipped. The API builds as part of that run.

No Teams-specific source, configuration, package, infrastructure or historical migration was changed. Teams flags are disabled in the isolated template, and the benchmark contract prohibits Teams targets and traffic.
