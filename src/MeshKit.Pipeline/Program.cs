using MeshKit.Core.Catalog;
using MeshKit.Core.Definitions;
using MeshKit.Meshy;
using MeshKit.Pipeline;
using Microsoft.Extensions.Logging;

return await Cli.RunAsync(args);

internal static class Cli
{
    private const string Usage = """
        meshkit-pipeline — generates MeshKit packs through the Meshy API.

        usage:
          meshkit-pipeline generate --pack packs/<slug>.yaml --out <catalog-dir>
                                    [--concurrency 2] [--poll-seconds 15] [--timeout-minutes 40] [--dry-run] [--regenerate]
          meshkit-pipeline zip      --pack-dir <catalog-dir>/<slug> --out <file.zip>
          meshkit-pipeline publish  --zip <file.zip> --url <https://store/api/ingest> [--token <token>]

        environment:
          MESHY_API_KEY          required by `generate` (unless --dry-run)
          MESHKIT_INGEST_TOKEN   default for `publish --token`

        exit codes: 0 ok · 1 some models failed (manifest kept, rerun resumes) · 2 usage/config error · 3 out of Meshy credits
        """;

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Console.WriteLine(Usage);
            return args.Length == 0 ? 2 : 0;
        }

        var options = ParseOptions(args.Skip(1));
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            return args[0] switch
            {
                "generate" => await GenerateAsync(options, cts.Token),
                "zip" => Zip(options),
                "publish" => await PublishAsync(options, cts.Token),
                _ => Fail($"Unknown command '{args[0]}'.\n\n{Usage}"),
            };
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Cancelled.");
            return 130;
        }
        catch (Exception ex) when (ex is PackDefinitionException or PackManifestException or InvalidOperationException)
        {
            return Fail(ex.Message);
        }
    }

    private static async Task<int> GenerateAsync(Dictionary<string, string> options, CancellationToken ct)
    {
        var packFile = Require(options, "pack");
        var outDir = Require(options, "out");
        var dryRun = options.ContainsKey("dry-run");

        var definition = PackDefinitionLoader.LoadFile(packFile);
        var errors = PackDefinitionValidator.Validate(definition);
        if (errors.Count > 0)
        {
            return Fail($"Pack definition '{packFile}' is invalid:\n  - {string.Join("\n  - ", errors)}");
        }

        var generatorOptions = GeneratorOptions.Default with
        {
            Concurrency = Int(options, "concurrency", GeneratorOptions.Default.Concurrency),
            PollInterval = TimeSpan.FromSeconds(Int(options, "poll-seconds", (int)GeneratorOptions.Default.PollInterval.TotalSeconds)),
            TaskTimeout = TimeSpan.FromMinutes(Int(options, "timeout-minutes", (int)GeneratorOptions.Default.TaskTimeout.TotalMinutes)),
            DefinitionDirectory = Path.GetDirectoryName(Path.GetFullPath(packFile)),
            Regenerate = options.ContainsKey("regenerate"),
        };

        var packDir = Path.Combine(outDir, definition.Slug);
        Console.WriteLine($"Pack '{definition.Slug}' — {definition.Models.Count} model(s), {(definition.Price.Amount / 100m).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)} {definition.Price.Currency.ToUpperInvariant()}");
        var gen = definition.Generation;
        Console.WriteLine($"  formats: {string.Join(", ", gen.TargetFormats)} · model_type: {gen.ModelType} · pbr: {gen.EnablePbr}");
        Console.WriteLine($"  mesh:    {(gen.ShouldRemesh ? $"remesh to ≤{gen.TargetPolycount?.ToString() ?? "default"} {gen.Topology}s" : "no remesh (polycount not enforced)")}"
            + $" · auto_size: {gen.AutoSize}{(gen.AutoSize ? $" ({gen.OriginAt})" : "")} · alpha_thumbnail: {gen.AlphaThumbnail}"
            + $" · ultra: {definition.Models.Count(m => m.Ultra ?? gen.UltraMode)}/{definition.Models.Count} model(s)"
            + (gen.TextureImage is null ? "" : $" · texture_image: {gen.TextureImage}"));
        Console.WriteLine($"  sample:  {definition.Sample ?? "none (set `sample: <model>` to offer a free model)"}");
        Console.WriteLine($"  lods:    {(gen.LodLevels.Count == 0 ? "none" : string.Join(" / ", gen.LodLevels) + " polygons (Meshy Remesh, 5 credits each)")}");
        Console.WriteLine($"  variants: {(definition.VariantList.Count == 0 ? "none" : string.Join(", ", definition.VariantList.Select(v => v.Slug)) + " (Meshy Retexture, 10 credits per model and variant)")}");
        Console.WriteLine($"  output:  {packDir}");
        if (dryRun)
        {
            foreach (var model in definition.Models)
            {
                Console.WriteLine($"  - {model.Slug}: {model.Prompt}");
            }

            Console.WriteLine("Dry run: no Meshy call made.");
            return 0;
        }

        using var loggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Information));
        var meshyOptions = MeshyOptions.FromEnvironment();
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        var meshy = new MeshyClient(http, meshyOptions, loggerFactory.CreateLogger<MeshyClient>());
        var generator = new PackGenerator(meshy, loggerFactory.CreateLogger<PackGenerator>());

        PackManifest manifest;
        try
        {
            manifest = await generator.GenerateAsync(definition, packDir, generatorOptions, ct);
        }
        catch (MeshyOutOfCreditsException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 3;
        }

        var failed = manifest.Models.Where(m => m.Status != ModelStatus.Succeeded).ToList();
        Console.WriteLine($"Done: {manifest.Models.Count - failed.Count}/{manifest.Models.Count} succeeded, {manifest.Models.Sum(m => m.ConsumedCredits)} credits consumed.");
        foreach (var model in failed)
        {
            Console.WriteLine($"  ✗ {model.Slug}: {model.Error}");
        }

        return failed.Count == 0 ? 0 : 1;
    }

    private static int Zip(Dictionary<string, string> options)
    {
        var packDir = Require(options, "pack-dir");
        var zipPath = Require(options, "out");
        if (!File.Exists(Path.Combine(packDir, PackManifestSerializer.FileName)))
        {
            return Fail($"'{packDir}' has no {PackManifestSerializer.FileName}; is it a pack directory?");
        }

        PackArchiver.Zip(packDir, zipPath);
        Console.WriteLine($"Wrote {zipPath} ({(new FileInfo(zipPath).Length / 1024 / 1024.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} MiB)");
        return 0;
    }

    private static async Task<int> PublishAsync(Dictionary<string, string> options, CancellationToken ct)
    {
        var zipPath = Require(options, "zip");
        var url = new Uri(Require(options, "url"), UriKind.Absolute);
        var token = options.GetValueOrDefault("token") ?? Environment.GetEnvironmentVariable("MESHKIT_INGEST_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
        {
            return Fail("No ingest token: pass --token or set MESHKIT_INGEST_TOKEN.");
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        await new PackPublisher(http).PublishAsync(zipPath, url, token, ct);
        Console.WriteLine($"Published {Path.GetFileName(zipPath)} to {url}");
        return 0;
    }

    private static Dictionary<string, string> ParseOptions(IEnumerable<string> args)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var list = args.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            if (!list[i].StartsWith("--", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Unexpected argument '{list[i]}'.");
            }

            var name = list[i][2..];
            if (i + 1 < list.Count && !list[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                result[name] = list[++i];
            }
            else
            {
                result[name] = "true";
            }
        }

        return result;
    }

    private static string Require(Dictionary<string, string> options, string name) =>
        options.TryGetValue(name, out var value) && value != "true"
            ? value
            : throw new InvalidOperationException($"Missing required option --{name}.");

    private static int Int(Dictionary<string, string> options, string name, int fallback) =>
        options.TryGetValue(name, out var value)
            ? int.TryParse(value, out var parsed) && parsed > 0 ? parsed : throw new InvalidOperationException($"--{name} must be a positive integer.")
            : fallback;

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 2;
    }
}
