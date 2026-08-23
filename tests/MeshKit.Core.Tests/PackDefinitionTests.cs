using MeshKit.Core.Definitions;

namespace MeshKit.Core.Tests;

public class PackDefinitionTests
{
    private const string SampleYaml = """
        slug: lowpoly-fantasy-props
        name: Low-Poly Fantasy Props
        description: Ten game-ready props.
        price:
          amount: 1900
          currency: eur
        generation:
          ai_model: latest
          model_type: lowpoly
          target_polycount: 5000
          enable_pbr: true
          texture_resolution: 2k
          target_formats: [glb, fbx, obj, usdz]
          preview: clay
        models:
          - slug: treasure-chest
            name: Treasure Chest
            prompt: a closed wooden treasure chest, low poly
            texture_prompt: weathered oak
          - slug: barrel
            name: Barrel
            prompt: a wooden barrel, low poly
        """;

    [Fact]
    public void Load_parses_every_field()
    {
        var pack = PackDefinitionLoader.Load(SampleYaml);

        Assert.Equal("lowpoly-fantasy-props", pack.Slug);
        Assert.Equal("Low-Poly Fantasy Props", pack.Name);
        Assert.Equal(1900, pack.Price.Amount);
        Assert.Equal("eur", pack.Price.Currency);
        Assert.Equal("lowpoly", pack.Generation.ModelType);
        Assert.Equal(5000, pack.Generation.TargetPolycount);
        Assert.True(pack.Generation.EnablePbr);
        Assert.Equal("2k", pack.Generation.TextureResolution);
        Assert.Equal(["glb", "fbx", "obj", "usdz"], pack.Generation.TargetFormats);
        Assert.Equal("clay", pack.Generation.Preview);
        Assert.Equal(2, pack.Models.Count);
        Assert.Equal("weathered oak", pack.Models[0].TexturePrompt);
        Assert.Null(pack.Models[1].TexturePrompt);
    }

    [Fact]
    public void Load_applies_generation_defaults_when_section_absent()
    {
        var pack = PackDefinitionLoader.Load("""
            slug: a
            name: A
            price: { amount: 100, currency: usd }
            models:
              - { slug: m, name: M, prompt: p }
            """);

        Assert.Equal("latest", pack.Generation.AiModel);
        Assert.Equal("standard", pack.Generation.ModelType);
        Assert.Equal(["glb"], pack.Generation.TargetFormats);
        Assert.False(pack.Generation.EnablePbr);
        Assert.Equal("textured", pack.Generation.Preview);
    }

    [Fact]
    public void Unknown_preview_mode_is_reported()
    {
        var pack = Valid() with { Generation = Valid().Generation with { Preview = "wireframe" } };
        Assert.Contains(PackDefinitionValidator.Validate(pack), e => e.Contains("generation.preview"));
    }

    [Fact]
    public void Valid_definition_has_no_errors()
    {
        var errors = PackDefinitionValidator.Validate(PackDefinitionLoader.Load(SampleYaml));
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("Bad Slug")]
    [InlineData("-leading")]
    [InlineData("trailing-")]
    [InlineData("double--dash")]
    [InlineData("")]
    public void Invalid_pack_slug_is_reported(string slug)
    {
        var pack = Valid() with { Slug = slug };
        Assert.Contains(PackDefinitionValidator.Validate(pack), e => e.Contains("slug", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Duplicate_model_slug_is_reported()
    {
        var pack = Valid();
        pack = pack with { Models = [pack.Models[0], pack.Models[0] with { Name = "Copy" }] };
        Assert.Contains(PackDefinitionValidator.Validate(pack), e => e.Contains("duplicate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Prompt_longer_than_600_chars_is_reported()
    {
        var pack = Valid();
        pack = pack with { Models = [pack.Models[0] with { Prompt = new string('x', 601) }] };
        Assert.Contains(PackDefinitionValidator.Validate(pack), e => e.Contains("600"));
    }

    [Fact]
    public void Texture_prompt_longer_than_600_chars_is_reported()
    {
        var pack = Valid();
        pack = pack with { Models = [pack.Models[0] with { TexturePrompt = new string('x', 601) }] };
        Assert.Contains(PackDefinitionValidator.Validate(pack), e => e.Contains("texture_prompt"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Non_positive_amount_is_reported(int amount)
    {
        var pack = Valid() with { Price = new Price(amount, "eur") };
        Assert.Contains(PackDefinitionValidator.Validate(pack), e => e.Contains("amount"));
    }

    [Theory]
    [InlineData("EUR")]
    [InlineData("eu")]
    [InlineData("euro")]
    public void Currency_must_be_three_lowercase_letters(string currency)
    {
        var pack = Valid() with { Price = new Price(100, currency) };
        Assert.Contains(PackDefinitionValidator.Validate(pack), e => e.Contains("currency"));
    }

    [Fact]
    public void Pack_without_models_is_reported()
    {
        var pack = Valid() with { Models = [] };
        Assert.Contains(PackDefinitionValidator.Validate(pack), e => e.Contains("at least one model"));
    }

    [Fact]
    public void Formats_without_glb_are_reported()
    {
        var pack = Valid() with { Generation = Valid().Generation with { TargetFormats = ["fbx"] } };
        Assert.Contains(PackDefinitionValidator.Validate(pack), e => e.Contains("glb"));
    }

    [Fact]
    public void Unknown_format_is_reported()
    {
        var pack = Valid() with { Generation = Valid().Generation with { TargetFormats = ["glb", "blend"] } };
        Assert.Contains(PackDefinitionValidator.Validate(pack), e => e.Contains("blend"));
    }

    [Theory]
    [InlineData("model_type", "voxel")]
    [InlineData("texture_resolution", "16k")]
    [InlineData("ai_model", "meshy-1")]
    public void Unknown_generation_enum_values_are_reported(string field, string value)
    {
        var gen = Valid().Generation;
        gen = field switch
        {
            "model_type" => gen with { ModelType = value },
            "texture_resolution" => gen with { TextureResolution = value },
            _ => gen with { AiModel = value },
        };
        Assert.Contains(PackDefinitionValidator.Validate(Valid() with { Generation = gen }), e => e.Contains(field));
    }

    [Fact]
    public void Missing_required_yaml_field_throws_with_field_name()
    {
        var ex = Assert.Throws<PackDefinitionException>(() => PackDefinitionLoader.Load("name: no slug here"));
        Assert.Contains("slug", ex.Message);
    }

    private static PackDefinition Valid() => PackDefinitionLoader.Load(SampleYaml);
}
