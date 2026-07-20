"""
DEPRECATED — superseded by optimize/loop.md (MCP-native iteration).
This script requires sqlcmd, which is no longer available.
The optimization loop now runs entirely through the Azure SQL MCP server.

Runs one optimization iteration.

Called by the LLM loop after it writes optimize/candidate_sp.sql.

Steps:
  1. Deploy candidate as dbo.ProcName_opt_test
  2. Verify correctness: full diff vs golden_output.csv for every test case
  3. If all correct: benchmark all test cases (n_runs averaged)
  4. If aggregate logical reads < best_score: accept (promote to real proc, update state)
  5. Always drop _opt_test proc at the end
  6. Print JSON result to stdout

Exit codes: 0 = ran successfully (check result.status for accept/reject/failure)
            1 = script-level error (bad config, missing files, sqlcmd unreachable)
"""

import json
import re
import shutil
import subprocess
import sys
from pathlib import Path

BASE = Path(__file__).parent


# ── helpers ──────────────────────────────────────────────────────────────────

def load_config():
    with open(BASE / "config.json") as f:
        return json.load(f)


def load_state():
    with open(BASE / "state.json") as f:
        return json.load(f)


def save_state(state):
    (BASE / "state.json").write_text(json.dumps(state, indent=2), encoding="utf-8")


def sqlcmd(config, *, sql_file=None, sql=None, output_csv=False):
    cmd = [
        "sqlcmd",
        "-S", config["server"],
        "-d", config["database"],
        "-U", config["username"],
        "-P", config["password"],
    ]
    if sql_file:
        cmd += ["-i", str(sql_file)]
    elif sql:
        cmd += ["-Q", sql]
    if output_csv:
        cmd += ["-s", ",", "-W", "-h", "-1"]

    result = subprocess.run(cmd, capture_output=True, text=True)
    return result.returncode, result.stdout, result.stderr


def write_temp(path, content):
    path.write_text(content, encoding="utf-8")


def test_proc_name(config):
    """Return (schema, base_name, full_test_name) for the _opt_test variant."""
    proc = config["proc_name"]
    if "." in proc:
        schema, base = proc.split(".", 1)
    else:
        schema, base = "dbo", proc
    base = base.strip("[]")
    return schema, base, f"{schema}.[{base}_opt_test]"


def make_test_proc_sql(candidate_text, config):
    """Substitute the proc name in the CREATE/ALTER header with the _opt_test variant."""
    _, _, full_test = test_proc_name(config)
    proc = re.escape(config["proc_name"])
    pattern = re.compile(
        r"(CREATE\s+OR\s+ALTER\s+PROCEDURE|ALTER\s+PROCEDURE|CREATE\s+PROCEDURE)"
        r"\s+" + proc,
        re.IGNORECASE,
    )
    result = pattern.sub(f"CREATE OR ALTER PROCEDURE {full_test}", candidate_text)
    if result == candidate_text:
        raise ValueError(
            f"Could not find 'CREATE/ALTER PROCEDURE {config['proc_name']}' in candidate_sp.sql. "
            "Make sure the proc header uses the exact name from config.json."
        )
    return result


def substitute_proc_in_params(params_text, config):
    """Replace original proc name with _opt_test name in a params.sql string."""
    _, base, full_test = test_proc_name(config)
    proc = config["proc_name"]
    text = re.sub(re.escape(proc), full_test, params_text, flags=re.IGNORECASE)
    if text == params_text:
        # Qualified name not found; fall back to bare base name only
        text = re.sub(r"\b" + re.escape(base) + r"\b", f"{base}_opt_test", text, flags=re.IGNORECASE)
    return text


def drop_test_proc(config):
    _, _, full_test = test_proc_name(config)
    sqlcmd(config, sql=f"IF OBJECT_ID('{full_test}', 'P') IS NOT NULL DROP PROCEDURE {full_test};")


def run_output(config, params_path, use_test_proc=False):
    params = params_path.read_text(encoding="utf-8")
    if use_test_proc:
        params = substitute_proc_in_params(params, config)
    tmp = BASE / "_tmp_output.sql"
    write_temp(tmp, f"SET NOCOUNT ON;\n{params}")
    try:
        _, stdout, _ = sqlcmd(config, sql_file=tmp, output_csv=True)
    finally:
        tmp.unlink(missing_ok=True)
    return stdout


def parse_statistics(text):
    lrs = re.findall(r"logical reads (\d+)", text, re.IGNORECASE)
    cpu = re.search(r"CPU time = (\d+) ms", text)
    elapsed = re.search(r"elapsed time = (\d+) ms", text)
    return {
        "logical_reads": sum(int(x) for x in lrs),
        "cpu_time_ms": int(cpu.group(1)) if cpu else 0,
        "elapsed_time_ms": int(elapsed.group(1)) if elapsed else 0,
    }


def benchmark_tc(config, params_path, n_runs, use_test_proc=False):
    params = params_path.read_text(encoding="utf-8")
    if use_test_proc:
        params = substitute_proc_in_params(params, config)
    stats_sql = (
        "SET NOCOUNT ON;\n"
        "SET STATISTICS TIME ON;\n"
        "SET STATISTICS IO ON;\n"
        f"{params}\n"
        "SET STATISTICS TIME OFF;\n"
        "SET STATISTICS IO OFF;\n"
    )
    tmp = BASE / "_tmp_bench.sql"
    write_temp(tmp, stats_sql)

    runs = []
    try:
        for _ in range(n_runs):
            _, stdout, stderr = sqlcmd(config, sql_file=tmp)
            runs.append(parse_statistics(stdout + stderr))
    finally:
        tmp.unlink(missing_ok=True)

    n = len(runs)
    return {
        "logical_reads": sum(r["logical_reads"] for r in runs) / n,
        "cpu_time_ms": sum(r["cpu_time_ms"] for r in runs) / n,
        "elapsed_time_ms": sum(r["elapsed_time_ms"] for r in runs) / n,
    }


def verify_correctness(config, tc_dir):
    """Full diff of test proc output vs golden. Returns (passed: bool, detail: str)."""
    golden = (tc_dir / "golden_output.csv").read_text(encoding="utf-8").strip()
    actual = run_output(config, tc_dir / "params.sql", use_test_proc=True).strip()

    golden_lines = golden.splitlines()
    actual_lines = actual.splitlines()

    if golden_lines == actual_lines:
        return True, "exact match"

    golden_set = set(golden_lines)
    actual_set = set(actual_lines)
    missing = len(golden_set - actual_set)
    extra = len(actual_set - golden_set)
    row_diff = len(actual_lines) - len(golden_lines)
    return False, (
        f"row count diff={row_diff:+d}, missing lines={missing}, extra lines={extra}"
    )


def append_log(entry):
    log = BASE / "benchmark_log.md"
    with open(log, "a", encoding="utf-8") as f:
        f.write(entry)


# ── main ─────────────────────────────────────────────────────────────────────

def main():
    config = load_config()
    state = load_state()
    n_runs = config.get("n_runs", 3)
    iteration = state["iteration"] + 1

    candidate_sp = BASE / "candidate_sp.sql"
    if not candidate_sp.exists():
        out = {"status": "error", "message": "candidate_sp.sql not found"}
        print(json.dumps(out, indent=2))
        sys.exit(1)

    tc_base = BASE / "test_cases"
    test_cases = sorted([
        d for d in tc_base.iterdir()
        if d.is_dir() and (d / "params.sql").exists()
    ])

    result = {
        "iteration": iteration,
        "status": None,
        "correctness": {},
        "performance": {},
        "total_logical_reads": None,
        "best_score": state["best_score"],
        "improvement_lr": None,
        "improvement_pct": None,
        "message": "",
    }

    # ── Step 1: Deploy candidate as _opt_test ────────────────────────────────
    candidate_text = candidate_sp.read_text(encoding="utf-8")
    try:
        test_proc_sql = make_test_proc_sql(candidate_text, config)
    except ValueError as e:
        result["status"] = "deploy_error"
        result["message"] = str(e)
        print(json.dumps(result, indent=2))
        sys.exit(0)

    tmp_sp = BASE / "_tmp_test_sp.sql"
    write_temp(tmp_sp, test_proc_sql)
    rc, stdout, stderr = sqlcmd(config, sql_file=tmp_sp)
    tmp_sp.unlink(missing_ok=True)

    if rc != 0:
        result["status"] = "deploy_error"
        result["message"] = f"CREATE test proc failed:\n{stderr}\n{stdout}"
        print(json.dumps(result, indent=2))
        return

    # ── Step 2: Correctness check ────────────────────────────────────────────
    all_correct = True
    for tc in test_cases:
        passed, detail = verify_correctness(config, tc)
        result["correctness"][tc.name] = {"passed": passed, "detail": detail}
        if not passed:
            all_correct = False

    if not all_correct:
        result["status"] = "correctness_failure"
        result["message"] = "One or more test cases produced incorrect output — see correctness field."
        drop_test_proc(config)
        state["iteration"] = iteration
        state["last_result"] = result
        state["techniques_tried"].append({"iteration": iteration, "outcome": "correctness_failure"})
        save_state(state)
        append_log(
            f"\n## Iteration {iteration} — CORRECTNESS FAILURE\n"
            + "".join(
                f"- {tc}: {'OK' if v['passed'] else 'FAIL — ' + v['detail']}\n"
                for tc, v in result["correctness"].items()
            )
            + "\n"
        )
        print(json.dumps(result, indent=2))
        return

    # ── Step 3: Benchmark ────────────────────────────────────────────────────
    total_lr = 0.0
    for tc in test_cases:
        metrics = benchmark_tc(config, tc / "params.sql", n_runs, use_test_proc=True)
        result["performance"][tc.name] = metrics
        total_lr += metrics["logical_reads"]

    result["total_logical_reads"] = total_lr
    improvement_lr = state["best_score"] - total_lr
    improvement_pct = (improvement_lr / state["best_score"] * 100) if state["best_score"] else 0.0
    result["improvement_lr"] = improvement_lr
    result["improvement_pct"] = round(improvement_pct, 2)

    # ── Step 4: Accept or reject ─────────────────────────────────────────────
    if total_lr < state["best_score"]:
        result["status"] = "accepted"
        result["message"] = (
            f"Improved by {improvement_lr:.0f} logical reads ({improvement_pct:.1f}%): "
            f"{total_lr:.0f} vs previous best {state['best_score']:.0f}"
        )
        shutil.copy(candidate_sp, BASE / "current_sp.sql")
        # Promote to real proc
        sqlcmd(config, sql_file=BASE / "current_sp.sql")

        state["best_score"] = total_lr
        state["best_score_breakdown"] = result["performance"]
        state["techniques_succeeded"].append({
            "iteration": iteration,
            "score": total_lr,
            "improvement_pct": round(improvement_pct, 2),
        })
        append_log(
            f"\n## Iteration {iteration} — ACCEPTED (+{improvement_pct:.1f}%)\n"
            f"- Total logical reads: {total_lr:.0f} (was {result['best_score']:.0f})\n"
            + "".join(
                f"- {tc}: {m['logical_reads']:.0f} LR | {m['cpu_time_ms']:.0f}ms CPU\n"
                for tc, m in result["performance"].items()
            )
            + "\n"
        )
    else:
        result["status"] = "rejected"
        result["message"] = (
            f"No improvement: {total_lr:.0f} >= best {state['best_score']:.0f} "
            f"({improvement_pct:+.1f}%)"
        )
        append_log(
            f"\n## Iteration {iteration} — REJECTED\n"
            f"- Total logical reads: {total_lr:.0f} (best: {state['best_score']:.0f})\n\n"
        )

    drop_test_proc(config)

    state["iteration"] = iteration
    state["last_result"] = result
    state["techniques_tried"].append({
        "iteration": iteration,
        "outcome": result["status"],
        "score": total_lr,
    })
    save_state(state)
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    main()
