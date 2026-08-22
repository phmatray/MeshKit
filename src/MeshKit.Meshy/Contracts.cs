namespace MeshKit.Meshy;

/// <summary>Stage 1 of text-to-3D: an untextured mesh. Doubles as MeshKit's free in-browser preview.</summary>
public sealed record PreviewRequest(
    string Prompt,
    string AiModel,
    string ModelType,
    int? TargetPolycount,
    IReadOnlyList<string> TargetFormats);

/// <summary>Stage 2: textures the preview mesh. This is the paid asset.</summary>
public sealed record RefineRequest(
    string PreviewTaskId,
    bool EnablePbr,
    string TextureResolution,
    string? TexturePrompt,
    string AiModel,
    IReadOnlyList<string> TargetFormats);

public enum MeshyTaskStatus
{
    Unknown,
    Pending,
    InProgress,
    Succeeded,
    Failed,
    Canceled,
}

/// <summary>A text-to-3D task as returned by <c>GET /openapi/v2/text-to-3d/{id}</c>.</summary>
public sealed record MeshyTask(
    string Id,
    MeshyTaskStatus Status,
    int Progress,
    IReadOnlyDictionary<string, string> ModelUrls,
    string? ThumbnailUrl,
    string? ErrorMessage,
    int ConsumedCredits)
{
    public bool IsTerminal => Status is MeshyTaskStatus.Succeeded or MeshyTaskStatus.Failed or MeshyTaskStatus.Canceled;
}

public interface IMeshyClient
{
    Task<string> CreatePreviewAsync(PreviewRequest request, CancellationToken cancellationToken);

    Task<string> CreateRefineAsync(RefineRequest request, CancellationToken cancellationToken);

    Task<MeshyTask> GetTaskAsync(string taskId, CancellationToken cancellationToken);

    /// <summary>Polls until the task is terminal. Throws <see cref="MeshyTaskFailedException"/> on FAILED/CANCELED, <see cref="MeshyTimeoutException"/> after <paramref name="timeout"/>.</summary>
    Task<MeshyTask> WaitForTaskAsync(string taskId, TimeSpan pollInterval, TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>Streams a signed asset URL to <paramref name="destinationPath"/>, creating directories. Returns bytes written.</summary>
    Task<long> DownloadAsync(Uri url, string destinationPath, CancellationToken cancellationToken);
}
