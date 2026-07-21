# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

This repo is an **autonomous SQL Server stored procedure optimization loop**. Claude iteratively rewrites a target stored procedure, deploys it as a shadow test proc, verifies correctness against golden output, benchmarks logical reads, and accepts or rejects the change — all without human intervention. The primary metric is total logical reads (sum across all test cases), chosen for buffer-cache stability.

## Prerequisites

- .NET 10 SDK (the MCP server targets `net10.0`)
- `AZURE_CONN_STRING` environment variable — valid ADO.NET connection string to Azure SQL or SQL Server
- Claude Code with the AzureSqlMcp MCP server configured in `.claude/settings.json`

## C# Conventions

Follow `CODING_GUIDELINES.md` for all C# work in this repo. Key points: SOLID principles, `await using` for all disposables, one reader open per connection at a time, return error strings from tool methods (don't throw), and extract any logic shared by two or more methods into a private helper immediately.

## MCP Server

```bash
# Build and run
cd AzureSqlMcp/AzureSqlMcp
dotnet run

# Build only
dotnet build
```

The MCP server exposes four tools over stdio (ModelContextProtocol 2.0 preview):

| Tool | Purpose |
|---|---|
| `deploy_sp` | Deploy `CREATE OR ALTER PROCEDURE` or `ALTER PROCEDURE` SQL |
| `execute_sp` | Run a proc and return result set as CSV (correctness checks) |
| `run_benchmark` | Run a proc with `SET STATISTICS IO/TIME ON` and return raw output |
| `get_sp_definition` | Read current proc definition from the database |
| `get_execution_stats` | Read DMV-based historical execution stats |
| `get_table_ddl` | Retrieve table DDL: columns, types, PK, unique constraints, indexes, foreign keys |

`deploy_sp` rejects SQL that doesn't start with `ALTER PROCEDURE` or `CREATE OR ALTER PROCEDURE`. Parameters for `execute_sp` and `run_benchmark` are passed as comma-separated `@param=value` strings — values containing commas or equals signs cannot be safely passed and will cause a hard stop.

## Running the Optimization Loop

### One-time setup

1. Paste your stored procedure into `optimize/initial_sp.sql` (must start with `CREATE OR ALTER PROCEDURE`)
2. Set `proc_name`, `n_runs`, `max_iterations`, `max_consecutive_failures` in `optimize/config.json`
3. Add at least one test case: `optimize/test_cases/tc_01/params.sql` containing `EXEC dbo.YourProc @param=value`
4. Ask Claude to follow `optimize/init.md`

Init deploys the SP, captures golden CSV output per test case via `execute_sp`, benchmarks the baseline (`n_runs + 1` calls, first discarded), writes `optimize/state.json` and `optimize/benchmark_log.md`.

### Running iterations

```
/loop optimize/loop.md
```

Each iteration: reads state → generates one hypothesis → deploys as `<proc_name>_opt_test` → correctness diff vs golden CSV → benchmark → accepts (≥ 0.5% improvement) or rejects → writes versioned skill snapshot.

**The real proc is never touched until a candidate is accepted.** On rejection or correctness failure, no rollback is needed.

## State Files (auto-generated, do not edit manually)

| File | Purpose |
|---|---|
| `optimize/state.json` | Iteration counter, best score, techniques tried/succeeded |
| `optimize/current_sp.sql` | Current best version of the SP |
| `optimize/candidate_sp.sql` | LLM writes its proposed rewrite here each iteration |
| `optimize/benchmark_log.md` | Per-iteration outcome log (technique name + score) |
| `optimize/test_cases/tc_*/golden_output.csv` | Reference output captured during init |

## Skill File Versioning

`optimize/skills/` holds one snapshot per iteration: `iter_NN_sql_server_optimization.md`. `iter_00` is the seed (general SQL Server techniques checklist). After each iteration the loop reads the previous snapshot, appends a **Proc-Specific Findings** entry, and saves as `iter_NN` — the previous file is never overwritten. This lets you `diff iter_07.md iter_08.md` to see exactly what the loop learned in iteration 8.

## Stopping Conditions

The loop stops when either condition is met (checked at the start of each iteration):
- `state.iteration >= config.max_iterations`
- Trailing streak of non-accepted outcomes (`rejected`, `correctness_failure`, `deploy_error`) ≥ `config.max_consecutive_failures`

## STATISTICS Output Parsing

When parsing `run_benchmark` output:
- Logical reads: sum all matches of `logical reads (\d+)` (case-insensitive) across all table lines
- CPU time: `CPU time = (\d+) ms`
- Elapsed time: `elapsed time = (\d+) ms`
