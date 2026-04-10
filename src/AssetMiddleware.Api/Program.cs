using System.Text.Json.Serialization;
using AssetMiddleware.Api.Endpoints;
using AssetMiddleware.Application.Configuration;
using AssetMiddleware.Application.Handlers;
using AssetMiddleware.Application.Interfaces;
using AssetMiddleware.Application.Services;
using AssetMiddleware.Application.Transformers;
using AssetMiddleware.Domain.Models.Events;
using AssetMiddleware.Infrastructure.DependencyInjection;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// ── Configuration binding ──────────────────────────────────────────────────
builder.Services.Configure<ServiceBusOptions>(
    builder.Configuration.GetSection(ServiceBusOptions.SectionName));
builder.Services.Configure<AssetHubOptions>(
    builder.Configuration.GetSection(AssetHubOptions.SectionName));
builder.Services.Configure<ResilienceOptions>(
    builder.Configuration.GetSection(ResilienceOptions.SectionName));

// ── JSON: enums serialised as strings, not integers ───────────────────────
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

// ── OpenAPI (Scalar) ───────────────────────────────────────────────────────
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, ct) =>
    {
        document.Info.Title = "AssetMiddleware API";
        document.Info.Version = "v1";
        document.Info.Description =
            "FieldOps → AssetHub integration middleware. " +
            "Exposes DLQ replay and status endpoints.";
        return Task.CompletedTask;
    });
});

// ── Domain / Application layer ─────────────────────────────────────────────
builder.Services.AddSingleton<IAssetTransformer, AssetTransformer>();
builder.Services.AddScoped<IEventHandler<AssetRegistrationEvent>, RegistrationEventHandler>();
builder.Services.AddScoped<IEventHandler<AssetCheckInEvent>, CheckInEventHandler>();
builder.Services.AddScoped<EventRouter>();

// ── Infrastructure layer (Service Bus, HTTP client, resilience, cache) ─────
builder.Services.AddInfrastructure(builder.Configuration);

// ── Health checks ──────────────────────────────────────────────────────────
builder.Services.AddHealthChecks();

var app = builder.Build();

// ── Middleware pipeline ────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options.Title = "AssetMiddleware";
        options.Theme = ScalarTheme.Default;
        options.DefaultHttpClient = new(ScalarTarget.CSharp, ScalarClient.HttpClient);
    });
}

app.MapHealthChecks("/health");

// ── Endpoints ─────────────────────────────────────────────────────────────
app.MapDlqReplayEndpoint();
app.MapStatusEndpoint();

app.Run();
