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

### `python` → `python3` in loop.md and init.md
Both files call `python optimize/compare_csv.py` for correctness diffs. macOS does not ship a `python` binary — only `python3`. Any machine without an explicit `python` alias will hit a hard stop mid-iteration with no meaningful error. Change both call sites to `python3`.

### Mid-iteration interruption leaves proc in candidate state
If context compaction or a crash occurs between Step 5 (deploy candidate) and Step 6 (correctness check), the live proc is left as the candidate while `state.json` and `current_sp.sql` still reflect the previous accepted version. On next restart the loop reads `current_sp.sql` but that no longer matches what is deployed. Fix: add a Step 0 at the top of `loop.md` that always calls `deploy_sp` with the content of `current_sp.sql` before reading state, guaranteeing the live proc is known-good at the start of every iteration.

### Benchmark token/call overhead — implement Option B
`run_benchmark` is called `n_runs + 1` times per test case, returning raw `STATISTICS IO/TIME` text (~350 tokens each). With `n_runs=3` and 3 test cases that is 12 tool calls and ~4,200 tokens of raw output per iteration — just for benchmarking. Option B moves the multi-run loop, warm-up discard, parsing, and averaging into the MCP server so the loop makes one call per test case and receives `logical_reads=N, cpu_ms=N, elapsed_ms=N` back. Implementation plan is at `/Users/macklov/.claude/plans/quirky-bubbling-pixel.md`.

### Benchmark calls should be parallelised across test cases
`loop.md` Step 7 implies sequential per-test-case calls. All test cases are independent — their benchmark calls can be issued in parallel, reducing wall-clock time per benchmark round by the number of test cases. Update Step 7 to explicitly say to call `run_benchmark` for all test cases simultaneously within each round.

### `params.sql` format is not validated at init time
`init.md` expects `params.sql` to contain a raw `@param=value` semicolon-separated string. Users naturally write EXEC statements. The mismatch causes a wrong-parameter call with no error. `init.md` Step 4 should check the file for `EXEC` or SQL comment prefixes and tell the user the expected format before proceeding.

### `init.md` should auto-populate `initial_sp.sql` from the database
Step 2 requires the user to manually paste the procedure into `initial_sp.sql`, then immediately deploys it — but the proc is already in the database. `init.md` could call `get_sp_definition` using `proc_name` from `config.json`, write the result (with `CREATE` replaced by `ALTER`) to `initial_sp.sql`, and skip the manual paste. The manual paste step would only be needed when the proc does not yet exist in the database.
