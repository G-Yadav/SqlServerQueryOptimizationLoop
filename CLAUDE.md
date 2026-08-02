# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

This repo is an **autonomous SQL Server stored procedure optimization loop**. Claude iteratively rewrites a target stored procedure, deploys it as a shadow test proc, verifies correctness against golden output, benchmarks logical reads, and accepts or rejects the change — all without human intervention. The primary metric is total logical reads (sum across all test cases), chosen for buffer-cache stability.

## Prerequisites

- .NET 10 SDK (the MCP server targets `net10.0`)
- `AZURE_CONN_STRING` environment variable — valid ADO.NET connection string to Azure SQL or SQL Server
- Claude Code with the AzureSqlMcp MCP server configured in `.claude/settings.json`

### Entra ID / MFA authentication (optional)

Two ways to authenticate with Microsoft Entra ID instead of a SQL login:

1. **Connection string only** — put an Entra auth mode directly in `AZURE_CONN_STRING`, e.g. `…;Authentication=Active Directory Interactive` or `Active Directory Device Code Flow`. `SqlClient` handles the MFA prompt and token refresh. No extra config; the prompt/device-code lands on the server's stderr.
2. **Token command** (`AZURE_TOKEN_COMMAND`) — for a headless, long-running loop. Set it to a shell command that prints an access token for the SQL resource, e.g. `az account get-access-token --resource https://database.windows.net/ --output json`. MFA is satisfied once out-of-band (e.g. a prior `az login`); the server runs the command to mint tokens and re-runs it to refresh ~5 min before expiry (parsing `accessToken`/`expires_on` from az JSON, or treating bare stdout as the token with a ~55 min default TTL). When `AZURE_TOKEN_COMMAND` is set, `AZURE_CONN_STRING` **must not** contain `Authentication`, `User ID`, or `Password` — they conflict with the access token, and startup fails fast if both are present. The token is applied via `SqlConnection.AccessToken`; the command is read from the environment only, never a tool parameter.

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

The MCP server exposes nine tools over stdio (ModelContextProtocol 2.0 preview):

| Tool | Purpose |
|---|---|
| `deploy_sp` | Deploy `ALTER PROCEDURE` SQL |
| `execute_sp` | Run a proc and return result set as CSV (correctness checks) |
| `run_benchmark` | Run a proc `nRuns + 1` times (first discarded as warm-up) and return averaged `logical_reads`, `cpu_ms`, `elapsed_ms` |
| `benchmark_all` | Benchmark a proc across several parameter sets in one call; returns per-set averages plus `total_logical_reads` |
| `get_execution_plan` | Run a proc and return the actual XML execution plan with runtime statistics |
| `get_sp_definition` | Read current proc definition from the database |
| `get_execution_stats` | Read DMV-based historical execution stats |
| `get_table_ddl` | Retrieve table DDL: columns, types, PK, unique constraints, indexes, foreign keys |
| `get_row_count` | Return exact row count for a table or view |

`deploy_sp` rejects SQL that doesn't start with `ALTER PROCEDURE`, matches a disallowed-pattern blocklist (`xp_cmdshell`, `OPENROWSET`/`OPENQUERY`/`OPENDATASOURCE`, `BULK INSERT`, `sp_OA*`, `DROP`), or contains a `DELETE` whose target is not a temp table (`#…`/`##…`) or table variable (`@…`) — deletes against persistent tables are blocked. Parameters for `execute_sp` and `run_benchmark` are passed as semicolon-separated `@param=value` strings (e.g. `@BusinessEntityID=2;@MaxDepth=3`) — values containing semicolons cannot be safely passed and will cause a hard stop. `benchmark_all` takes the same per-set format, with one set per line (newline-separated).

`run_benchmark` and `benchmark_all` parse `SET STATISTICS IO/TIME` output **server-side** — the raw STATISTICS text never reaches the model. Parsing runs the vendored open-source STATISTICS parser (`Infrastructure/Resources/StatsParser/parser.js`) in-process via Jint; there is no Node dependency.

## Running the Optimization Loop

### One-time setup

1. Set `proc_name`, `n_runs`, `max_iterations`, `max_consecutive_failures` in `optimize/config.json`
2. Add at least one test case: `optimize/test_cases/tc_01/params.sql` containing the raw semicolon-separated parameter string (e.g. `@BusinessEntityID=2`), or leave empty if the proc takes no parameters
3. Ask Claude to follow `optimize/init.md`

You don't paste the procedure anywhere — init populates `optimize/initial_sp.sql` from the database via `get_sp_definition` (`CREATE` → `ALTER`). The proc must already exist in the database, since `deploy_sp` can only `ALTER` an existing proc.

Init deploys the SP, captures golden CSV output per test case via `execute_sp` (written directly to file), benchmarks the baseline (`n_runs + 1` calls, first discarded), and writes `optimize/state.json`.

### Running iterations

```
/loop optimize/loop.md
```

Each iteration: reads state → generates one hypothesis → deploys candidate directly over the real proc → correctness diff vs golden CSV → benchmark → accepts (≥ 0.5% improvement) or rejects → appends to `findings.md`. On rejection or correctness failure, the real proc is restored from `current_sp.sql`.

## State Files (auto-generated, do not edit manually)

| File | Purpose |
|---|---|
| `optimize/state.json` | Iteration counter, best score, techniques tried/succeeded |
| `optimize/current_sp.sql` | Current best version of the SP |
| `optimize/candidate_sp.sql` | LLM writes its proposed rewrite here each iteration |
| `optimize/test_cases/tc_*/golden_output.csv` | Reference output captured during init |
| `optimize/skills/techniques.md` | General SQL Server optimization reference (static) |
| `optimize/skills/findings.md` | Append-only proc-specific findings written by the loop |

## Stopping Conditions

The loop stops when either condition is met (checked at the start of each iteration):
- `state.iteration >= config.max_iterations`
- Trailing streak of non-accepted outcomes (`rejected`, `correctness_failure`, `deploy_error`) ≥ `config.max_consecutive_failures`
