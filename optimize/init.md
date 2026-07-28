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

Read `optimize/initial_sp.sql`. Verify the DDL header starts with `ALTER PROCEDURE`. If it doesn't, stop and tell the user to change it to `ALTER PROCEDURE`.

Call `deploy_sp` with the full content of `initial_sp.sql`.

If deployment fails, stop and show the error. The user must fix the SQL before continuing.

---

## Step 3 — Set up working files

Write the content of `optimize/initial_sp.sql` to:
- `optimize/current_sp.sql`
- `optimize/candidate_sp.sql`

---

## Step 4 — Capture golden outputs

Find all test case directories: `optimize/test_cases/tc_*/`, sorted by name. For each one, verify that `params.sql` exists. If no test cases are found, stop and tell the user to create at least one test case under `optimize/test_cases/tc_01/params.sql`.

For each test case:
1. Read `params.sql` — the file contains a raw semicolon-separated `@param=value` string (e.g. `@BusinessEntityID=2;@MaxDepth=3`), or is empty/absent if the proc takes no parameters.
   - **Hard stop:** if any parameter value contains a semicolon, stop immediately: `PARAMETER ERROR: value in <tc_dir>/params.sql cannot be safely passed (contains semicolon).`
   - Pass `null` if the file is empty or the proc takes no parameters
2. Call `execute_sp` with `spName = proc_name`, the extracted parameters, and `outputFilePath = optimize/test_cases/<tc_dir>/golden_output.csv`
3. Print: `<tc_dir>: <N> row(s) captured` (the row count is in the tool's return value)

---

## Step 5 — Benchmark the initial SP

For each test case (same order as Step 4):
1. Read `params.sql` directly — same format and hard-stop rule as Step 4.
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

## Step 7 — Verify skill files

Check that both of the following exist:
- `optimize/skills/techniques.md`
- `optimize/skills/findings.md`

If either is missing, stop and tell the user to create the missing file before running the loop. Both are required.

---

## Step 8 — Print summary

```
Initialization complete.
Baseline score : <baseline_total_lr> total logical reads
Test cases     : <N>
Max iterations : <config.max_iterations>

Next step: run `/loop optimize/loop.md` to start optimizing.
```
