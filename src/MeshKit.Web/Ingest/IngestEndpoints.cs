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

            // Both limits must follow MaxUploadBytes: Kestrel's request body cap AND the multipart
            // parser's (which defaults to 128 MB — a real pack is 250+ MB).
            var max = options.Value.MaxUploadBytes;
            if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
            {
                limit.MaxRequestBodySize = max;
            }

            if (!http.Request.HasFormContentType)
            {
                return Results.BadRequest(new { error = "Send the pack as multipart/form-data with a 'file' field." });
            }

            http.Features.Set<IFormFeature>(new FormFeature(http.Request, new FormOptions
            {
                MultipartBodyLengthLimit = max,
                MultipartBoundaryLengthLimit = 256,
            }));

            IFormCollection form;
            try
            {
                form = await http.Request.ReadFormAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is InvalidDataException or Microsoft.AspNetCore.Http.BadHttpRequestException)
            {
                return Results.Problem($"Upload rejected: {ex.Message} (limit {max} bytes).", statusCode: StatusCodes.Status413PayloadTooLarge);
            }
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
