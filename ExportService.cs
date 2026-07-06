using Microsoft.Extensions.Logging;
using System.Text.Json;

public class ExportService(GrafanaClient grafana, ILogger<ExportService> logger)
{
    public async Task RunAsync(AppConfig config, List<Measurement> measurements)
    {
        if (config.Exports.Count == 0)
            return;

        if (string.IsNullOrEmpty(config.GrafanaDashboardUid))
        {
            logger.LogWarning("Exports configured but GrafanaDashboardUid is empty — skipping");
            return;
        }

        var state = LoadState(config.AnnotationStatePath);

        foreach (var export in config.Exports)
            await ProcessExportAsync(config, measurements, export, state);

        SaveState(config.AnnotationStatePath, state);
    }

    private async Task ProcessExportAsync(AppConfig config, List<Measurement> measurements, ExportConfig export, Dictionary<string, long> state)
    {
        var matches = measurements
            .Where(m => m.ParameterId == export.ParameterId)
            .Where(m => string.IsNullOrEmpty(export.ScenarioContains)
                || m.Scenario.Contains(export.ScenarioContains, StringComparison.OrdinalIgnoreCase))
            .OrderBy(m => m.Timestamp)
            .ToList();

        if (matches.Count == 0)
        {
            logger.LogInformation("[Export {Name}] No measurements found (parameter_id={ParameterId})",
                export.Name, export.ParameterId);
            return;
        }

        var lastSeen = state.GetValueOrDefault(export.Name, 0L);
        var newOnes = matches.Where(m => m.Timestamp > lastSeen).ToList();

        foreach (var m in newOnes)
        {
            var text = string.IsNullOrEmpty(export.Unit)
                ? $"{export.Name}: {m.Value}"
                : $"{export.Name}: {m.Value} {export.Unit}";
            await grafana.CreateAnnotationAsync(m.Timestamp * 1000, config.GrafanaDashboardUid, [export.Name.ToLowerInvariant()], text);
            logger.LogInformation("[Export {Name}] Annotated {Value} at {Timestamp} (id={Id})",
                export.Name, m.Value, DateTimeOffset.FromUnixTimeSeconds(m.Timestamp).ToString("u"), m.Id);
            state[export.Name] = m.Timestamp;
        }

        if (newOnes.Count == 0)
            logger.LogInformation("[Export {Name}] Already annotated up to {Timestamp}",
                export.Name, DateTimeOffset.FromUnixTimeSeconds(lastSeen).ToString("u"));
    }

    private static Dictionary<string, long> LoadState(string path)
    {
        if (!File.Exists(path))
            return new();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, long>>(File.ReadAllText(path)) ?? new();
        }
        catch (JsonException)
        {
            return new();
        }
    }

    private static void SaveState(string path, Dictionary<string, long> state)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(state));
    }
}
