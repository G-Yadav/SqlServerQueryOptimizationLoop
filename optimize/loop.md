# SP Optimization Loop — Iteration Instructions

You are running one iteration of a stored procedure optimization loop.
Your goal: make the stored procedure faster without changing its output on any test case.

---

## Step 1 — Read current state

Read ALL of the following before doing anything else:

- `optimize/state.json` — current iteration number, best score, techniques tried/succeeded
- `optimize/config.json` — `proc_name`, `n_runs`, `max_iterations`, `max_consecutive_failures` (default 3 if absent)
- `optimize/current_sp.sql` — the current best version of the stored procedure
- `optimize/benchmark_log.md` — per-iteration outcome log with technique names; full reasoning is in the skill file
- The latest skill snapshot: `optimize/skills/iter_NN_sql_server_optimization.md` where NN is the highest number present in `optimize/skills/`

---

## Step 2 — Check stopping conditions

**Max iterations:** if `state.iteration >= config.max_iterations`, output:

```
LOOP COMPLETE: max iterations reached.
Best score: <state.best_score> total logical reads.
Final skill file: optimize/skills/iter_NN_sql_server_optimization.md
```

Then stop.

**Consecutive failures:** count the trailing streak of non-accepted outcomes at the end of `state.techniques_tried` (outcomes of `rejected`, `correctness_failure`, or `deploy_error`). If this streak ≥ `config.max_consecutive_failures` (default 3 if the field is absent from config.json), output:

```
LOOP STOPPED: <N> consecutive non-accepted iterations — ideas may be exhausted.
Best score: <state.best_score> total logical reads.
Final skill file: optimize/skills/iter_NN_sql_server_optimization.md
```

Then stop.

---

## Step 3 — Generate one optimization hypothesis

Based on what you read from the skill file and benchmark log:

- Do NOT repeat a technique already recorded in the **Proc-Specific Findings** section of the skill file
- Work through the **General Techniques** checklist in order — prefer unchecked items first
- Then try proc-specific ideas suggested by the findings of prior iterations
- Choose ONE specific, targeted optimization per iteration

Write 1–2 sentences explaining your hypothesis and why you expect it to reduce logical reads.

---

## Step 4 — Write the candidate SP

Write the optimized stored procedure to `optimize/candidate_sp.sql`.

Rules:
- Use exactly `CREATE OR ALTER PROCEDURE <proc_name>` as the header — `proc_name` comes from `config.json`
- Keep all original parameters and their data types
- Only modify the body
- The output for every test case must remain identical to the golden baseline

---

## Step 5 — Deploy the candidate SP as a test proc

The candidate is tested in isolation — the real proc is never touched until acceptance.

**Build the test-proc name:** append `_opt_test` to the base name of `proc_name`.
Example: `dbo.uspGetManagerEmployees` → test proc = `dbo.uspGetManagerEmployees_opt_test`

**Build the test-proc SQL:** read `optimize/candidate_sp.sql` and replace the proc name in the `CREATE OR ALTER PROCEDURE` header with the test-proc name. Keep the rest of the SQL identical.

Call `deploy_sp` with the test-proc SQL. **Maximum 3 attempts:**
- If `deploy_sp` returns an error: fix the SQL syntax error in `optimize/candidate_sp.sql`, rebuild the test-proc SQL, and retry
- After 3 failed attempts, record a `deploy_error` failure:
  - Compute `iteration = state.iteration + 1`
  - Update `optimize/state.json`: increment `iteration`, set `last_result` to `{"status": "deploy_error"}`, append `{"iteration": <N>, "outcome": "deploy_error"}` to `techniques_tried`
  - Append to `optimize/benchmark_log.md`:
    ```

    ## Iteration <N> — DEPLOY ERROR: <technique name>
    - Failed to deploy test proc after 3 attempts
    ```
  - Write skill snapshot (Step 9)
  - Output `DEPLOY ERROR (iteration N): gave up after 3 attempts` and stop

---

## Step 6 — Correctness check

Find all test case directories: `optimize/test_cases/tc_*/`, sorted by name.

For each test case:
1. Read its `params.sql` and extract the parameter values from the `EXEC` statement as a comma-separated `@param=value` string.
   - Strip surrounding single-quotes from string values only if the value contains no commas or equals signs
   - **Hard stop:** if any parameter value (after removing quotes) contains a comma or equals sign, output: `PARAMETER ERROR: value in <tc_dir>/params.sql cannot be safely passed via the MCP parameter format (contains comma or equals sign). Simplify the value before continuing.` Then stop without updating state.
   - Pass `null` if the proc takes no parameters
2. Call `execute_sp` with `spName = <test-proc name>` (the `_opt_test` variant) and the extracted parameters
3. Read `optimize/test_cases/<tc_dir>/golden_output.csv`
4. Split both the returned output and the golden file content into lines, trim each line, and drop trailing empty lines. Compare line-by-line. An exact match is required.

**If ANY test case output differs from golden:**
- The real proc was never touched — no rollback needed
- Compute `iteration = state.iteration + 1`
- Update `optimize/state.json`:
  - `iteration` → incremented value
  - `last_result` → `{"status": "correctness_failure", "correctness": {<per-tc results>}}`
  - Append to `techniques_tried`: `{"iteration": <N>, "outcome": "correctness_failure"}`
- Append to `optimize/benchmark_log.md`:
  ```

  ## Iteration <N> — CORRECTNESS FAILURE: <technique name>
  - <tc_dir>: OK
  - <tc_dir>: FAIL — <diff summary>
  ```
- Write skill snapshot (Step 9)
- Output `CORRECTNESS FAILURE (iteration N): <technique name>` and stop

---

## Step 7 — Benchmark

For each test case:
1. Extract parameters the same way as Step 6 (same hard-stop rule applies).
2. Call `run_benchmark` with `spName = <test-proc name>` and those parameters, **`n_runs + 1` times**.
3. **Discard the first call's result** (warm-up run — absorbs plan compilation cost).
4. For each of the remaining `n_runs` calls, parse the STATISTICS output:
   - Logical reads: sum all matches of `logical reads (\d+)` (case-insensitive)
   - CPU time ms: match `CPU time = (\d+) ms`
   - Elapsed time ms: match `elapsed time = (\d+) ms`
5. Average the `n_runs` results for this test case.

Sum `logical_reads` across all test cases → `total_logical_reads`.

---

## Step 8 — Accept or reject

Compute `improvement_lr = state.best_score - total_logical_reads` and `improvement_pct = round(improvement_lr / state.best_score * 100, 2)`.

**Accept threshold:** improvement must be ≥ 0.5% (`improvement_pct >= 0.5`). A smaller difference is within measurement noise and is treated as no improvement.

### If `total_logical_reads < state.best_score` AND `improvement_pct >= 0.5` → ACCEPTED

Promote the candidate to the real proc: call `deploy_sp` with the full content of `optimize/candidate_sp.sql` (the original version with the real proc name — NOT the `_opt_test` version).

**If promotion fails:**
- The real proc is still the previous best (it was never touched) — do not update `state.json` or `current_sp.sql`
- Output: `PROMOTION FAILED (iteration N): deploy_sp failed during acceptance. Real proc is unchanged. Manual intervention required.` and stop

On successful promotion:
- Write `optimize/candidate_sp.sql` content to `optimize/current_sp.sql` (overwrite)
- Compute `iteration = state.iteration + 1`
- Update `optimize/state.json`:
  - `iteration` → incremented value
  - `best_score` → `total_logical_reads`
  - `best_score_breakdown` → `{<tc_dir>: {logical_reads, cpu_time_ms, elapsed_time_ms}, ...}`
  - `last_result` → `{"status": "accepted", "total_logical_reads": ..., "improvement_pct": ...}`
  - Append to `techniques_tried`: `{"iteration": <N>, "outcome": "accepted", "score": <total_lr>}`
  - Append to `techniques_succeeded`: `{"iteration": <N>, "score": <total_lr>, "improvement_pct": <pct>}`
- Append to `optimize/benchmark_log.md`:
  ```

  ## Iteration <N> — ACCEPTED (+<improvement_pct>%): <technique name>
  - Total logical reads: <total> (was <previous_best>)
  - <tc_dir>: <lr> LR | <cpu>ms CPU
  ```
- Write skill snapshot (Step 9)
- Output:
  ```
  ACCEPTED (iteration N): <technique name>
  Improvement: X% — Y logical reads → Z logical reads
  Skill updated: optimize/skills/iter_NN_sql_server_optimization.md
  ```

### If below threshold → REJECTED

The real proc was never touched — no rollback needed.

- Compute `iteration = state.iteration + 1`
- Update `optimize/state.json`:
  - `iteration` → incremented value
  - `last_result` → `{"status": "rejected", "total_logical_reads": ..., "improvement_pct": ...}`
  - Append to `techniques_tried`: `{"iteration": <N>, "outcome": "rejected", "score": <total_lr>}`
- Append to `optimize/benchmark_log.md`:
  ```

  ## Iteration <N> — REJECTED: <technique name>
  - Total logical reads: <total> (best: <state.best_score>, improvement: <improvement_pct>%)
  ```
- Write skill snapshot (Step 9)
- Output:
  ```
  REJECTED (iteration N): <technique name>
  Score: Y logical reads (best remains Z, improvement <improvement_pct>% < 0.5% threshold)
  Skill updated: optimize/skills/iter_NN_sql_server_optimization.md
  Will try a different approach next iteration.
  ```

---

## Step 9 — Update the skill file

Determine the iteration number: `NN = state.iteration` (the value in `state.json` after you updated it in Step 5, 6, or 8).

Read the content of the previous skill snapshot (`iter_(NN-1)_sql_server_optimization.md`).
Append a new entry to the **Proc-Specific Findings** section using this format:

```markdown
### Iteration NN — <Technique Name> — <ACCEPTED / REJECTED / CORRECTNESS FAILURE / DEPLOY ERROR>
- **What was changed:** (specific SQL construct that was modified)
- **Hypothesis:** (why you expected this to reduce logical reads)
- **Result:** total logical reads X → Y (Z%)
- **Why it worked / failed:** (what the benchmark or diff revealed)
- **Lesson for future iterations:** (what this tells us about the proc's bottlenecks)
```

For `correctness_failure` entries, replace the Result line with:
```
- **Failed test cases:** <list with diff summary>
```

For `deploy_error` entries, replace Result and Why lines with:
```
- **Error:** deploy_sp failed after 3 attempts — <last error message>
```

If the technique was in the General Techniques checklist, also mark it inline as `✓ Tried — <ACCEPTED/REJECTED> (iter NN)`.

Save the updated content as `optimize/skills/iter_NN_sql_server_optimization.md`.
Do NOT modify the previous snapshot — each file is a permanent record.
