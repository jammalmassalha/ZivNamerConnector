using SapSync.Core.Interfaces;
using SapSync.Core.Models;
using SapSync.Core.Services;
using SapSync.Infrastructure.Persistence;
using SapSync.Infrastructure.Sap;

const string AngularCorsPolicy = "AngularClient";

var builder = WebApplication.CreateBuilder(args);

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

// SAP NCo allows exactly one destination configuration per process -> Singleton.
builder.Services.AddSingleton<SapDestinationManager>();
builder.Services.AddSingleton<ISapDestinationManager>(sp => sp.GetRequiredService<SapDestinationManager>());
builder.Services.AddSingleton<IRfcDestinationProvider>(sp => sp.GetRequiredService<SapDestinationManager>());

// Per-request units of work -> Scoped.
builder.Services.AddScoped<ISapTableReader, SapTableReader>();
builder.Services.AddScoped<ISqlBulkWriter, SqlBulkWriter>();
builder.Services.AddScoped<ISapSyncOrchestrator, SapSyncOrchestrator>();

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
