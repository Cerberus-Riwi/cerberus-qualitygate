using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QualityGateService.Config;
using QualityGateService.Data;
using QualityGateService.Rules;
using QualityGateService.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddEnvironmentVariables();

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "CERBERUS Quality Gate Service",
        Version = "v1",
        Description = "Evaluates normalized security findings and blocks unsafe deployments."
    });

    var xmlFileName = $"{typeof(Program).Assembly.GetName().Name}.xml";
    var xmlFilePath = Path.Combine(AppContext.BaseDirectory, xmlFileName);
    if (File.Exists(xmlFilePath))
    {
        options.IncludeXmlComments(xmlFilePath);
    }
});

builder.Services
    .AddOptions<QualityGateSettings>()
    .Bind(builder.Configuration.GetSection(QualityGateSettings.SectionName))
    .PostConfigure<IConfiguration>((settings, configuration) =>
    {
        settings.RabbitMQHost = GetSetting(configuration, "RABBITMQ_HOST", settings.RabbitMQHost);
        settings.RabbitMQQueue = GetSetting(configuration, "RABBITMQ_QUEUE", settings.RabbitMQQueue);
        settings.RabbitMQExchange = GetSetting(configuration, "RABBITMQ_EXCHANGE", settings.RabbitMQExchange);
        settings.RabbitMQResultsExchange = GetSetting(configuration, "RABBITMQ_RESULTS_EXCHANGE", settings.RabbitMQResultsExchange);
        settings.KubernetesNamespace = GetSetting(configuration, "KUBERNETES_NAMESPACE", settings.KubernetesNamespace);
        settings.KubernetesApiUrl = GetSetting(configuration, "K8S_API_URL", settings.KubernetesApiUrl);
        settings.KubernetesServiceAccountToken = GetOptionalSetting(configuration, "K8S_SERVICE_ACCOUNT_TOKEN", settings.KubernetesServiceAccountToken);
        settings.InternalToken = GetOptionalSetting(configuration, "QUALITY_GATE_INTERNAL_TOKEN", settings.InternalToken);

        if (int.TryParse(configuration["EXPECTED_SCAN_RESULTS_PER_SCAN"], out var expectedScanResultsPerScan))
        {
            settings.ExpectedScanResultsPerScan = expectedScanResultsPerScan;
        }

        if (int.TryParse(configuration["PENDING_SCAN_TIMEOUT_SECONDS"], out var pendingScanTimeoutSeconds))
        {
            settings.PendingScanTimeoutSeconds = pendingScanTimeoutSeconds;
        }
    })
    .ValidateDataAnnotations();

builder.Services.AddDbContext<AppDbContext>((serviceProvider, options) =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var connectionString = configuration["DB_CONNECTION_STRING"]
        ?? configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Database connection string is not configured.");

    options.UseNpgsql(connectionString);
});

builder.Services.AddScoped<CvssRulesEngine>();
builder.Services.AddSingleton<QualityGateAggregationService>();
builder.Services.AddScoped<IQualityGateEvaluatorService, QualityGateEvaluatorService>();
builder.Services.AddScoped<IRollbackService, RollbackService>();
builder.Services.AddHostedService<RabbitMQConsumerService>();
builder.Services.AddHttpClient<IRollbackService, RollbackService>((serviceProvider, client) =>
{
    var settings = serviceProvider.GetRequiredService<IOptions<QualityGateSettings>>().Value;
    client.BaseAddress = new Uri(settings.KubernetesApiUrl);
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    dbContext.Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.MapControllers();

app.Run();

static string GetSetting(IConfiguration configuration, string key, string fallback)
{
    return string.IsNullOrWhiteSpace(configuration[key]) ? fallback : configuration[key]!;
}

static string? GetOptionalSetting(IConfiguration configuration, string key, string? fallback)
{
    return string.IsNullOrWhiteSpace(configuration[key]) ? fallback : configuration[key];
}
