using System.Net;
using System.Text.Json;
using MeshKit.Meshy;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeshKit.Pipeline.Tests;

public class MeshyClientTests
{
    private static readonly PreviewRequest Preview = new(
        Prompt: "a chest", AiModel: "latest", ModelType: "standard", TargetPolycount: 5000, TargetFormats: ["glb", "fbx"]);

    private static (MeshyClient Client, FakeHttpMessageHandler Handler) Create(MeshyOptions? options = null)
    {
        var handler = new FakeHttpMessageHandler();
        options ??= new MeshyOptions { ApiKey = "msy_test", RetryBaseDelay = TimeSpan.FromMilliseconds(1) };
        var client = new MeshyClient(new HttpClient(handler), options, NullLogger<MeshyClient>.Instance);
        return (client, handler);
    }

    [Fact]
    public async Task CreatePreview_posts_snake_case_body_with_bearer()
    {
        var (client, handler) = Create();
        handler.Enqueue(HttpStatusCode.Accepted, """{"result":"task-1"}""");

        var id = await client.CreatePreviewAsync(Preview, CancellationToken.None);

        Assert.Equal("task-1", id);
        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.meshy.ai/openapi/v2/text-to-3d", request.RequestUri!.ToString());
        Assert.Equal("Bearer msy_test", request.Headers.Authorization!.ToString());
        using var doc = JsonDocument.Parse(body!);
        Assert.Equal("preview", doc.RootElement.GetProperty("mode").GetString());
        Assert.Equal("a chest", doc.RootElement.GetProperty("prompt").GetString());
        Assert.Equal("standard", doc.RootElement.GetProperty("model_type").GetString());
        Assert.Equal(5000, doc.RootElement.GetProperty("target_polycount").GetInt32());
        Assert.Equal(2, doc.RootElement.GetProperty("target_formats").GetArrayLength());
    }

    [Fact]
    public async Task CreatePreview_posts_the_meshy7_levers()
    {
        var (client, handler) = Create();
        handler.Enqueue(HttpStatusCode.Accepted, """{"result":"task-1"}""");

        await client.CreatePreviewAsync(Preview with
        {
            ShouldRemesh = true, Topology = "quad", UltraMode = true, AutoSize = true, OriginAt = "center", AlphaThumbnail = true,
        }, CancellationToken.None);

        using var doc = JsonDocument.Parse(handler.Requests[0].Body!);
        Assert.True(doc.RootElement.GetProperty("should_remesh").GetBoolean());
        Assert.Equal("quad", doc.RootElement.GetProperty("topology").GetString());
        Assert.True(doc.RootElement.GetProperty("ultra_mode").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("auto_size").GetBoolean());
        Assert.Equal("center", doc.RootElement.GetProperty("origin_at").GetString());
        Assert.True(doc.RootElement.GetProperty("alpha_thumbnail").GetBoolean());
    }

    [Fact]
    public async Task CreateRefine_posts_texture_image_and_size_levers()
    {
        var (client, handler) = Create();
        handler.Enqueue(HttpStatusCode.Accepted, """{"result":"task-2"}""");

        await client.CreateRefineAsync(
            new RefineRequest("task-1", EnablePbr: true, TextureResolution: "2k", TexturePrompt: null, AiModel: "latest", TargetFormats: ["glb"],
                TextureImageUrl: "data:image/png;base64,AAAA", AutoSize: true, OriginAt: "bottom", AlphaThumbnail: true),
            CancellationToken.None);

        using var doc = JsonDocument.Parse(handler.Requests[0].Body!);
        Assert.Equal("data:image/png;base64,AAAA", doc.RootElement.GetProperty("texture_image_url").GetString());
        Assert.False(doc.RootElement.TryGetProperty("texture_prompt", out _));
        Assert.True(doc.RootElement.GetProperty("auto_size").GetBoolean());
        Assert.Equal("bottom", doc.RootElement.GetProperty("origin_at").GetString());
        Assert.True(doc.RootElement.GetProperty("alpha_thumbnail").GetBoolean());
    }

    [Fact]
    public async Task GetTask_maps_alpha_thumbnail_url()
    {
        var (client, handler) = Create();
        handler.Enqueue(HttpStatusCode.OK, """{"id":"task-2","status":"SUCCEEDED","progress":100,"model_urls":{"glb":"https://cdn/x.glb"},"thumbnail_url":"https://cdn/t.png","alpha_thumbnail_url":"https://cdn/t-alpha.png"}""");

        var task = await client.GetTaskAsync("task-2", CancellationToken.None);

        Assert.Equal("https://cdn/t-alpha.png", task.AlphaThumbnailUrl);
    }

    [Fact]
    public async Task CreateRemesh_posts_to_the_remesh_path_and_polls_it()
    {
        var (client, handler) = Create();
        handler.Enqueue(HttpStatusCode.Accepted, """{"result":"remesh-1"}""");
        handler.Enqueue(HttpStatusCode.OK, """{"id":"remesh-1","type":"remesh","status":"SUCCEEDED","progress":100,"model_urls":{"glb":"https://cdn/lod.glb"},"consumed_credits":5}""");

        var id = await client.CreateRemeshAsync(new RemeshRequest("task-2", ["glb", "fbx"], "triangle", 2000), CancellationToken.None);
        var task = await client.GetTaskAsync(id, CancellationToken.None, MeshyTaskKind.Remesh);

        Assert.Equal("remesh-1", id);
        Assert.Equal("https://api.meshy.ai/openapi/v1/remesh", handler.Requests[0].Request.RequestUri!.ToString());
        using var doc = JsonDocument.Parse(handler.Requests[0].Body!);
        Assert.Equal("task-2", doc.RootElement.GetProperty("input_task_id").GetString());
        Assert.Equal("triangle", doc.RootElement.GetProperty("topology").GetString());
        Assert.Equal(2000, doc.RootElement.GetProperty("target_polycount").GetInt32());
        Assert.Equal(2, doc.RootElement.GetProperty("target_formats").GetArrayLength());
        Assert.Equal("https://api.meshy.ai/openapi/v1/remesh/remesh-1", handler.Requests[1].Request.RequestUri!.ToString());
        Assert.Equal(MeshyTaskStatus.Succeeded, task.Status);
        Assert.Equal(5, task.ConsumedCredits);
    }

    [Fact]
    public async Task CreatePreview_omits_null_polycount()
    {
        var (client, handler) = Create();
        handler.Enqueue(HttpStatusCode.Accepted, """{"result":"task-1"}""");

        await client.CreatePreviewAsync(Preview with { TargetPolycount = null }, CancellationToken.None);

        using var doc = JsonDocument.Parse(handler.Requests[0].Body!);
        Assert.False(doc.RootElement.TryGetProperty("target_polycount", out _));
    }

    [Fact]
    public async Task CreateRefine_posts_preview_task_id_and_texture_settings()
    {
        var (client, handler) = Create();
        handler.Enqueue(HttpStatusCode.Accepted, """{"result":"task-2"}""");

        var id = await client.CreateRefineAsync(
            new RefineRequest(PreviewTaskId: "task-1", EnablePbr: true, TextureResolution: "4k", TexturePrompt: "oak", AiModel: "latest", TargetFormats: ["glb"]),
            CancellationToken.None);

        Assert.Equal("task-2", id);
        using var doc = JsonDocument.Parse(handler.Requests[0].Body!);
        Assert.Equal("refine", doc.RootElement.GetProperty("mode").GetString());
        Assert.Equal("task-1", doc.RootElement.GetProperty("preview_task_id").GetString());
        Assert.True(doc.RootElement.GetProperty("enable_pbr").GetBoolean());
        Assert.Equal("4k", doc.RootElement.GetProperty("texture_resolution").GetString());
        Assert.Equal("oak", doc.RootElement.GetProperty("texture_prompt").GetString());
    }

    [Fact]
    public async Task GetTask_maps_status_urls_thumbnail_and_credits()
    {
        var (client, handler) = Create();
        handler.Enqueue(HttpStatusCode.OK, """
            {"id":"task-2","status":"SUCCEEDED","progress":100,
             "model_urls":{"glb":"https://cdn/x.glb?Expires=1","fbx":"https://cdn/x.fbx"},
             "thumbnail_url":"https://cdn/t.png","consumed_credits":30,"task_error":null,
             "texture_urls":[{"base_color":"https://cdn/bc.png","normal":"https://cdn/n.png"}]}
            """);

        var task = await client.GetTaskAsync("task-2", CancellationToken.None);

        Assert.Equal("https://api.meshy.ai/openapi/v2/text-to-3d/task-2", handler.Requests[0].Request.RequestUri!.ToString());
        Assert.Equal(MeshyTaskStatus.Succeeded, task.Status);
        Assert.Equal(100, task.Progress);
        Assert.Equal("https://cdn/x.glb?Expires=1", task.ModelUrls["glb"]);
        Assert.Equal("https://cdn/t.png", task.ThumbnailUrl);
        Assert.Equal(30, task.ConsumedCredits);
        Assert.Equal("https://cdn/n.png", Assert.Single(task.TextureUrls)["normal"]);
        Assert.Null(task.ErrorMessage);
    }

    [Fact]
    public async Task GetTask_maps_failure_message()
    {
        var (client, handler) = Create();
        handler.Enqueue(HttpStatusCode.OK, """{"id":"t","status":"FAILED","progress":0,"task_error":{"message":"nsfw"}}""");

        var task = await client.GetTaskAsync("t", CancellationToken.None);

        Assert.Equal(MeshyTaskStatus.Failed, task.Status);
        Assert.Equal("nsfw", task.ErrorMessage);
        Assert.Empty(task.ModelUrls);
    }

    [Fact]
    public async Task Payment_required_surfaces_as_out_of_credits()
    {
        var (client, handler) = Create();
        handler.Enqueue(HttpStatusCode.PaymentRequired, """{"message":"insufficient credits"}""");

        var ex = await Assert.ThrowsAsync<MeshyOutOfCreditsException>(() => client.CreatePreviewAsync(Preview, CancellationToken.None));

        Assert.Contains("insufficient credits", ex.Message);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Rate_limited_then_ok_succeeds_after_retry()
    {
        var (client, handler) = Create();
        handler.Enqueue(HttpStatusCode.TooManyRequests, """{"message":"slow down"}""");
        handler.Enqueue(HttpStatusCode.Accepted, """{"result":"task-1"}""");

        var id = await client.CreatePreviewAsync(Preview, CancellationToken.None);

        Assert.Equal("task-1", id);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Persistent_rate_limit_gives_up_after_max_attempts()
    {
        var (client, handler) = Create(new MeshyOptions { ApiKey = "k", MaxAttempts = 3, RetryBaseDelay = TimeSpan.FromMilliseconds(1) });
        for (var i = 0; i < 3; i++)
        {
            handler.Enqueue(HttpStatusCode.TooManyRequests, "{}");
        }

        var ex = await Assert.ThrowsAsync<MeshyApiException>(() => client.CreatePreviewAsync(Preview, CancellationToken.None));

        Assert.Equal(HttpStatusCode.TooManyRequests, ex.StatusCode);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task Bad_request_is_not_retried()
    {
        var (client, handler) = Create();
        handler.Enqueue(HttpStatusCode.BadRequest, """{"message":"prompt too long"}""");

        var ex = await Assert.ThrowsAsync<MeshyApiException>(() => client.CreatePreviewAsync(Preview, CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Contains("prompt too long", ex.Message);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task WaitForTask_polls_until_succeeded()
    {
        var (client, handler) = Create();
        handler.Enqueue(HttpStatusCode.OK, """{"id":"t","status":"PENDING","progress":0}""");
        handler.Enqueue(HttpStatusCode.OK, """{"id":"t","status":"IN_PROGRESS","progress":50}""");
        handler.Enqueue(HttpStatusCode.OK, """{"id":"t","status":"SUCCEEDED","progress":100,"model_urls":{"glb":"https://cdn/x.glb"}}""");

        var task = await client.WaitForTaskAsync("t", TimeSpan.FromMilliseconds(1), TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.Equal(MeshyTaskStatus.Succeeded, task.Status);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task WaitForTask_throws_on_failed_with_message()
    {
        var (client, handler) = Create();
        handler.Enqueue(HttpStatusCode.OK, """{"id":"t","status":"FAILED","progress":10,"task_error":{"message":"moderation"}}""");

        var ex = await Assert.ThrowsAsync<MeshyTaskFailedException>(
            () => client.WaitForTaskAsync("t", TimeSpan.FromMilliseconds(1), TimeSpan.FromSeconds(5), CancellationToken.None));

        Assert.Equal("t", ex.TaskId);
        Assert.Contains("moderation", ex.Message);
    }

    [Fact]
    public async Task WaitForTask_times_out()
    {
        var (client, handler) = Create();
        for (var i = 0; i < 100; i++)
        {
            handler.Enqueue(HttpStatusCode.OK, """{"id":"t","status":"IN_PROGRESS","progress":10}""");
        }

        await Assert.ThrowsAsync<MeshyTimeoutException>(
            () => client.WaitForTaskAsync("t", TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(60), CancellationToken.None));
    }

    [Fact]
    public async Task Download_streams_to_disk_without_leaking_the_api_key()
    {
        var (client, handler) = Create();
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        handler.EnqueueBytes(bytes);
        var dir = Directory.CreateTempSubdirectory("meshkit-dl");
        var target = Path.Combine(dir.FullName, "nested", "x.glb");

        var written = await client.DownloadAsync(new Uri("https://assets.meshy.ai/x.glb?Expires=1"), target, CancellationToken.None);

        Assert.Equal(5, written);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(target));
        Assert.Null(handler.Requests[0].Request.Headers.Authorization);
        dir.Delete(recursive: true);
    }
}
