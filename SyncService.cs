using Microsoft.Extensions.Logging;
using System.Globalization;

public class SyncService(LabcomClient labcom, PrometheusClient prometheus, ILogger<SyncService> logger)
{
    public async Task<int> RunCycleAsync(AppConfig config, int? cachedAccountId)
    {
        var accountId = cachedAccountId ?? await labcom.GetAccountIdAsync()
            ?? throw new InvalidOperationException("No account found in LabCom");

        var from = DateTimeOffset.UtcNow.AddDays(-config.LookbackDays).ToUnixTimeSeconds();
        var measurements = await labcom.GetMeasurementsAsync(accountId, from);

        foreach (var mapping in config.Mappings)
            await ProcessMappingAsync(config, accountId, measurements, mapping);

        return accountId;
    }

    private async Task ProcessMappingAsync(AppConfig config, int accountId, List<Measurement> measurements, MappingConfig mapping)
    {
        var triggers = measurements
            .Where(m => m.Scenario.Contains(mapping.TriggerScenarioContains, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(m => m.Timestamp)
            .Take(config.TopN)
            .ToList();

        if (triggers.Count == 0)
        {
            logger.LogDebug("[{Mapping}] No trigger measurements found", mapping.Name);
            return;
        }

        foreach (var target in mapping.Targets)
            await ProcessTargetAsync(config, accountId, measurements, mapping.Name, triggers, target);
    }

    private async Task ProcessTargetAsync(AppConfig config, int accountId, List<Measurement> measurements,
        string mappingName, List<Measurement> triggers, TargetConfig target)
    {
        var existingTargets = measurements
            .Where(m => m.Scenario.Contains(target.ScenarioContains, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var existingTimestamps = existingTargets.Select(m => m.Timestamp).ToHashSet();

        var parameterId = target.ParameterId;
        if (!parameterId.HasValue)
        {
            var sample = existingTargets.FirstOrDefault();
            if (sample is null)
            {
                logger.LogWarning("[{Mapping} / {Target}] No existing target measurements to discover parameter_id — " +
                    "add a manual ORP measurement in LabCom first, or set ParameterId in config.json",
                    mappingName, target.ScenarioContains);
                return;
            }
            parameterId = sample.ParameterId;
            logger.LogInformation("[{Mapping} / {Target}] Discovered parameter_id={ParameterId} (scenario_id={ScenarioId})",
                mappingName, target.ScenarioContains, parameterId, sample.ScenarioId);
        }

        int written = 0;
        foreach (var trigger in triggers)
        {
            if (existingTimestamps.Contains(trigger.Timestamp))
                continue;

            var value = await prometheus.QueryAtAsync(target.PrometheusMetric, trigger.Timestamp);
            if (value is null)
            {
                logger.LogWarning("[{Mapping} / {Target}] No Prometheus data for {Metric} at {Timestamp} (ts={Ts})",
                    mappingName, target.ScenarioContains, target.PrometheusMetric,
                    DateTimeOffset.FromUnixTimeSeconds(trigger.Timestamp).ToString("u"), trigger.Timestamp);
                continue;
            }

            var valueStr = value.Value.ToString("G6", CultureInfo.InvariantCulture);
            await labcom.CreateMeasurementAsync(accountId, parameterId.Value, valueStr, trigger.Timestamp, config.SyncComment);
            logger.LogInformation("[{Mapping} / {Target}] Wrote {Value} at {Timestamp} (trigger id={TriggerId})",
                mappingName, target.ScenarioContains, valueStr,
                DateTimeOffset.FromUnixTimeSeconds(trigger.Timestamp).ToString("u"), trigger.Id);

            existingTimestamps.Add(trigger.Timestamp);
            written++;
        }

        if (written == 0)
            logger.LogDebug("[{Mapping} / {Target}] All {Count} trigger(s) already have target measurements",
                mappingName, target.ScenarioContains, triggers.Count);
    }
}
