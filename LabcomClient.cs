using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

public class LabcomClient(HttpClient http, Uri endpoint, ILogger<LabcomClient> logger)
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private async Task<JsonDocument> ExecuteAsync(string query)
    {
        var body = JsonSerializer.Serialize(new { query });
        var response = await http.PostAsync(endpoint, new StringContent(body, Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (doc.RootElement.TryGetProperty("errors", out var errors))
            logger.LogWarning("GraphQL errors: {Errors}", errors.GetRawText());
        return doc;
    }

    public async Task<int?> GetAccountIdAsync()
    {
        using var doc = await ExecuteAsync("{ CloudAccount { Accounts { id } } }");
        var accounts = doc.RootElement
            .GetProperty("data").GetProperty("CloudAccount").GetProperty("Accounts");
        if (accounts.GetArrayLength() == 0) return null;
        var id = accounts[0].GetProperty("id").GetInt32();
        logger.LogInformation("Using account_id: {AccountId}", id);
        return id;
    }

    public async Task<List<Measurement>> GetMeasurementsAsync(int accountId, long from)
    {
        var q = $"{{ Measurements(accountId: [{accountId}], from: {from}) {{ id scenario scenario_id parameter parameter_id value timestamp }} }}";
        using var doc = await ExecuteAsync(q);
        var arr = doc.RootElement.GetProperty("data").GetProperty("Measurements");
        var result = new List<Measurement>(arr.GetArrayLength());
        foreach (var el in arr.EnumerateArray())
        {
            result.Add(new Measurement
            {
                Id = el.GetProperty("id").GetInt32(),
                Scenario = el.GetProperty("scenario").GetString() ?? "",
                ScenarioId = el.GetProperty("scenario_id").GetInt32(),
                Parameter = el.GetProperty("parameter").GetString() ?? "",
                ParameterId = el.GetProperty("parameter_id").GetInt32(),
                Value = el.GetProperty("value").GetString() ?? "",
                Timestamp = el.GetProperty("timestamp").GetInt64(),
            });
        }
        return result;
    }

    public async Task<int?> CreateMeasurementAsync(int accountId, int parameterId, string value, long timestamp, string comment)
    {
        var mutation = $$"""
            mutation {
              createMeasurement(
                value: "{{Escape(value)}}"
                account_id: {{accountId}}
                parameter_id: {{parameterId}}
                timestamp: {{timestamp}}
                comment: "{{Escape(comment)}}"
              ) { id }
            }
            """;
        using var doc = await ExecuteAsync(mutation);
        return doc.RootElement.GetProperty("data").GetProperty("createMeasurement").GetProperty("id").GetInt32();
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
