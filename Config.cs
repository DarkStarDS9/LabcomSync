public record TargetConfig
{
    public string ScenarioContains { get; init; } = "";
    public string PrometheusMetric { get; init; } = "";
    public int? ScenarioId { get; init; }
    public int? ParameterId { get; init; }
}

public record MappingConfig
{
    public string Name { get; init; } = "";
    public string TriggerScenarioContains { get; init; } = "";
    public List<TargetConfig> Targets { get; init; } = [];
}

public record ExportConfig
{
    public string Name { get; init; } = "";
    public int ParameterId { get; init; }
    public string ScenarioContains { get; init; } = "";
    public string Unit { get; init; } = "";
}

public record AppConfig
{
    public string LabcomGraphqlUrl { get; init; } = "https://backend.labcom.cloud/graphql";
    public string PrometheusUrl { get; init; } = "http://prometheus:9090";
    public string SyncComment { get; init; } = "prometheus-sync";
    public int IntervalSeconds { get; init; } = 60;
    public int LookbackDays { get; init; } = 30;
    public int TopN { get; init; } = 10;
    public List<MappingConfig> Mappings { get; init; } = [];
    public string GrafanaUrl { get; init; } = "http://grafana:3000";
    public string GrafanaDashboardUid { get; init; } = "";
    public string AnnotationStatePath { get; init; } = "/data/annotated_state.json";
    public List<ExportConfig> Exports { get; init; } = [];
}

public record Measurement
{
    public int Id { get; init; }
    public string Scenario { get; init; } = "";
    public int ScenarioId { get; init; }
    public string Parameter { get; init; } = "";
    public int ParameterId { get; init; }
    public string Value { get; init; } = "";
    public long Timestamp { get; init; }
}
