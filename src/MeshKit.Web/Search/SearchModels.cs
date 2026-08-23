namespace MeshKit.Web.Search;

public enum SearchSort
{
    Relevance,
    Name,
    TrianglesAsc,
    TrianglesDesc,
    PriceAsc,
}

/// <summary>Everything the search box and facet sidebar can express. All filters are AND-ed; tags are all-required.</summary>
public sealed record SearchQuery(
    string? Text = null,
    IReadOnlyList<string>? Tags = null,
    string? Category = null,
    string? Style = null,
    string? Format = null,
    string? Pack = null,
    int? MaxTriangles = null,
    SearchSort Sort = SearchSort.Relevance,
    int Page = 1,
    int PageSize = 24,
    bool Free = false)
{
    public bool HasFilters => !string.IsNullOrWhiteSpace(Text) || Tags is { Count: > 0 } || Category is not null || Style is not null || Format is not null || Pack is not null || MaxTriangles is not null || Free;
}

public sealed record SearchHit(
    string PackSlug,
    string PackName,
    string ModelSlug,
    string Name,
    string? Thumbnail,
    IReadOnlyList<string> Tags,
    string Category,
    string Style,
    int? Triangles,
    IReadOnlyList<string> Formats,
    long PriceAmount,
    string PriceCurrency,
    bool PreviewTextured,
    double Score,
    bool IsFree = false);

public sealed record FacetValue(string Value, int Count);

public sealed record SearchFacets(
    IReadOnlyList<FacetValue> Categories,
    IReadOnlyList<FacetValue> Styles,
    IReadOnlyList<FacetValue> Tags,
    IReadOnlyList<FacetValue> Formats,
    IReadOnlyList<FacetValue> Packs,
    int FreeSamples = 0);

public sealed record SearchResult(IReadOnlyList<SearchHit> Hits, int Total, SearchFacets Facets, SearchQuery Query)
{
    public int PageCount => Math.Max(1, (int)Math.Ceiling(Total / (double)Query.PageSize));
}

public interface ISearchService
{
    SearchResult Search(SearchQuery query);

    /// <summary>Quick name/tag suggestions for the search box.</summary>
    IReadOnlyList<string> Suggest(string prefix, int limit = 8);
}
