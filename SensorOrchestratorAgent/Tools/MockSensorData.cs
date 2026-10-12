namespace SensorOrchestratorAgent.Tools;

public sealed record SensorValue(double Value, string Unit);

public sealed record SensorReading(
    string SensorId,
    DateTimeOffset Timestamp,
    string Status,
    IReadOnlyDictionary<string, SensorValue> Readings);

public sealed record SensorHistory(
    string SensorId,
    DateTimeOffset Start,
    DateTimeOffset End,
    int Count,
    IReadOnlyList<SensorReading> Data);

/// <summary>Generates simulated sensor data so the agent can run without a real sensor backend.</summary>
internal static class MockSensorData
{
    private static readonly string[] s_statuses = ["online", "online", "online", "degraded"];

    public static SensorReading Reading(string sensorId, DateTimeOffset timestamp)
    {
        // Same sensor + same minute gives the same values, so repeated calls look consistent.
        var random = new Random(StableSeed(sensorId, timestamp.ToUnixTimeSeconds() / 60));

        return new SensorReading(
            sensorId,
            timestamp,
            s_statuses[random.Next(s_statuses.Length)],
            new Dictionary<string, SensorValue>
            {
                ["temperature"] = new(Math.Round(18 + (random.NextDouble() * 14), 2), "°C"),
                ["humidity"] = new(Math.Round(30 + (random.NextDouble() * 40), 2), "%"),
                ["pressure"] = new(Math.Round(995 + (random.NextDouble() * 30), 2), "hPa"),
            });
    }

    public static SensorHistory History(string sensorId, DateTimeOffset start, DateTimeOffset end, int maxPoints)
    {
        var span = end - start;
        var points = Math.Clamp((int)span.TotalHours, 1, Math.Max(1, maxPoints));
        var step = span / points;

        var data = Enumerable.Range(0, points)
            .Select(i => Reading(sensorId, start + (step * i)))
            .ToList();

        return new SensorHistory(sensorId, start, end, data.Count, data);
    }

    private static int StableSeed(string sensorId, long bucket)
    {
        unchecked
        {
            var hash = 17;
            foreach (var c in sensorId)
            {
                hash = (hash * 31) + c;
            }

            return (hash * 31) + (int)bucket;
        }
    }
}
