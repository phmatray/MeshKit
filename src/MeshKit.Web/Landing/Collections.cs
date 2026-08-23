using MeshKit.Core.Definitions;
using MeshKit.Web.Search;

namespace MeshKit.Web.Landing;

/// <summary>
/// The indexable "3D models" pages: one per category and one per style that actually has models, plus the free
/// samples page. Static copy per key; the model lists come from the search index, so a new pack extends the pages
/// on its own. Search facet URLs stay noindex — these are the pages meant to rank.
/// </summary>
public sealed record Collection(string Key, CollectionKind Kind, string Title, string Headline, string Intro, string Query)
{
    public string Path => Kind == CollectionKind.Free ? "free-3d-models" : $"3d-models/{Key}";
}

public enum CollectionKind
{
    Category,
    Style,
    Free,
}

public static class Collections
{
    public static readonly Collection Free = new("free", CollectionKind.Free,
        "Free 3D models — one game-ready sample from every pack",
        "Free 3D models, game-ready",
        "Every MeshKit pack gives away one complete model: GLB, FBX, OBJ and USDZ, PBR texture maps, LODs where the pack has them, and the same licence as the paid pack. Open it in Blender, Unity, Godot or Unreal and judge the topology, UVs, scale and pivot before spending anything. A free account is all it takes.",
        "free game-ready 3D models");

    private static readonly Dictionary<string, (string Title, string Headline, string Intro)> CategoryCopy = new(StringComparer.Ordinal)
    {
        ["props"] = ("Props 3D models — low-poly, PBR, game-ready", "Props 3D models", "Crates, barrels, lanterns, consoles — the clutter that makes a level feel lived in. Every prop is a single watertight mesh with PBR maps, a real-world size and a floor-level pivot, so it drops straight into a scene."),
        ["nature"] = ("Nature 3D models — trees, rocks and ground cover", "Nature 3D models", "Trees, rocks, stumps, logs, mushrooms and flowers for forests, clearings and trails. Low polycounts and LODs keep scenes cheap; real-world scale keeps an oak an oak and a mushroom a mushroom."),
        ["weapons"] = ("Weapons 3D models — swords, shields, bows and staffs", "Weapons 3D models", "Melee, ranged and magic weapons with shields, textured in a chunky stylized look. Upright, real-world scale, pivot at the grip end, ready to parent to a hand bone."),
        ["characters"] = ("Character 3D models — game-ready", "Character 3D models", "Game-ready characters with PBR textures and real-world height."),
        ["environment"] = ("Environment 3D models — game-ready", "Environment 3D models", "Modular pieces and set dressing for building levels."),
        ["vehicles"] = ("Vehicle 3D models — game-ready", "Vehicle 3D models", "Drones, carts and vehicles with PBR textures and real-world scale."),
        ["architecture"] = ("Architecture 3D models — game-ready", "Architecture 3D models", "Wells, walls, doors and other built pieces at real-world scale."),
        ["food"] = ("Food 3D models — game-ready", "Food 3D models", "Food and drink props with PBR textures."),
        ["furniture"] = ("Furniture 3D models — game-ready", "Furniture 3D models", "Tables, chests, consoles and other furniture at real-world scale."),
        ["misc"] = ("Miscellaneous 3D models — game-ready", "Miscellaneous 3D models", "Everything that does not fit another category."),
    };

    private static readonly Dictionary<string, (string Title, string Headline, string Intro)> StyleCopy = new(StringComparer.Ordinal)
    {
        ["lowpoly"] = ("Low-poly 3D models — PBR, LODs, real-world scale", "Low-poly 3D models", "Every model is remeshed to a stated triangle budget — usually 4,000 to 5,000 — and ships with lighter LODs on top. Clean enough for mobile and VR, textured with 2k PBR maps so they still read well up close."),
        ["stylized"] = ("Stylized 3D models — chunky, hand-painted look", "Stylized 3D models", "Chunky proportions and painterly PBR textures for games that want character rather than realism. Modest polycounts, LODs included."),
        ["realistic"] = ("Realistic 3D models — PBR", "Realistic 3D models", "Photoreal PBR models for high-fidelity scenes."),
        ["voxel"] = ("Voxel 3D models", "Voxel 3D models", "Blocky voxel models for retro-styled games."),
        ["handpainted"] = ("Hand-painted 3D models", "Hand-painted 3D models", "Hand-painted texture style, low polycounts."),
    };

    /// <summary>Every collection that currently has at least one model, in a stable order. Never includes empty ones.</summary>
    public static IReadOnlyList<Collection> Available(ISearchService search)
    {
        var all = search.Search(new SearchQuery(PageSize: 1));
        var result = new List<Collection>();
        if (all.Facets.FreeSamples > 0)
        {
            result.Add(Free);
        }

        foreach (var category in all.Facets.Categories.Select(f => f.Value).Where(GenerationSettings.KnownCategories.Contains).Order(StringComparer.Ordinal))
        {
            result.Add(ForCategory(category));
        }

        foreach (var style in all.Facets.Styles.Select(f => f.Value).Where(GenerationSettings.KnownStyles.Contains).Order(StringComparer.Ordinal))
        {
            result.Add(ForStyle(style));
        }

        return result;
    }

    /// <summary>Resolves a <c>/3d-models/{key}</c> segment: a category first, then a style; null when unknown or empty.</summary>
    public static Collection? Resolve(string key, ISearchService search) =>
        Available(search).FirstOrDefault(c => c.Kind != CollectionKind.Free && c.Key == key);

    public static Collection ForCategory(string key) => CategoryCopy.TryGetValue(key, out var c)
        ? new Collection(key, CollectionKind.Category, c.Title, c.Headline, c.Intro, $"{key} 3D models")
        : new Collection(key, CollectionKind.Category, $"{Capitalise(key)} 3D models — game-ready", $"{Capitalise(key)} 3D models", "Game-ready models with PBR textures and real-world scale.", $"{key} 3D models");

    public static Collection ForStyle(string key) => StyleCopy.TryGetValue(key, out var c)
        ? new Collection(key, CollectionKind.Style, c.Title, c.Headline, c.Intro, $"{key} 3D models")
        : new Collection(key, CollectionKind.Style, $"{Capitalise(key)} 3D models — game-ready", $"{Capitalise(key)} 3D models", "Game-ready models with PBR textures and real-world scale.", $"{key} 3D models");

    public static SearchQuery QueryFor(Collection collection, int pageSize = 48) => collection.Kind switch
    {
        CollectionKind.Free => new SearchQuery(Free: true, PageSize: pageSize, Sort: SearchSort.Name),
        CollectionKind.Category => new SearchQuery(Category: collection.Key, PageSize: pageSize, Sort: SearchSort.Name),
        _ => new SearchQuery(Style: collection.Key, PageSize: pageSize, Sort: SearchSort.Name),
    };

    private static string Capitalise(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>The questions a buyer asks before trusting an AI-generated pack; the answers are what the pipeline enforces.</summary>
    public static readonly IReadOnlyList<(string Question, string Answer)> Faq =
    [
        ("Which formats do I get?", "GLB, FBX, OBJ (with MTL) and USDZ for every model, plus the PBR texture maps as separate PNG files (base colour, normal, roughness, metallic, emission). Textures are 2k."),
        ("Are the models really low-poly?", "Yes, by construction: each pack states a triangle budget and every model is remeshed to it — the triangle count is measured from the file and shown on the pack page, not typed by hand. Most packs also ship two lighter LODs."),
        ("Are they scaled correctly?", "Models are sized to real-world dimensions with the pivot at the base (floor level), so a barrel is about a metre tall in Unity, Unreal, Godot or Blender without rescaling. The pack page shows each model's size in metres."),
        ("Can I try before buying?", "Every pack has one free sample model — the complete asset in every format, with textures and licence, not a watered-down preview. It needs a free account and nothing else."),
        ("What can I use them for?", "Games, apps, renders, videos and prototypes, commercial or not, under the MeshKit Royalty-Free Asset Licence that ships in every pack: use and modify freely, no attribution required; the only thing you may not do is resell or redistribute the models themselves."),
        ("How were they made?", "Generated with Meshy from written prompts, then remeshed to budget, sized, checked and curated by hand. Anything that did not come out right was regenerated."),
    ];
}
