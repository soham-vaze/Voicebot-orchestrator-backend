using VoiceBot.Application.Interfaces;
using VoiceBot.Application.Services;
using VoiceBot.Infrastructure.Backends;

var builder = WebApplication.CreateBuilder(args);

// -----------------------------------------------------------------------
// ✅ LOGGING CONFIGURATION
// -----------------------------------------------------------------------
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var logger = LoggerFactory
    .Create(logging => logging.AddConsole())
    .CreateLogger("Program");

logger.LogInformation("🚀 Starting VoiceBot API...");

// -----------------------------------------------------------------------
// Controllers & API explorer
// -----------------------------------------------------------------------
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

logger.LogInformation("✅ Controllers and Swagger configured");

// -----------------------------------------------------------------------
// HttpClient Configuration
// -----------------------------------------------------------------------
builder.Services.AddHttpClient("PythonPipeline", client =>
{
    var baseUrl = builder.Configuration["PythonPipeline:BaseUrl"]
        ?? throw new InvalidOperationException("Missing config key: PythonPipeline:BaseUrl");

    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromMinutes(5);

    logger.LogInformation("🌐 PythonPipeline HttpClient configured with BaseUrl: {BaseUrl}", baseUrl);
});

// -----------------------------------------------------------------------
// Pipeline Mode Selection
// -----------------------------------------------------------------------
var pipelineMode = builder.Configuration["PipelineMode"] ?? "Fast";

logger.LogInformation("⚙️ Selected Pipeline Mode: {PipelineMode}", pipelineMode);

switch (pipelineMode)
{
    case "Fast":
        builder.Services.AddScoped<ILlmBackend>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var http = factory.CreateClient("PythonPipeline");
            var backendLogger = sp.GetRequiredService<ILogger<FastPipelineBackend>>();

            backendLogger.LogInformation("🧠 FastPipelineBackend initialized");

            return new FastPipelineBackend(http, backendLogger);
        });
        break;

    case "WhatsAppTeam":
        builder.Services.AddScoped<ILlmBackend, WhatsAppTeamBackend>();
        logger.LogWarning("⚠️ Using WhatsAppTeamBackend (may be incomplete)");
        break;

    default:
        logger.LogError("❌ Invalid PipelineMode: {PipelineMode}", pipelineMode);
        throw new InvalidOperationException(
            $"Unknown PipelineMode '{pipelineMode}'. Valid values: \"Fast\", \"WhatsAppTeam\".");
}

// -----------------------------------------------------------------------
// Orchestrator
// -----------------------------------------------------------------------
builder.Services.AddScoped<IVoiceOrchestrator, VoiceOrchestrator>();
logger.LogInformation("🎯 VoiceOrchestrator registered");

// -----------------------------------------------------------------------
// CORS
// -----------------------------------------------------------------------
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

logger.LogInformation("🌍 CORS policy configured");

// -----------------------------------------------------------------------
// Build App
// -----------------------------------------------------------------------
var app = builder.Build();

logger.LogInformation("🏗️ Application built successfully");

// -----------------------------------------------------------------------
// Middleware pipeline
// -----------------------------------------------------------------------
app.UseCors("AllowFrontend");

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

logger.LogInformation("🚦 Middleware pipeline configured");

// -----------------------------------------------------------------------
// Run App
// -----------------------------------------------------------------------
logger.LogInformation("🔥 VoiceBot API is running...");
app.Run();