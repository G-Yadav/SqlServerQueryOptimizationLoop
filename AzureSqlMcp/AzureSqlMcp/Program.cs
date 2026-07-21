using AzureSqlMcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);

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
