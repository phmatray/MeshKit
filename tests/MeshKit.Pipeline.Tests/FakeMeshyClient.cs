using System.Collections.Concurrent;
using MeshKit.Meshy;

namespace MeshKit.Pipeline.Tests;

/// <summary>
/// Scripted Meshy: preview task ids are <c>prev-&lt;prompt&gt;</c>, refine ids <c>ref-&lt;previewId&gt;</c>.
/// <see cref="Outcomes"/> decides, per task id, what <see cref="WaitForTaskAsync"/> returns or throws.
/// </summary>
public sealed class FakeMeshyClient : IMeshyClient
{
    public ConcurrentBag<PreviewRequest> PreviewRequests { get; } = [];
    public ConcurrentBag<RefineRequest> RefineRequests { get; } = [];
    public ConcurrentBag<RemeshRequest> RemeshRequests { get; } = [];
    public ConcurrentBag<RetextureRequest> RetextureRequests { get; } = [];
    public ConcurrentBag<(Uri Url, string Path)> Downloads { get; } = [];

    /// <summary>Task id → factory. Default: every task succeeds with a glb + fbx and a thumbnail.</summary>
    public ConcurrentDictionary<string, Func<MeshyTask>> Outcomes { get; } = new();

    /// <summary>Set to make <see cref="CreatePreviewAsync"/> throw (e.g. out of credits) for a given prompt.</summary>
    public ConcurrentDictionary<string, Exception> CreateFailures { get; } = new();

    public int MaxObservedConcurrency { get; private set; }
    private int _inFlight;

    public Task<string> CreatePreviewAsync(PreviewRequest request, CancellationToken cancellationToken)
    {
        PreviewRequests.Add(request);
        if (CreateFailures.TryGetValue(request.Prompt, out var ex))
        {
            throw ex;
        }

        return Task.FromResult("prev-" + request.Prompt);
    }

    public Task<string> CreateRefineAsync(RefineRequest request, CancellationToken cancellationToken)
    {
        RefineRequests.Add(request);
        return Task.FromResult("ref-" + request.PreviewTaskId);
    }

    public Task<string> CreateRemeshAsync(RemeshRequest request, CancellationToken cancellationToken)
    {
        RemeshRequests.Add(request);
        if (CreateFailures.TryGetValue($"remesh:{request.InputTaskId}", out var ex))
        {
            throw ex;
        }

        return Task.FromResult($"lod-{request.TargetPolycount}-{request.InputTaskId}");
    }

    public Task<string> CreateRetextureAsync(RetextureRequest request, CancellationToken cancellationToken)
    {
        RetextureRequests.Add(request);
        if (CreateFailures.TryGetValue($"retexture:{request.InputTaskId}", out var ex))
        {
            throw ex;
        }

        // variant tasks look like refine tasks to the fake: textures + thumbnail
        return Task.FromResult($"ref-var-{request.TextStylePrompt}-{request.InputTaskId}");
    }

    public Task<MeshyTask> GetTaskAsync(string taskId, CancellationToken cancellationToken, MeshyTaskKind kind = MeshyTaskKind.TextTo3d) => Task.FromResult(Resolve(taskId));

    public async Task<MeshyTask> WaitForTaskAsync(string taskId, TimeSpan pollInterval, TimeSpan timeout, CancellationToken cancellationToken, MeshyTaskKind kind = MeshyTaskKind.TextTo3d)
    {
        var now = Interlocked.Increment(ref _inFlight);
        lock (this)
        {
            MaxObservedConcurrency = Math.Max(MaxObservedConcurrency, now);
        }

        try
        {
            await Task.Delay(5, cancellationToken);
            var task = Resolve(taskId);
            return task.Status == MeshyTaskStatus.Succeeded ? task : throw new MeshyTaskFailedException(taskId, task.Status, task.ErrorMessage);
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
        }
    }

    public async Task<long> DownloadAsync(Uri url, string destinationPath, CancellationToken cancellationToken)
    {
        Downloads.Add((url, destinationPath));
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        var bytes = destinationPath.EndsWith(".glb", StringComparison.Ordinal) ? TestGlb.Cube() : System.Text.Encoding.UTF8.GetBytes(url.ToString());
        await File.WriteAllBytesAsync(destinationPath, bytes, cancellationToken);
        return bytes.Length;
    }

    public static MeshyTask Succeeded(string id, params string[] formats) => new(
        Id: id,
        Status: MeshyTaskStatus.Succeeded,
        Progress: 100,
        ModelUrls: formats.ToDictionary(f => f, f => $"https://cdn.test/{id}/{id}.{f}"),
        ThumbnailUrl: $"https://cdn.test/{id}/thumb.png",
        TextureUrls: id.StartsWith("ref-", StringComparison.Ordinal)
            ? [new Dictionary<string, string> { ["base_color"] = $"https://cdn.test/{id}/base_color.png" }]
            : [],
        ErrorMessage: null,
        ConsumedCredits: id.StartsWith("ref-", StringComparison.Ordinal) ? 10 : id.StartsWith("lod-", StringComparison.Ordinal) ? 5 : 20,
        AlphaThumbnailUrl: null);

    public static MeshyTask Failed(string id, string message) => new(
        id, MeshyTaskStatus.Failed, 0, new Dictionary<string, string>(), null, [], message, 0);

    private MeshyTask Resolve(string taskId) =>
        Outcomes.TryGetValue(taskId, out var factory) ? factory() : Succeeded(taskId, "glb", "fbx");
}
