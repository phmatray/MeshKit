using System.Net;

namespace MeshKit.Meshy;

public class MeshyApiException(HttpStatusCode statusCode, string message) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}

/// <summary>HTTP 402: the account has no credits left. Nothing else in the run can succeed, so callers abort.</summary>
public sealed class MeshyOutOfCreditsException(string message) : MeshyApiException(HttpStatusCode.PaymentRequired, message);

public sealed class MeshyTaskFailedException(string taskId, MeshyTaskStatus status, string? message)
    : Exception($"Meshy task {taskId} ended {status}: {message ?? "no error message"}")
{
    public string TaskId { get; } = taskId;

    public MeshyTaskStatus Status { get; } = status;
}

public sealed class MeshyTimeoutException(string taskId, TimeSpan timeout)
    : Exception($"Meshy task {taskId} did not finish within {timeout}.")
{
    public string TaskId { get; } = taskId;
}
