using System.Text.Json;
using MeshKit.Core.Catalog;

namespace MeshKit.Web.Components.Shared;

/// <summary>Schema.org payloads. Serialised once, escaped for a script tag (no "&lt;/script" injection).</summary>
public static class JsonLdBuilder
{
    private static readonly JsonSerializerOptions Options = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    public const string LegalName = "Atypical Consulting";
    public const string Vat = "BE 0744.517.956";

    public static string Organization(string baseUrl) => Serialize(new
    {
        context = "https://schema.org",
        type = "Organization",
        name = "MeshKit by Atypical Consulting",
        legalName = LegalName,
        vatID = Vat,
        url = baseUrl,
        logo = $"{baseUrl}/og-default.png",
        sameAs = new[] { "https://www.atypical.consulting" },
    });

    public static string Product(string baseUrl, PackManifest pack, string? thumbnail) => Serialize(new
    {
        context = "https://schema.org",
        type = "Product",
        name = pack.Name,
        description = pack.Description,
        image = thumbnail,
        url = $"{baseUrl}/packs/{pack.Slug}",
        sku = pack.Slug,
        category = pack.Category,
        keywords = string.Join(", ", pack.TagList),
        brand = new { type = "Brand", name = "MeshKit" },
        offers = new
        {
            type = "Offer",
            price = (pack.Price.Amount / 100m).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            priceCurrency = pack.Price.Currency.ToUpperInvariant(),
            availability = "https://schema.org/InStock",
            url = $"{baseUrl}/packs/{pack.Slug}",
            seller = new { type = "Organization", name = LegalName },
        },
        additionalProperty = new object[]
        {
            new { type = "PropertyValue", name = "Models", value = pack.Models.Count },
            new { type = "PropertyValue", name = "Formats", value = string.Join(", ", pack.Models.SelectMany(m => m.Files.Select(f => f.Format)).Where(f => f is "glb" or "fbx" or "obj" or "usdz" or "stl" or "3mf").Distinct().Order()) },
        },
    });

    /// <summary>A landing page: a list of the models it shows, plus its FAQ.</summary>
    public static string Collection(string baseUrl, string name, string description, string path, IEnumerable<(string Name, string Url, string? Image)> items, IEnumerable<(string Question, string Answer)> faq) => Serialize(new object[]
    {
        new
        {
            context = "https://schema.org",
            type = "CollectionPage",
            name,
            description,
            url = $"{baseUrl}/{path}",
            mainEntity = new
            {
                type = "ItemList",
                itemListElement = items.Select((item, i) => new
                {
                    type = "ListItem",
                    position = i + 1,
                    name = item.Name,
                    url = item.Url,
                    image = item.Image,
                }).ToList(),
            },
        },
        new
        {
            context = "https://schema.org",
            type = "FAQPage",
            mainEntity = faq.Select(f => new
            {
                type = "Question",
                name = f.Question,
                acceptedAnswer = new { type = "Answer", text = f.Answer },
            }).ToList(),
        },
    });

    private static string Serialize(object value)
    {
        // "@context"/"@type" cannot be C# property names: rename after serialising.
        var json = JsonSerializer.Serialize(value, Options)
            .Replace("\"context\":", "\"@context\":", StringComparison.Ordinal)
            .Replace("\"type\":", "\"@type\":", StringComparison.Ordinal);
        return json.Replace("</", "<\\/", StringComparison.Ordinal);
    }
}
