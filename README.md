# SQL Server Query Optimization Loop

An autonomous agent loop that iteratively optimizes SQL Server stored procedures using Claude and a custom MCP server. On each iteration, the agent proposes one targeted rewrite, deploys it as a test proc, verifies correctness against golden output, benchmarks it using `SET STATISTICS IO/TIME`, and accepts or rolls back — all without human intervention.

## How it works

1. **Init** — paste your stored procedure into `optimize/initial_sp.sql`, configure `optimize/config.json`, then ask Claude to follow `optimize/init.md`. This deploys the proc, captures golden output for each test case, and benchmarks the baseline.
2. **Loop** — run `/loop optimize/loop.md`. Each iteration:
   - Picks one untried optimization technique from the skill file
   - Deploys a candidate as a shadow proc (`_opt_test` suffix) — the real proc is never touched until acceptance
   - Runs a correctness check (CSV diff against golden output)
   - Benchmarks `n_runs` warm executions and averages logical reads
   - Accepts (promotes to real proc) if improvement ≥ 0.5%; otherwise rejects and tries again next iteration
   - Appends findings to a versioned skill file so each iteration builds on prior knowledge
3. **Stop** — loop ends when `max_iterations` is reached or `max_consecutive_failures` non-accepted iterations occur in a row

The primary metric is **total logical reads** across all test cases — stable across warm buffer-cache runs, unaffected by network or I/O variance.

## Repository layout

```
optimize/               # Agent instructions and runtime state
  init.md               # One-time initialization instructions for Claude
  loop.md               # Per-iteration instructions for Claude
  config.json           # proc_name, n_runs, max_iterations
  initial_sp.sql        # Your stored procedure (you provide)
  skills/               # Versioned skill snapshots (one per iteration)
  test_cases/           # One subdirectory per test case, each with params.sql

AzureSqlMcp/            # .NET MCP server that Claude calls as tools
  SqlTools.cs           # deploy_sp, execute_sp, run_benchmark, get_sp_definition
```

## Prerequisites

- .NET 8+ to run the MCP server
- An Azure SQL (or SQL Server) database with your stored procedure
- `AZURE_CONN_STRING` environment variable set to a valid ADO.NET connection string
- Claude Code with the AzureSqlMcp MCP server configured

## Quick start

```bash
# 1. Start the MCP server
cd AzureSqlMcp/AzureSqlMcp
dotnet run

# 2. Paste your SP and configure
#    - edit optimize/initial_sp.sql
#    - edit optimize/config.json  (set proc_name, n_runs, max_iterations)
#    - add optimize/test_cases/tc_01/params.sql  (one EXEC call)

# 3. Initialize
#    In Claude Code: ask Claude to "follow optimize/init.md"

# 4. Run the optimization loop
#    /loop optimize/loop.md
```
