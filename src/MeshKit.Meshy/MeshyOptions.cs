namespace MeshKit.Meshy;

public sealed class MeshyOptions
{
    public const string EnvironmentVariable = "MESHY_API_KEY";

    public required string ApiKey { get; init; }

    public Uri BaseAddress { get; init; } = new("https://api.meshy.ai");

    /// <summary>Attempts for a single call on 429/5xx/transport errors (exponential backoff, base <see cref="RetryBaseDelay"/>).</summary>
    public int MaxAttempts { get; init; } = 3;

    public TimeSpan RetryBaseDelay { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Reads <c>MESHY_API_KEY</c>; throws a clear error when it is missing.</summary>
    public static MeshyOptions FromEnvironment()
    {
        var key = Environment.GetEnvironmentVariable(EnvironmentVariable);
        return string.IsNullOrWhiteSpace(key)
            ? throw new InvalidOperationException($"{EnvironmentVariable} is not set. Create a key at https://www.meshy.ai/settings/api and export it.")
            : new MeshyOptions { ApiKey = key.Trim() };
    }
}
