namespace MeshKit.Pipeline;

/// <param name="Concurrency">Models in flight at once. Meshy queues per account; 2 is a sane default.</param>
/// <param name="PollInterval">Delay between task status checks.</param>
/// <param name="TaskTimeout">Maximum wait for a single preview or refine task.</param>
/// <param name="DefinitionDirectory">Directory of the pack YAML, used to resolve a relative <c>license.file</c> and <c>generation.texture_image</c>.</param>
/// <param name="Regenerate">
/// Ignore models already generated and redo every one. Resume only notices a changed <em>prompt</em>; a changed
/// generation setting (polycount, remesh, size…) looks identical to it, so this is the way to apply one.
/// </param>
public sealed record GeneratorOptions(int Concurrency, TimeSpan PollInterval, TimeSpan TaskTimeout, string? DefinitionDirectory = null, bool Regenerate = false)
{
    public static readonly GeneratorOptions Default = new(
        Concurrency: 2,
        PollInterval: TimeSpan.FromSeconds(15),
        TaskTimeout: TimeSpan.FromMinutes(40));
}
