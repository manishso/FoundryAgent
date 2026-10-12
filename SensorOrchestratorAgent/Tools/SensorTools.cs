using SensorOrchestratorAgent.Configuration;

namespace SensorOrchestratorAgent.Tools;

/// <summary>Function tools exposed to the orchestrator agent: get_live_sensor_data and get_history.</summary>
public sealed class SensorTools
{
    private readonly SensorApiClient _client;
    private readonly SensorApiOptions _options;
    private readonly ILogger<SensorTools> _logger;

    public SensorTools(SensorApiClient client, SensorApiOptions options, ILogger<SensorTools> logger)
    {
        this._client = client;
        this._options = options;
        this._logger = logger;
    }

    public IList<AITool> AsAITools() =>
    [
        AIFunctionFactory.Create(this.GetLiveSensorDataAsync, name: "get_live_sensor_data"),
        AIFunctionFactory.Create(this.GetHistoryAsync, name: "get_history"),
    ];

    [Description("Get the latest live reading (temperature, humidity, pressure, status) for a sensor.")]
    public async Task<string> GetLiveSensorDataAsync(
        [Description("Unique id of the sensor, e.g. 'sensor-001'.")] string sensorId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sensorId))
        {
            return Error("sensorId is required.");
        }

        sensorId = sensorId.Trim();
        this._logger.LogInformation("get_live_sensor_data called for {SensorId}", sensorId);

        return await this.ExecuteAsync(sensorId, () => this._client.GetLiveAsync(sensorId, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
    }

    [Description("Get historical readings for a sensor over a time range.")]
    public async Task<string> GetHistoryAsync(
        [Description("Unique id of the sensor, e.g. 'sensor-001'.")] string sensorId,
        [Description("Start of the range in ISO 8601 (e.g. 2026-10-11T00:00:00Z). Defaults to the configured look-back window.")] string? startTime = null,
        [Description("End of the range in ISO 8601. Defaults to now.")] string? endTime = null,
        [Description("Maximum number of data points to return.")] int? maxPoints = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sensorId))
        {
            return Error("sensorId is required.");
        }

        sensorId = sensorId.Trim();

        DateTimeOffset end = DateTimeOffset.UtcNow;
        if (!string.IsNullOrWhiteSpace(endTime) && !TryParseIso(endTime, out end))
        {
            return Error($"endTime must be an ISO 8601 date/time, got '{endTime}'.");
        }

        DateTimeOffset start = end.AddHours(-this._options.DefaultHistoryHours);
        if (!string.IsNullOrWhiteSpace(startTime) && !TryParseIso(startTime, out start))
        {
            return Error($"startTime must be an ISO 8601 date/time, got '{startTime}'.");
        }

        if (start >= end)
        {
            return Error("startTime must be earlier than endTime.");
        }

        var limit = Math.Clamp(maxPoints ?? this._options.MaxHistoryPoints, 1, this._options.MaxHistoryPoints);
        this._logger.LogInformation("get_history called for {SensorId} from {Start} to {End} (limit {Limit})", sensorId, start, end, limit);

        return await this.ExecuteAsync(
                sensorId,
                () => this._client.GetHistoryAsync(sensorId, start, end, limit, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
    }

    // Errors are returned as JSON so the model can explain them instead of the run failing.
    private async Task<string> ExecuteAsync(string sensorId, Func<Task<string>> call, CancellationToken cancellationToken)
    {
        try
        {
            return await call().ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is not null)
        {
            this._logger.LogWarning(ex, "Sensor API returned {StatusCode} for {SensorId}", ex.StatusCode, sensorId);
            return Error($"Sensor API returned {(int)ex.StatusCode} for sensor '{sensorId}'.");
        }
        catch (HttpRequestException ex)
        {
            this._logger.LogError(ex, "Could not reach the sensor API for {SensorId}", sensorId);
            return Error("Could not reach the sensor API.");
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            this._logger.LogError(ex, "Sensor API timed out for {SensorId}", sensorId);
            return Error("The sensor API timed out.");
        }
    }

    private static bool TryParseIso(string value, out DateTimeOffset result) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out result);

    private static string Error(string message) => JsonSerializer.Serialize(new { error = message });
}
