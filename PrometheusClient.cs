using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.Json;

public class PrometheusClient(HttpClient http, string baseUrl, ILogger<PrometheusClient> logger)
{
    public async Task<double?> QueryAtAsync(string metric, long timestamp)
    {
        var url = $"{baseUrl}/api/v1/query?query={Uri.EscapeDataString(metric)}&time={timestamp}";
        var response = await http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var result = doc.RootElement.GetProperty("data").GetProperty("result");
        if (result.GetArrayLength() == 0)
            return null;
        var valueStr = result[0].GetProperty("value")[1].GetString();
        if (double.TryParse(valueStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
            return d;
        logger.LogWarning("Could not parse Prometheus value '{Value}' for metric {Metric}", valueStr, metric);
        return null;
    }
}
