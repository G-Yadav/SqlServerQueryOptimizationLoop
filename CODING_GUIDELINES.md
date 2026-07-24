# C# Coding Guidelines

## SOLID Principles

### Single Responsibility Principle
Each class and method does one thing. If a method's name requires "and" to describe it, split it.

```csharp
// Bad — parses AND executes
public async Task<string> ParseAndRunQuery(string sql) { ... }

// Good — each method has one job
private static string? AddParameters(SqlCommand cmd, string? parameters) { ... }
public static async Task<string> RunBenchmark(string spName, string? parameters) { ... }
```

A tool method is responsible for its MCP contract (input validation + result formatting). Shared mechanics (connection open, parameter parsing) belong in private helpers — not inlined into every tool.

### Open/Closed Principle
Code should be open for extension, closed for modification. Add new tools by adding new methods — don't modify existing ones to accommodate new behaviour. Shared helpers (`OpenConnectionAsync`, `AddParameters`) are the stable base; new tools compose them.

### Liskov Substitution Principle
Relevant when introducing abstractions. If you extract an interface (e.g. `ISqlToolExecutor`), any implementation must honour the same contract: same error return conventions, same null semantics, same async patterns. Do not introduce a subtype that silently swallows errors or returns different shapes.

### Interface Segregation Principle
Don't force callers to depend on methods they don't use. If the tool surface grows, split by concern rather than adding more methods to `SqlTools`. For example, schema inspection tools (`GetTableDdl`, `GetSpDefinition`) and execution tools (`RunBenchmark`, `ExecuteSp`) are natural seams.

### Dependency Inversion Principle
High-level logic should not depend on the concrete `SqlConnection` constructor directly. Depend on abstractions at boundaries — pass a connection factory or `IDbConnection` rather than calling `new SqlConnection(ConnString)` in every method. This also makes unit testing possible without a live database.

```csharp
// Preferred — connection creation is centralised and swappable
private static async Task<SqlConnection> OpenConnectionAsync()
{
    var conn = new SqlConnection(ConnString);
    await conn.OpenAsync();
    return conn;
}
```

---

## Clean Architecture

### The Dependency Rule
Dependencies only point inward. Outer layers know about inner layers; inner layers know nothing about outer layers or frameworks.

```
Presentation  →  Application  →  Domain
Infrastructure               →  Domain
```

Framework code (MCP, ASP.NET, EF Core) lives only in outer layers. If a domain or application class imports a NuGet package that isn't a pure utility (e.g. `Microsoft.Data.SqlClient`, `ModelContextProtocol`), it is in the wrong layer.

### Layers in this project

**Domain** — pure business logic with no framework dependencies. Currently absent; add it when rules or calculations emerge that are independent of SQL Server (e.g. benchmark result comparison, improvement threshold logic).

**Application** — orchestrates domain objects and defines interfaces for infrastructure. Owns use-case logic: "run benchmark, compare to baseline, decide accept/reject." Does not reference `SqlConnection` or MCP types directly — it calls interfaces.

**Infrastructure** — implements the interfaces defined by Application. All SQL Server access lives here: `SqlConnectionFactory`, `SpTools` query methods, `SchemaTools` query methods. Depends on `Microsoft.Data.SqlClient`.

**Presentation** — the MCP tool classes (`SpTools`, `SchemaTools`). Translate MCP input to application calls and format results as strings. The only layer allowed to reference `ModelContextProtocol`. Tool methods should be thin: validate input, call an application service, return the result. Business logic does not belong here.

### Practical rules

- Tool methods (`[McpServerTool]`) must not contain business logic. If a tool method is doing more than validating input, calling one service method, and formatting output, extract the logic into an application service.
- Interfaces for infrastructure belong in Application, not Infrastructure. `ISqlConnectionFactory` lives in the Application layer; `SqlConnectionFactory` in Infrastructure implements it.
- Never reference an outer layer from an inner one. Domain must not import `Microsoft.Data.SqlClient`. Application must not reference `ModelContextProtocol`.
- New features follow the same inward dependency: add a domain concept → add an application service that uses it → add infrastructure that implements the interface → wire the tool method to the service.

### Current layer mapping

| Class | Layer | Notes |
|---|---|---|
| `SqlConnectionFactory` | Infrastructure | Implements connection creation; owns the ADO.NET dependency |
| `SpInspectionTools`, `SpDeploymentTools`, `SpExecutionTools` | Presentation | MCP entry points; should stay thin |
| `SchemaTools` | Presentation | MCP entry point for DDL queries |
| DDL-reading helpers in `SchemaTools` | Infrastructure | SQL queries against system catalogs — belong in a dedicated infrastructure class if the layer is formalised |

---

## General C# Style

**Namespaces**
- Namespaces mirror the folder structure: `AzureSqlMcp.Application`, `AzureSqlMcp.Infrastructure`, `AzureSqlMcp.Presentation`.
- Each file's namespace must match its directory. A file in `Infrastructure/` uses `namespace AzureSqlMcp.Infrastructure;`.
- Cross-layer references are resolved with `using` directives, not by flattening namespaces (e.g. Infrastructure files add `using AzureSqlMcp.Application;`).

**Naming**
- Methods: `PascalCase`. Private helpers: `PascalCase`. Parameters and locals: `camelCase`.
- Async methods end in `Async` unless they are MCP tool entry points (the framework controls those names).
- Be descriptive: `AddParameters` beats `SetParams`.

**Async**
- All database calls are `async`/`await` end-to-end. Never `.Result` or `.Wait()`.
- Dispose connections and readers with `await using`, not `using`.
- Open one reader at a time per connection — SQL Server does not require MARS and enabling it adds complexity.

**Error handling**
- Return a descriptive error string from tool methods rather than throwing. The MCP framework surfaces exceptions as opaque errors; a returned string is visible to Claude.
- Validate inputs at the top of each tool method before opening a connection.

**Private helpers**
- Extract any logic shared by two or more tool methods into a `private static` helper immediately — don't wait for a third copy.
- Helpers return data or an error string (`string?` for nullable error); they do not write to the database or produce side effects beyond their name implies.

**SQL strings**
- Use verbatim string literals (`@"..."`) for multi-line SQL. Indent SQL keywords consistently.
- Never concatenate user-supplied values into SQL strings. Always use `cmd.Parameters.AddWithValue`.

**Brevity**
- Prefer expression-bodied members for single-expression methods.
- Use `switch` expressions over `if/else if` chains for type/value dispatch (e.g. type-name → SQL type string).
- No redundant comments that restate the code. Comments explain *why*, not *what*.
