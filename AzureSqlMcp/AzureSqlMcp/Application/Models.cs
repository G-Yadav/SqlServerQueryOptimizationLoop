namespace AzureSqlMcp;

public record ColumnDef(
    string Name, string TypeStr,
    bool IsIdentity, long? Seed, long? Increment,
    bool IsNullable, string? Default);

public record ConstraintDef(string Keyword, string Name, string Columns);

public record IndexDef(string Name, bool IsClustered, bool IsUnique, string KeyColumns, string? IncludedColumns);

public record ForeignKeyDef(string Name, string FkColumns, string RefTable, string RefColumns, string? OnDelete, string? OnUpdate);

public record TableDdlData(
    string Schema, string Name,
    IReadOnlyList<ColumnDef> Columns,
    IReadOnlyList<ConstraintDef> Constraints,
    IReadOnlyList<IndexDef> Indexes,
    IReadOnlyList<ForeignKeyDef> ForeignKeys);

public record SpExecutionStats(
    long ExecutionCount, long AvgElapsedUs,
    long AvgLogicalReads, long AvgCpuUs,
    DateTime LastExecutionTime);
