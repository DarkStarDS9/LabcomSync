using Microsoft.Extensions.Logging;
using System.Text.Json;

var token = Environment.GetEnvironmentVariable("LABCOM_TOKEN")
    ?? throw new InvalidOperationException("LABCOM_TOKEN environment variable is required");

var configPath = Environment.GetEnvironmentVariable("CONFIG_PATH") ?? "/config/config.json";
var appConfig = JsonSerializer.Deserialize<AppConfig>(
    await File.ReadAllTextAsync(configPath),
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
    ?? throw new InvalidOperationException($"Failed to parse {configPath}");

using var loggerFactory = LoggerFactory.Create(b =>
    b.AddConsole().SetMinimumLevel(LogLevel.Information));

var logger = loggerFactory.CreateLogger("LabcomSync");
logger.LogInformation("Starting LabcomSync — {Count} mapping(s) configured, interval {Interval}s",
    appConfig.Mappings.Count, appConfig.IntervalSeconds);

var labcomHttp = new HttpClient();
labcomHttp.DefaultRequestHeaders.Add("Authorization", token);

var prometheusHttp = new HttpClient();

var labcomClient = new LabcomClient(labcomHttp, new Uri(appConfig.LabcomGraphqlUrl), loggerFactory.CreateLogger<LabcomClient>());
var prometheusClient = new PrometheusClient(prometheusHttp, appConfig.PrometheusUrl, loggerFactory.CreateLogger<PrometheusClient>());
var syncService = new SyncService(labcomClient, prometheusClient, loggerFactory.CreateLogger<SyncService>());

int? cachedAccountId = null;

while (true)
{
    try
    {
        cachedAccountId = await syncService.RunCycleAsync(appConfig, cachedAccountId);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Sync cycle failed — will retry in {Interval}s", appConfig.IntervalSeconds);
        cachedAccountId = null;
    }

    await Task.Delay(TimeSpan.FromSeconds(appConfig.IntervalSeconds));
}
