using System.Net.Http.Headers;
using System.Text.Json;
using SensorOrchestratorAgent.Configuration;

namespace SensorOrchestratorAgent.Tools;

/// <summary>Thin HTTP client for the sensor backend. Returns simulated data when UseMock is true.</summary>
public sealed class SensorApiClient
{
    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly SensorApiOptions _options;

    public SensorApiClient(HttpClient httpClient, SensorApiOptions options)
    {
        this._httpClient = httpClient;
        this._options = options;

        if (!options.UseMock)
        {
            // Trailing slash keeps the base path (e.g. /api/) when combined with relative tool paths.
            this._httpClient.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            this._httpClient.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            this._httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            if (options.ApiKey is { Length: > 0 } apiKey)
            {
                this._httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }
        }
    }

    public async Task<string> GetLiveAsync(string sensorId, CancellationToken cancellationToken)
    {
        if (this._options.UseMock)
        {
            return JsonSerializer.Serialize(MockSensorData.Reading(sensorId, DateTimeOffset.UtcNow), s_jsonOptions);
        }

        return await this.GetStringAsync(BuildPath(this._options.LiveDataPath, sensorId), cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> GetHistoryAsync(
        string sensorId,
        DateTimeOffset start,
        DateTimeOffset end,
        int maxPoints,
        CancellationToken cancellationToken)
    {
        if (this._options.UseMock)
        {
            return JsonSerializer.Serialize(MockSensorData.History(sensorId, start, end, maxPoints), s_jsonOptions);
        }

        var query = $"?start={Uri.EscapeDataString(start.ToString("O"))}&end={Uri.EscapeDataString(end.ToString("O"))}&limit={maxPoints}";
        return await this.GetStringAsync(BuildPath(this._options.HistoryPath, sensorId) + query, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> GetStringAsync(string relativeUrl, CancellationToken cancellationToken)
    {
        using var response = await this._httpClient.GetAsync(relativeUrl, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string BuildPath(string template, string sensorId) =>
        template.Replace("{sensorId}", Uri.EscapeDataString(sensorId), StringComparison.OrdinalIgnoreCase).TrimStart('/');
}
