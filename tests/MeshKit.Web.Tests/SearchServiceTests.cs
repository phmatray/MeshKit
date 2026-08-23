using MeshKit.Web.Catalog;
using MeshKit.Web.Search;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MeshKit.Web.Tests;

public sealed class SearchServiceTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("meshkit-search");
    private readonly CatalogService _catalog;
    private readonly SearchService _search;

    public SearchServiceTests()
    {
        TestPacks.WriteRich(_root.FullName, "fantasy-props", "Low-Poly Fantasy Props", "Dungeon dressing for prototypes.", "props", "lowpoly", ["fantasy", "dungeon"], 1900,
            ("treasure-chest", "Treasure Chest", "a closed wooden treasure chest with iron bands", ["chest", "loot", "wooden"], "furniture", 1200, ["glb", "fbx", "obj"]),
            ("wooden-barrel", "Wooden Barrel", "a wooden storage barrel", ["barrel", "wooden"], null, 800, ["glb", "fbx"]),
            ("iron-lantern", "Iron Lantern", "a hanging iron lantern", ["lantern", "metal", "light"], null, 2600, ["glb", "fbx", "usdz"]));
        TestPacks.WriteRich(_root.FullName, "scifi-props", "Low-Poly Sci-Fi Props", "Space station clutter.", "props", "lowpoly", ["sci-fi", "space"], 2400,
            ("cargo-crate", "Cargo Crate", "a futuristic cargo crate", ["crate", "container"], null, 900, ["glb", "fbx"]),
            ("energy-cell", "Energy Cell", "a glowing energy cell battery", ["battery", "light", "glow"], null, 1500, ["glb"]));
        TestPacks.Write(_root.FullName, "broken", sellable: false);
        _catalog = new CatalogService(Options.Create(new CatalogOptions { Path = _root.FullName }), NullLogger<CatalogService>.Instance);
        _search = new SearchService(_catalog, NullLogger<SearchService>.Instance);
    }

    [Fact]
    public void Empty_query_lists_every_sellable_model_with_facets()
    {
        var result = _search.Search(new SearchQuery());

        Assert.Equal(5, result.Total);
        Assert.DoesNotContain(result.Hits, h => h.PackSlug == "broken");
        Assert.Equal([("props", 4), ("furniture", 1)], result.Facets.Categories.Select(f => (f.Value, f.Count)));
        Assert.Contains(result.Facets.Tags, f => f.Value == "wooden" && f.Count == 2);
        Assert.Contains(result.Facets.Tags, f => f.Value == "fantasy" && f.Count == 3); // pack tag inherited by its models
        Assert.Contains(result.Facets.Formats, f => f.Value == "usdz" && f.Count == 1);
        Assert.Equal(2, result.Facets.Packs.Count);
    }

    [Fact]
    public void Name_match_outranks_prompt_match()
    {
        var result = _search.Search(new SearchQuery(Text: "chest"));

        Assert.Equal("treasure-chest", result.Hits[0].ModelSlug);
        Assert.Single(result.Hits);
    }

    [Fact]
    public void Stemming_and_prefix_find_related_words()
    {
        Assert.Contains(_search.Search(new SearchQuery(Text: "lanterns")).Hits, h => h.ModelSlug == "iron-lantern"); // porter: lanterns → lantern
        Assert.Contains(_search.Search(new SearchQuery(Text: "barr")).Hits, h => h.ModelSlug == "wooden-barrel");   // prefix
        Assert.Equal(2, _search.Search(new SearchQuery(Text: "wood")).Total);                                         // tag + prompt
    }

    [Fact]
    public void Multiple_words_are_all_required()
    {
        var result = _search.Search(new SearchQuery(Text: "wooden chest"));

        Assert.Equal(["treasure-chest"], result.Hits.Select(h => h.ModelSlug));
    }

    [Fact]
    public void Pack_description_and_category_are_searchable()
    {
        Assert.Equal(2, _search.Search(new SearchQuery(Text: "space station")).Total);
        Assert.Equal(5, _search.Search(new SearchQuery(Text: "lowpoly")).Total);
    }

    [Fact]
    public void Filters_compose_with_and_semantics()
    {
        Assert.Equal(2, _search.Search(new SearchQuery(Tags: ["light"])).Total);
        Assert.Equal(1, _search.Search(new SearchQuery(Tags: ["light"], Pack: "fantasy-props")).Total);
        Assert.Equal(1, _search.Search(new SearchQuery(Tags: ["wooden", "loot"])).Total);
        Assert.Equal(2, _search.Search(new SearchQuery(Format: "obj")).Total + _search.Search(new SearchQuery(Format: "usdz")).Total);
        Assert.Equal(3, _search.Search(new SearchQuery(MaxTriangles: 1200)).Total);
        Assert.Equal(1, _search.Search(new SearchQuery(Category: "furniture")).Total);
        Assert.Equal(0, _search.Search(new SearchQuery(Style: "realistic")).Total);
    }

    [Fact]
    public void Facets_reflect_the_filtered_set()
    {
        var result = _search.Search(new SearchQuery(Pack: "scifi-props"));

        Assert.DoesNotContain(result.Facets.Tags, f => f.Value == "fantasy");
        Assert.Contains(result.Facets.Tags, f => f.Value == "sci-fi" && f.Count == 2);
        Assert.Equal([("Low-Poly Sci-Fi Props", 2)], result.Facets.Packs.Select(f => (f.Value, f.Count)));
    }

    [Fact]
    public void Sorting_and_paging()
    {
        var byTris = _search.Search(new SearchQuery(Sort: SearchSort.TrianglesAsc, PageSize: 2));
        Assert.Equal(["wooden-barrel", "cargo-crate"], byTris.Hits.Select(h => h.ModelSlug));
        Assert.Equal(3, byTris.PageCount);

        var page3 = _search.Search(new SearchQuery(Sort: SearchSort.TrianglesAsc, PageSize: 2, Page: 3));
        Assert.Equal(["iron-lantern"], page3.Hits.Select(h => h.ModelSlug));

        var byName = _search.Search(new SearchQuery(Sort: SearchSort.Name));
        Assert.Equal("Cargo Crate", byName.Hits[0].Name);
    }

    [Theory]
    [InlineData("chest OR barrel")]
    [InlineData("\"unbalanced")]
    [InlineData("name:chest")]
    [InlineData("*")]
    [InlineData("(lantern")]
    public void Operator_characters_in_user_text_are_neutralised(string text)
    {
        var result = _search.Search(new SearchQuery(Text: text)); // must not throw an FTS syntax error

        Assert.True(result.Total >= 0);
    }

    [Fact]
    public void BuildMatch_quotes_and_prefixes_each_token()
    {
        Assert.Equal("\"wooden\"* \"chest\"*", SearchService.BuildMatch("Wooden chest!"));
        Assert.Null(SearchService.BuildMatch("   "));
    }

    [Fact]
    public void Suggest_completes_names_then_tags()
    {
        Assert.Equal(["Cargo Crate", "crate"], _search.Suggest("cra"));
        Assert.Empty(_search.Suggest(""));
    }

    [Fact]
    public void Index_rebuilds_after_catalog_reload()
    {
        Assert.Equal(5, _search.Search(new SearchQuery()).Total);
        TestPacks.WriteRich(_root.FullName, "late", "Late Pack", "d", "nature", "stylized", ["tree"], 900, ("oak", "Oak Tree", "an oak", ["wood"], null, 400, ["glb"]));

        _catalog.Reload();

        Assert.Equal(6, _search.Search(new SearchQuery()).Total);
        Assert.Contains(_search.Search(new SearchQuery()).Facets.Categories, f => f.Value == "nature");
    }

    public void Dispose()
    {
        _search.Dispose();
        _root.Delete(recursive: true);
    }
}
