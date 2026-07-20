# SP Optimization Loop — Session Reconstruction Prompt

## Goal
Build an autonomous optimization loop for a SQL Server stored procedure where an LLM iteratively generates optimization techniques, evaluates them, and records all outcomes (accepted, rejected, correctness failure) into a single evolving skill file that is versioned per iteration.

## Final Design Decisions

### Database
- **SQL Server** (T-SQL stored procedures)
- Access via Azure SQL MCP server (`AZURE_CONN_STRING` environment variable)
- No sysadmin access (no `DBCC DROPCLEANBUFFERS`)

### Benchmarking
- **Method:** `SET STATISTICS TIME ON; SET STATISTICS IO ON;`
- **Runs per iteration:** `n_runs + 1` calls, first (warm-up) discarded, remaining `n_runs` averaged
- **Primary metric:** total logical reads (sum across all tables, all test cases) — chosen because it is buffer-cache-stable across warm runs
- **Secondary metrics:** CPU time (ms), elapsed time (ms)
- **Aggregation across test cases:** sum of logical reads across all test cases

### Correctness Verification
- **Method:** Full diff (exact line-by-line comparison) against golden output CSV
- **Golden capture:** Run the original proc via `execute_sp` MCP tool once per test case, write result to `golden_output.csv`
- **No non-deterministic columns** — full diff is safe
- **Test proc isolation:** Candidate SP is deployed as `dbo.ProcName_opt_test` during testing; only promoted to the real proc name on acceptance. The real proc is never touched until a candidate is accepted.
- **Multi-test-case rule:** ALL test cases must pass correctness before performance is even measured. A single correctness failure rejects the entire iteration.
- **Verification pipeline:**
  1. Deploy candidate as `_opt_test` proc (max 3 attempts; failure after 3 → `deploy_error`, increment iteration, write skill, stop)
  2. Full diff vs golden for every test case using `execute_sp` on `_opt_test`
  3. If any diff → `correctness_failure`: real proc untouched, increment iteration, write skill snapshot, stop
  4. If all pass → benchmark all test cases via `run_benchmark` on `_opt_test` (`n_runs + 1` calls, discard first warm-up)
  5. If aggregate score improves by ≥ 0.5% → `accepted`: call `deploy_sp` to promote to real proc, write skill snapshot
  6. If not improved or below 0.5% threshold → `rejected`: real proc untouched, write skill snapshot
  7. Skill snapshot is always written regardless of outcome (accepted / rejected / correctness_failure / deploy_error)

### Test Cases
- **Provided by the user** — multiple filter combinations covering the proc's parameter space
- **Format:** `optimize/test_cases/tc_NN/params.sql` containing the `EXEC dbo.YourProc @param=value` call
- **Initialization step:** `init.md` (followed by Claude) runs each test case via `execute_sp` and captures `golden_output.csv` automatically

### Skill Files
- There is ONE skill file that evolves across all iterations: `optimize/skills/iter_NN_sql_server_optimization.md`
- `iter_00` is the seed — pre-populated with general SQL Server optimization techniques as a starting checklist
- After each iteration (accepted, rejected, or correctness failure), the LLM reads the previous snapshot, appends the new finding, and saves it as `iter_NN` (where NN = current iteration number)
- Each file is a permanent snapshot — never overwritten — so users can `diff iter_07 iter_08` to see exactly what the loop learned in iteration 8
- The seed checklist items are marked inline as `✓ Tried — ACCEPTED/REJECTED (iter NN)` as they are attempted
- The loop reads the latest snapshot at the start of each iteration to know what has already been tried

### Loop Mechanics
- Claude generates one optimization hypothesis per iteration
- Deploys candidate as `_opt_test` proc via `deploy_sp` MCP tool (max 3 attempts before recording `deploy_error`)
- Runs correctness and benchmark against `_opt_test`; real proc is never touched until acceptance
- A skill snapshot (`iter_NN`) is always written regardless of outcome
- Acceptance requires ≥ 0.5% improvement; sub-threshold differences are treated as noise and rejected
- Benchmarking runs `n_runs + 1` calls per test case; first call (warm-up) is discarded
- **Stopping conditions** (checked each iteration before generating a hypothesis):
  1. `state.iteration >= max_iterations` — budget exhausted
  2. Trailing streak of non-accepted outcomes ≥ `max_consecutive_failures` — ideas likely exhausted
- State tracked in `optimize/state.json`

## Directory Structure to Build

```
optimize/
  config.json             ← proc name, n_runs, max_iterations, max_consecutive_failures
  initial_sp.sql          ← user provides: original stored procedure
  current_sp.sql          ← auto: current best version (starts as copy of initial)
  candidate_sp.sql        ← auto: LLM writes here each iteration
  state.json              ← auto: iteration #, best score, techniques tried/succeeded
  benchmark_log.md        ← auto: per-iteration results log
  init.md                 ← run once (via Claude): capture golden outputs + baseline benchmark
  loop.md                 ← the /loop prompt Claude follows each iteration
  test_cases/
    tc_01/
      params.sql          ← user provides: EXEC call with filter params
      golden_output.csv   ← auto: captured during init
    tc_02/
      params.sql
      golden_output.csv
  skills/
    iter_00_sql_server_optimization.md  ← seed: general SQL Server techniques checklist
    iter_01_sql_server_optimization.md  ← auto: iter_00 + iteration 1 finding
    iter_02_sql_server_optimization.md  ← auto: iter_01 + iteration 2 finding
    ...                                 ← one snapshot per iteration, never overwritten
```

## Files to Implement

### config.json
```json
{
  "proc_name": "dbo.YourProc",
  "n_runs": 3,
  "max_iterations": 10,
  "max_consecutive_failures": 3
}
```
Connection is provided via `AZURE_CONN_STRING` environment variable (read by the MCP server).

### init.md responsibilities (run once via Claude)
1. Validate config + `initial_sp.sql` exists and is not a placeholder
2. Deploy initial SP to DB via `deploy_sp` MCP tool
3. Copy `initial_sp.sql` → `current_sp.sql` and `candidate_sp.sql`
4. For each `test_cases/tc_*/params.sql`: call `execute_sp`, write result to `golden_output.csv`
5. Benchmark initial SP across all test cases (`n_runs + 1` calls each, first discarded, `n_runs` averaged) via `run_benchmark` MCP tool
6. Write `state.json` with baseline score
7. Initialize `benchmark_log.md`
8. Verify `skills/iter_00_sql_server_optimization.md` seed exists (warn if missing)

### loop.md responsibilities (the /loop prompt)
Each iteration Claude must:
1. Read `state.json`, `config.json`, `current_sp.sql`, `benchmark_log.md`, and the latest `skills/iter_NN_sql_server_optimization.md`
2. Check stopping conditions: `iteration >= max_iterations` OR trailing non-accepted streak ≥ `max_consecutive_failures`
3. Generate ONE optimization hypothesis not yet tried (guided by the skill checklist and prior findings)
4. Write optimized SQL to `candidate_sp.sql` (`CREATE OR ALTER PROCEDURE <proc_name>` header)
5. Deploy candidate as `<proc_name>_opt_test` via `deploy_sp` (max 3 attempts; after 3 failures → `deploy_error`, increment iteration, write skill, stop)
6. Correctness check: call `execute_sp` on `_opt_test` per test case, diff vs `golden_output.csv`; on failure: real proc untouched, increment iteration, write skill, stop
7. Benchmark: call `run_benchmark` on `_opt_test`, `n_runs + 1` times per test case, discard first (warm-up), average remaining
8. Accept (≥ 0.5% improvement) or reject: on accept, promote via `deploy_sp` with real proc name; on reject, real proc untouched
9. Regardless of outcome: read previous skill snapshot, append new finding, save as `iter_NN_sql_server_optimization.md`

### Skill file versioning
- Seed file: `skills/iter_00_sql_server_optimization.md` — pre-populated with general techniques
- Each iteration appends to the **Proc-Specific Findings** section and marks the attempted general technique as `✓ Tried`
- Saved as a new file `iter_NN_sql_server_optimization.md` — previous snapshot untouched
- Finding format per iteration:
```markdown
### Iteration NN — <Technique Name> — <ACCEPTED / REJECTED / CORRECTNESS FAILURE / DEPLOY ERROR>
- **What was changed:** ...
- **Hypothesis:** ...
- **Result:** total logical reads X → Y (Z%)
- **Why it worked / failed:** ...
- **Lesson for future iterations:** ...
```
- `correctness_failure`: replace Result line with `**Failed test cases:** <list with diff summary>`
- `deploy_error`: replace Result and Why lines with `**Error:** deploy_sp failed after 3 attempts — <message>`

## MCP Tools Used

All database access goes through the Azure SQL MCP server (`AZURE_CONN_STRING` env var).

| Tool | Purpose |
|---|---|
| `deploy_sp` | Deploy `CREATE OR ALTER PROCEDURE` or `ALTER PROCEDURE` SQL |
| `execute_sp` | Run a proc and return the result set as CSV (for correctness checks) |
| `run_benchmark` | Run a proc with `STATISTICS IO/TIME` and return the raw output |
| `get_sp_definition` | Read the current proc definition from the database |
| `get_execution_stats` | Read DMV-based historical execution stats |

### STATISTICS output parsing (for `run_benchmark` output)
```
Table 'TableName'. Scan count N, logical reads N, physical reads N, ...
 SQL Server Execution Times:
   CPU time = N ms,  elapsed time = N ms.
```
- Logical reads: sum all matches of `logical reads (\d+)` (case-insensitive)
- CPU time: `CPU time = (\d+) ms`
- Elapsed time: `elapsed time = (\d+) ms`

### Parameter format for `execute_sp` and `run_benchmark`
Parameters are passed as a comma-separated `@param=value` string (e.g. `@Filter1=SomeValue,@Filter2=42`).
Strip surrounding single-quotes from string values **only if the value contains no commas or equals signs** — if it does, leave the quotes in place.

## What the user still needs to provide before running
1. The stored procedure SQL → paste into `optimize/initial_sp.sql`
2. Connection string → set `AZURE_CONN_STRING` environment variable
3. Test case EXEC calls → one per `optimize/test_cases/tc_NN/params.sql`

## Run order
```
# 1. Fill in initial_sp.sql, test_cases/tc_*/params.sql, set AZURE_CONN_STRING
#    Ask Claude to follow optimize/init.md

# 2. Start the optimization loop
/loop optimize/loop.md
```
