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
public sealed class PackGenerator(IMeshyClient meshy, ILogger<PackGenerator> logger, TimeProvider? timeProvider = null, LicenseWriter? licenseWriter = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly LicenseWriter _licenses = licenseWriter ?? new LicenseWriter(LicenseWriter.Licensor.AtypicalConsulting);

    public async Task<PackManifest> GenerateAsync(PackDefinition definition, string packDirectory, GeneratorOptions options, CancellationToken cancellationToken)
    {
        var textureImage = ResolveTextureImage(definition.Generation, options.DefinitionDirectory);
        Directory.CreateDirectory(packDirectory);
        var manifestPath = Path.Combine(packDirectory, PackManifestSerializer.FileName);

        var entries = SeedEntries(definition, packDirectory, manifestPath, options.Regenerate);
        await ReconcilePreviewsAsync(definition, entries, packDirectory, cancellationToken);
        ReconcileMetadata(definition, entries, packDirectory);
        var license = _licenses.Write(definition, packDirectory, options.DefinitionDirectory, _time.GetUtcNow());
        var manifest = PackManifest.FromDefinition(definition, _time.GetUtcNow()) with { Models = entries.Values.ToList(), License = license };
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
                var entry = await GenerateModelAsync(definition, model, packDirectory, options, textureImage, abort.Token);
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

    private Dictionary<string, ModelEntry> SeedEntries(PackDefinition definition, string packDirectory, string manifestPath, bool regenerate)
    {
        var previous = new Dictionary<string, ModelEntry>(StringComparer.Ordinal);
        if (regenerate)
        {
            logger.LogInformation("--regenerate: ignoring previously generated models");
        }
        else if (File.Exists(manifestPath))
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
            if (previous.TryGetValue(model.Slug, out var old) && old.Status == ModelStatus.Succeeded && AssetsPresent(old, packDirectory)
                && string.Equals(old.Prompt, model.Prompt, StringComparison.Ordinal))
            {
                // Keep the generated assets, refresh the human-facing metadata from the definition.
                entries[model.Slug] = old with { Name = model.Name, Prompt = model.Prompt, Tags = model.Tags, Category = model.Category };
            }
            else
            {
                entries[model.Slug] = fresh;
            }
        }

        return entries;
    }

    /// <summary>
    /// Brings already-succeeded entries in line with <c>generation.preview</c>. Textured: copy the refined
    /// glb (and fetch the refine task's thumbnail — a free GET) if not on disk yet. Clay: point back at the
    /// clay assets kept from the preview stage. No task is ever created here.
    /// </summary>
    private async Task ReconcilePreviewsAsync(PackDefinition definition, Dictionary<string, ModelEntry> entries, string packDirectory, CancellationToken ct)
    {
        var wantTextured = definition.Generation.Preview == GenerationSettings.PreviewTextured;
        foreach (var slug in entries.Keys.ToList())
        {
            var entry = entries[slug];
            if (entry.Status != ModelStatus.Succeeded || entry.PreviewTextured == wantTextured)
            {
                continue;
            }

            if (!wantTextured)
            {
                var clayPreview = PackPaths.ClayPreview(slug);
                var clayThumb = PackPaths.ClayThumbnail(slug);
                if (File.Exists(PackPaths.Resolve(packDirectory, clayPreview)))
                {
                    entries[slug] = entry with
                    {
                        PreviewTextured = false,
                        Preview = clayPreview,
                        Thumbnail = File.Exists(PackPaths.Resolve(packDirectory, clayThumb)) ? clayThumb : entry.Thumbnail,
                    };
                    logger.LogInformation("[{Model}] preview switched to clay", slug);
                }

                continue;
            }

            var textured = await PublishTexturedPreviewAsync(slug, entry.Files, packDirectory, ct);
            if (textured is null)
            {
                logger.LogWarning("[{Model}] no refined glb on disk; keeping the clay preview", slug);
                continue;
            }

            var thumbPath = PackPaths.TexturedThumbnail(slug);
            string? thumb = File.Exists(PackPaths.Resolve(packDirectory, thumbPath)) ? thumbPath : null;
            if (thumb is null && entry.RefineTaskId is not null)
            {
                try
                {
                    var task = await meshy.GetTaskAsync(entry.RefineTaskId, ct);
                    if (task.BestThumbnailUrl is { } url)
                    {
                        thumb = await TryDownloadAsync(new Uri(url), thumbPath, packDirectory, ct);
                    }
                }
                catch (Exception ex) when (ex is MeshyApiException or HttpRequestException)
                {
                    logger.LogWarning("[{Model}] refine thumbnail unavailable ({Message}); keeping the clay thumbnail", slug, ex.Message);
                }
            }

            entries[slug] = entry with { PreviewTextured = true, Preview = textured, Thumbnail = thumb ?? entry.Thumbnail };
            logger.LogInformation("[{Model}] preview switched to textured", slug);
        }
    }

    /// <summary>Measures succeeded models that predate metadata (or whose files changed) — reads the glb on disk, no Meshy call.</summary>
    private void ReconcileMetadata(PackDefinition definition, Dictionary<string, ModelEntry> entries, string packDirectory)
    {
        foreach (var slug in entries.Keys.ToList())
        {
            var entry = entries[slug];
            if (entry.Status != ModelStatus.Succeeded || entry.Metadata is not null || entry.Files.Count == 0)
            {
                continue;
            }

            var metadata = TryDescribe(slug, entry.Files, packDirectory, definition.Generation);
            if (metadata is not null)
            {
                entries[slug] = entry with { Metadata = metadata };
                logger.LogInformation("[{Model}] measured: {Tris} tris, {W}×{H}×{D} m", slug, metadata.Triangles, metadata.Width, metadata.Height, metadata.Depth);
            }
        }
    }

    private ModelMetadata? TryDescribe(string slug, IReadOnlyList<ModelFile> files, string packDirectory, GenerationSettings gen)
    {
        try
        {
            return GlbInspector.Describe(packDirectory, files, gen.EnablePbr, gen.TextureResolution);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException or InvalidOperationException or OverflowException)
        {
            logger.LogWarning("[{Model}] could not measure glb: {Message}", slug, ex.Message);
            return null;
        }
    }

    /// <summary>Copies the refined glb into <c>public/preview/</c>; null when the model has no glb.</summary>
    private static async Task<string?> PublishTexturedPreviewAsync(string slug, IReadOnlyList<ModelFile> files, string packDirectory, CancellationToken ct)
    {
        var glb = files.FirstOrDefault(f => f.Format == "glb");
        if (glb is null || !File.Exists(PackPaths.Resolve(packDirectory, glb.Path)))
        {
            return null;
        }

        var target = PackPaths.TexturedPreview(slug);
        var targetFull = PackPaths.Resolve(packDirectory, target);
        Directory.CreateDirectory(Path.GetDirectoryName(targetFull)!);
        await using (var source = File.OpenRead(PackPaths.Resolve(packDirectory, glb.Path)))
        await using (var dest = File.Create(targetFull + ".part"))
        {
            await source.CopyToAsync(dest, ct);
        }

        File.Move(targetFull + ".part", targetFull, overwrite: true);
        return target;
    }

    private async Task<string?> TryDownloadAsync(Uri url, string relativePath, string packDirectory, CancellationToken ct)
    {
        try
        {
            await meshy.DownloadAsync(url, PackPaths.Resolve(packDirectory, relativePath), ct);
            return relativePath;
        }
        catch (Exception ex) when (ex is MeshyApiException or HttpRequestException or IOException)
        {
            logger.LogWarning("Optional download {Path} failed: {Message}", relativePath, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// The pack's <c>texture_image</c> as a <c>data:</c> URI, or null when none is set. Read once per run and
    /// before any Meshy call, so a missing file costs nothing. Meshy accepts base64 data URIs for jpg/jpeg/png.
    /// </summary>
    internal static string? ResolveTextureImage(GenerationSettings gen, string? definitionDirectory)
    {
        if (gen.TextureImage is null)
        {
            return null;
        }

        var path = Path.GetFullPath(Path.Combine(definitionDirectory ?? Directory.GetCurrentDirectory(), gen.TextureImage));
        if (!File.Exists(path))
        {
            throw new PackDefinitionException($"generation.texture_image '{gen.TextureImage}' not found at {path}.");
        }

        var mime = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            var ext => throw new PackDefinitionException($"generation.texture_image '{gen.TextureImage}': Meshy accepts .png, .jpg or .jpeg, not '{ext}'."),
        };
        return $"data:{mime};base64,{Convert.ToBase64String(File.ReadAllBytes(path))}";
    }

    private static bool AssetsPresent(ModelEntry entry, string packDirectory)
    {
        var paths = new[] { entry.Thumbnail, entry.Preview }.Concat(entry.Files.Select(f => f.Path));
        return entry.Files.Count > 0
            && paths.All(p => p is not null && PackPaths.IsSafeRelative(p) && File.Exists(PackPaths.Resolve(packDirectory, p)));
    }

    private async Task<ModelEntry> GenerateModelAsync(PackDefinition pack, ModelDefinition model, string packDirectory, GeneratorOptions options, string? textureImage, CancellationToken ct)
    {
        var entry = ModelEntry.Pending(model);
        var gen = pack.Generation;
        try
        {
            var ultra = model.Ultra ?? gen.UltraMode;
            logger.LogInformation("[{Model}] preview task starting{Ultra}", model.Slug, ultra ? " (ultra)" : "");
            var previewId = await meshy.CreatePreviewAsync(
                new PreviewRequest(model.Prompt, gen.AiModel, gen.ModelType, gen.TargetPolycount, ["glb"],
                    ShouldRemesh: gen.ShouldRemesh,
                    Topology: gen.Topology,
                    UltraMode: ultra,
                    AutoSize: gen.AutoSize,
                    OriginAt: gen.AutoSize ? gen.OriginAt : null,
                    AlphaThumbnail: gen.AlphaThumbnail), ct);
            entry = entry with { PreviewTaskId = previewId };
            var preview = await meshy.WaitForTaskAsync(previewId, options.PollInterval, options.TaskTimeout, ct);

            if (!preview.ModelUrls.TryGetValue("glb", out var previewGlb))
            {
                throw new InvalidOperationException($"Preview task {previewId} succeeded without a glb url.");
            }

            var previewPath = $"{PackPaths.PreviewDir}/{model.Slug}.glb";
            await meshy.DownloadAsync(new Uri(previewGlb), PackPaths.Resolve(packDirectory, previewPath), ct);

            string? thumbnailPath = null;
            if (preview.BestThumbnailUrl is { } previewThumb)
            {
                thumbnailPath = $"{PackPaths.ThumbsDir}/{model.Slug}.png";
                await meshy.DownloadAsync(new Uri(previewThumb), PackPaths.Resolve(packDirectory, thumbnailPath), ct);
            }

            logger.LogInformation("[{Model}] refine task starting", model.Slug);
            // Meshy takes a texture prompt OR a reference image: the model's own prompt wins over the pack palette.
            var refineId = await meshy.CreateRefineAsync(
                new RefineRequest(previewId, gen.EnablePbr, gen.TextureResolution, model.TexturePrompt, gen.AiModel, gen.TargetFormats,
                    TextureImageUrl: model.TexturePrompt is null ? textureImage : null,
                    AutoSize: gen.AutoSize,
                    OriginAt: gen.AutoSize ? gen.OriginAt : null,
                    AlphaThumbnail: gen.AlphaThumbnail), ct);
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

            // Always keep the textured preview assets next to the clay ones: switching modes later is then
            // a manifest change, never a regeneration.
            var texturedPreview = await PublishTexturedPreviewAsync(model.Slug, files, packDirectory, ct);
            var texturedThumb = refined.BestThumbnailUrl is null
                ? null
                : await TryDownloadAsync(new Uri(refined.BestThumbnailUrl), PackPaths.TexturedThumbnail(model.Slug), packDirectory, ct);

            logger.LogInformation("[{Model}] done: {Files} file(s), {Credits} credits", model.Slug, files.Count, preview.ConsumedCredits + refined.ConsumedCredits);
            var done = entry with
            {
                Status = ModelStatus.Succeeded,
                Error = null,
                Thumbnail = thumbnailPath,
                Preview = previewPath,
                Files = files,
                ConsumedCredits = preview.ConsumedCredits + refined.ConsumedCredits,
                PreviewTextured = false,
                Metadata = TryDescribe(model.Slug, files, packDirectory, gen),
            };
            return pack.Generation.Preview == GenerationSettings.PreviewTextured && texturedPreview is not null
                ? done with { PreviewTextured = true, Preview = texturedPreview, Thumbnail = texturedThumb ?? thumbnailPath }
                : done;
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
