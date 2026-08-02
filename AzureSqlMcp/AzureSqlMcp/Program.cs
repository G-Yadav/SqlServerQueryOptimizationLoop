using AzureSqlMcp.Application;
using AzureSqlMcp.Infrastructure;
using AzureSqlMcp.Presentation;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddOptions<SqlConnectionOptions>()
    .Configure(o =>
    {
        o.ConnectionString = builder.Configuration["AZURE_CONN_STRING"] ?? string.Empty;
        o.TokenCommand = builder.Configuration["AZURE_TOKEN_COMMAND"];
    })
    .Validate(o => !string.IsNullOrEmpty(o.ConnectionString), "AZURE_CONN_STRING is not set.")
    .Validate(o => string.IsNullOrWhiteSpace(o.TokenCommand) || !ConnectionStringHasInlineAuth(o.ConnectionString),
        "When AZURE_TOKEN_COMMAND is set, AZURE_CONN_STRING must not contain Authentication, User ID, or Password — they conflict with an access token.")
    .ValidateOnStart();

// Infrastructure
builder.Services.AddSingleton<IAccessTokenProvider, CommandAccessTokenProvider>();
builder.Services.AddSingleton<ISqlConnectionFactory, SqlConnectionFactory>();
builder.Services.AddSingleton<IStatisticsParser, StatisticsParser>();
builder.Services.AddSingleton<ITableSchemaRepository, TableSchemaRepository>();
builder.Services.AddSingleton<ISpInspectionRepository, SpInspectionRepository>();
builder.Services.AddSingleton<ISpDeploymentRepository, SpDeploymentRepository>();
builder.Services.AddSingleton<ISpExecutionRepository, SpExecutionRepository>();

// Presentation
builder.Services.AddSingleton<SchemaTools>();
builder.Services.AddSingleton<SpInspectionTools>();
builder.Services.AddSingleton<SpDeploymentTools>();
builder.Services.AddSingleton<SpExecutionTools>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();

// True if the connection string carries inline credentials that would conflict with an access token.
static bool ConnectionStringHasInlineAuth(string connectionString)
{
    if (string.IsNullOrWhiteSpace(connectionString)) return false;
    var b = new SqlConnectionStringBuilder(connectionString);
    return b.Authentication != SqlAuthenticationMethod.NotSpecified
        || !string.IsNullOrEmpty(b.UserID)
        || !string.IsNullOrEmpty(b.Password);
}
