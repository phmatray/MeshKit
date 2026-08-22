using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

namespace MeshKit.Web.Ingest;

public static class IngestEndpoints
{
    public static IEndpointRouteBuilder MapIngestEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/ingest", async (HttpContext http, IOptions<IngestOptions> options, PackImporter importer, CancellationToken cancellationToken) =>
        {
            var expected = options.Value.Token;
            if (string.IsNullOrWhiteSpace(expected))
            {
                return Results.Problem("Ingest is disabled: MeshKit:Ingest:Token is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            if (!TokenMatches(http.Request.Headers.Authorization.ToString(), expected))
            {
                return Results.Unauthorized();
            }

            if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
            {
                limit.MaxRequestBodySize = options.Value.MaxUploadBytes;
            }

            if (!http.Request.HasFormContentType)
            {
                return Results.BadRequest(new { error = "Send the pack as multipart/form-data with a 'file' field." });
            }

            var form = await http.Request.ReadFormAsync(cancellationToken);
            var file = form.Files["file"];
            if (file is null || file.Length == 0)
            {
                return Results.BadRequest(new { error = "Missing or empty 'file' field." });
            }

            await using var stream = file.OpenReadStream();
            var result = await importer.ImportAsync(stream, cancellationToken);
            return result.Success
                ? Results.Created($"/packs/{result.Slug}", new { slug = result.Slug, sellable = result.Sellable })
                : Results.BadRequest(new { error = result.Error });
        }).DisableAntiforgery();

        return app;
    }

    private static bool TokenMatches(string authorization, string expected)
    {
        const string prefix = "Bearer ";
        if (!authorization.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var presented = Encoding.UTF8.GetBytes(authorization[prefix.Length..].Trim());
        var wanted = Encoding.UTF8.GetBytes(expected);
        return CryptographicOperations.FixedTimeEquals(presented, wanted);
    }
}
