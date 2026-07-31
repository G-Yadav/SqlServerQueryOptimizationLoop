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

(`optimize/initial_sp.sql` is populated from the database in Step 2 — no manual paste needed.)

---

## Step 2 — Fetch and deploy the initial stored procedure

Always populate `initial_sp.sql` from the database — it is the source of truth for the live proc, and `deploy_sp` can only `ALTER` a proc that already exists.

1. Call `get_sp_definition` with `spName = proc_name`.
2. If it returns `Not found` (or an empty definition), the proc does not exist in the database. `deploy_sp` cannot create it, so stop and tell the user to create the procedure in the database first, then re-run init.
3. Replace the leading `CREATE` keyword of the `CREATE PROCEDURE` statement with `ALTER` (case-insensitive, first occurrence only — leave any later `CREATE` in the body untouched). Write the result to `optimize/initial_sp.sql`, then print: `Populated initial_sp.sql from the database (CREATE → ALTER).`

Call `deploy_sp` with the full content of `initial_sp.sql`.

If deployment fails, stop and show the error.

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
   - **Format hard stop:** after stripping whitespace, if the content is non-empty and is not in the raw `@param=value` form — i.e. it begins with `--` or `/*` (a SQL comment), contains `EXEC` or `EXECUTE` (case-insensitive), or its first non-whitespace character is not `@` — stop immediately: `PARAMETER FORMAT ERROR: <tc_dir>/params.sql must contain a raw semicolon-separated @param=value string (e.g. @BusinessEntityID=2;@MaxDepth=3), not an EXEC statement, SQL comment, or other SQL. Fix the file and re-run init.` This catches the common mistake of pasting an `EXEC dbo.Proc @p=1` call, which would otherwise be sent as a wrong-parameter call with no error.
   - **Hard stop:** if any parameter value contains a semicolon, stop immediately: `PARAMETER ERROR: value in <tc_dir>/params.sql cannot be safely passed (contains semicolon).`
   - Pass `null` if the file is empty or the proc takes no parameters
2. Call `execute_sp` with `spName = proc_name`, the extracted parameters, and `outputFilePath = optimize/test_cases/<tc_dir>/golden_output.csv`
3. Print: `<tc_dir>: <N> row(s) captured` (the row count is in the tool's return value)

---

## Step 5 — Benchmark the initial SP

Read each test case's `params.sql` in the same sorted order as Step 4 — same format and hard-stop rule as Step 4.

Make a single `benchmark_all` call:
- `spName = proc_name`
- `parameterSets` = the test cases' parameter strings joined by newlines, in sorted order — one line per test case, in the Step 4 order. Use a blank line for a test case that takes no parameters.
- `nRuns = config.n_runs`

The server runs each set `n_runs + 1` times (discarding the warm-up), parses and averages the STATISTICS output, and returns one line per test case (`run_1`, `run_2`, … matching the input order) as `logical_reads=N, cpu_ms=N, elapsed_ms=N`, followed by `total_logical_reads=N`.

Map each `run_i` back to its test case by position and print: `<tc_dir>: <lr> logical reads | <cpu>ms CPU | <elapsed>ms elapsed`.

Set `baseline_total_lr` = the returned `total_logical_reads`.

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
