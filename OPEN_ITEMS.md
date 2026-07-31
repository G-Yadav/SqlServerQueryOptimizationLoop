# Open Items

## MCP Server

### Verify database connectivity at startup
The MCP protocol provides no hook between handshake completion and the first tool call. If the server exits before the `initialize` handshake completes, the client (Claude Code, MCP Inspector) only sees a dead pipe — not the actual error. Options to explore:

- **Before `RunAsync()`** — run `SELECT 1` after `Build()`. Fails fast; error visible on stderr but not inside Claude's context.
- **First tool call** — let the first real call surface the error as a returned string. Claude can see and act on it, but startup looks healthy when it may not be.
- **`IHostedService.StartAsync`** — same client-visible behaviour as the `RunAsync()` option; more idiomatic for complex startup sequences.

Decision needed: who needs to see the error — the operator (stderr is enough) or Claude (needs a tool-call-time error string)?

### Lazy loading tool definitions
All tool schemas are sent to the client on `tools/list` during initialization. For 8 tools this is negligible, but worth revisiting if the tool count grows significantly. The protocol supports dynamic tool registration via `notifications/tools/list_changed` — server starts with an empty/minimal list, emits the notification when relevant tools are determined, client re-fetches. Client support is inconsistent in 2026 (Claude Code has open feature requests, most clients don't re-fetch after `list_changed`). Revisit when tool count approaches 50+ and context cost becomes measurable.

## Optimization Loop

### ~~`python` → `python3` in loop.md and init.md~~ ✅ Done
`loop.md` Step 6 called `python optimize/compare_csv.py` for correctness diffs. macOS does not ship a `python` binary — only `python3` — so any machine without an explicit `python` alias would hit a hard stop mid-iteration with no meaningful error. **Resolved** by changing the call site in `loop.md` to `python3` (and the usage docstring in `compare_csv.py`). Note: `init.md` never referenced `compare_csv`, so only `loop.md` needed the fix.

### ~~Mid-iteration interruption leaves proc in candidate state~~ ✅ Done
If context compaction or a crash occurred between Step 5 (deploy candidate) and Step 6 (correctness check), the live proc was left as the candidate while `state.json` and `current_sp.sql` still reflected the previous accepted version, so the next restart read a `current_sp.sql` that no longer matched what was deployed. **Resolved** by adding a Step 0 to `loop.md` that reads `optimize/current_sp.sql` and re-deploys it via `deploy_sp` before reading state — idempotent (`ALTER PROCEDURE`) on a clean start, and it guarantees the live proc is known-good at the start of every iteration. Step 0 also hard-stops with a clear message if `current_sp.sql` is missing (loop not initialised).

### ~~Benchmark token/call overhead — implement Option B~~ ✅ Done
`run_benchmark` was called `n_runs + 1` times per test case, returning raw `STATISTICS IO/TIME` text (~350 tokens each) — 12 tool calls and ~4,200 tokens per iteration. **Resolved** by moving the multi-run loop, warm-up discard, parsing, and averaging into the MCP server (a superset of the original Option B plan). `run_benchmark` now takes `nRuns` and returns `logical_reads=N, cpu_ms=N, elapsed_ms=N`; a new `benchmark_all` tool runs every test case in a single call and returns per-set averages plus `total_logical_reads`. Parsing is done server-side by the vendored open-source `parser.js` run in-process via Jint (`Infrastructure/Resources/StatsParser/`), so raw STATISTICS text never reaches the model. `loop.md` Step 7 and `init.md` Step 5 now make one `benchmark_all` call per round.

### ~~Benchmark calls should be parallelised across test cases~~ ✅ Superseded
The per-test-case calls are now collapsed into a single `benchmark_all` call per benchmark round (see above), so there is nothing left to parallelise at the loop level. If server-side wall-clock time becomes a concern, the batch loop inside `RunBenchmarkBatchAsync` could run sets concurrently on separate connections — but each set still needs its own connection to keep STATISTICS output isolated.

### ~~`params.sql` format is not validated at init time~~ ✅ Done
`init.md` expects `params.sql` to contain a raw `@param=value` semicolon-separated string, but users naturally write `EXEC` statements — a mismatch that caused a wrong-parameter call with no error. **Resolved** by adding a format hard-stop to `init.md` Step 4: after stripping whitespace, a non-empty file that isn't in raw `@param=value` form (begins with `--`/`/*`, contains `EXEC`/`EXECUTE`, or its first non-whitespace char isn't `@`) stops init with `PARAMETER FORMAT ERROR: …` and the expected format. Validation lives at init time (the setup gate); the loop reuses the already-validated files.

### ~~`init.md` should auto-populate `initial_sp.sql` from the database~~ ✅ Done
Step 2 previously required the user to manually paste the procedure into `initial_sp.sql` even though the proc was already in the database. **Resolved** by reworking `init.md` Step 2 into "Fetch and deploy the initial stored procedure": init **always** calls `get_sp_definition` with `proc_name`, replaces the leading `CREATE` of the `CREATE PROCEDURE` statement with `ALTER` (first occurrence only), and writes it to `initial_sp.sql` — no placeholder detection or user paste. Since `deploy_sp` can only `ALTER` an existing proc, a `Not found` result hard-stops init asking the user to create the proc in the DB first. The placeholder hard-stop was removed from Step 1, and `CLAUDE.md` one-time setup no longer mentions pasting.
