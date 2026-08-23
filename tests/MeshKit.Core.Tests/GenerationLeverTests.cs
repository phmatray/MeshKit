using MeshKit.Core.Definitions;

namespace MeshKit.Core.Tests;

/// <summary>The Meshy 7 levers: remesh budget, topology, real-world size, alpha thumbnails, ultra, texture reference.</summary>
public class GenerationLeverTests
{
    private const string Yaml = """
        slug: nature-kit
        name: Nature Kit
        price: { amount: 1900, currency: eur }
        category: nature
        style: lowpoly
        generation:
          ai_model: latest
          model_type: standard
          should_remesh: true
          topology: quad
          target_polycount: 4000
          ultra_mode: true
          auto_size: true
          origin_at: center
          alpha_thumbnail: false
          texture_image: palette.png
          enable_pbr: true
          target_formats: [glb]
        models:
          - slug: oak
            name: Oak
            prompt: an oak tree
            ultra: false
          - slug: pine
            name: Pine
            prompt: a pine tree
            texture_prompt: dark green needles
        """;

    private static PackDefinition Valid() => PackDefinitionLoader.Load(Yaml);

    [Fact]
    public void Load_parses_the_levers()
    {
        var pack = Valid();
        var gen = pack.Generation;

        Assert.True(gen.ShouldRemesh);
        Assert.Equal("quad", gen.Topology);
        Assert.True(gen.UltraMode);
        Assert.True(gen.AutoSize);
        Assert.Equal("center", gen.OriginAt);
        Assert.False(gen.AlphaThumbnail);
        Assert.Equal("palette.png", gen.TextureImage);
        Assert.False(pack.Models[0].Ultra);
        Assert.Null(pack.Models[1].Ultra);
        Assert.Empty(PackDefinitionValidator.Validate(pack));
    }

    [Fact]
    public void Defaults_are_meshy_defaults_except_alpha_thumbnails()
    {
        var gen = PackDefinitionLoader.Load("""
            slug: p
            name: P
            price: { amount: 100, currency: eur }
            models: [{ slug: a, name: A, prompt: a }]
            """).Generation;

        Assert.False(gen.ShouldRemesh);
        Assert.Equal("triangle", gen.Topology);
        Assert.False(gen.UltraMode);
        Assert.False(gen.AutoSize);
        Assert.Equal("bottom", gen.OriginAt);
        Assert.True(gen.AlphaThumbnail);
        Assert.Null(gen.TextureImage);
    }

    [Fact]
    public void Polycount_without_remesh_is_reported_because_meshy_ignores_it()
    {
        var pack = Valid() with { Generation = Valid().Generation with { ShouldRemesh = false } };

        var error = Assert.Single(PackDefinitionValidator.Validate(pack));
        Assert.Contains("should_remesh", error);
    }

    [Fact]
    public void Smart_topology_honours_polycount_without_remesh()
    {
        var pack = Valid() with { Generation = Valid().Generation with { ShouldRemesh = false, ModelType = "smart-topology", AiModel = "meshy-t2", UltraMode = false } };

        Assert.Empty(PackDefinitionValidator.Validate(pack));
    }

    [Fact]
    public void Deprecated_lowpoly_model_type_is_reported()
    {
        var pack = Valid() with { Generation = Valid().Generation with { ModelType = "lowpoly" } };

        Assert.Contains(PackDefinitionValidator.Validate(pack), e => e.Contains("deprecated"));
    }

    [Theory]
    [InlineData("standard", "meshy-t2")]
    [InlineData("smart-topology", "latest")]
    [InlineData("smart-topology", "meshy-7")]
    public void Model_type_and_ai_model_must_agree(string modelType, string aiModel)
    {
        var pack = Valid() with { Generation = Valid().Generation with { ModelType = modelType, AiModel = aiModel, UltraMode = false } };

        Assert.Contains(PackDefinitionValidator.Validate(pack), e => e.Contains("ai_model"));
    }

    [Theory]
    [InlineData("meshy-5")]
    [InlineData("meshy-6")]
    public void Ultra_mode_needs_meshy_7(string aiModel)
    {
        var pack = Valid() with { Generation = Valid().Generation with { AiModel = aiModel } };

        Assert.Contains(PackDefinitionValidator.Validate(pack), e => e.Contains("ultra_mode"));
    }

    [Theory]
    [InlineData("topology", "hex")]
    [InlineData("origin_at", "top")]
    public void Unknown_topology_or_origin_is_reported(string field, string value)
    {
        var gen = field == "topology" ? Valid().Generation with { Topology = value } : Valid().Generation with { OriginAt = value };
        var pack = Valid() with { Generation = gen };

        Assert.Contains(PackDefinitionValidator.Validate(pack), e => e.Contains(field));
    }

    [Fact]
    public void Lods_parse_and_must_be_distinct_below_budget_and_at_most_three()
    {
        var pack = PackDefinitionLoader.Load(Yaml.Replace("target_polycount: 4000", "target_polycount: 4000\n  lods: [2000, 800]"));
        Assert.Equal([2000, 800], pack.Generation.LodLevels);
        Assert.Empty(PackDefinitionValidator.Validate(pack));

        var gen = pack.Generation;
        Assert.Contains(PackDefinitionValidator.Validate(pack with { Generation = gen with { Lods = [2000, 2000] } }), e => e.Contains("distinct"));
        Assert.Contains(PackDefinitionValidator.Validate(pack with { Generation = gen with { Lods = [4000] } }), e => e.Contains("not below target_polycount"));
        Assert.Contains(PackDefinitionValidator.Validate(pack with { Generation = gen with { Lods = [50] } }), e => e.Contains("minimum of 100"));
        Assert.Contains(PackDefinitionValidator.Validate(pack with { Generation = gen with { Lods = [3000, 2000, 1000, 500] } }), e => e.Contains("at most 3"));
    }

    [Fact]
    public void Variants_parse_and_are_validated()
    {
        var pack = PackDefinitionLoader.Load(Yaml.Replace("models:", "variants:\n  - { slug: snow, name: Snow, prompt: covered in fresh snow }\n  - { slug: autumn, name: Autumn, prompt: autumn colours }\nmodels:"));
        Assert.Equal(["snow", "autumn"], pack.VariantList.Select(v => v.Slug));
        Assert.Equal("Snow", pack.VariantList[0].Name);
        Assert.Empty(PackDefinitionValidator.Validate(pack));

        Assert.Contains(PackDefinitionValidator.Validate(pack with { Variants = [new VariantDefinition("snow", "A", "x"), new VariantDefinition("snow", "B", "y")] }), e => e.Contains("Duplicate variant"));
        Assert.Contains(PackDefinitionValidator.Validate(pack with { Variants = [new VariantDefinition("Bad Slug", "A", "x")] }), e => e.Contains("Variant slug"));
        Assert.Contains(PackDefinitionValidator.Validate(pack with { Variants = [new VariantDefinition("a", "A", new string('x', 601))] }), e => e.Contains("600"));
        Assert.Contains(PackDefinitionValidator.Validate(pack with { Variants = Enumerable.Range(0, 4).Select(i => new VariantDefinition($"v{i}", "V", "p")).ToList() }), e => e.Contains("at most 3"));
    }

    [Fact]
    public void Sample_names_one_of_the_models_and_reaches_the_manifest()
    {
        var pack = PackDefinitionLoader.Load(Yaml.Replace("models:", "sample: pine\nmodels:"));

        Assert.Equal("pine", pack.Sample);
        Assert.Empty(PackDefinitionValidator.Validate(pack));
        Assert.Equal("pine", Catalog.PackManifest.FromDefinition(pack, DateTimeOffset.UnixEpoch).Sample);
    }

    [Fact]
    public void Sample_that_is_not_a_model_is_reported()
    {
        var pack = Valid() with { Sample = "birch" };

        var error = Assert.Single(PackDefinitionValidator.Validate(pack));
        Assert.Contains("sample 'birch'", error);
    }

    [Theory]
    [InlineData("/etc/palette.png")]
    [InlineData("../palette.png")]
    public void Texture_image_must_be_a_safe_relative_path(string path)
    {
        var pack = Valid() with { Generation = Valid().Generation with { TextureImage = path } };

        Assert.Contains(PackDefinitionValidator.Validate(pack), e => e.Contains("texture_image"));
    }
}
