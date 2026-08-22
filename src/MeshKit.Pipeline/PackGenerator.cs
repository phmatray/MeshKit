using MeshKit.Core.Catalog;
using MeshKit.Core.Definitions;
using MeshKit.Meshy;
using Microsoft.Extensions.Logging;

namespace MeshKit.Pipeline;

/// <summary>
/// Turns a <see cref="PackDefinition"/> into a pack directory (<c>manifest.json</c>, <c>public/</c>,
/// <c>private/</c>) by driving Meshy preview → refine per model. Resume-safe: the manifest is
/// rewritten after every model, and models already succeeded on disk are skipped.
/// </summary>
public sealed class PackGenerator(IMeshyClient meshy, ILogger<PackGenerator> logger, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public async Task<PackManifest> GenerateAsync(PackDefinition definition, string packDirectory, GeneratorOptions options, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(packDirectory);
        var manifestPath = Path.Combine(packDirectory, PackManifestSerializer.FileName);

        var entries = SeedEntries(definition, packDirectory, manifestPath);
        var manifest = PackManifest.FromDefinition(definition, _time.GetUtcNow()) with { Models = entries.Values.ToList() };
        var gate = new object();
        void Persist()
        {
            lock (gate)
            {
                manifest = manifest with { Models = definition.Models.Select(m => entries[m.Slug]).ToList() };
                PackManifestSerializer.WriteFile(manifestPath, manifest);
            }
        }

        Persist();

        var pending = definition.Models.Where(m => entries[m.Slug].Status != ModelStatus.Succeeded).ToList();
        logger.LogInformation("Pack {Slug}: {Pending} model(s) to generate, {Done} already complete",
            definition.Slug, pending.Count, definition.Models.Count - pending.Count);

        using var abort = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var semaphore = new SemaphoreSlim(Math.Max(1, options.Concurrency));
        MeshyOutOfCreditsException? outOfCredits = null;

        var work = pending.Select(async model =>
        {
            await semaphore.WaitAsync(abort.Token);
            try
            {
                var entry = await GenerateModelAsync(definition, model, packDirectory, options, abort.Token);
                entries[model.Slug] = entry;
            }
            catch (MeshyOutOfCreditsException ex)
            {
                // Nothing else can succeed without credits: stop the whole run, keep the manifest as-is for resume.
                outOfCredits ??= ex;
                await abort.CancelAsync();
            }
            catch (OperationCanceledException) when (abort.IsCancellationRequested)
            {
                // Sibling aborted the run; leave this model Pending so a rerun resumes it.
            }
            finally
            {
                semaphore.Release();
                Persist();
            }
        });

        await Task.WhenAll(work);
        cancellationToken.ThrowIfCancellationRequested();

        if (outOfCredits is not null)
        {
            throw outOfCredits;
        }

        return manifest;
    }

    private Dictionary<string, ModelEntry> SeedEntries(PackDefinition definition, string packDirectory, string manifestPath)
    {
        var previous = new Dictionary<string, ModelEntry>(StringComparer.Ordinal);
        if (File.Exists(manifestPath))
        {
            try
            {
                foreach (var entry in PackManifestSerializer.ReadFile(manifestPath).Models)
                {
                    previous[entry.Slug] = entry;
                }
            }
            catch (PackManifestException ex)
            {
                logger.LogWarning("Ignoring unreadable manifest at {Path}: {Message}", manifestPath, ex.Message);
            }
        }

        var entries = new Dictionary<string, ModelEntry>(StringComparer.Ordinal);
        foreach (var model in definition.Models)
        {
            var fresh = ModelEntry.Pending(model);
            if (previous.TryGetValue(model.Slug, out var old) && old.Status == ModelStatus.Succeeded && AssetsPresent(old, packDirectory))
            {
                // Keep the generated assets, refresh the human-facing metadata from the definition.
                entries[model.Slug] = old with { Name = model.Name, Prompt = model.Prompt };
            }
            else
            {
                entries[model.Slug] = fresh;
            }
        }

        return entries;
    }

    private static bool AssetsPresent(ModelEntry entry, string packDirectory)
    {
        var paths = new[] { entry.Thumbnail, entry.Preview }.Concat(entry.Files.Select(f => f.Path));
        return entry.Files.Count > 0
            && paths.All(p => p is not null && PackPaths.IsSafeRelative(p) && File.Exists(PackPaths.Resolve(packDirectory, p)));
    }

    private async Task<ModelEntry> GenerateModelAsync(PackDefinition pack, ModelDefinition model, string packDirectory, GeneratorOptions options, CancellationToken ct)
    {
        var entry = ModelEntry.Pending(model);
        var gen = pack.Generation;
        try
        {
            logger.LogInformation("[{Model}] preview task starting", model.Slug);
            var previewId = await meshy.CreatePreviewAsync(
                new PreviewRequest(model.Prompt, gen.AiModel, gen.ModelType, gen.TargetPolycount, ["glb"]), ct);
            entry = entry with { PreviewTaskId = previewId };
            var preview = await meshy.WaitForTaskAsync(previewId, options.PollInterval, options.TaskTimeout, ct);

            if (!preview.ModelUrls.TryGetValue("glb", out var previewGlb))
            {
                throw new InvalidOperationException($"Preview task {previewId} succeeded without a glb url.");
            }

            var previewPath = $"{PackPaths.PreviewDir}/{model.Slug}.glb";
            await meshy.DownloadAsync(new Uri(previewGlb), PackPaths.Resolve(packDirectory, previewPath), ct);

            string? thumbnailPath = null;
            if (preview.ThumbnailUrl is not null)
            {
                thumbnailPath = $"{PackPaths.ThumbsDir}/{model.Slug}.png";
                await meshy.DownloadAsync(new Uri(preview.ThumbnailUrl), PackPaths.Resolve(packDirectory, thumbnailPath), ct);
            }

            logger.LogInformation("[{Model}] refine task starting", model.Slug);
            var refineId = await meshy.CreateRefineAsync(
                new RefineRequest(previewId, gen.EnablePbr, gen.TextureResolution, model.TexturePrompt, gen.AiModel, gen.TargetFormats), ct);
            entry = entry with { RefineTaskId = refineId };
            var refined = await meshy.WaitForTaskAsync(refineId, options.PollInterval, options.TaskTimeout, ct);

            var files = new List<ModelFile>();
            foreach (var (format, url) in refined.ModelUrls.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                var relative = $"{PackPaths.PrivateRoot}/{model.Slug}/{model.Slug}.{format}";
                var bytes = await meshy.DownloadAsync(new Uri(url), PackPaths.Resolve(packDirectory, relative), ct);
                files.Add(new ModelFile(format, relative, bytes));
            }

            foreach (var textureSet in refined.TextureUrls)
            {
                foreach (var (map, url) in textureSet.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                {
                    var relative = $"{PackPaths.PrivateRoot}/{model.Slug}/textures/{map}.png";
                    var bytes = await meshy.DownloadAsync(new Uri(url), PackPaths.Resolve(packDirectory, relative), ct);
                    files.Add(new ModelFile(map, relative, bytes));
                }
            }

            if (files.Count == 0)
            {
                throw new InvalidOperationException($"Refine task {refineId} succeeded without any model url.");
            }

            logger.LogInformation("[{Model}] done: {Files} file(s), {Credits} credits", model.Slug, files.Count, preview.ConsumedCredits + refined.ConsumedCredits);
            return entry with
            {
                Status = ModelStatus.Succeeded,
                Error = null,
                Thumbnail = thumbnailPath,
                Preview = previewPath,
                Files = files,
                ConsumedCredits = preview.ConsumedCredits + refined.ConsumedCredits,
            };
        }
        catch (MeshyOutOfCreditsException)
        {
            throw;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is MeshyApiException or MeshyTaskFailedException or MeshyTimeoutException or IOException or InvalidOperationException or HttpRequestException)
        {
            logger.LogError("[{Model}] failed: {Message}", model.Slug, ex.Message);
            return entry with { Status = ModelStatus.Failed, Error = ex.Message, Files = [] };
        }
    }
}
