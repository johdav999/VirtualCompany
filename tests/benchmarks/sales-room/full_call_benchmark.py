"""Fail-closed full browser Sales-room benchmark coordinator and analyzer.

The driver named in an authorized run must exercise the deployed Virtual Company
API and its LiveKit rooms. This module owns the frozen protocol, spend/load gates,
evidence validation and accounting; it does not implement another agent stack.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import re
import shlex
import subprocess
import sys
import unittest

ROOT = Path(__file__).resolve().parents[3]
FIXTURE = Path(__file__).with_name("fixture-v1.json")
RATES = Path(__file__).with_name("rates-2026-09-10.json")
ARMS = ("live_generated", "cached", "live_composed")
POLICIES = ("local_vad", "continuous_benchmark_only")
DUTIES = (10, 30, 60)
LANGUAGES = ("en", "sv")
TARGETS_MS = {
    "join": 5000,
    "takeover_to_silence": 300,
    "answer_start": 3000,
    "slide_sync": 500,
}


def load(path: Path):
    return json.loads(path.read_text(encoding="utf-8"))


def canonical(value) -> bytes:
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False).encode()


def digest(value) -> str:
    return hashlib.sha256(canonical(value)).hexdigest()


def percentile(values, percent):
    if not values:
        return None
    ordered = sorted(values)
    return ordered[max(0, math.ceil(percent / 100 * len(ordered)) - 1)]


def validate_fixture(fixture):
    if fixture.get("schema_version") != "1.0" or fixture.get("duration_seconds") != 1800:
        raise ValueError("fixture must be the versioned 30-minute protocol")
    totals = fixture.get("category_seconds", {})
    if totals != {"approved_presentation": 1080, "human_discussion": 360,
                  "live_answers": 180, "host_activity_and_pauses": 180}:
        raise ValueError("fixture category durations must be 18/6/3/3 minutes")
    if tuple(fixture.get("languages", [])) != LANGUAGES:
        raise ValueError("English and Swedish fixtures are required")
    scenarios = fixture.get("speech_detection_scenarios", [])
    if sorted(x.get("duty_cycle_percent") for x in scenarios) != list(DUTIES):
        raise ValueError("10/30/60 percent speech-duty fixtures are required")
    required = {"quiet_speech", "short_words", "overlap", "background_noise", "output_echo"}
    if any(not required.issubset(set(x.get("features", []))) for x in scenarios):
        raise ValueError("every speech fixture must cover the required acoustic cases")
    return digest(fixture)


def validate_rates(rates):
    if rates.get("currency") != "USD" or not rates.get("checked_utc"):
        raise ValueError("dated USD rate evidence is required")
    sources = rates.get("sources", [])
    if not sources or any(not x.get("url", "").startswith("https://") for x in sources):
        raise ValueError("primary rate-card sources are required")
    return digest(rates)


def deduplicate_usage(events):
    unique = {}
    for event in events:
        event_id = event.get("event_id")
        if not event_id:
            raise ValueError("usage event_id is required")
        quantity = event.get("quantity")
        unit_size = event.get("unit_size", 1)
        rate = event.get("unit_rate_usd")
        actual = event.get("cost_usd")
        if quantity is None or quantity < 0:
            raise ValueError("non-negative usage quantity is required")
        if not isinstance(unit_size, (int, float)) or unit_size <= 0:
            raise ValueError("positive usage unit_size is required")
        if actual is None:
            if rate is None or rate < 0:
                raise ValueError("actual cost or a non-negative rate is required")
            actual = quantity / unit_size * rate
        if actual < 0:
            raise ValueError("usage cost cannot be negative")
        normalized = dict(event)
        normalized["cost_usd"] = actual
        previous = unique.get(event_id)
        if previous is not None:
            if canonical(previous) != canonical(normalized):
                raise ValueError("conflicting duplicate usage event")
            continue
        unique[event_id] = normalized
    return unique


def latency_summary(trials):
    result = {}
    for name, target in TARGETS_MS.items():
        values = [value for trial in trials for value in trial.get("latency_ms", {}).get(name, [])
                  if isinstance(value, (int, float)) and value >= 0]
        result[name] = {
            "samples": len(values),
            "p95_ms": percentile(values, 95),
            "target_ms": target,
            "misses": sum(x > target for x in values),
        }
    return result


def step_capacity(steps):
    passing = []
    resources = []
    for step in sorted(steps, key=lambda x: x.get("concurrency", 0)):
        concurrency = step.get("concurrency", 0)
        active_seconds = step.get("integrated_active_call_seconds", 0)
        baseline_cpu = step.get("matched_baseline_cpu_seconds")
        loaded_cpu = step.get("loaded_cpu_seconds")
        baseline_memory = step.get("matched_baseline_working_set_bytes")
        loaded_memory = step.get("peak_working_set_bytes")
        cpu_per_call = (None if not active_seconds or baseline_cpu is None or loaded_cpu is None
                        else max(0, loaded_cpu - baseline_cpu) / active_seconds)
        memory_per_call = (None if not concurrency or baseline_memory is None or loaded_memory is None
                           else max(0, loaded_memory - baseline_memory) / concurrency)
        trials = step.get("trials", [])
        latency = latency_summary(trials)
        complete = bool(trials) and all(x.get("completed") and x.get("quality_passed") for x in trials)
        stable = (step.get("drain_complete") is True and step.get("queue_end", 0) <= step.get("queue_start", 0)
                  and step.get("dropped_frames", 0) == 0 and step.get("restarts", 0) == 0
                  and all(x["samples"] > 0 and x["misses"] == 0 for x in latency.values()))
        passed = complete and stable
        if passed:
            passing.append(concurrency)
        resources.append({
            "concurrency": concurrency,
            "passed": passed,
            "incremental_cpu_core_seconds_per_active_call_second": cpu_per_call,
            "incremental_peak_working_set_bytes_per_call": memory_per_call,
            "allocated_bytes": step.get("allocated_bytes"),
            "network_bytes": step.get("network_bytes"),
            "storage_bytes": step.get("storage_bytes"),
            "storage_operations": step.get("storage_operations"),
            "instances": step.get("instances"),
            "scale_events": step.get("scale_events", []),
            "queue_peak": step.get("queue_peak"),
            "latency": latency,
        })
    return (max(passing) if passing else None), resources


def analyze(evidence, fixture, rates):
    fixture_hash = validate_fixture(fixture)
    rates_hash = validate_rates(rates)
    blockers = list(evidence.get("blockers", []))
    if evidence.get("fixture_sha256") != fixture_hash:
        blockers.append("fixture hash is absent or does not match fixture-v1.json")
    if evidence.get("rates_sha256") != rates_hash:
        blockers.append("rate-card hash is absent or does not match the dated snapshot")
    events = deduplicate_usage(evidence.get("usage_events", []))
    trials = evidence.get("trials", [])
    steps = evidence.get("capacity_steps", [])
    if trials:
        revision = evidence.get("application_revision", "")
        if not re.fullmatch(r"[0-9a-fA-F]{40,64}", revision):
            blockers.append("exact application revision is absent or invalid")
        target = evidence.get("target", {})
        required_target = ("name", "resource_id", "region", "runtime", "instance_sku",
                           "load_generator_resource_id")
        if any(not target.get(key) for key in required_target):
            blockers.append("actual target identity, region, runtime, SKU and load generator are required")
        if target.get("resource_id") == target.get("load_generator_resource_id"):
            blockers.append("load generator must use a separate resource")
        if not isinstance(evidence.get("fixed_hosting_cost_usd"), (int, float)):
            blockers.append("existing fixed hosting cost is absent")
        if not evidence.get("fixed_cost_allocation_assumption"):
            blockers.append("fixed-cost allocation assumption is absent")
        if evidence.get("invoice_reconciled") is not True and not evidence.get("unresolved_estimates"):
            blockers.append("unreconciled pricing must be labelled as an unresolved estimate")
        required_trial = {
            "arm", "language", "input_policy", "duty_cycle_percent", "completed", "quality_passed",
            "fixture_audio_sha256", "received_audio_ms", "detected_audio_ms", "forwarded_audio_ms",
            "provider_billed_audio_ms", "generated_output_ms", "played_output_ms",
            "cancelled_output_ms", "cache_hit", "narration_generation_requests", "retries",
            "failures", "usage_event_ids", "latency_ms",
        }
        for trial in trials:
            missing = required_trial.difference(trial)
            if missing:
                blockers.append(f"trial evidence is incomplete: {','.join(sorted(missing))}")
            audio_hashes = trial.get("fixture_audio_sha256", {})
            if not audio_hashes or any(not re.fullmatch(r"[0-9a-fA-F]{64}", value or "")
                                       for value in audio_hashes.values()):
                blockers.append("trial fixture audio hashes are absent or invalid")
        for arm in ARMS:
            if not any(x.get("arm") == arm for x in trials):
                blockers.append(f"{arm} arm is absent")
        for language in LANGUAGES:
            for policy in POLICIES:
                for duty in DUTIES:
                    if not any(x.get("language") == language and x.get("input_policy") == policy and
                               x.get("duty_cycle_percent") == duty for x in trials):
                        blockers.append(f"speech matrix is missing {language}/{policy}/{duty}")
        phases = {x.get("cache_phase") for x in trials if x.get("arm") == "cached"}
        if not {"cold", "warm", "partial_revision"}.issubset(phases):
            blockers.append("cached arm must include cold, warm and partial revision")
        if not evidence.get("cleanup_complete"):
            blockers.append("benchmark cleanup/drain is incomplete")
    required_step = {
        "concurrency", "sample_interval_seconds", "matched_baseline_cpu_seconds", "loaded_cpu_seconds",
        "integrated_active_call_seconds", "matched_baseline_working_set_bytes", "peak_working_set_bytes",
        "allocated_bytes", "network_bytes", "storage_bytes", "storage_operations", "instances",
        "scale_events", "queue_start", "queue_peak", "queue_end", "dropped_frames", "restarts",
        "drain_complete", "incremental_azure_cost_usd", "trials",
    }
    for step in steps:
        missing = required_step.difference(step)
        if missing:
            blockers.append(f"capacity evidence is incomplete: {','.join(sorted(missing))}")
    tested = sorted({x.get("concurrency") for x in steps if isinstance(x.get("concurrency"), int)})
    if steps and (not tested or tested[0] != 1):
        blockers.append("capacity sequence must start at concurrency 1")
    failed_at = min((x.get("concurrency") for x in steps if not step_capacity([x])[0]), default=None)
    if failed_at is not None and any(x > failed_at for x in tested):
        blockers.append("load continued after a failed quality/capacity step")
    if any(x.get("sample_interval_seconds") != 1 for x in steps):
        blockers.append("capacity samples must use one-second intervals")
    sustainable, resources = step_capacity(steps)
    arms = {}
    for arm in ARMS:
        arm_trials = [x for x in trials if x.get("arm") == arm]
        completed = [x for x in arm_trials if x.get("completed") and x.get("quality_passed")]
        ids = {event_id for x in arm_trials for event_id in x.get("usage_event_ids", [])}
        missing_ids = ids.difference(events)
        if missing_ids:
            blockers.append(f"{arm} refers to unknown usage events")
        provider = sum(events[x]["cost_usd"] for x in ids.intersection(events))
        azure_incremental = sum(x.get("incremental_azure_cost_usd", 0) for x in steps if x.get("arm") == arm)
        full = None if not completed else (provider + azure_incremental) / len(completed)
        arms[arm] = {
            "attempted_calls": len(arm_trials),
            "quality_passing_completed_calls": len(completed),
            "all_attempt_provider_cost_usd": provider,
            "incremental_azure_cost_usd": azure_incremental,
            "total_cost_per_completed_30_minute_call_usd": full,
            "latency": latency_summary(arm_trials),
        }
    warm = [x for x in trials if x.get("arm") == "cached" and x.get("cache_phase") == "warm"
            and x.get("completed") and x.get("quality_passed")]
    if any(x.get("narration_generation_requests") != 0 for x in warm):
        blockers.append("valid warm cache hit made a narration-generation request")
    cold_ids = {x for t in trials if t.get("arm") == "cached" and
                t.get("cache_phase") in ("cold", "partial_revision")
                for x in t.get("narration_generation_usage_event_ids", [])}
    cold_generation = sum(events[x]["cost_usd"] for x in cold_ids if x in events)
    warm_marginal = None if not warm else sum(
        events[x]["cost_usd"] for t in warm for x in t.get("usage_event_ids", []) if x in events) / len(warm)
    cache = {
        "cold_and_revision_generation_cost_usd": cold_generation if trials else None,
        "warm_marginal_cost_per_call_usd": warm_marginal,
        "amortized_generation_cost_usd": {str(n): (cold_generation / n if trials else None)
                                           for n in (1, 10, 40, 80)},
    }
    speech = {}
    for duty in DUTIES:
        rows = {}
        for policy in POLICIES:
            selected = [x for x in trials if x.get("input_policy") == policy and x.get("duty_cycle_percent") == duty]
            billed = sum(x.get("provider_billed_audio_ms", 0) for x in selected)
            forwarded = sum(x.get("forwarded_audio_ms", 0) for x in selected)
            received = sum(x.get("received_audio_ms", 0) for x in selected)
            rows[policy] = {"calls": len(selected), "received_ms": received, "forwarded_ms": forwarded,
                            "provider_billed_ms": billed,
                            "first_word_loss_count": sum(x.get("first_word_loss_count", 0) for x in selected),
                            "last_word_loss_count": sum(x.get("last_word_loss_count", 0) for x in selected),
                            "false_triggers": sum(x.get("false_triggers", 0) for x in selected)}
        continuous = rows["continuous_benchmark_only"]["provider_billed_ms"]
        vad = rows["local_vad"]["provider_billed_ms"]
        rows["metered_billed_audio_savings_percent"] = None if not continuous else (continuous - vad) * 100 / continuous
        speech[str(duty)] = rows
    cost_breakdown = {}
    for event in events.values():
        key = f"{event.get('vendor', 'unknown')}:{event.get('category', 'unknown')}"
        cost_breakdown[key] = cost_breakdown.get(key, 0) + event["cost_usd"]
    measured = bool(trials and steps and all(arms[x]["quality_passing_completed_calls"] for x in ARMS)
                    and sustainable is not None and not blockers)
    return {
        "schema_version": "1.0",
        "status": "measured" if measured else "blocked",
        "blockers": sorted(set(blockers)),
        "application_revision": evidence.get("application_revision"),
        "target": evidence.get("target"),
        "fixture_sha256": fixture_hash,
        "rates_sha256": rates_hash,
        "pricing": {"checked_utc": rates["checked_utc"], "currency": rates["currency"],
                    "region": evidence.get("target", {}).get("region") if evidence.get("target") else None,
                    "sources": rates["sources"], "invoice_reconciled": evidence.get("invoice_reconciled", False)},
        "arms": arms,
        "cache": cache,
        "speech_detection": speech,
        "highest_quality_passing_tested_concurrency": sustainable,
        "resources": resources,
        "cost_by_vendor_and_category_usd": cost_breakdown,
        "fixed_hosting_cost_usd": evidence.get("fixed_hosting_cost_usd"),
        "fixed_cost_allocation_assumption": evidence.get("fixed_cost_allocation_assumption"),
        "unresolved_estimates": evidence.get("unresolved_estimates", []),
        "teams_traffic": "prohibited",
    }


def validate_authorization(plan):
    auth = plan.get("authorization", {})
    target = plan.get("target", {})
    if auth.get("authorized") is not True or target.get("isolated") is not True:
        raise ValueError("an explicitly authorized isolated target is required")
    if not auth.get("authorized_by"):
        raise ValueError("the load/spend authorization owner is required")
    if not target.get("name") or not target.get("region") or not target.get("resource_id"):
        raise ValueError("named target, region and resource ID are required")
    if (not target.get("load_generator_resource_id") or
            target.get("load_generator_resource_id") == target.get("resource_id")):
        raise ValueError("a separate load-generator resource ID is required")
    ceiling = auth.get("max_concurrency")
    spend = auth.get("max_spend_usd")
    if not isinstance(ceiling, int) or ceiling not in (1, 5, 10, 25, 50):
        raise ValueError("max_concurrency must be an authorized benchmark step")
    if not isinstance(spend, (int, float)) or spend <= 0:
        raise ValueError("a positive spend envelope is required")
    if plan.get("teams_traffic") is not False:
        raise ValueError("Teams traffic must be explicitly disabled")
    if plan.get("steps") != [1, 5, 10, 25, 50]:
        raise ValueError("benchmark steps must be exactly 1/5/10/25/50")
    if not re.fullmatch(r"[0-9a-fA-F]{40,64}", plan.get("application_revision", "")):
        raise ValueError("an exact application revision is required")
    for name in plan.get("required_environment", []):
        if not os.environ.get(name):
            raise ValueError(f"required environment variable is absent: {name}")


def prepare(output: Path):
    if output.exists():
        raise FileExistsError("evidence directories are immutable; choose a new output")
    fixture, rates = load(FIXTURE), load(RATES)
    fixture_hash, rates_hash = validate_fixture(fixture), validate_rates(rates)
    output.mkdir(parents=True)
    plan = {
        "schema_version": "1.0",
        "application_revision": None,
        "fixture_sha256": fixture_hash,
        "rates_sha256": rates_hash,
        "target": {"name": None, "resource_id": None, "region": "swedencentral", "isolated": False,
                   "runtime": None, "instance_sku": None, "load_generator_resource_id": None},
        "authorization": {"authorized": False, "authorized_by": None, "max_spend_usd": None, "max_concurrency": 1},
        "required_environment": ["LIVEKIT_URL", "LIVEKIT_API_KEY", "LIVEKIT_API_SECRET", "OPENAI_API_KEY",
                                 "VC_BENCHMARK_HOST_BEARER", "VC_BENCHMARK_GUEST_TOKEN"],
        "steps": [1, 5, 10, 25, 50],
        "paired_low_concurrency_first": True,
        "stop_on_any_quality_miss": True,
        "teams_traffic": False,
        "driver_contract": "driver-contract.md",
    }
    (output / "run-plan.json").write_text(json.dumps(plan, indent=2), encoding="utf-8")
    blocked = {"fixture_sha256": fixture_hash, "rates_sha256": rates_hash, "application_revision": None,
               "target": None, "blockers": [
                   "named authorized isolated Azure target is not configured",
                   "LiveKit credentials are not configured",
                   "permitted synthetic test participant credentials are not configured",
                   "explicit load/spend envelope is not configured"],
               "usage_events": [], "trials": [], "capacity_steps": [],
               "unresolved_estimates": ["Azure SKU/rate", "LiveKit plan allowance and invoice usage",
                                        "provider invoice reconciliation"]}
    (output / "blocked-evidence.json").write_text(json.dumps(blocked, indent=2), encoding="utf-8")
    report = analyze(blocked, fixture, rates)
    (output / "report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    return report


def run_driver(plan_path: Path, driver: str, output: Path):
    plan = load(plan_path)
    validate_authorization(plan)
    if output.exists():
        raise FileExistsError("evidence directories are immutable; choose a new output")
    if (plan.get("fixture_sha256") != validate_fixture(load(FIXTURE)) or
            plan.get("rates_sha256") != validate_rates(load(RATES))):
        raise ValueError("plan fixture or rate snapshot is stale")
    steps = [x for x in plan["steps"] if x <= plan["authorization"]["max_concurrency"]]
    output.mkdir(parents=True)
    request = {"plan": plan, "steps": steps, "fixture": load(FIXTURE), "rates": load(RATES)}
    completed = subprocess.run(shlex.split(driver), input=json.dumps(request), text=True,
                               capture_output=True, check=False)
    if completed.returncode:
        raise RuntimeError(f"production-path driver failed with exit code {completed.returncode}")
    evidence = json.loads(completed.stdout)
    if evidence.get("application_revision") != plan.get("application_revision"):
        evidence.setdefault("blockers", []).append("evidence application revision differs from the run plan")
    if evidence.get("target") != plan.get("target"):
        evidence.setdefault("blockers", []).append("evidence target differs from the authorized run plan")
    total_cost = sum(x["cost_usd"] for x in deduplicate_usage(evidence.get("usage_events", [])).values())
    total_cost += sum(x.get("incremental_azure_cost_usd", 0) for x in evidence.get("capacity_steps", []))
    if total_cost > plan["authorization"]["max_spend_usd"]:
        evidence.setdefault("blockers", []).append("reported benchmark spend exceeded the authorized envelope")
    report = analyze(evidence, load(FIXTURE), load(RATES))
    (output / "evidence.json").write_text(json.dumps(evidence, indent=2), encoding="utf-8")
    (output / "report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    return report


class OfflineTests(unittest.TestCase):
    def setUp(self):
        self.fixture, self.rates = load(FIXTURE), load(RATES)

    def test_protocol_is_exact_and_versioned(self):
        self.assertEqual(64, len(validate_fixture(self.fixture)))

    def test_usage_deduplication_and_conflict(self):
        event = {"event_id": "p:1", "vendor": "openai", "category": "audio",
                 "quantity": 2, "unit": "minute", "unit_rate_usd": .017}
        self.assertEqual(.034, deduplicate_usage([event, event])["p:1"]["cost_usd"])
        with self.assertRaises(ValueError):
            deduplicate_usage([event, event | {"quantity": 3}])

    def test_usage_unit_conversion(self):
        event = {"event_id": "audio:1", "vendor": "openai", "category": "audio-output",
                 "quantity": 2_000_000, "unit": "audio_token", "unit_size": 1_000_000,
                 "unit_rate_usd": 20}
        self.assertEqual(40, deduplicate_usage([event])["audio:1"]["cost_usd"])

    def test_units_cache_amortization_and_blocked_completeness(self):
        blocked = {"fixture_sha256": digest(self.fixture), "rates_sha256": digest(self.rates),
                   "blockers": ["target absent"], "usage_events": [], "trials": [], "capacity_steps": []}
        result = analyze(blocked, self.fixture, self.rates)
        self.assertEqual("blocked", result["status"])
        self.assertIsNone(result["highest_quality_passing_tested_concurrency"])
        self.assertIn("80", result["cache"]["amortized_generation_cost_usd"] or {})
        for arm in ARMS:
            self.assertIn("total_cost_per_completed_30_minute_call_usd", result["arms"][arm])

    def test_capacity_subtracts_baseline_and_requires_drain(self):
        trials = [{"completed": True, "quality_passed": True,
                   "latency_ms": {x: [TARGETS_MS[x] - 1] for x in TARGETS_MS}}]
        step = {"concurrency": 5, "trials": trials, "loaded_cpu_seconds": 110,
                "matched_baseline_cpu_seconds": 10, "integrated_active_call_seconds": 500,
                "peak_working_set_bytes": 600, "matched_baseline_working_set_bytes": 100,
                "queue_start": 0, "queue_end": 0, "dropped_frames": 0, "restarts": 0, "drain_complete": True}
        sustainable, resources = step_capacity([step])
        self.assertEqual(5, sustainable)
        self.assertAlmostEqual(.2, resources[0]["incremental_cpu_core_seconds_per_active_call_second"])
        self.assertEqual(100, resources[0]["incremental_peak_working_set_bytes_per_call"])

    def test_failed_attempt_cost_and_metered_vad_savings_are_included(self):
        events = [
            {"event_id": "ok", "vendor": "openai", "category": "answer", "quantity": 1, "cost_usd": .1},
            {"event_id": "failed", "vendor": "openai", "category": "retry", "quantity": 1, "cost_usd": .2},
        ]
        trials = [
            {"arm": "live_generated", "completed": True, "quality_passed": True,
             "language": "en", "input_policy": "local_vad", "duty_cycle_percent": 30,
             "provider_billed_audio_ms": 300, "usage_event_ids": ["ok"]},
            {"arm": "live_generated", "completed": False, "quality_passed": False,
             "language": "en", "input_policy": "continuous_benchmark_only", "duty_cycle_percent": 30,
             "provider_billed_audio_ms": 1000, "usage_event_ids": ["failed"]},
        ]
        evidence = {"fixture_sha256": digest(self.fixture), "rates_sha256": digest(self.rates),
                    "usage_events": events, "trials": trials, "capacity_steps": []}
        result = analyze(evidence, self.fixture, self.rates)
        self.assertAlmostEqual(.3, result["arms"]["live_generated"]["all_attempt_provider_cost_usd"])
        self.assertAlmostEqual(.3, result["arms"]["live_generated"]["total_cost_per_completed_30_minute_call_usd"])
        self.assertEqual(70, result["speech_detection"]["30"]["metered_billed_audio_savings_percent"])

    def test_authorization_is_fail_closed(self):
        with self.assertRaises(ValueError):
            validate_authorization({"authorization": {"authorized": False}, "target": {}, "teams_traffic": False})


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--prepare", type=Path)
    parser.add_argument("--analyze", type=Path)
    parser.add_argument("--plan", type=Path)
    parser.add_argument("--driver")
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    if args.self_test:
        unittest.main(argv=[sys.argv[0]], exit=True)
    elif args.prepare:
        print(json.dumps(prepare(args.prepare)))
    elif args.analyze and args.output:
        if args.output.exists():
            raise SystemExit("refusing to overwrite existing report")
        args.output.write_text(json.dumps(analyze(load(args.analyze), load(FIXTURE), load(RATES)), indent=2), encoding="utf-8")
    elif args.plan and args.driver and args.output:
        print(json.dumps(run_driver(args.plan, args.driver, args.output)))
    else:
        parser.error("use --self-test, --prepare DIR, --analyze EVIDENCE --output REPORT, or --plan PLAN --driver COMMAND --output DIR")


if __name__ == "__main__":
    main()
