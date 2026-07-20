# SP Optimization Loop — Initialization Instructions

Run this once before starting the optimization loop.
Follow every step in order. Do not skip steps.

---

## Step 1 — Validate inputs

Read `optimize/config.json`. Verify:
- `proc_name` is not `dbo.YourProc`
- `n_runs` and `max_iterations` are present and are positive integers

If any field is still a placeholder, stop and tell the user which fields need filling in.

Verify that the `AZURE_CONN_STRING` environment variable is set (non-empty). If not, stop and tell the user to set it before running init.

Read `optimize/initial_sp.sql`. If the file contains only the placeholder comment (content starts with `--` after stripping whitespace), stop and tell the user to paste their stored procedure into the file.

---

## Step 2 — Deploy the initial stored procedure

Read `optimize/initial_sp.sql`. Verify the DDL header starts with `CREATE OR ALTER PROCEDURE` or `ALTER PROCEDURE`. If it starts with `CREATE PROCEDURE` only, stop and tell the user to change it to `CREATE OR ALTER PROCEDURE` — the MCP tool requires one of those two forms.

Call `deploy_sp` with the full content of `initial_sp.sql`.

If deployment fails, stop and show the error. The user must fix the SQL before continuing.

---

## Step 3 — Set up working files

Write the content of `optimize/initial_sp.sql` to:
- `optimize/current_sp.sql`
- `optimize/candidate_sp.sql`

(Both start as identical copies of the initial SP.)

---

## Step 4 — Capture golden outputs

Find all test case directories: `optimize/test_cases/tc_*/`, sorted by name. For each one, verify that `params.sql` exists. If no test cases are found, stop and tell the user to create at least one test case under `optimize/test_cases/tc_01/params.sql`.

For each test case:
1. Read its `params.sql` and extract the parameter values from the `EXEC` statement as a comma-separated `@param=value` string.
   - Strip surrounding single-quotes from string values only if the value contains no commas or equals signs
   - **Hard stop:** if any parameter value (after removing quotes) contains a comma or equals sign, stop immediately and output: `PARAMETER ERROR: value in <tc_dir>/params.sql cannot be safely passed via the MCP parameter format (contains comma or equals sign). Simplify the value before continuing.` Do not proceed with init.
   - Pass `null` if the proc takes no parameters
2. Call `execute_sp` with `spName = proc_name` (from config.json) and the extracted parameters.
3. Write the returned output to `optimize/test_cases/<tc_dir>/golden_output.csv` (no trailing newline).
4. Print: `<tc_dir>: <N> row(s) captured`

---

## Step 5 — Benchmark the initial SP

For each test case (same order as Step 4):
1. Extract parameters the same way as Step 4 (same hard-stop rule applies).
2. Call `run_benchmark` with `spName = proc_name` and those parameters, **`n_runs + 1` times**.
3. **Discard the first call's result** (warm-up run — absorbs plan compilation cost).
4. For each of the remaining `n_runs` calls, parse the STATISTICS output:
   - Logical reads: sum all matches of `logical reads (\d+)` (case-insensitive)
   - CPU time ms: match `CPU time = (\d+) ms`
   - Elapsed time ms: match `elapsed time = (\d+) ms`
5. Average the `n_runs` results for this test case.
6. Print: `<tc_dir>: <avg_lr> logical reads | <avg_cpu>ms CPU | <avg_elapsed>ms elapsed`

Sum `logical_reads` across all test cases → `baseline_total_lr`.

Print: `Baseline total logical reads: <baseline_total_lr>`

---

## Step 6 — Write state.json

Write `optimize/state.json` with this exact structure:

```json
{
  "iteration": 0,
  "max_iterations": <config.max_iterations>,
  "best_score": <baseline_total_lr>,
  "best_score_breakdown": {
    "<tc_dir>": {
      "logical_reads": <avg>,
      "cpu_time_ms": <avg>,
      "elapsed_time_ms": <avg>
    }
  },
  "techniques_tried": [],
  "techniques_succeeded": [],
  "last_result": null
}
```

---

## Step 7 — Write benchmark_log.md

Write `optimize/benchmark_log.md` with this content:

```markdown
# Benchmark Log

## Baseline — initial_sp.sql
- **Total logical reads:** <baseline_total_lr>
- Per test case:
  - <tc_dir>: <avg_lr> LR | <avg_cpu>ms CPU
```

---

## Step 8 — Verify seed skill file

Check whether `optimize/skills/iter_00_sql_server_optimization.md` exists.
- If it does: print `Skill seed: iter_00_sql_server_optimization.md`
- If it does not: print a warning that the loop will start without a seeded skill file.

---

## Step 9 — Print summary

```
Initialization complete.
Baseline score : <baseline_total_lr> total logical reads
Test cases     : <N>
Max iterations : <config.max_iterations>

Next step: run `/loop optimize/loop.md` to start optimizing.
```
