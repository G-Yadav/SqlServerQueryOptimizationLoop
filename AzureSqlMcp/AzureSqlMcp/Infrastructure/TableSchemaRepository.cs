using Microsoft.Data.SqlClient;

namespace AzureSqlMcp;

public class TableSchemaRepository(ISqlConnectionFactory db) : ITableSchemaRepository
{
    public async Task<TableDdlData?> GetTableDdlAsync(string tableName)
    {
        await using var conn = await db.OpenConnectionAsync();

        var objectId = await ResolveObjectIdAsync(conn, tableName);
        if (objectId == null) return null;

        var (schema, name) = await ReadTableNameAsync(conn, objectId.Value);
        var columns     = await ReadColumnsAsync(conn, objectId.Value);
        var constraints = await ReadConstraintsAsync(conn, objectId.Value);
        var indexes     = await ReadIndexesAsync(conn, objectId.Value);
        var foreignKeys = await ReadForeignKeysAsync(conn, objectId.Value);

        return new TableDdlData(schema, name, columns, constraints, indexes, foreignKeys);
    }

    public async Task<string> GetRowCountAsync(string objectName)
    {
        await using var conn = await db.OpenConnectionAsync();

        string? schemaName = null, objName = null;
        {
            var cmd = new SqlCommand(@"
                SELECT s.name, o.name
                FROM sys.objects o
                JOIN sys.schemas s ON o.schema_id = s.schema_id
                WHERE o.object_id = OBJECT_ID(@name) AND o.type IN ('U', 'V')", conn);
            cmd.Parameters.AddWithValue("@name", objectName);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync())
                return $"'{objectName}' not found or is not a table or view.";
            schemaName = r.GetString(0);
            objName    = r.GetString(1);
        }

        // Names sourced from sys catalog, not user input — safe to interpolate
        var countCmd = new SqlCommand($"SELECT COUNT_BIG(*) FROM [{schemaName}].[{objName}]", conn);
        var result = await countCmd.ExecuteScalarAsync();
        return ((long)result!).ToString("N0");
    }

    private static async Task<int?> ResolveObjectIdAsync(SqlConnection conn, string tableName)
    {
        var cmd = new SqlCommand("SELECT OBJECT_ID(@name, 'U')", conn);
        cmd.Parameters.AddWithValue("@name", tableName);
        var result = await cmd.ExecuteScalarAsync();
        return result == null || result == DBNull.Value ? null : (int)result;
    }

    private static async Task<(string Schema, string Name)> ReadTableNameAsync(SqlConnection conn, int objectId)
    {
        var cmd = new SqlCommand("SELECT SCHEMA_NAME(schema_id), name FROM sys.tables WHERE object_id = @id", conn);
        cmd.Parameters.AddWithValue("@id", objectId);
        await using var r = await cmd.ExecuteReaderAsync();
        await r.ReadAsync();
        return (r.GetString(0), r.GetString(1));
    }

    private static async Task<IReadOnlyList<ColumnDef>> ReadColumnsAsync(SqlConnection conn, int objectId)
    {
        var cmd = new SqlCommand(@"
            SELECT
                c.name          AS col_name,    -- 0
                tp.name         AS type_name,   -- 1
                c.max_length,                   -- 2
                c.precision,                    -- 3
                c.scale,                        -- 4
                c.is_nullable,                  -- 5
                c.is_identity,                  -- 6
                ic2.seed_value,                 -- 7
                ic2.increment_value,            -- 8
                dc.definition   AS default_def  -- 9
            FROM sys.columns c
            JOIN sys.types tp ON c.user_type_id = tp.user_type_id
            LEFT JOIN sys.identity_columns ic2 ON c.object_id = ic2.object_id AND c.column_id = ic2.column_id
            LEFT JOIN sys.default_constraints dc ON c.default_object_id = dc.object_id
            WHERE c.object_id = @id
            ORDER BY c.column_id", conn);
        cmd.Parameters.AddWithValue("@id", objectId);

        var cols = new List<ColumnDef>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var typeName   = r.GetString(1);
            var maxLen     = r.GetInt16(2);
            var prec       = r.GetByte(3);
            var scale      = r.GetByte(4);
            var isIdentity = r.GetBoolean(6);

            var typeStr = typeName.ToLower() switch
            {
                "nvarchar" or "nchar"                           => maxLen == -1 ? $"{typeName}(MAX)" : $"{typeName}({maxLen / 2})",
                "varchar" or "char" or "varbinary" or "binary"  => maxLen == -1 ? $"{typeName}(MAX)" : $"{typeName}({maxLen})",
                "decimal" or "numeric"                          => $"{typeName}({prec},{scale})",
                "datetime2" or "time" or "datetimeoffset"       => $"{typeName}({scale})",
                "float"                                         => prec <= 24 ? "real" : "float",
                _                                               => typeName
            };

            cols.Add(new ColumnDef(
                Name:       r.GetString(0),
                TypeStr:    typeStr,
                IsIdentity: isIdentity,
                Seed:       isIdentity && !r.IsDBNull(7) ? Convert.ToInt64(r.GetValue(7)) : null,
                Increment:  isIdentity && !r.IsDBNull(8) ? Convert.ToInt64(r.GetValue(8)) : null,
                IsNullable: r.GetBoolean(5),
                Default:    r.IsDBNull(9) ? null : r.GetString(9)));
        }
        return cols;
    }

    private static async Task<IReadOnlyList<ConstraintDef>> ReadConstraintsAsync(SqlConnection conn, int objectId)
    {
        var cmd = new SqlCommand(@"
            SELECT
                k.type,   -- 0: 'PK' or 'UQ'
                k.name,   -- 1
                STRING_AGG('[' + c.name + ']', ', ') WITHIN GROUP (ORDER BY ic.key_ordinal) AS cols -- 2
            FROM sys.key_constraints k
            JOIN sys.index_columns ic ON k.parent_object_id = ic.object_id AND k.unique_index_id = ic.index_id
            JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
            WHERE k.parent_object_id = @id AND k.type IN ('PK', 'UQ')
            GROUP BY k.type, k.name
            ORDER BY k.type ASC", conn); // ASC: 'PK' before 'UQ'
        cmd.Parameters.AddWithValue("@id", objectId);

        var defs = new List<ConstraintDef>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            defs.Add(new ConstraintDef(
                Keyword: r.GetString(0) == "PK" ? "PRIMARY KEY" : "UNIQUE",
                Name:    r.GetString(1),
                Columns: r.GetString(2)));
        return defs;
    }

    private static async Task<IReadOnlyList<IndexDef>> ReadIndexesAsync(SqlConnection conn, int objectId)
    {
        var cmd = new SqlCommand(@"
            SELECT
                i.name,       -- 0
                i.type_desc,  -- 1
                i.is_unique,  -- 2
                STRING_AGG('[' + c.name + ']' + CASE ic.is_descending_key WHEN 1 THEN ' DESC' ELSE '' END, ', ')
                    WITHIN GROUP (ORDER BY ic.key_ordinal) AS key_cols, -- 3
                (SELECT STRING_AGG('[' + c2.name + ']', ', ') WITHIN GROUP (ORDER BY ic2.index_column_id)
                 FROM sys.index_columns ic2
                 JOIN sys.columns c2 ON ic2.object_id = c2.object_id AND ic2.column_id = c2.column_id
                 WHERE ic2.object_id = i.object_id AND ic2.index_id = i.index_id AND ic2.is_included_column = 1
                ) AS included_cols -- 4
            FROM sys.indexes i
            JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id AND ic.is_included_column = 0
            JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
            WHERE i.object_id = @id AND i.type > 0 AND i.is_primary_key = 0 AND i.is_unique_constraint = 0
            GROUP BY i.name, i.type_desc, i.is_unique, i.index_id", conn);
        cmd.Parameters.AddWithValue("@id", objectId);

        var defs = new List<IndexDef>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            defs.Add(new IndexDef(
                Name:            r.GetString(0),
                IsClustered:     r.GetString(1) == "CLUSTERED",
                IsUnique:        r.GetBoolean(2),
                KeyColumns:      r.GetString(3),
                IncludedColumns: r.IsDBNull(4) ? null : r.GetString(4)));
        return defs;
    }

    private static async Task<IReadOnlyList<ForeignKeyDef>> ReadForeignKeysAsync(SqlConnection conn, int objectId)
    {
        var cmd = new SqlCommand(@"
            SELECT
                fk.name,  -- 0
                STRING_AGG('[' + fc.name + ']', ', ') WITHIN GROUP (ORDER BY fkc.constraint_column_id) AS fk_cols,  -- 1
                '[' + SCHEMA_NAME(rt.schema_id) + '].[' + rt.name + ']'                               AS ref_table, -- 2
                STRING_AGG('[' + rc.name + ']', ', ') WITHIN GROUP (ORDER BY fkc.constraint_column_id) AS ref_cols, -- 3
                fk.delete_referential_action_desc, -- 4
                fk.update_referential_action_desc  -- 5
            FROM sys.foreign_keys fk
            JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
            JOIN sys.columns fc ON fkc.parent_object_id = fc.object_id AND fkc.parent_column_id = fc.column_id
            JOIN sys.columns rc ON fkc.referenced_object_id = rc.object_id AND fkc.referenced_column_id = rc.column_id
            JOIN sys.tables rt ON fk.referenced_object_id = rt.object_id
            WHERE fk.parent_object_id = @id
            GROUP BY fk.name, rt.name, rt.schema_id, fk.delete_referential_action_desc, fk.update_referential_action_desc", conn);
        cmd.Parameters.AddWithValue("@id", objectId);

        var defs = new List<ForeignKeyDef>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var del = r.GetString(4);
            var upd = r.GetString(5);
            defs.Add(new ForeignKeyDef(
                Name:      r.GetString(0),
                FkColumns: r.GetString(1),
                RefTable:  r.GetString(2),
                RefColumns:r.GetString(3),
                OnDelete:  del == "NO_ACTION" ? null : del.Replace("_", " "),
                OnUpdate:  upd == "NO_ACTION" ? null : upd.Replace("_", " ")));
        }
        return defs;
    }
}
