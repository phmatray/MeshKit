namespace MeshKit.Web.Search;

public static class SearchEndpoints
{
    /// <summary>JSON search for tooling and future clients: <c>GET /api/search?q=chest&amp;tag=wooden&amp;format=fbx</c>.</summary>
    public static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/search", (ISearchService search, string? q, string[]? tag, string? category, string? style, string? format, string? pack, int? maxtris, string? sort, int? page, int? pageSize) =>
        {
            var query = new SearchQuery(
                Text: q,
                Tags: tag,
                Category: category,
                Style: style,
                Format: format,
                Pack: pack,
                MaxTriangles: maxtris,
                Sort: Enum.TryParse<SearchSort>(sort, ignoreCase: true, out var s) ? s : SearchSort.Relevance,
                Page: Math.Max(1, page ?? 1),
                PageSize: Math.Clamp(pageSize ?? 24, 1, 100));
            var result = search.Search(query);
            return Results.Ok(new
            {
                total = result.Total,
                page = query.Page,
                pageCount = result.PageCount,
                hits = result.Hits.Select(h => new
                {
                    pack = h.PackSlug,
                    packName = h.PackName,
                    model = h.ModelSlug,
                    name = h.Name,
                    url = $"/packs/{h.PackSlug}?model={h.ModelSlug}",
                    thumbnail = h.Thumbnail is null ? null : Catalog.CatalogEndpoints.PublicUrl(h.PackSlug, h.Thumbnail),
                    tags = h.Tags,
                    category = h.Category,
                    style = h.Style,
                    triangles = h.Triangles,
                    formats = h.Formats,
                    price = new { amount = h.PriceAmount, currency = h.PriceCurrency },
                }),
                facets = result.Facets,
            });
        });

        app.MapGet("/api/search/suggest", (ISearchService search, string? q) => Results.Ok(search.Suggest(q ?? string.Empty)));
        return app;
    }
}
