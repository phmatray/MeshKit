using System.Text;
using MeshKit.Web.Catalog;
using Microsoft.Extensions.Options;

namespace MeshKit.Web.Seo;

public static class SeoEndpoints
{
    public static IEndpointRouteBuilder MapSeoEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/robots.txt", (IOptions<MeshKitOptions> options) =>
            Results.Text($"""
                User-agent: *
                Allow: /
                Disallow: /account/
                Disallow: /library
                Disallow: /checkout/
                Disallow: /api/
                Sitemap: {options.Value.PublicBaseUrl.TrimEnd('/')}/sitemap.xml

                """, "text/plain"));

        app.MapGet("/sitemap.xml", (ICatalogService catalog, Search.ISearchService search, IOptions<MeshKitOptions> options) =>
        {
            var baseUrl = options.Value.PublicBaseUrl.TrimEnd('/');
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">\n");
            void Url(string path, string changefreq, string priority, DateTimeOffset? lastmod = null)
            {
                sb.Append("  <url><loc>").Append(baseUrl).Append('/').Append(path.TrimStart('/')).Append("</loc>");
                if (lastmod is { } d)
                {
                    sb.Append("<lastmod>").Append(d.ToString("yyyy-MM-dd")).Append("</lastmod>");
                }

                sb.Append("<changefreq>").Append(changefreq).Append("</changefreq><priority>").Append(priority).Append("</priority></url>\n");
            }

            Url("", "weekly", "1.0");
            Url("packs", "weekly", "0.9");
            Url("search", "weekly", "0.6");
            Url("3d-models", "weekly", "0.7");
            foreach (var collection in Landing.Collections.Available(search))
            {
                Url(collection.Path, "weekly", collection.Kind == Landing.CollectionKind.Free ? "0.9" : "0.7");
            }

            Url("legal/terms", "yearly", "0.2");
            Url("legal/privacy", "yearly", "0.2");
            Url("legal/licence", "yearly", "0.3");
            foreach (var pack in catalog.Sellable)
            {
                Url($"packs/{pack.Slug}", "monthly", "0.8", pack.GeneratedAt);
                Url($"packs/{pack.Slug}/licence", "yearly", "0.2", pack.GeneratedAt);
            }

            sb.Append("</urlset>\n");
            return Results.Text(sb.ToString(), "application/xml");
        });

        return app;
    }
}
