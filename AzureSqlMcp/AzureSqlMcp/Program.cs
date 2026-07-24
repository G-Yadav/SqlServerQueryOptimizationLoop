using AzureSqlMcp.Application;
using AzureSqlMcp.Infrastructure;
using AzureSqlMcp.Presentation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddOptions<SqlConnectionOptions>()
    .Configure(o => o.ConnectionString = builder.Configuration["AZURE_CONN_STRING"] ?? string.Empty)
    .Validate(o => !string.IsNullOrEmpty(o.ConnectionString), "AZURE_CONN_STRING is not set.")
    .ValidateOnStart();

// Infrastructure
builder.Services.AddSingleton<ISqlConnectionFactory, SqlConnectionFactory>();
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
