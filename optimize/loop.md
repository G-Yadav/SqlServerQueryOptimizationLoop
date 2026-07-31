# SP Optimization Loop — Iteration Instructions

Your goal: make the stored procedure faster without changing its output on any test case.

---

## Step 0 — Restore the known-good proc

Before reading state, guarantee the live proc matches the current best version. If a previous iteration was interrupted between deploy (Step 5) and accept/reject (Step 8) — by a crash, context compaction, or manual stop — the database may still hold an unverified candidate while `current_sp.sql` reflects the last accepted version.

- Read `optimize/current_sp.sql` and call `deploy_sp` with its full content.
- If `optimize/current_sp.sql` does not exist, the loop has not been initialised — output `LOOP ERROR: optimize/current_sp.sql not found — run optimize/init.md first.` and stop.
- This is idempotent (`ALTER PROCEDURE`); on a clean start it re-deploys the same definition and costs nothing.

---

## Step 1 — Read current state

Read ALL of the following before doing anything else:

- `optimize/state.json` — iteration counter, best score, techniques tried
- `optimize/config.json` — `proc_name`, `n_runs`, `max_iterations`, `max_consecutive_failures`
- `optimize/current_sp.sql` — the current best version of the stored procedure
- `optimize/skills/findings.md` — what has been tried on this proc so far

---

## Step 2 — Check stopping conditions

**Max iterations:** if `state.iteration >= config.max_iterations`, output:

```
LOOP COMPLETE: max iterations reached.
Best score: <state.best_score> total logical reads.
```

Then stop.

**Consecutive failures:** count the trailing streak of non-accepted outcomes (`rejected`, `correctness_failure`, `deploy_error`) at the end of `state.techniques_tried`. If this streak ≥ `config.max_consecutive_failures`, output:

```
LOOP STOPPED: <N> consecutive non-accepted iterations — ideas may be exhausted.
Best score: <state.best_score> total logical reads.
See optimize/skills/findings.md for a full record of what was tried.
```

Then stop.

---

## Step 3 — Reason and generate hypothesis

Read `optimize/current_sp.sql` and `optimize/skills/findings.md`. Reason freely about what is most likely to reduce logical reads on this specific proc:

- Do not repeat any technique already recorded in `findings.md`
- If the proc definition or prior findings suggest a specific bottleneck, gather evidence before deciding — call `get_execution_plan`, `get_table_ddl`, or `get_row_count` if useful
- Read `optimize/skills/techniques.md` if you need general SQL Server optimization patterns to draw from

Write 1–2 sentences stating your hypothesis and why you expect it to reduce logical reads.

---

## Step 4 — Write the candidate SP

Write your optimized stored procedure to `optimize/candidate_sp.sql`.

Rules:
- Use exactly `ALTER PROCEDURE <proc_name>` as the header — `proc_name` comes from `config.json`
- Keep all original parameters and their data types
- Only modify the body

---

## Step 5 — Deploy the candidate SP

Call `deploy_sp` with the full content of `optimize/candidate_sp.sql`. This deploys directly over the real proc.

**On deploy error — maximum 3 attempts:**
- Fix the SQL syntax error in `candidate_sp.sql` and retry
- After 3 failed attempts:
  - `iteration = state.iteration + 1`
  - Update `optimize/state.json`: increment `iteration`, set `last_result` to `{"status": "deploy_error"}`, append `{"iteration": <N>, "outcome": "deploy_error"}` to `techniques_tried`
  - Append to `optimize/skills/findings.md` (see Step 9 format)
  - Output `DEPLOY ERROR (iteration N): gave up after 3 attempts` and stop

---

## Step 6 — Correctness check

Find all test case directories: `optimize/test_cases/tc_*/`, sorted by name.

For each test case:
1. Read `params.sql` — the file contains a raw semicolon-separated `@param=value` string (e.g. `@BusinessEntityID=2;@MaxDepth=3`), or is empty/absent if the proc takes no parameters.
   - **Hard stop:** if any parameter value contains a semicolon, output: `PARAMETER ERROR: value in <tc_dir>/params.sql cannot be safely passed (contains semicolon).` Then stop without updating state.
   - Pass `null` if the file is empty or the proc takes no parameters
2. Call `execute_sp` with `spName = proc_name`, the extracted parameters, and `outputFilePath = /tmp/opt_candidate_<tc_dir>.csv`
3. Run: `python3 optimize/compare_csv.py optimize/test_cases/<tc_dir>/golden_output.csv /tmp/opt_candidate_<tc_dir>.csv`
4. If the script exits non-zero, capture its output as the diff summary — this test case failed

**On any correctness failure:**
- Restore the proc: call `deploy_sp` with the full content of `optimize/current_sp.sql`
- `iteration = state.iteration + 1`
- Update `optimize/state.json`: increment `iteration`, set `last_result` to `{"status": "correctness_failure"}`, append `{"iteration": <N>, "outcome": "correctness_failure"}` to `techniques_tried`
- Append to `optimize/skills/findings.md` (see Step 9 format)
- Output `CORRECTNESS FAILURE (iteration N): <technique name>` and stop

---

## Step 7 — Benchmark

Read each test case's `params.sql` in sorted order — same format and hard-stop rule as Step 6.

Make a single `benchmark_all` call:
- `spName = proc_name`
- `parameterSets` = the test cases' parameter strings joined by newlines, in sorted order — one line per test case. Use a blank line for a test case that takes no parameters.
- `nRuns = config.n_runs`

The server runs each set `n_runs + 1` times (discarding the warm-up), parses and averages the STATISTICS output, and returns one line per test case (`run_1`, `run_2`, … matching the input order) as `logical_reads=N, cpu_ms=N, elapsed_ms=N`, followed by `total_logical_reads=N`.

Set `total_logical_reads` = the returned `total_logical_reads`. Map each `run_i` back to its test case by position for the `best_score_breakdown`.

---

## Step 8 — Accept or reject

`improvement_pct = round((state.best_score - total_logical_reads) / state.best_score * 100, 2)`

**Accept threshold:** `improvement_pct >= 0.5`

### Accepted

- Write `optimize/candidate_sp.sql` content to `optimize/current_sp.sql`
- `iteration = state.iteration + 1`
- Update `optimize/state.json`:
  - `iteration` → incremented value
  - `best_score` → `total_logical_reads`
  - `best_score_breakdown` → `{<tc_dir>: {logical_reads, cpu_time_ms, elapsed_time_ms}, ...}`
  - `last_result` → `{"status": "accepted", "total_logical_reads": ..., "improvement_pct": ...}`
  - Append to `techniques_tried`: `{"iteration": <N>, "outcome": "accepted", "score": <total_lr>}`
  - Append to `techniques_succeeded`: `{"iteration": <N>, "score": <total_lr>, "improvement_pct": <pct>}`
- Append to `optimize/skills/findings.md` (see Step 9)
- Output:
  ```
  ACCEPTED (iteration N): <technique name>
  Improvement: X% — Y logical reads → Z logical reads
  ```

### Rejected

- Restore the proc: call `deploy_sp` with the full content of `optimize/current_sp.sql`
- `iteration = state.iteration + 1`
- Update `optimize/state.json`:
  - `iteration` → incremented value
  - `last_result` → `{"status": "rejected", "total_logical_reads": ..., "improvement_pct": ...}`
  - Append to `techniques_tried`: `{"iteration": <N>, "outcome": "rejected", "score": <total_lr>}`
- Append to `optimize/skills/findings.md` (see Step 9)
- Output:
  ```
  REJECTED (iteration N): <technique name>
  Score: Y logical reads (best: Z, improvement: <improvement_pct>% < 0.5% threshold)
  ```

---

## Step 9 — Append to findings.md

Append the following to `optimize/skills/findings.md`:

```markdown
### Iteration N — <Technique Name> — <ACCEPTED | REJECTED | CORRECTNESS FAILURE | DEPLOY ERROR>

<Free-form summary: what was changed, why you expected it to help, what the result showed,
and what this tells you about the proc's bottlenecks. Be specific enough that a future
iteration can build on this without repeating the same mistake. Include before/after scores
for accepted and rejected iterations. For correctness failures, include which test case
failed and the diff. For deploy errors, include the last error message.>
```
