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
logger.LogInformation("Starting LabcomSync — {Count} mapping(s), {ExportCount} export(s) configured, interval {Interval}s",
    appConfig.Mappings.Count, appConfig.Exports.Count, appConfig.IntervalSeconds);

if (appConfig.Exports.Count > 0 && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GRAFANA_TOKEN")))
    logger.LogWarning("Exports configured but GRAFANA_TOKEN is not set — annotation POSTs will fail");

var labcomHttp = new HttpClient();
labcomHttp.DefaultRequestHeaders.Add("Authorization", token);

var prometheusHttp = new HttpClient();
var grafanaHttp = new HttpClient();

var labcomClient = new LabcomClient(labcomHttp, new Uri(appConfig.LabcomGraphqlUrl), loggerFactory.CreateLogger<LabcomClient>());
var prometheusClient = new PrometheusClient(prometheusHttp, appConfig.PrometheusUrl, loggerFactory.CreateLogger<PrometheusClient>());
var syncService = new SyncService(labcomClient, prometheusClient, loggerFactory.CreateLogger<SyncService>());

var grafanaToken = Environment.GetEnvironmentVariable("GRAFANA_TOKEN") ?? "";
var grafanaClient = new GrafanaClient(grafanaHttp, appConfig.GrafanaUrl, grafanaToken, loggerFactory.CreateLogger<GrafanaClient>());
var exportService = new ExportService(grafanaClient, loggerFactory.CreateLogger<ExportService>());

int? cachedAccountId = null;

while (true)
{
    try
    {
        var accountId = cachedAccountId ?? await labcomClient.GetAccountIdAsync()
            ?? throw new InvalidOperationException("No account found in LabCom");

        var from = DateTimeOffset.UtcNow.AddDays(-appConfig.LookbackDays).ToUnixTimeSeconds();
        var measurements = await labcomClient.GetMeasurementsAsync(accountId, from);

        await syncService.ProcessMappingsAsync(appConfig, accountId, measurements);
        await exportService.RunAsync(appConfig, measurements);

        cachedAccountId = accountId;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Sync cycle failed — will retry in {Interval}s", appConfig.IntervalSeconds);
        cachedAccountId = null;
    }

    var nowSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    var nextSec = (nowSec / appConfig.IntervalSeconds + 1) * appConfig.IntervalSeconds;
    var nextRun = DateTimeOffset.FromUnixTimeSeconds(nextSec);
    logger.LogInformation("Next run at {NextRun:u}", nextRun);
    await Task.Delay(nextRun - DateTimeOffset.UtcNow);
}
