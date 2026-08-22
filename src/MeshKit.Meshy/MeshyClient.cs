using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace MeshKit.Meshy;

/// <summary>
/// Thin client over Meshy's text-to-3D v2 API. The bearer token is attached per request to the
/// API host only: asset downloads go to signed storage URLs that must never see the key.
/// </summary>
public sealed class MeshyClient(HttpClient http, MeshyOptions options, ILogger<MeshyClient> logger, TimeProvider? timeProvider = null) : IMeshyClient
{
    private const string TextTo3dPath = "/openapi/v2/text-to-3d";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public async Task<string> CreatePreviewAsync(PreviewRequest request, CancellationToken cancellationToken)
    {
        var body = new
        {
            mode = "preview",
            prompt = request.Prompt,
            ai_model = request.AiModel,
            model_type = request.ModelType,
            target_polycount = request.TargetPolycount,
            target_formats = request.TargetFormats,
        };
        return await CreateTaskAsync(body, cancellationToken);
    }

    public async Task<string> CreateRefineAsync(RefineRequest request, CancellationToken cancellationToken)
    {
        var body = new
        {
            mode = "refine",
            preview_task_id = request.PreviewTaskId,
            enable_pbr = request.EnablePbr,
            texture_resolution = request.TextureResolution,
            texture_prompt = request.TexturePrompt,
            ai_model = request.AiModel,
            target_formats = request.TargetFormats,
        };
        return await CreateTaskAsync(body, cancellationToken);
    }

    public async Task<MeshyTask> GetTaskAsync(string taskId, CancellationToken cancellationToken)
    {
        using var response = await SendWithRetryAsync(
            () => ApiRequest(HttpMethod.Get, $"{TextTo3dPath}/{Uri.EscapeDataString(taskId)}"), cancellationToken);
        var dto = await response.Content.ReadFromJsonAsync<TaskDto>(Json, cancellationToken)
            ?? throw new MeshyApiException(response.StatusCode, "Empty task response.");
        return dto.ToTask();
    }

    public async Task<MeshyTask> WaitForTaskAsync(string taskId, TimeSpan pollInterval, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = _time.GetUtcNow() + timeout;
        var lastProgress = -1;
        while (true)
        {
            var task = await GetTaskAsync(taskId, cancellationToken);
            if (task.Progress != lastProgress)
            {
                logger.LogInformation("Meshy task {TaskId}: {Status} {Progress}%", taskId, task.Status, task.Progress);
                lastProgress = task.Progress;
            }

            if (task.Status == MeshyTaskStatus.Succeeded)
            {
                return task;
            }

            if (task.IsTerminal)
            {
                throw new MeshyTaskFailedException(taskId, task.Status, task.ErrorMessage);
            }

            if (_time.GetUtcNow() >= deadline)
            {
                throw new MeshyTimeoutException(taskId, timeout);
            }

            await Task.Delay(pollInterval, _time, cancellationToken);
        }
    }

    public async Task<long> DownloadAsync(Uri url, string destinationPath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new MeshyApiException(response.StatusCode, $"Download of {url.GetLeftPart(UriPartial.Path)} failed with {(int)response.StatusCode}.");
        }

        var temp = destinationPath + ".part";
        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var file = File.Create(temp))
        {
            await source.CopyToAsync(file, cancellationToken);
        }

        File.Move(temp, destinationPath, overwrite: true);
        return new FileInfo(destinationPath).Length;
    }

    private async Task<string> CreateTaskAsync(object body, CancellationToken cancellationToken)
    {
        using var response = await SendWithRetryAsync(
            () =>
            {
                var request = ApiRequest(HttpMethod.Post, TextTo3dPath);
                request.Content = JsonContent.Create(body, options: Json);
                return request;
            },
            cancellationToken);
        var dto = await response.Content.ReadFromJsonAsync<CreateResponse>(Json, cancellationToken);
        return string.IsNullOrEmpty(dto?.Result)
            ? throw new MeshyApiException(response.StatusCode, "Task creation returned no task id.")
            : dto.Result;
    }

    private HttpRequestMessage ApiRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, new Uri(options.BaseAddress, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        return request;
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var request = requestFactory();
            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException ex) when (attempt < options.MaxAttempts)
            {
                logger.LogWarning(ex, "Meshy call failed (attempt {Attempt}/{Max}), retrying", attempt, options.MaxAttempts);
                await BackoffAsync(attempt, null, cancellationToken);
                continue;
            }

            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            var message = await ReadErrorMessageAsync(response, cancellationToken);
            var retryable = response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;
            if (retryable && attempt < options.MaxAttempts)
            {
                logger.LogWarning("Meshy answered {Status} (attempt {Attempt}/{Max}): {Message}", (int)response.StatusCode, attempt, options.MaxAttempts, message);
                var retryAfter = response.Headers.RetryAfter?.Delta;
                response.Dispose();
                await BackoffAsync(attempt, retryAfter, cancellationToken);
                continue;
            }

            var status = response.StatusCode;
            response.Dispose();
            throw status == HttpStatusCode.PaymentRequired
                ? new MeshyOutOfCreditsException($"Meshy refused the request: out of credits ({message}).")
                : new MeshyApiException(status, $"Meshy answered {(int)status} {status}: {message}");
        }
    }

    private Task BackoffAsync(int attempt, TimeSpan? retryAfter, CancellationToken cancellationToken)
    {
        var delay = retryAfter ?? options.RetryBaseDelay * Math.Pow(2, attempt - 1);
        return Task.Delay(delay, _time, cancellationToken);
    }

    private static async Task<string> ReadErrorMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("message", out var msg))
            {
                return msg.GetString() ?? body;
            }
        }
        catch (JsonException)
        {
            // Not JSON: fall through and return the raw body.
        }

        return body;
    }

    private sealed record CreateResponse(string? Result);

    private sealed class TaskDto
    {
        public string Id { get; set; } = string.Empty;
        public string? Status { get; set; }
        public int Progress { get; set; }
        public Dictionary<string, string>? ModelUrls { get; set; }
        public string? ThumbnailUrl { get; set; }
        public TaskErrorDto? TaskError { get; set; }
        public int ConsumedCredits { get; set; }

        public MeshyTask ToTask() => new(
            Id,
            Status switch
            {
                "PENDING" => MeshyTaskStatus.Pending,
                "IN_PROGRESS" => MeshyTaskStatus.InProgress,
                "SUCCEEDED" => MeshyTaskStatus.Succeeded,
                "FAILED" => MeshyTaskStatus.Failed,
                "CANCELED" => MeshyTaskStatus.Canceled,
                _ => MeshyTaskStatus.Unknown,
            },
            Progress,
            (ModelUrls ?? []).Where(kv => !string.IsNullOrEmpty(kv.Value)).ToDictionary(kv => kv.Key, kv => kv.Value),
            ThumbnailUrl,
            TaskError?.Message,
            ConsumedCredits);
    }

    private sealed class TaskErrorDto
    {
        public string? Message { get; set; }
    }
}
