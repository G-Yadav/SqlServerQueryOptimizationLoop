# SQL Server Optimization Techniques

Reference guide for T-SQL query optimization. Use this when you need general patterns to draw from — it is not a checklist to work through in order.

---

## SARGability and Type Mismatches

SARGable predicates can use ordered index access. Avoid functions or conversions on indexed columns.

| Non-SARGable | Safer pattern |
|---|---|
| `WHERE YEAR(OrderDate) = 2026` | `WHERE OrderDate >= '20260101' AND OrderDate < '20270101'` |
| `WHERE LEFT(Name, 3) = 'ABC'` | `WHERE Name LIKE 'ABC%'` |
| `WHERE Amount * 1.1 > 1000` | `WHERE Amount > 1000 / 1.1` |
| `WHERE CONVERT(date, Dt) = @d` | `WHERE Dt >= @d AND Dt < DATEADD(day, 1, @d)` |
| `WHERE VarcharCol = 123` | `WHERE VarcharCol = '123'` |

A syntactically SARGable predicate can still scan if a parameter, temp column, or join key has the wrong type or collation — check actual data types.

---

## Identifying the Root Bottleneck

Use actual execution plans and `STATISTICS IO, TIME`. Rank findings by measured impact:

- High logical reads or row reads
- Bad estimate vs actual row gaps
- Scans caused by non-SARGable predicates or missing access paths
- Key lookups multiplied by many executions
- Sort/hash/window spills from poor estimates or missing order
- `CONVERT_IMPLICIT` warnings on join or filter columns

---

## Proving Join Changes

Never remove or replace joins only because selected columns come from one table. Prove:

- **Does the join filter rows?** Compare base count vs joined count; check trusted foreign keys.
- **Does the join multiply rows?** Check uniqueness on the joined key.
- **Can it be replaced with `EXISTS`?** Use a semi-join when only existence is needed.
- **Are outer joins preserved?** Predicates in `WHERE` can accidentally convert `LEFT JOIN` to inner join.

Validate equivalence with `EXCEPT` in both directions before accepting any join change.

---

## Temp Tables and Staging

Temp tables are often the right optimization tool, but bad types create hidden conversions.

Before using a temp table:
- Match staged column types against source metadata (length, collation, precision, nullability)
- Add appropriate clustered or nonclustered indexes after load when row counts justify them
- Update temp-table statistics when phased optimization depends on accurate cardinality

Do not stage huge unfiltered tables — only stage after a selective filter.

---

## Rewrite Templates

| Situation | Template |
|---|---|
| Highly selective predicate before huge joins | Stage selective keys first, index the stage, then join |
| Huge detail table aggregated later | Aggregate early if grouping preserves semantics |
| Join only tests existence | Replace with `EXISTS` |
| OR across different columns | Split into `UNION ALL` branches with duplicate guards |
| Catch-all optional predicates | Dynamic SQL or targeted recompilation |
| Bad estimates from table variables/TVFs | Use temp tables, inline TVFs, or recompile |

---

## Parameter Sensitivity

Parameter sniffing occurs when a plan compiled for one value is reused for a very different value.

| Option | Best for | Caution |
|---|---|---|
| `OPTION (RECOMPILE)` | Infrequent or highly variable statements | Adds compile CPU |
| `OPTIMIZE FOR (@p = value)` | Stable representative value | Can age badly as data changes |
| `OPTIMIZE FOR UNKNOWN` | Average distribution is acceptable | Can be mediocre for all cases |
| Dynamic SQL | Optional predicates and varied shapes | Requires safe parameterization |
| Query Store hints | SQL Server 2022+ or Azure SQL, no code change | Monitor regressions |

---

## Execution Plan Signals

| Plan evidence | Likely action |
|---|---|
| Scan with residual predicate | Fix SARGability, key order, or filtered index |
| Seek with high rows read | Add more selective key columns |
| Key lookup repeated many times | Cover query or reduce outer rows first |
| Sort or hash spill | Fix estimates, reduce rows/width, add order-compatible index |
| `CONVERT_IMPLICIT` on column | Align parameter/temp/source data types |
| Estimate off by 10x+ | Check stats, skew, table variables, predicates |
| Missing-index warning | Treat as candidate only; merge with existing indexes |

---

## Statistics and Cardinality

```sql
DBCC SHOW_STATISTICS('dbo.TableName', 'IndexOrStatsName');
UPDATE STATISTICS dbo.TableName IndexOrStatsName WITH FULLSCAN;
```

For large partitioned tables, evaluate incremental statistics and filtered stats.
