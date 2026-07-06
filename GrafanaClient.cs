using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

public class GrafanaClient(HttpClient http, string baseUrl, string token, ILogger<GrafanaClient> logger)
{
    public async Task CreateAnnotationAsync(long timestampMs, string dashboardUid, List<string> tags, string text)
    {
        var body = JsonSerializer.Serialize(new
        {
            dashboardUID = dashboardUid,
            time = timestampMs,
            tags,
            text,
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/annotations")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Grafana annotation POST failed ({Status}): {Body}",
                response.StatusCode, await response.Content.ReadAsStringAsync());
        }
    }
}
