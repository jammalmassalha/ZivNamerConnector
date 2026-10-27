using Microsoft.Extensions.Options;
using SapSync.Api;
using SapSync.Core.Interfaces;
using SapSync.Core.Models;
using SapSync.Core.Services;
using SapSync.Infrastructure.Persistence;
using SapSync.Infrastructure.Sap;

const string AngularCorsPolicy = "AngularClient";

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    // null for developer builds -> ASP.NET Core resolves the environment as usual.
    EnvironmentName = BuildEnvironment.BakedEnvironment,
});

// Strongly typed, validated configuration.
builder.Services.AddOptions<SapSettings>()
    .Bind(builder.Configuration.GetSection(SapSettings.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<SqlSettings>()
    .Bind(builder.Configuration.GetSection(SqlSettings.SectionName))
    .PostConfigure(settings =>
    {
        if (string.IsNullOrWhiteSpace(settings.ConnectionString))
        {
            settings.ConnectionString = builder.Configuration.GetConnectionString(settings.ConnectionStringName);
        }
    })
    .ValidateDataAnnotations()
    .Validate(settings => !string.IsNullOrWhiteSpace(settings.ConnectionString), "A SQL Server connection string is required.")
    .ValidateOnStart();

builder.Services.AddOptions<SyncTablesSettings>()
    .Bind(builder.Configuration.GetSection(SyncTablesSettings.SectionName))
    .Validate(
        settings => settings.Tables.Values.All(t =>
            !string.IsNullOrWhiteSpace(t.TargetDbTable) && t.PrimaryKeyColumns.Count > 0 && t.Fields.Count > 0 && t.PageSize > 0),
        $"Every {SyncTablesSettings.SectionName} table needs TargetDbTable, PrimaryKeyColumns, Fields and a positive PageSize.")
    .ValidateOnStart();

// SAP NCo allows exactly one destination configuration per process -> Singleton.
builder.Services.AddSingleton<SapDestinationManager>();
builder.Services.AddSingleton<ISapDestinationManager>(sp => sp.GetRequiredService<SapDestinationManager>());
builder.Services.AddSingleton<IRfcDestinationProvider>(sp => sp.GetRequiredService<SapDestinationManager>());

// Per-request units of work -> Scoped.
builder.Services.AddScoped<ISapTableReader, SapTableReader>();
builder.Services.AddScoped<ISqlBulkWriter, SqlBulkWriter>();
builder.Services.AddScoped<ISapSyncOrchestrator, SapSyncOrchestrator>();
builder.Services.AddScoped<ITableSyncService, TableSyncService>();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
if (allowedOrigins is not { Length: > 0 })
{
    allowedOrigins = new[] { "http://localhost:4200" };
}

builder.Services.AddCors(options =>
{
    options.AddPolicy(AngularCorsPolicy, policy => policy
        .WithOrigins(allowedOrigins)
        .AllowCredentials()
        .AllowAnyMethod()
        .AllowAnyHeader());
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

var sapSettings = app.Services.GetRequiredService<IOptions<SapSettings>>().Value;
app.Logger.LogInformation(
    "Environment {Environment} (build: {BuildEnvironment}), SAP destination {SapDestination} on {SapHost} client {SapClient}",
    app.Environment.EnvironmentName,
    BuildEnvironment.BakedEnvironment ?? "developer",
    sapSettings.DestinationName,
    sapSettings.AppServerHost ?? sapSettings.MessageServerHost,
    sapSettings.Client);

if (BuildEnvironment.RuntimeOverrideIgnored)
{
    app.Logger.LogWarning(
        "ASPNETCORE_ENVIRONMENT/DOTNET_ENVIRONMENT={RuntimeEnvironment} is ignored: this is a {BuildEnvironment} build",
        BuildEnvironment.RuntimeEnvironment, BuildEnvironment.BakedEnvironment);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors(AngularCorsPolicy);
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program
{
}
