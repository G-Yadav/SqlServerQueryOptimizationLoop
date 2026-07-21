using System.Text;

namespace AzureSqlMcp;

public static class DdlFormatter
{
    public static string Format(TableDdlData data)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"CREATE TABLE [{data.Schema}].[{data.Name}]");
        sb.AppendLine("(");

        var colLines        = data.Columns.Select(FormatColumn);
        var constraintLines = data.Constraints.Select(c => $"    CONSTRAINT [{c.Name}] {c.Keyword} ({c.Columns})");
        sb.AppendLine(string.Join(",\n", colLines.Concat(constraintLines)));
        sb.AppendLine(");");

        if (data.Indexes.Count > 0)
        {
            sb.AppendLine();
            foreach (var idx in data.Indexes)
            {
                var clust  = idx.IsClustered ? "CLUSTERED " : "NONCLUSTERED ";
                var unique = idx.IsUnique ? "UNIQUE " : "";
                sb.Append($"CREATE {unique}{clust}INDEX [{idx.Name}] ON [{data.Schema}].[{data.Name}] ({idx.KeyColumns})");
                if (idx.IncludedColumns != null) sb.Append($" INCLUDE ({idx.IncludedColumns})");
                sb.AppendLine(";");
            }
        }

        if (data.ForeignKeys.Count > 0)
        {
            sb.AppendLine();
            foreach (var fk in data.ForeignKeys)
            {
                sb.Append($"-- FK [{fk.Name}]: ({fk.FkColumns}) REFERENCES {fk.RefTable} ({fk.RefColumns})");
                if (fk.OnDelete != null) sb.Append($" ON DELETE {fk.OnDelete}");
                if (fk.OnUpdate != null) sb.Append($" ON UPDATE {fk.OnUpdate}");
                sb.AppendLine();
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static string FormatColumn(ColumnDef col)
    {
        var line = $"    [{col.Name}] {col.TypeStr}";
        if (col.IsIdentity) line += $" IDENTITY({col.Seed ?? 1},{col.Increment ?? 1})";
        line += col.IsNullable ? " NULL" : " NOT NULL";
        if (col.Default != null) line += $" DEFAULT {col.Default}";
        return line;
    }
}
