# SP Optimization Loop

Autonomously optimizes a SQL Server stored procedure by iteratively generating, testing, and benchmarking candidate rewrites. Each iteration is guided by a skill file that accumulates what has been tried and learned.

## Prerequisites

- Azure SQL MCP server running with `AZURE_CONN_STRING` set
- The stored procedure already exists in the database

## Setup

1. **Paste your stored procedure** into `initial_sp.sql`
2. **Set the proc name** in `config.json` (`proc_name`)
3. **Add test cases** — one `EXEC` call per file:
   ```
   test_cases/tc_01/params.sql
   test_cases/tc_02/params.sql
   ```
4. **Seed the skill file** — ensure `skills/iter_00_sql_server_optimization.md` exists
5. **Initialize** — ask Claude to follow `init.md`
6. **Run the loop** — `/loop optimize/loop.md`

## How it works

| Phase | What happens |
|---|---|
| Init | Deploys the initial SP, captures golden output per test case, benchmarks baseline |
| Each iteration | Generates one optimization hypothesis → deploys candidate → correctness check → benchmark → accept or rollback |
| Skill file | Every iteration appends its finding (accepted/rejected/failure) to a versioned snapshot |
| Stopping | Loop ends when `max_iterations` in `config.json` is reached |

## Key files

| File | Purpose |
|---|---|
| `config.json` | Proc name, number of benchmark runs, max iterations |
| `initial_sp.sql` | Your original stored procedure (you provide) |
| `current_sp.sql` | Current best version (auto-maintained) |
| `state.json` | Iteration counter, best score, history (auto-maintained) |
| `benchmark_log.md` | Per-iteration results log (auto-maintained) |
| `init.md` | Initialization instructions for Claude |
| `loop.md` | Per-iteration instructions for Claude |
| `skills/iter_NN_*.md` | Versioned skill snapshots — one per completed iteration |

## Primary metric

**Total logical reads** (summed across all test cases, averaged over `n_runs`). Buffer-cache-stable on warm runs; unaffected by network or I/O variance.
