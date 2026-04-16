# Executive Cockpit Performance And Observability

TASK-3.5.4 uses low-cardinality metrics and structured logs for the executive cockpit endpoint, cache, invalidation, and widget refresh paths. Do not add raw tenant ids or user ids as metric labels; tenant-specific investigation should use correlated logs and traces.

## Metrics

| Metric | Unit | Dimensions | Purpose |
| --- | --- | --- | --- |
| `executive_cockpit_endpoint_latency_ms` | ms | `endpoint`, `status`, `cache_outcome`, `widget` | Derive endpoint p95 for dashboard, KPI, and widget calls. |
| `executive_cockpit_endpoint_payload_bytes` | bytes | `endpoint`, `status`, `cache_outcome`, `widget` | Detect payload growth that can affect render and network latency. |
| `executive_cockpit_cache_hits` | count | `area`, `outcome` | Cache effectiveness numerator. |
| `executive_cockpit_cache_misses` | count | `area`, `outcome` | Cache effectiveness denominator and stale namespace detection. |
| `executive_cockpit_cache_sets` | count | `area` | Confirms cache population after misses. |
| `executive_cockpit_cache_lookup_duration_ms` | ms | `area`, `outcome` | Redis lookup cost and cache error impact. |
| `executive_cockpit_cache_invalidations` | count | `trigger`, `entity_type` | Invalidation volume by low-cardinality trigger. |
| `executive_cockpit_cache_invalidation_lag_ms` | ms | `trigger`, `entity_type` | Update-to-invalidation lag. |
| `executive_cockpit_invalidation_duration_ms` | ms | `trigger` | Outbox invalidation handler execution time. |
| `executive_cockpit_invalidation_lag_ms` | ms | `trigger` | Outbox-created-to-processed lag. |
| `executive_cockpit_widget_fetch_ms` | ms | `widget`, `status` | Blazor widget API fetch timing for partial refresh. |
| `executive_cockpit_widget_render_ms` | ms | `widget`, `status` | Blazor render completion timing after widget payload application. |

## Example PromQL

Endpoint p95:

```promql
histogram_quantile(
  0.95,
  sum by (le, endpoint, cache_outcome) (
    rate(executive_cockpit_endpoint_latency_ms_bucket{endpoint=~"dashboard|kpis|widget"}[5m])
  )
)
```

Cache hit rate:

```promql
sum(rate(executive_cockpit_cache_hits[5m]))
/
(sum(rate(executive_cockpit_cache_hits[5m])) + sum(rate(executive_cockpit_cache_misses[5m])))
```

Invalidation lag p95:

```promql
histogram_quantile(
  0.95,
  sum by (le, trigger) (
    rate(executive_cockpit_cache_invalidation_lag_ms_bucket[5m])
  )
)
```

Widget render p95:

```promql
histogram_quantile(
  0.95,
  sum by (le, widget) (
    rate(executive_cockpit_widget_render_ms_bucket[5m])
  )
)
```

## Performance Harness

The staging-style performance checks are in `tests/VirtualCompany.Api.Tests/ExecutiveCockpitPerformanceTests.cs`. They are no-op unless `EXECUTIVE_COCKPIT_PERF_BASE_URL` and `EXECUTIVE_COCKPIT_PERF_COMPANY_ID` are set.

Run:

```bash
EXECUTIVE_COCKPIT_PERF_BASE_URL=https://staging-api.example.com \
EXECUTIVE_COCKPIT_PERF_COMPANY_ID=00000000-0000-0000-0000-000000000000 \
EXECUTIVE_COCKPIT_PERF_SAMPLE_COUNT=50 \
dotnet test tests/VirtualCompany.Api.Tests/VirtualCompany.Api.Tests.csproj --filter ExecutiveCockpitPerformanceTests
```

Optional authentication inputs are `EXECUTIVE_COCKPIT_PERF_BEARER_TOKEN` for bearer auth or `EXECUTIVE_COCKPIT_PERF_DEV_SUBJECT`, `EXECUTIVE_COCKPIT_PERF_DEV_EMAIL`, and `EXECUTIVE_COCKPIT_PERF_DEV_DISPLAY_NAME` for a staging dev-header profile.

The cached dashboard and widget p95 target is 2.5 seconds. Invalidation should be visible within 60 seconds. If an environment exposes a safe mutation probe, set `EXECUTIVE_COCKPIT_PERF_INVALIDATION_PROBE_PATH` to a company-scoped POST endpoint that triggers a task, workflow, approval, or agent-status update.