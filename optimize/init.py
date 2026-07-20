"""
DEPRECATED — superseded by optimize/init.md.
This script requires sqlcmd, which is no longer available.
Ask Claude to follow optimize/init.md instead.

One-time initialization script for the SP optimization loop.

Steps:
  1. Copies initial_sp.sql -> current_sp.sql
  2. Applies the initial SP to the database
  3. Runs each test case and captures golden_output.csv
  4. Benchmarks the initial SP across all test cases
  5. Writes state.json and benchmark_log.md
"""

import json
import re
import shutil
import subprocess
import sys
from pathlib import Path

BASE = Path(__file__).parent


def load_config():
    with open(BASE / "config.json") as f:
        return json.load(f)


def sqlcmd(config, *, sql_file=None, sql=None, output_csv=False):
    cmd = [
        "sqlcmd",
        "-S", config["server"],
        "-d", config["database"],
        "-U", config["username"],
        "-P", config["password"],
        "-b",  # exit on error
    ]
    if sql_file:
        cmd += ["-i", str(sql_file)]
    elif sql:
        cmd += ["-Q", sql]
    if output_csv:
        cmd += ["-s", ",", "-W", "-h", "-1"]

    result = subprocess.run(cmd, capture_output=True, text=True)
    if result.returncode != 0:
        raise RuntimeError(
            f"sqlcmd failed (rc={result.returncode}):\n"
            f"STDOUT: {result.stdout}\nSTDERR: {result.stderr}"
        )
    return result.stdout, result.stderr


def write_temp(path, content):
    path.write_text(content, encoding="utf-8")


def capture_output(config, params_sql_path):
    params = params_sql_path.read_text(encoding="utf-8")
    tmp = BASE / "_tmp_capture.sql"
    write_temp(tmp, f"SET NOCOUNT ON;\n{params}")
    try:
        stdout, _ = sqlcmd(config, sql_file=tmp, output_csv=True)
    finally:
        tmp.unlink(missing_ok=True)
    return stdout


def parse_statistics(text):
    """Return summed logical reads, last CPU time and elapsed time from STATISTICS output."""
    lrs = re.findall(r"logical reads (\d+)", text, re.IGNORECASE)
    cpu = re.search(r"CPU time = (\d+) ms", text)
    elapsed = re.search(r"elapsed time = (\d+) ms", text)
    return {
        "logical_reads": sum(int(x) for x in lrs),
        "cpu_time_ms": int(cpu.group(1)) if cpu else 0,
        "elapsed_time_ms": int(elapsed.group(1)) if elapsed else 0,
    }


def benchmark_test_case(config, params_sql_path, n_runs):
    params = params_sql_path.read_text(encoding="utf-8")
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
            stdout, stderr = sqlcmd(config, sql_file=tmp)
            runs.append(parse_statistics(stdout + stderr))
    finally:
        tmp.unlink(missing_ok=True)

    return {
        "logical_reads": sum(r["logical_reads"] for r in runs) / len(runs),
        "cpu_time_ms": sum(r["cpu_time_ms"] for r in runs) / len(runs),
        "elapsed_time_ms": sum(r["elapsed_time_ms"] for r in runs) / len(runs),
    }


def main():
    config = load_config()
    n_runs = config.get("n_runs", 3)

    initial_sp = BASE / "initial_sp.sql"
    if not initial_sp.exists():
        print("ERROR: optimize/initial_sp.sql not found.")
        sys.exit(1)

    sp_content = initial_sp.read_text(encoding="utf-8")
    if sp_content.strip().startswith("--"):
        print("ERROR: optimize/initial_sp.sql still contains only the placeholder comment.")
        sys.exit(1)

    # 1. Copy initial -> current
    shutil.copy(initial_sp, BASE / "current_sp.sql")
    shutil.copy(initial_sp, BASE / "candidate_sp.sql")
    print("[1/4] Copied initial_sp.sql -> current_sp.sql, candidate_sp.sql")

    # 2. Apply SP to database
    print("[2/4] Applying stored procedure to database...")
    sqlcmd(config, sql_file=initial_sp)
    print("      Done.")

    # 3. Capture golden outputs
    tc_base = BASE / "test_cases"
    test_cases = sorted([
        d for d in tc_base.iterdir()
        if d.is_dir() and (d / "params.sql").exists()
    ])
    if not test_cases:
        print("ERROR: No test cases found in optimize/test_cases/*/params.sql")
        sys.exit(1)

    print(f"[3/4] Capturing golden outputs for {len(test_cases)} test case(s)...")
    for tc in test_cases:
        output = capture_output(config, tc / "params.sql")
        (tc / "golden_output.csv").write_text(output, encoding="utf-8")
        rows = len([l for l in output.strip().splitlines() if l.strip()])
        print(f"      {tc.name}: {rows} row(s)")

    # 4. Benchmark initial SP
    print(f"[4/4] Benchmarking initial SP ({n_runs} runs per test case)...")
    total_lr = 0.0
    initial_metrics = {}
    for tc in test_cases:
        metrics = benchmark_test_case(config, tc / "params.sql", n_runs)
        initial_metrics[tc.name] = metrics
        total_lr += metrics["logical_reads"]
        print(
            f"      {tc.name}: {metrics['logical_reads']:.0f} logical reads | "
            f"{metrics['cpu_time_ms']:.0f}ms CPU | {metrics['elapsed_time_ms']:.0f}ms elapsed"
        )
    print(f"      ─────────────────────────────────────────")
    print(f"      Baseline total logical reads: {total_lr:.0f}")

    # Write state.json
    state = {
        "iteration": 0,
        "max_iterations": config.get("max_iterations", 10),
        "best_score": total_lr,
        "best_score_breakdown": initial_metrics,
        "techniques_tried": [],
        "techniques_succeeded": [],
        "last_result": None,
    }
    (BASE / "state.json").write_text(json.dumps(state, indent=2), encoding="utf-8")

    # Write benchmark_log.md
    breakdown = "".join(
        f"  - {tc}: {m['logical_reads']:.0f} LR | {m['cpu_time_ms']:.0f}ms CPU\n"
        for tc, m in initial_metrics.items()
    )
    (BASE / "benchmark_log.md").write_text(
        f"# Benchmark Log\n\n"
        f"## Baseline — initial_sp.sql\n"
        f"- **Total logical reads:** {total_lr:.0f}\n"
        f"- Per test case:\n{breakdown}\n",
        encoding="utf-8",
    )

    # Verify iter_00 skill snapshot exists
    seed = BASE / "skills" / "iter_00_sql_server_optimization.md"
    if not seed.exists():
        print("WARNING: optimize/skills/iter_00_sql_server_optimization.md not found.")
        print("         The loop will start without a seeded skill file.")
    else:
        print(f"Skill seed     : {seed.name}")

    print("\nInitialization complete.")
    print(f"Baseline score : {total_lr:.0f} total logical reads")
    print(f"Test cases     : {len(test_cases)}")
    print(f"Max iterations : {config.get('max_iterations', 10)}")
    print("\nNext step: run `/loop optimize/loop.md` to start optimizing.")


if __name__ == "__main__":
    main()
